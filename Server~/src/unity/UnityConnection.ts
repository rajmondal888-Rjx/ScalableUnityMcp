import WebSocket from 'ws';
import { EventEmitter } from 'events';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';

export enum ConnectionState {
  Disconnected = 'disconnected',
  Connecting = 'connecting',
  Connected = 'connected',
  Reconnecting = 'reconnecting'
}

export const UnityCloseCode = {
  PLAY_MODE: 4001
} as const;

export interface ConnectionStateChange {
  previousState: ConnectionState;
  currentState: ConnectionState;
  reason?: string;
  attemptNumber?: number;
}

export interface UnityConnectionConfig {
  host: string;
  port: number;
  requestTimeout: number;
  connectTimeout?: number;
  clientName?: string;
  minReconnectDelay?: number;
  maxReconnectDelay?: number;
  reconnectBackoffMultiplier?: number;
  maxReconnectAttempts?: number;
  heartbeatInterval?: number;
  heartbeatTimeout?: number;
  playModePollingInterval?: number;
}

const DEFAULT_CONFIG = {
  connectTimeout: 5000,
  minReconnectDelay: 1000,
  maxReconnectDelay: 30000,
  reconnectBackoffMultiplier: 2,
  maxReconnectAttempts: -1,       // never give up — Unity may be recompiling or reloading
  heartbeatInterval: 45000,
  heartbeatTimeout: 20000,        // Unity can be busy for >5s during heavy ops
  playModePollingInterval: 3000
};

export class UnityConnection extends EventEmitter {
  private logger: Logger;
  private config: Required<UnityConnectionConfig>;
  private ws: WebSocket | null = null;
  private state: ConnectionState = ConnectionState.Disconnected;

  private reconnectAttempt: number = 0;
  private reconnectTimer: NodeJS.Timeout | null = null;
  private connectionTimeoutTimer: NodeJS.Timeout | null = null;
  private isManualDisconnect: boolean = false;
  private isPlayModeReconnect: boolean = false;

  private heartbeatTimer: NodeJS.Timeout | null = null;
  private heartbeatTimeoutTimer: NodeJS.Timeout | null = null;
  private lastPongTime: number = 0;
  private awaitingPong: boolean = false;

  constructor(logger: Logger, config: UnityConnectionConfig) {
    super();
    this.logger = logger;
    this.config = { ...DEFAULT_CONFIG, ...config } as Required<UnityConnectionConfig>;
  }

  public get connectionState(): ConnectionState { return this.state; }
  public get isConnected(): boolean {
    return this.state === ConnectionState.Connected && this.ws !== null && this.ws.readyState === WebSocket.OPEN;
  }
  public get isConnecting(): boolean {
    return this.state === ConnectionState.Connecting || this.state === ConnectionState.Reconnecting;
  }
  public get timeSinceLastPong(): number {
    return this.lastPongTime === 0 ? -1 : Date.now() - this.lastPongTime;
  }

  public updateConfig(config: Partial<UnityConnectionConfig>): void {
    this.config = { ...this.config, ...config };
  }

  public async connect(): Promise<void> {
    if (this.isConnected) { this.logger.debug('Already connected'); return; }
    if (this.isConnecting) { this.logger.debug('Connection in progress'); return; }
    this.isManualDisconnect = false;
    return this.doConnect();
  }

  public disconnect(reason?: string): void {
    this.isManualDisconnect = true;
    this.stopReconnectTimer();
    this.stopHeartbeat();
    this.closeWebSocket(reason || 'Manual disconnect');
    this.setState(ConnectionState.Disconnected, reason || 'Manual disconnect');
  }

  public send(message: string): void {
    if (!this.isConnected || !this.ws) throw new McpUnityError(ErrorType.CONNECTION, 'Not connected to Unity');
    try {
      this.ws.send(message);
    } catch (err) {
      throw new McpUnityError(ErrorType.CONNECTION, `Send failed: ${err instanceof Error ? err.message : String(err)}`);
    }
  }

  public get webSocket(): WebSocket | null { return this.ws; }

  private async doConnect(): Promise<void> {
    const isReconnecting = this.reconnectAttempt > 0;
    this.setState(
      isReconnecting ? ConnectionState.Reconnecting : ConnectionState.Connecting,
      isReconnecting ? `Reconnection attempt ${this.reconnectAttempt}` : 'Connecting'
    );

    return new Promise<void>((resolve, reject) => {
      const wsUrl = `ws://${this.config.host}:${this.config.port}/McpUnity`;
      this.logger.debug(`Connecting to ${wsUrl}...`);

      const options: WebSocket.ClientOptions = {
        headers: { 'X-Client-Name': this.config.clientName || '' },
        origin: this.config.clientName || ''
      };

      this.closeWebSocket('Preparing new connection');
      this.ws = new WebSocket(wsUrl, options);

      this.clearConnectionTimeout();
      this.connectionTimeoutTimer = setTimeout(() => {
        if (this.ws && this.ws.readyState === WebSocket.CONNECTING) {
          this.logger.warn('Connection timeout');
          this.closeWebSocket('Connection timeout');
          const error = new McpUnityError(ErrorType.CONNECTION, 'Connection timeout');
          this.handleConnectionFailure(error);
          reject(error);
        }
      }, this.config.connectTimeout);

      this.ws.onopen = () => {
        this.clearConnectionTimeout();
        this.logger.info('WebSocket connected to Unity');
        this.reconnectAttempt = 0;
        this.isPlayModeReconnect = false;
        this.lastPongTime = Date.now();
        this.setState(ConnectionState.Connected, 'Connection established');
        this.startHeartbeat();
        resolve();
      };

      this.ws.onerror = (err) => {
        this.clearConnectionTimeout();
        const errorMessage = err.message || 'Unknown error';
        this.logger.error(`WebSocket error: ${errorMessage}`);
        this.emit('error', new McpUnityError(ErrorType.CONNECTION, `Connection failed: ${errorMessage}`));
      };

      this.ws.onmessage = (event) => {
        this.emit('message', event.data.toString());
      };

      this.ws.onclose = (event) => {
        this.clearConnectionTimeout();
        this.stopHeartbeat();
        const reason = event.reason || `Code: ${event.code}`;
        this.logger.debug(`WebSocket closed: ${reason}`);

        if (event.code === UnityCloseCode.PLAY_MODE) {
          this.logger.info('Unity entering Play mode — using fast polling');
          this.isPlayModeReconnect = true;
        }

        this.ws = null;

        if (!this.isManualDisconnect) {
          this.handleConnectionFailure(new McpUnityError(ErrorType.CONNECTION, reason));
        } else {
          this.setState(ConnectionState.Disconnected, reason);
        }

        if (this.state === ConnectionState.Connecting) {
          reject(new McpUnityError(ErrorType.CONNECTION, reason));
        }
      };

      this.ws.on('pong', () => this.handlePong());
    });
  }

  private handleConnectionFailure(error: McpUnityError): void {
    if (this.isManualDisconnect) { this.setState(ConnectionState.Disconnected, 'Manual disconnect'); return; }

    if (!this.isPlayModeReconnect &&
        this.config.maxReconnectAttempts !== -1 &&
        this.reconnectAttempt >= this.config.maxReconnectAttempts) {
      this.logger.error(`Max reconnection attempts (${this.config.maxReconnectAttempts}) reached`);
      this.setState(ConnectionState.Disconnected, 'Max reconnection attempts reached');
      this.emit('error', new McpUnityError(ErrorType.CONNECTION, 'Max reconnection attempts reached'));
      return;
    }

    const delay = this.isPlayModeReconnect ? this.config.playModePollingInterval : this.calculateBackoffDelay();
    this.reconnectAttempt++;
    const modeInfo = this.isPlayModeReconnect ? ' (Play mode polling)' : '';
    this.logger.info(`Scheduling reconnection attempt ${this.reconnectAttempt} in ${delay}ms${modeInfo}`);
    this.setState(ConnectionState.Reconnecting, `Waiting ${delay}ms before attempt ${this.reconnectAttempt}${modeInfo}`);

    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      this.doConnect().catch((err) => {
        this.logger.warn(`Reconnection attempt ${this.reconnectAttempt} failed: ${err.message}`);
      });
    }, delay);
  }

  private calculateBackoffDelay(): number {
    const delay = this.config.minReconnectDelay * Math.pow(this.config.reconnectBackoffMultiplier, this.reconnectAttempt);
    const jitter = delay * 0.2 * Math.random();
    return Math.min(delay + jitter, this.config.maxReconnectDelay);
  }

  private stopReconnectTimer(): void {
    if (this.reconnectTimer) { clearTimeout(this.reconnectTimer); this.reconnectTimer = null; }
    this.reconnectAttempt = 0;
  }

  private startHeartbeat(): void {
    this.stopHeartbeat();
    if (this.config.heartbeatInterval <= 0) return;
    this.heartbeatTimer = setInterval(() => this.sendHeartbeat(), this.config.heartbeatInterval);
  }

  private stopHeartbeat(): void {
    if (this.heartbeatTimer) { clearInterval(this.heartbeatTimer); this.heartbeatTimer = null; }
    if (this.heartbeatTimeoutTimer) { clearTimeout(this.heartbeatTimeoutTimer); this.heartbeatTimeoutTimer = null; }
    this.awaitingPong = false;
  }

  private sendHeartbeat(): void {
    if (!this.isConnected || !this.ws) return;
    if (this.awaitingPong) { this.logger.warn('No pong received, connection may be stale'); this.handleStaleConnection(); return; }

    try {
      this.awaitingPong = true;
      this.ws.ping();
      this.heartbeatTimeoutTimer = setTimeout(() => {
        if (this.awaitingPong) { this.logger.warn('Heartbeat timeout'); this.handleStaleConnection(); }
      }, this.config.heartbeatTimeout);
    } catch (err) {
      this.logger.error(`Failed to send heartbeat: ${err instanceof Error ? err.message : String(err)}`);
      this.awaitingPong = false;
    }
  }

  private handlePong(): void {
    this.awaitingPong = false;
    this.lastPongTime = Date.now();
    if (this.heartbeatTimeoutTimer) { clearTimeout(this.heartbeatTimeoutTimer); this.heartbeatTimeoutTimer = null; }
    this.logger.debug('Heartbeat pong received');
  }

  private handleStaleConnection(): void {
    this.logger.warn('Stale connection detected, forcing reconnection');
    this.awaitingPong = false;
    this.closeWebSocket('Stale connection detected');
    this.handleConnectionFailure(new McpUnityError(ErrorType.CONNECTION, 'Stale connection detected'));
  }

  private closeWebSocket(reason?: string): void {
    if (!this.ws) return;
    this.logger.debug(`Closing WebSocket: ${reason || 'No reason'}`);
    this.clearConnectionTimeout();
    const socket = this.ws;
    this.ws = null;
    socket.onopen = null;
    socket.onmessage = null;
    socket.onerror = null;
    socket.onclose = null;
    socket.removeAllListeners('pong');
    try { socket.terminate(); } catch (err) {
      this.logger.error(`Error closing WebSocket: ${err instanceof Error ? err.message : String(err)}`);
    }
  }

  private clearConnectionTimeout(): void {
    if (this.connectionTimeoutTimer) { clearTimeout(this.connectionTimeoutTimer); this.connectionTimeoutTimer = null; }
  }

  private setState(newState: ConnectionState, reason?: string): void {
    if (this.state === newState) return;
    const previousState = this.state;
    this.state = newState;
    this.logger.debug(`Connection state: ${previousState} -> ${newState} (${reason || 'no reason'})`);
    this.emit('stateChange', { previousState, currentState: newState, reason, attemptNumber: this.reconnectAttempt > 0 ? this.reconnectAttempt : undefined } as ConnectionStateChange);
  }

  public forceReconnect(): void {
    this.logger.info('Forcing reconnection...');
    this.isManualDisconnect = false;
    this.stopReconnectTimer();
    this.closeWebSocket('Force reconnect');
    this.reconnectAttempt = 0;
    this.doConnect().catch((err) => this.logger.warn(`Force reconnect failed: ${err.message}`));
  }

  public getStats() {
    return {
      state: this.state,
      reconnectAttempt: this.reconnectAttempt,
      timeSinceLastPong: this.timeSinceLastPong,
      isAwaitingPong: this.awaitingPong
    };
  }
}
