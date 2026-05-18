import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { McpUnityClient } from '../../unity/McpUnityClient.js';
import type { Logger } from '../../utils/logger.js';
import type { Plugin } from '../../core/types.js';

const MaterialPlugin: Plugin = {
  name: 'material',
  register(server: McpServer, unity: McpUnityClient, logger: Logger): void {
    server.tool('create_material', 'Creates a new material with the specified shader and saves it to the project.',
      z.object({
        name: z.string().describe('The name of the material'),
        shader: z.string().optional().describe('The shader name. Auto-detects render pipeline if not specified'),
        savePath: z.string().describe('The asset path to save the material (e.g., "Assets/Materials/MyMaterial.mat")'),
        color: z.object({
          r: z.number().min(0).max(1),
          g: z.number().min(0).max(1),
          b: z.number().min(0).max(1),
          a: z.number().min(0).max(1).optional().default(1)
        }).optional().describe('The base color of the material (0-1 per channel)'),
        properties: z.record(z.any()).optional().describe('Optional initial property values as key-value pairs')
      }).shape,
      async (params) => {
        logger.info('create_material', params);
        const res: any = await unity.sendRequest({ method: 'create_material', params: { name: params.name, shader: params.shader, savePath: params.savePath, color: params.color, properties: params.properties } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('assign_material', "Assigns a material to a GameObject's Renderer component at a specific material slot",
      z.object({
        instanceId: z.number().optional().describe('The instance ID of the GameObject'),
        objectPath: z.string().optional().describe('The path of the GameObject in the hierarchy (alternative to instanceId)'),
        materialPath: z.string().describe('The asset path to the material (e.g., "Assets/Materials/MyMaterial.mat")'),
        slot: z.number().int().min(0).optional().default(0).describe('The material slot index (default: 0)')
      }).shape,
      async (params) => {
        logger.info('assign_material', params);
        const res: any = await unity.sendRequest({ method: 'assign_material', params: { instanceId: params.instanceId, objectPath: params.objectPath, materialPath: params.materialPath, slot: params.slot ?? 0 } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('modify_material', "Modifies properties of an existing material. Supports colors, floats, and textures.",
      z.object({
        materialPath: z.string().describe('The asset path to the material (e.g., "Assets/Materials/MyMaterial.mat")'),
        properties: z.record(z.any()).describe('Property name to value mapping. Colors: {r,g,b,a}, Floats: number, Textures: asset path string')
      }).shape,
      async (params) => {
        logger.info('modify_material', params);
        const res: any = await unity.sendRequest({ method: 'modify_material', params: { materialPath: params.materialPath, properties: params.properties } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('get_material_info', "Gets detailed information about a material including its shader and all properties with current values",
      z.object({
        materialPath: z.string().describe('The asset path to the material (e.g., "Assets/Materials/MyMaterial.mat")')
      }).shape,
      async (params) => {
        logger.info('get_material_info', params);
        const res: any = await unity.sendRequest({ method: 'get_material_info', params: { materialPath: params.materialPath } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );
  }
};

export default MaterialPlugin;
