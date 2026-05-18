import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { McpUnityClient } from '../../unity/McpUnityClient.js';
import type { Logger } from '../../utils/logger.js';
import type { Plugin } from '../../core/types.js';

const EditorPlugin: Plugin = {
  name: 'editor',
  register(server: McpServer, unity: McpUnityClient, logger: Logger): void {
    server.tool('execute_menu_item', "Executes a Unity menu item by path",
      z.object({
        menuPath: z.string().describe('The path to the menu item to execute (e.g. "GameObject/Create Empty")')
      }).shape,
      async (params) => {
        logger.info('execute_menu_item', params);
        const res: any = await unity.sendRequest({ method: 'execute_menu_item', params: { menuPath: params.menuPath } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('recompile_scripts', "Recompiles all scripts in the Unity project.",
      z.object({
        returnWithLogs: z.boolean().optional().default(true).describe('Whether to return compilation logs'),
        logsLimit: z.number().int().min(0).max(1000).optional().default(100).describe('Maximum number of compilation logs to return')
      }).shape,
      async (params) => {
        logger.info('recompile_scripts', params);
        const res: any = await unity.sendRequest({ method: 'recompile_scripts', params: { returnWithLogs: params.returnWithLogs, logsLimit: params.logsLimit } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('run_tests', "Runs Unity's Test Runner tests",
      z.object({
        testMode: z.string().optional().default('EditMode').describe('The test mode to run (EditMode or PlayMode) - defaults to EditMode'),
        testFilter: z.string().optional().default('').describe('The specific test filter to run (e.g. specific test name or class name, must include namespace)'),
        returnOnlyFailures: z.boolean().optional().default(true).describe('Whether to show only failed tests in the results'),
        returnWithLogs: z.boolean().optional().default(false).describe('Whether to return the test logs in the results')
      }).shape,
      async (params) => {
        logger.info('run_tests', params);
        const res: any = await unity.sendRequest({ method: 'run_tests', params: { testMode: params.testMode, testFilter: params.testFilter, returnOnlyFailures: params.returnOnlyFailures, returnWithLogs: params.returnWithLogs } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('send_console_log', "Sends console log messages to the Unity console",
      z.object({
        message: z.string().describe('The message to display in the Unity console'),
        type: z.string().optional().describe('The type of message (info, warning, error) - defaults to info')
      }).shape,
      async (params) => {
        logger.info('send_console_log', params);
        const res: any = await unity.sendRequest({ method: 'send_console_log', params: { message: params.message, type: params.type } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('batch_execute', "Executes multiple tool operations in a single batch request. Reduces network round-trips and enables atomic operations with rollback support.",
      z.object({
        operations: z.array(z.object({
          tool: z.string().describe('The name of the tool to execute'),
          params: z.record(z.any()).optional().default({}).describe('Parameters to pass to the tool'),
          id: z.string().optional().describe('Optional identifier for this operation')
        })).min(1).max(100).describe('Array of operations to execute sequentially'),
        stopOnError: z.boolean().default(true).describe('If true, stops execution on the first error. Default: true'),
        atomic: z.boolean().default(false).describe('If true, rolls back all operations if any fails (uses Unity Undo system). Default: false')
      }).shape,
      async (params) => {
        logger.info('batch_execute', params);
        const res: any = await unity.sendRequest({ method: 'batch_execute', params: { operations: params.operations.map((op, i) => ({ tool: op.tool, params: op.params ?? {}, id: op.id ?? String(i) })), stopOnError: params.stopOnError ?? true, atomic: params.atomic ?? false } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('add_package', "Adds packages into the Unity Package Manager",
      z.object({
        source: z.string().describe('The source to use (registry, github, or disk) to add the package'),
        packageName: z.string().optional().describe('The package name to add from Unity registry (e.g. com.unity.textmeshpro)'),
        version: z.string().optional().describe('The version to use for registry packages (optional)'),
        repositoryUrl: z.string().optional().describe('The GitHub repository URL (e.g. https://github.com/username/repo.git)'),
        branch: z.string().optional().describe('The branch to use for GitHub packages (optional)'),
        path: z.string().optional().describe('The path to use (folder path for disk method or subfolder for GitHub)')
      }).shape,
      async (params) => {
        logger.info('add_package', params);
        const res: any = await unity.sendRequest({ method: 'add_package', params });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('get_console_logs', "Retrieves logs from the Unity console with pagination support to avoid token limits",
      z.object({
        logType: z.enum(['info', 'warning', 'error']).optional().describe('The type of logs to retrieve — defaults to all logs if not specified'),
        offset: z.number().int().min(0).optional().describe('Starting index for pagination (0-based, defaults to 0)'),
        limit: z.number().int().min(1).max(500).optional().describe('Maximum number of logs to return (defaults to 50, max 500)'),
        includeStackTrace: z.boolean().optional().describe('Whether to include stack trace. Set to false to save 80-90% tokens unless debugging.')
      }).shape,
      async (params) => {
        logger.info('get_console_logs', params);
        const res: any = await unity.sendRequest({ method: 'get_console_logs', params: { logType: params.logType, offset: params.offset, limit: params.limit, includeStackTrace: params.includeStackTrace } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );
  }
};

export default EditorPlugin;
