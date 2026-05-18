import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { McpUnityClient } from '../../unity/McpUnityClient.js';
import type { Logger } from '../../utils/logger.js';
import type { Plugin } from '../../core/types.js';

const ResourcesPlugin: Plugin = {
  name: 'resources',
  register(server: McpServer, unity: McpUnityClient, logger: Logger): void {
    server.resource('get_menu_items', 'unity://menu-items', { mimeType: 'application/json', description: 'List of available menu items in Unity to execute' },
      async () => {
        logger.info('resource: get_menu_items');
        const res: any = await unity.sendRequest({ method: 'get_menu_items', params: {} });
        return { contents: [{ uri: 'unity://menu-items', mimeType: 'application/json', text: res.message ?? JSON.stringify(res) }] };
      }
    );

    server.resource('get_scenes_hierarchy', 'unity://scenes_hierarchy', { mimeType: 'application/json', description: 'Retrieve all GameObjects in the Unity loaded scenes with their active state' },
      async () => {
        logger.info('resource: get_scenes_hierarchy');
        const res: any = await unity.sendRequest({ method: 'get_scenes_hierarchy', params: {} });
        return { contents: [{ uri: 'unity://scenes_hierarchy', mimeType: 'application/json', text: res.message ?? JSON.stringify(res) }] };
      }
    );

    server.resource('get_packages', 'unity://packages', { mimeType: 'application/json', description: 'Retrieve all packages from the Unity Package Manager' },
      async () => {
        logger.info('resource: get_packages');
        const res: any = await unity.sendRequest({ method: 'get_packages', params: {} });
        return { contents: [{ uri: 'unity://packages', mimeType: 'application/json', text: res.message ?? JSON.stringify(res) }] };
      }
    );

    server.resource('get_assets', 'unity://assets', { mimeType: 'application/json', description: 'Retrieve assets from the Unity Asset Database' },
      async () => {
        logger.info('resource: get_assets');
        const res: any = await unity.sendRequest({ method: 'get_assets', params: {} });
        return { contents: [{ uri: 'unity://assets', mimeType: 'application/json', text: res.message ?? JSON.stringify(res) }] };
      }
    );

    // Tests — three URIs for the same resource handler
    const testsMeta = { mimeType: 'application/json', description: "Retrieve tests from Unity's Test Runner" };
    for (const [name, uri, mode] of [
      ['get_tests_editmode', 'unity://tests/EditMode', 'EditMode'],
      ['get_tests_playmode', 'unity://tests/PlayMode', 'PlayMode'],
      ['get_tests_all',     'unity://tests/',          ''],
    ] as const) {
      const testMode = mode;
      const testUri = uri;
      server.resource(name, testUri, testsMeta,
        async () => {
          logger.info(`resource: ${name}`);
          const res: any = await unity.sendRequest({ method: 'get_tests', params: { testMode } });
          return { contents: [{ uri: testUri, mimeType: 'application/json', text: res.message ?? JSON.stringify(res) }] };
        }
      );
    }

    // Console logs — four URIs
    const logsMeta = { mimeType: 'application/json', description: 'Retrieve Unity console logs by type with pagination support' };
    const logConfigs = [
      { name: 'get_logs_all',     uri: 'unity://logs/',        logType: undefined, offset: 0, limit: 50,  includeStackTrace: true },
      { name: 'get_logs_error',   uri: 'unity://logs/error',   logType: 'error',   offset: 0, limit: 20,  includeStackTrace: true },
      { name: 'get_logs_warning', uri: 'unity://logs/warning', logType: 'warning', offset: 0, limit: 30,  includeStackTrace: true },
      { name: 'get_logs_info',    uri: 'unity://logs/info',    logType: 'info',    offset: 0, limit: 25,  includeStackTrace: false },
    ] as const;

    for (const cfg of logConfigs) {
      const { name, uri: logUri, logType, offset, limit, includeStackTrace } = cfg;
      server.resource(name, logUri, logsMeta,
        async () => {
          logger.info(`resource: ${name}`);
          const res: any = await unity.sendRequest({ method: 'get_console_logs', params: { logType, offset, limit, includeStackTrace } });
          return { contents: [{ uri: logUri, mimeType: 'application/json', text: res.message ?? JSON.stringify(res) }] };
        }
      );
    }
  }
};

export default ResourcesPlugin;
