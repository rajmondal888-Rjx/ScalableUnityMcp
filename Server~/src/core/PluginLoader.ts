import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { Plugin } from './types.js';
import type { McpUnityClient } from '../unity/McpUnityClient.js';
import { Logger, LogLevel } from '../utils/logger.js';

// ---- BASELINE PLUGINS ----
import ScenePlugin      from '../plugins/scene/index.js';
import GameObjectPlugin from '../plugins/gameobject/index.js';
import TransformPlugin  from '../plugins/transform/index.js';
import ComponentPlugin  from '../plugins/component/index.js';
import MaterialPlugin   from '../plugins/material/index.js';
import PrefabPlugin     from '../plugins/prefab/index.js';
import EditorPlugin     from '../plugins/editor/index.js';
import ResourcesPlugin  from '../plugins/resources/index.js';

// ---- CUSTOM PLUGINS — add new imports here ----
// import MyCustomPlugin from '../plugins/custom/myTool/index.js';

const ALL_PLUGINS: Plugin[] = [
  ScenePlugin,
  GameObjectPlugin,
  TransformPlugin,
  ComponentPlugin,
  MaterialPlugin,
  PrefabPlugin,
  EditorPlugin,
  ResourcesPlugin,
  // Add custom plugins below:
  // MyCustomPlugin,
];

export function loadPlugins(
  server: McpServer,
  unity: McpUnityClient,
  rootLogger: Logger
): void {
  for (const plugin of ALL_PLUGINS) {
    const logger = new Logger(plugin.name, rootLogger.level);
    logger.info(`Registering plugin: ${plugin.name}`);
    plugin.register(server, unity, logger);
  }
  rootLogger.info(`Loaded ${ALL_PLUGINS.length} plugins`);
}
