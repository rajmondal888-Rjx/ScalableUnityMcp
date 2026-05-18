import { v4 as uuidv4 } from 'uuid';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { promises as fs } from 'fs';
import path from 'path';
import { UnityConnection, ConnectionState, ConnectionStateChange, UnityConnectionConfig } from './UnityConnection.js';
import { CommandQueue, CommandQueueConfig, CommandQueueStats } from './CommandQueue.js';

const SETTINGS_PATH = path.resolve(process.cwd(), './ProjectSettings/ScalableMcpSettings.json');

interface PendingRequest {
  resolve: (value: unknown) => void;
  reject: (reason: unknown) => void;
  timeout: NodeJS.Timeout;
}

interface UnityRequest {
  id?: string;
  method: string;
  params: unknown;
}

interface UnityResponse {
  id: string;
  result?: unknown;
  error?: { message: string; type: string; details?: unknown };
}

export type ConnectionStateCallback = (change: ConnectionStateChange) => void;

export { ConnectionState, type ConnectionStateChange } from './UnityConnection.js';
export { type CommandQueueConfig, type CommandQueueStats } from './CommandQueue.js';

export interface SendRequestOptions {
  queueIfDisconnected?: boolean;
  timeout?: number;
}

export interface McpUnityClientConfig {
  queue?: CommandQueueConfig;
  queueingEnabled?: boolean;
}

export class McpUnityClient {
  private logger: Logger;
  private port = 8096;
  private host = 'localhost';
  private requestTimeout = 10000;

  private connection: UnityConnection | null = null;
  private pendingRequests = new Map<string, PendingRequest>();
  private clientName = '';
  private stateListeners = new Set<ConnectionStateCallback>();
  private commandQueue: CommandQueue;
  private queueingEnabled: boolean;
  private isReplayingQueue = false;

  constructor(logger: Logger, config?: McpUnityClientConfig) {
    this.logger = logger;
    this.commandQueue = new CommandQueue(logger, config?.queue);
    this.queueingEnabled = config?.queueingEnabled ?? true;
  }

  public setQueueingEnabled(enabled: boolean): void {
    this.queueingEnabled = enabled;
  }

  public get isQueueingEnabled(): boolean { return this.queueingEnabled; }
  public getQueueStats(): CommandQueueStats { return this.commandQueue.getStats(); }
  public get queuedCommandCount(): number { return this.commandQueue.size; }

  public async start(clientName?: string): Promise<void> {
    try {
      this.logger.info('Reading startup configuration...');
      await this.parseAndSetConfig();
      this.clientName = clientName || '';

      const config: UnityConnectionConfig = {
        host: this.host,
        port: this.port,
        requestTimeout: this.requestTimeout,
        clientName: this.clientName,
      };

      this.connection = new UnityConnection(this.logger, config);

      this.connection.on('stateChange', (change: ConnectionStateChange) => this.handleStateChange(change));
      this.connection.on('message', (data: string) => this.handleMessage(data));
      this.connection.on('error', (error: McpUnityError) => {
        this.logger.error(`Connection error: ${error.message}`);
        this.rejectAllPendingRequests(error);
      });

      this.logger.info('Connecting to Unity WebSocket...');
      await this.connection.connect();
      this.logger.info('Connected to Unity');
    } catch (error) {
      this.logger.warn(`Could not connect to Unity: ${error instanceof Error ? error.message : String(error)}`);
      this.logger.warn('Will retry on next request');
    }
  }

  private async parseAndSetConfig(): Promise<void> {
    const config = await this.readSettingsFile();
    this.port = config.Port ? parseInt(String(config.Port), 10) : 8095;
    this.host = process.env.UNITY_HOST || (config.Host as string | undefined) || 'localhost';
    this.requestTimeout = config.RequestTimeoutSeconds ? parseInt(String(config.RequestTimeoutSeconds), 10) * 1000 : 10000;
    this.logger.info(`Port: ${this.port}, Host: ${this.host}, Timeout: ${this.requestTimeout / 1000}s`);
  }

  private handleStateChange(change: ConnectionStateChange): void {
    for (const listener of this.stateListeners) {
      try { listener(change); } catch (err) {
        this.logger.error(`State listener error: ${err instanceof Error ? err.message : String(err)}`);
      }
    }

    if (change.currentState === ConnectionState.Connected &&
        (change.previousState === ConnectionState.Reconnecting || change.previousState === ConnectionState.Connecting)) {
      this.replayQueuedCommands();
    } else if (change.currentState === ConnectionState.Disconnected) {
      if (change.reason?.includes('Max reconnection attempts')) {
        this.commandQueue.clear(change.reason);
      }
      this.rejectAllPendingRequests(new McpUnityError(ErrorType.CONNECTION, change.reason || 'Connection lost'));
    }
  }

  private async replayQueuedCommands(): Promise<void> {
    if (this.isReplayingQueue) return;
    const commands = this.commandQueue.drain();
    if (commands.length === 0) return;
    this.isReplayingQueue = true;
    this.logger.info(`Replaying ${commands.length} queued commands`);
    for (const command of commands) {
      try {
        const result = await this.sendRequestInternal(command.request as UnityRequest, command.timeout);
        command.resolve(result);
        this.commandQueue.recordReplaySuccess();
      } catch (error) {
        command.reject(error);
      }
    }
    this.isReplayingQueue = false;
  }

  private handleMessage(data: string): void {
    try {
      const response = JSON.parse(data) as UnityResponse;
      if (response.id && this.pendingRequests.has(response.id)) {
        const pending = this.pendingRequests.get(response.id)!;
        clearTimeout(pending.timeout);
        this.pendingRequests.delete(response.id);
        if (response.error) {
          pending.reject(new McpUnityError(ErrorType.TOOL_EXECUTION, response.error.message || 'Unknown error', response.error.details));
        } else {
          pending.resolve(response.result);
        }
      }
    } catch (e) {
      this.logger.error(`Error parsing message: ${e instanceof Error ? e.message : String(e)}`);
    }
  }

  private rejectAllPendingRequests(error: McpUnityError): void {
    for (const [id, pending] of this.pendingRequests.entries()) {
      clearTimeout(pending.timeout);
      pending.reject(error);
      this.pendingRequests.delete(id);
    }
  }

  public async stop(): Promise<void> {
    this.commandQueue.dispose();
    if (this.connection) {
      this.connection.disconnect('Server stopping');
      this.connection.removeAllListeners();
      this.connection = null;
    }
    this.rejectAllPendingRequests(new McpUnityError(ErrorType.CONNECTION, 'Server stopped'));
    this.logger.info('Unity client stopped');
  }

  public async sendRequest(request: UnityRequest, options: SendRequestOptions = {}): Promise<unknown> {
    const { queueIfDisconnected = this.queueingEnabled, timeout } = options;
    const requestId = (request.id as string) || uuidv4();
    const message: UnityRequest = { ...request, id: requestId };

    if (this.isConnected) return this.sendRequestInternal(message, timeout);

    if (!this.connection) throw new McpUnityError(ErrorType.CONNECTION, 'Not started — call start() first');

    if (queueIfDisconnected && (this.connectionState === ConnectionState.Reconnecting || this.connectionState === ConnectionState.Connecting)) {
      return new Promise((resolve, reject) => {
        this.commandQueue.enqueue({ id: requestId, request: message, resolve, reject, timeout });
      });
    }

    this.logger.info('Not connected, connecting first...');
    try {
      await this.connection.connect();
      return this.sendRequestInternal(message, timeout);
    } catch (error) {
      if (queueIfDisconnected) {
        return new Promise((resolve, reject) => {
          this.commandQueue.enqueue({ id: requestId, request: message, resolve, reject, timeout });
        });
      }
      throw new McpUnityError(ErrorType.CONNECTION, `Not connected: ${error instanceof Error ? error.message : String(error)}`);
    }
  }

  private sendRequestInternal(request: UnityRequest, customTimeout?: number): Promise<unknown> {
    const requestId = request.id as string;
    const timeoutMs = customTimeout ?? this.requestTimeout;

    return new Promise((resolve, reject) => {
      if (!this.connection || !this.isConnected) {
        reject(new McpUnityError(ErrorType.CONNECTION, 'Not connected to Unity'));
        return;
      }

      const timeout = setTimeout(() => {
        if (this.pendingRequests.has(requestId)) {
          this.pendingRequests.delete(requestId);
          reject(new McpUnityError(ErrorType.TIMEOUT, 'Request timed out'));
        }
      }, timeoutMs);

      this.pendingRequests.set(requestId, { resolve, reject, timeout });

      try {
        this.connection.send(JSON.stringify(request));
      } catch (err) {
        clearTimeout(timeout);
        this.pendingRequests.delete(requestId);
        reject(new McpUnityError(ErrorType.CONNECTION, `Send failed: ${err instanceof Error ? err.message : String(err)}`));
      }
    });
  }

  public get isConnected(): boolean { return this.connection !== null && this.connection.isConnected; }
  public get connectionState(): ConnectionState { return this.connection?.connectionState ?? ConnectionState.Disconnected; }
  public get isConnecting(): boolean { return this.connection?.isConnecting ?? false; }

  public onConnectionStateChange(callback: ConnectionStateCallback): () => void {
    this.stateListeners.add(callback);
    return () => { this.stateListeners.delete(callback); };
  }

  public forceReconnect(): void {
    if (this.connection) this.connection.forceReconnect();
    else this.logger.warn('Cannot force reconnect — not started');
  }

  public getConnectionStats() {
    const stats = this.connection?.getStats();
    return {
      state: stats?.state ?? ConnectionState.Disconnected,
      pendingRequests: this.pendingRequests.size,
      reconnectAttempt: stats?.reconnectAttempt,
      timeSinceLastPong: stats?.timeSinceLastPong
    };
  }

  private async readSettingsFile(): Promise<Record<string, unknown>> {
    try {
      const content = await fs.readFile(SETTINGS_PATH, 'utf-8');
      return JSON.parse(content);
    } catch {
      return {};
    }
  }
}
