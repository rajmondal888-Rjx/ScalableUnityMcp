import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { McpUnityClient } from '../unity/McpUnityClient.js';
import type { Logger } from '../utils/logger.js';

/**
 * A plugin encapsulates one tool/resource category.
 * Drop a new file in src/plugins/<category>/index.ts that exports a
 * default object satisfying this interface — PluginLoader picks it up.
 *
 * To add a new category:
 *   1. Create src/plugins/custom/<name>/index.ts implementing this interface
 *   2. Import it and add it to ALL_PLUGINS in src/core/PluginLoader.ts
 *   3. Create the matching C# handler in unity-plugin/Editor/Custom/<Name>Handler.cs
 *   4. Register it in unity-plugin/Editor/Core/HandlerRegistry.cs
 */
export interface Plugin {
  readonly name: string;
  register(server: McpServer, unity: McpUnityClient, logger: Logger): void;
}
