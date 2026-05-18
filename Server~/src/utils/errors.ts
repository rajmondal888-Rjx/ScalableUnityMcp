export enum ErrorType {
  CONNECTION = 'connection_error',
  TOOL_EXECUTION = 'tool_execution_error',
  RESOURCE_FETCH = 'resource_fetch_error',
  VALIDATION = 'validation_error',
  INTERNAL = 'internal_error',
  TIMEOUT = 'timeout_error'
}

export class McpUnityError extends Error {
  constructor(
    public readonly type: ErrorType,
    message: string,
    public readonly details?: unknown
  ) {
    super(message);
    this.name = 'McpUnityError';
  }
}
