import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';

export interface QueuedCommand {
  id: string;
  request: { id?: string; method: string; params: unknown };
  resolve: (value: unknown) => void;
  reject: (reason: unknown) => void;
  queuedAt: number;
  timeout?: number;
}

export interface CommandQueueConfig {
  maxSize?: number;
  defaultTimeout?: number;
  cleanupInterval?: number;
}

export interface CommandQueueStats {
  size: number;
  maxSize: number;
  droppedCount: number;
  expiredCount: number;
  replayedCount: number;
}

export interface EnqueueResult {
  success: boolean;
  position?: number;
  reason?: string;
}

const DEFAULT_CONFIG = { maxSize: 100, defaultTimeout: 60000, cleanupInterval: 5000 };

export class CommandQueue {
  private queue: QueuedCommand[] = [];
  private config: Required<CommandQueueConfig>;
  private cleanupTimer: NodeJS.Timeout | null = null;
  private logger: Logger;
  private droppedCount = 0;
  private expiredCount = 0;
  private replayedCount = 0;

  constructor(logger: Logger, config: CommandQueueConfig = {}) {
    this.logger = logger;
    this.config = { ...DEFAULT_CONFIG, ...config };
    this.startCleanupTimer();
  }

  public enqueue(command: Omit<QueuedCommand, 'queuedAt'>): EnqueueResult {
    if (this.queue.length >= this.config.maxSize) {
      this.droppedCount++;
      this.logger.warn(`Queue full (${this.config.maxSize}), dropping: ${command.request.method}`);
      command.reject(new McpUnityError(ErrorType.CONNECTION, `Command queue full (${this.config.maxSize}). Try again later.`));
      return { success: false, reason: `Queue is full (max: ${this.config.maxSize})` };
    }
    const queuedCommand: QueuedCommand = { ...command, queuedAt: Date.now(), timeout: command.timeout ?? this.config.defaultTimeout };
    this.queue.push(queuedCommand);
    return { success: true, position: this.queue.length };
  }

  public get size(): number { return this.queue.length; }
  public get isEmpty(): boolean { return this.queue.length === 0; }
  public get isFull(): boolean { return this.queue.length >= this.config.maxSize; }

  public drain(): QueuedCommand[] {
    this.cleanupExpired();
    const commands = [...this.queue];
    this.queue = [];
    if (commands.length > 0) this.logger.info(`Draining ${commands.length} commands for replay`);
    return commands;
  }

  public peek(): QueuedCommand | undefined { return this.queue[0]; }

  public clear(reason = 'Queue cleared'): void {
    const count = this.queue.length;
    for (const cmd of this.queue) cmd.reject(new McpUnityError(ErrorType.CONNECTION, reason));
    this.queue = [];
    if (count > 0) this.logger.info(`Cleared ${count} commands: ${reason}`);
  }

  public cleanupExpired(): number {
    const now = Date.now();
    const initial = this.queue.length;
    this.queue = this.queue.filter(cmd => {
      const timeout = cmd.timeout ?? this.config.defaultTimeout;
      if ((now - cmd.queuedAt) > timeout) {
        this.expiredCount++;
        cmd.reject(new McpUnityError(ErrorType.TIMEOUT, `Command expired after ${timeout}ms in queue`));
        return false;
      }
      return true;
    });
    const expired = initial - this.queue.length;
    if (expired > 0) this.logger.info(`Cleaned up ${expired} expired commands`);
    return expired;
  }

  private startCleanupTimer(): void {
    if (this.cleanupTimer) clearInterval(this.cleanupTimer);
    this.cleanupTimer = setInterval(() => this.cleanupExpired(), this.config.cleanupInterval);
    this.cleanupTimer.unref();
  }

  public stopCleanupTimer(): void {
    if (this.cleanupTimer) { clearInterval(this.cleanupTimer); this.cleanupTimer = null; }
  }

  public recordReplaySuccess(): void { this.replayedCount++; }

  public getStats(): CommandQueueStats {
    return { size: this.queue.length, maxSize: this.config.maxSize, droppedCount: this.droppedCount, expiredCount: this.expiredCount, replayedCount: this.replayedCount };
  }

  public resetStats(): void { this.droppedCount = 0; this.expiredCount = 0; this.replayedCount = 0; }

  public updateConfig(config: Partial<CommandQueueConfig>): void {
    this.config = { ...this.config, ...config };
    if (config.cleanupInterval !== undefined) this.startCleanupTimer();
  }

  public dispose(): void { this.stopCleanupTimer(); this.clear('Command queue disposed'); }
}
