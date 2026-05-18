import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { McpUnityClient } from '../../unity/McpUnityClient.js';
import type { Logger } from '../../utils/logger.js';
import type { Plugin } from '../../core/types.js';

const PrefabPlugin: Plugin = {
  name: 'prefab',
  register(server: McpServer, unity: McpUnityClient, logger: Logger): void {
    server.tool('create_prefab', "Creates a prefab with optional MonoBehaviour script and serialized field values",
      z.object({
        prefabName: z.string().describe('The name of the prefab to create'),
        componentName: z.string().optional().describe('The name of the MonoBehaviour Component to add to the prefab (optional)'),
        fieldValues: z.record(z.any()).optional().describe('Optional JSON object of serialized field values to apply to the prefab')
      }).shape,
      async (params) => {
        logger.info('create_prefab', params);
        const res: any = await unity.sendRequest({ method: 'create_prefab', params });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );
  }
};

export default PrefabPlugin;
