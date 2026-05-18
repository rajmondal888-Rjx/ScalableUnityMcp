import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { Logger, LogLevel } from './utils/logger.js';
import { McpUnityClient } from './unity/McpUnityClient.js';
import { loadPlugins } from './core/PluginLoader.js';
import { loadDynamicTools } from './core/DynamicToolLoader.js';

const logger = new Logger('ScalableMCP', LogLevel.INFO);

async function main() {
  const server = new McpServer({
    name: 'Scalable Unity MCP Server',
    version: '1.0.0',
  });

  const unity = new McpUnityClient(new Logger('Unity', LogLevel.INFO));

  // Register static baseline plugins (scene, gameobject, transform, etc.)
  loadPlugins(server, unity, logger);

  // Pre-connect to Unity and register any custom tools before the MCP transport starts.
  // This ensures Claude sees the full tool list on its first tools/list request.
  logger.info('Pre-connecting to Unity to discover custom tools...');
  try {
    await unity.start('Scalable MCP Server');
    if (unity.isConnected) {
      await loadDynamicTools(server, unity, logger);
    } else {
      logger.warn('Unity not reachable — custom tools will not appear until Claude Code is restarted with Unity open');
    }
  } catch (err) {
    logger.warn(`Unity pre-connect failed: ${err instanceof Error ? err.message : String(err)}`);
    logger.warn('Baseline tools are still available. Restart Claude Code with Unity open to load custom tools.');
  }

  // Start the MCP stdio transport — Claude Code now sees all registered tools
  const transport = new StdioServerTransport();
  await server.connect(transport);
  logger.info('MCP server ready');

  const shutdown = async (signal: string) => {
    logger.info(`Received ${signal}, shutting down`);
    await unity.stop();
    await server.close();
    process.exit(0);
  };

  process.on('SIGINT',  () => shutdown('SIGINT'));
  process.on('SIGTERM', () => shutdown('SIGTERM'));
  process.on('SIGHUP',  () => shutdown('SIGHUP'));

  process.stdin.on('close', () => shutdown('stdin close'));

  process.on('uncaughtException', (err: NodeJS.ErrnoException) => {
    if (err.code === 'EPIPE' || err.code === 'EOF') return;
    logger.error(`Uncaught exception: ${err.message}`);
    process.exit(1);
  });

  process.on('unhandledRejection', (reason) => {
    logger.error(`Unhandled rejection: ${reason instanceof Error ? reason.message : String(reason)}`);
  });
}

main().catch((err) => {
  process.stderr.write(`Fatal: ${err instanceof Error ? err.message : String(err)}\n`);
  process.exit(1);
});
