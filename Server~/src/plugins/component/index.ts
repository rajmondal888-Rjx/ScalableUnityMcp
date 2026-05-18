import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { McpUnityClient } from '../../unity/McpUnityClient.js';
import type { Logger } from '../../utils/logger.js';
import type { Plugin } from '../../core/types.js';

const ComponentPlugin: Plugin = {
  name: 'component',
  register(server: McpServer, unity: McpUnityClient, logger: Logger): void {
    server.tool('update_component', "Updates component fields on a GameObject or adds it to the GameObject if it does not contain the component",
      z.object({
        instanceId: z.number().optional().describe('The instance ID of the GameObject to update'),
        objectPath: z.string().optional().describe('The path of the GameObject in the hierarchy (alternative to instanceId)'),
        componentName: z.string().describe('The name of the component to update or add'),
        componentData: z.record(z.any()).optional().describe('An object containing the fields to update on the component (optional)')
      }).shape,
      async (params) => {
        logger.info('update_component', params);
        const res: any = await unity.sendRequest({ method: 'update_component', params: { instanceId: params.instanceId, objectPath: params.objectPath, componentName: params.componentName, componentData: params.componentData } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );
  }
};

export default ComponentPlugin;
