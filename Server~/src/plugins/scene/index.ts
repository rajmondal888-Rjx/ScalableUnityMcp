import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { McpUnityClient } from '../../unity/McpUnityClient.js';
import type { Logger } from '../../utils/logger.js';
import { McpUnityError, ErrorType } from '../../utils/errors.js';
import type { Plugin } from '../../core/types.js';

const ScenePlugin: Plugin = {
  name: 'scene',
  register(server: McpServer, unity: McpUnityClient, logger: Logger): void {
    server.tool('create_scene', 'Creates a new scene and saves it to the specified path',
      z.object({
        sceneName: z.string().describe("The name of the scene to create (without extension)"),
        folderPath: z.string().optional().describe("The folder path under 'Assets' to save into (default: Assets)"),
        addToBuildSettings: z.boolean().optional().describe("Whether to add the scene to Build Settings"),
        makeActive: z.boolean().optional().describe("Whether to open/make the new scene active after creating it"),
      }).shape,
      async (params) => {
        logger.info('create_scene', params);
        if (!params.sceneName) throw new McpUnityError(ErrorType.VALIDATION, "'sceneName' is required");
        const res: any = await unity.sendRequest({ method: 'create_scene', params });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('load_scene', "Loads a scene by path or name. Supports additive loading (default: false)",
      z.object({
        scenePath: z.string().optional().describe("Full asset path to the scene (e.g., 'Assets/Scenes/MyScene.unity')"),
        sceneName: z.string().optional().describe('Scene name without extension (used if scenePath not provided)'),
        folderPath: z.string().optional().describe("Optional folder scope to resolve sceneName under 'Assets'"),
        additive: z.boolean().optional().describe('Load additively if true; default false')
      }).shape,
      async (params) => {
        logger.info('load_scene', params);
        const res: any = await unity.sendRequest({ method: 'load_scene', params });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('unload_scene', "Unloads a scene by path or name (does not delete the scene asset)",
      z.object({
        scenePath: z.string().optional().describe("Full asset path to the scene (e.g., 'Assets/Scenes/MyScene.unity')"),
        sceneName: z.string().optional().describe('Scene name without extension (used if scenePath not provided)'),
        saveIfDirty: z.boolean().optional().describe('If true, saves the scene before unloading if it has unsaved changes. Default: true'),
        removeScene: z.boolean().optional().describe('If true, removes the scene from the hierarchy. Default: true')
      }).shape,
      async (params) => {
        logger.info('unload_scene', params);
        const res: any = await unity.sendRequest({ method: 'unload_scene', params });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('save_scene', "Saves the current active scene. Optionally saves to a new path (Save As)",
      z.object({
        scenePath: z.string().optional().describe("The path to save the scene to (e.g., 'Assets/Scenes/MyScene.unity'). Required if saveAs is true"),
        saveAs: z.boolean().optional().describe('If true, saves to a new path specified by scenePath. Default: false')
      }).shape,
      async (params) => {
        logger.info('save_scene', params);
        const res: any = await unity.sendRequest({ method: 'save_scene', params });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('delete_scene', "Deletes a scene by path or name and removes it from Build Settings",
      z.object({
        scenePath: z.string().optional().describe("Full asset path to the scene (e.g., 'Assets/Scenes/MyScene.unity')"),
        sceneName: z.string().optional().describe('Scene name without extension (used if scenePath not provided)'),
        folderPath: z.string().optional().describe("Optional folder scope to resolve sceneName under 'Assets'")
      }).shape,
      async (params) => {
        logger.info('delete_scene', params);
        const res: any = await unity.sendRequest({ method: 'delete_scene', params });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );

    server.tool('get_scene_info', "Gets information about the active scene and all currently loaded scenes",
      z.object({}).shape,
      async (params) => {
        logger.info('get_scene_info');
        const res: any = await unity.sendRequest({ method: 'get_scene_info', params });
        return { content: [{ type: res.type ?? 'text', text: res.message }] };
      }
    );
  }
};

export default ScenePlugin;
