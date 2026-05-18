import fs from 'fs';
import path from 'path';

export enum LogLevel {
  DEBUG = 0,
  INFO = 1,
  WARN = 2,
  ERROR = 3
}

export class Logger {
  private readonly prefix: string;
  readonly level: LogLevel;
  private static fileLogging = process.env.LOGGING_FILE === 'true';
  private static consoleLogging = process.env.LOGGING === 'true';
  private static logFile = path.join(process.cwd(), 'scalable-mcp.log');

  constructor(name: string, level: LogLevel = LogLevel.INFO) {
    this.prefix = `[${name}]`;
    this.level = level;
  }

  debug(message: string, ...args: unknown[]): void {
    if (this.level <= LogLevel.DEBUG) this.write('DEBUG', message, args);
  }

  info(message: string, ...args: unknown[]): void {
    if (this.level <= LogLevel.INFO) this.write('INFO', message, args);
  }

  warn(message: string, ...args: unknown[]): void {
    if (this.level <= LogLevel.WARN) this.write('WARN', message, args);
  }

  error(message: string, ...args: unknown[]): void {
    if (this.level <= LogLevel.ERROR) this.write('ERROR', message, args);
  }

  private write(level: string, message: string, args: unknown[]): void {
    const line = `${new Date().toISOString()} ${level} ${this.prefix} ${message}${args.length ? ' ' + JSON.stringify(args) : ''}`;
    if (Logger.consoleLogging) process.stderr.write(line + '\n');
    if (Logger.fileLogging) {
      try { fs.appendFileSync(Logger.logFile, line + '\n'); } catch {}
    }
  }
}
