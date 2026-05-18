import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { McpUnityClient } from '../../unity/McpUnityClient.js';
import type { Logger } from '../../utils/logger.js';
import type { Plugin } from '../../core/types.js';

const vec3 = z.object({ x: z.number(), y: z.number(), z: z.number() });
const goRef = {
  instanceId: z.number().optional().describe('The instance ID of the GameObject'),
  objectPath: z.string().optional().describe('The path of the GameObject in the hierarchy (alternative to instanceId)'),
};

const TransformPlugin: Plugin = {
  name: 'transform',
  register(server: McpServer, unity: McpUnityClient, logger: Logger): void {
    server.tool('move_gameobject', "Moves a GameObject to a new position. Supports world/local space and absolute/relative positioning.",
      z.object({
        ...goRef,
        position: vec3.describe('The target position'),
        space: z.enum(['world', 'local']).default('world').describe('Coordinate space: "world" or "local"'),
        relative: z.boolean().default(false).describe('If true, adds to current position instead of setting absolute position')
      }).shape,
      async (params) => {
        logger.info('move_gameobject', params);
        const res: any = await unity.sendRequest({ method: 'move_gameobject', params: { instanceId: params.instanceId, objectPath: params.objectPath, position: params.position, space: params.space, relative: params.relative } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('rotate_gameobject', "Rotates a GameObject using Euler angles. Supports world/local space and absolute/relative rotation.",
      z.object({
        ...goRef,
        rotation: vec3.describe('The rotation in Euler angles (degrees)'),
        space: z.enum(['world', 'local']).default('world').describe('Coordinate space: "world" or "local"'),
        relative: z.boolean().default(false).describe('If true, adds to current rotation instead of setting absolute rotation')
      }).shape,
      async (params) => {
        logger.info('rotate_gameobject', params);
        const res: any = await unity.sendRequest({ method: 'rotate_gameobject', params: { instanceId: params.instanceId, objectPath: params.objectPath, rotation: params.rotation, space: params.space, relative: params.relative } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('scale_gameobject', "Scales a GameObject. Supports absolute and relative (multiplicative) scaling.",
      z.object({
        ...goRef,
        scale: vec3.describe('The scale values'),
        relative: z.boolean().default(false).describe('If true, multiplies current scale instead of setting absolute scale')
      }).shape,
      async (params) => {
        logger.info('scale_gameobject', params);
        const res: any = await unity.sendRequest({ method: 'scale_gameobject', params: { instanceId: params.instanceId, objectPath: params.objectPath, scale: params.scale, relative: params.relative } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('set_transform', "Sets a GameObject's transform (position, rotation, scale) in one operation. All transform properties are optional.",
      z.object({
        ...goRef,
        position: vec3.optional().describe('The position to set'),
        rotation: vec3.optional().describe('The rotation in Euler angles (degrees)'),
        scale: vec3.optional().describe('The scale to set'),
        space: z.enum(['world', 'local']).default('world').describe('Coordinate space for position and rotation')
      }).shape,
      async (params) => {
        logger.info('set_transform', params);
        const res: any = await unity.sendRequest({ method: 'set_transform', params: { instanceId: params.instanceId, objectPath: params.objectPath, position: params.position, rotation: params.rotation, scale: params.scale, space: params.space } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );
  }
};

export default TransformPlugin;
