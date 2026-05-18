import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { McpUnityClient } from '../../unity/McpUnityClient.js';
import type { Logger } from '../../utils/logger.js';
import type { Plugin } from '../../core/types.js';

const GameObjectPlugin: Plugin = {
  name: 'gameobject',
  register(server: McpServer, unity: McpUnityClient, logger: Logger): void {
    server.tool('add_asset_to_scene', 'Adds an asset from the AssetDatabase to the Unity scene',
      z.object({
        assetPath: z.string().optional().describe('The path of the asset in the AssetDatabase'),
        guid: z.string().optional().describe('The GUID of the asset'),
        position: z.object({
          x: z.number().default(0), y: z.number().default(0), z: z.number().default(0)
        }).optional().describe('Position in the scene (defaults to Vector3.zero)'),
        parentPath: z.string().optional().describe('The path of the parent GameObject in the hierarchy'),
        parentId: z.number().optional().describe('The instance ID of the parent GameObject')
      }).shape,
      async (params) => {
        logger.info('add_asset_to_scene', params);
        const res: any = await unity.sendRequest({ method: 'add_asset_to_scene', params });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('get_gameobject', "Retrieves detailed information about a specific GameObject by instance ID, name, or hierarchical path (e.g., 'Parent/Child/MyObject'). Returns all component properties.",
      z.object({
        idOrName: z.string().describe("The instance ID (integer), name, or hierarchical path of the GameObject to retrieve.")
      }).shape,
      async (params) => {
        logger.info('get_gameobject', params);
        const res: any = await unity.sendRequest({ method: 'get_gameobject', params: { idOrName: params.idOrName } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('update_gameobject', "Updates properties of a GameObject. If it does not exist at the specified path, it will be created.",
      z.object({
        instanceId: z.number().optional().describe('The instance ID of the GameObject to update'),
        objectPath: z.string().optional().describe('The path of the GameObject in the hierarchy (alternative to instanceId)'),
        gameObjectData: z.object({
          name: z.string().optional().describe('New name for the GameObject'),
          tag: z.string().optional().describe('New tag for the GameObject'),
          layer: z.number().int().optional().describe('New layer for the GameObject'),
          activeSelf: z.boolean().optional().describe('Set the active state of the GameObject'),
          isStatic: z.boolean().optional().describe('Set the static state of the GameObject'),
        }).describe('An object containing the fields to update on the GameObject.')
      }).shape,
      async (params) => {
        logger.info('update_gameobject', params);
        const res: any = await unity.sendRequest({ method: 'update_gameobject', params: { instanceId: params.instanceId, objectPath: params.objectPath, gameObjectData: params.gameObjectData } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('duplicate_gameobject', "Duplicates a GameObject in the Unity scene. Can create multiple copies and optionally rename or reparent them.",
      z.object({
        instanceId: z.number().optional().describe('The instance ID of the GameObject to duplicate'),
        objectPath: z.string().optional().describe('The path of the GameObject in the hierarchy (alternative to instanceId)'),
        newName: z.string().optional().describe('New name for the duplicated GameObject(s). If count > 1, numbers will be appended.'),
        newParent: z.string().optional().describe('Path to the new parent GameObject.'),
        newParentId: z.number().optional().describe('Instance ID of the new parent GameObject (alternative to newParent path).'),
        count: z.number().int().min(1).max(100).default(1).describe('Number of copies to create. Default: 1, Max: 100'),
      }).shape,
      async (params) => {
        logger.info('duplicate_gameobject', params);
        const res: any = await unity.sendRequest({ method: 'duplicate_gameobject', params: { instanceId: params.instanceId, objectPath: params.objectPath, newName: params.newName, newParent: params.newParent, newParentId: params.newParentId, count: params.count ?? 1 } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('delete_gameobject', "Deletes a GameObject from the Unity scene. By default, also deletes all children.",
      z.object({
        instanceId: z.number().optional().describe('The instance ID of the GameObject to delete'),
        objectPath: z.string().optional().describe('The path of the GameObject in the hierarchy (alternative to instanceId)'),
        includeChildren: z.boolean().default(true).describe("If true (default), deletes all children. If false, children are moved to the deleted object's parent."),
      }).shape,
      async (params) => {
        logger.info('delete_gameobject', params);
        const res: any = await unity.sendRequest({ method: 'delete_gameobject', params: { instanceId: params.instanceId, objectPath: params.objectPath, includeChildren: params.includeChildren ?? true } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('reparent_gameobject', "Changes the parent of a GameObject. Can move to a new parent or to the root level (null parent).",
      z.object({
        instanceId: z.number().optional().describe('The instance ID of the GameObject to reparent'),
        objectPath: z.string().optional().describe('The path of the GameObject in the hierarchy (alternative to instanceId)'),
        newParent: z.string().nullable().optional().describe('Path to the new parent GameObject. Use null to move to root level.'),
        newParentId: z.number().nullable().optional().describe('Instance ID of the new parent GameObject. Use null to move to root level.'),
        worldPositionStays: z.boolean().default(true).describe('If true (default), the world position is preserved.'),
      }).shape,
      async (params) => {
        logger.info('reparent_gameobject', params);
        const res: any = await unity.sendRequest({ method: 'reparent_gameobject', params: { instanceId: params.instanceId, objectPath: params.objectPath, newParent: params.newParent, newParentId: params.newParentId, worldPositionStays: params.worldPositionStays ?? true } });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('select_gameobject', "Sets the selected GameObject in the Unity editor by path, name or instance ID",
      z.object({
        objectPath: z.string().optional().describe('The path or name of the GameObject to select (e.g. "Main Camera")'),
        objectName: z.string().optional().describe('The name of the GameObject to select'),
        instanceId: z.number().optional().describe('The instance ID of the GameObject to select')
      }).shape,
      async (params) => {
        logger.info('select_gameobject', params);
        const res: any = await unity.sendRequest({ method: 'select_gameobject', params });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );
  }
};

export default GameObjectPlugin;
