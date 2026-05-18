import { z, ZodTypeAny } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { McpUnityClient } from '../unity/McpUnityClient.js';
import type { Logger } from '../utils/logger.js';

interface ParamDef {
  type?: string;
  description?: string;
  required?: boolean;
}

interface CustomTool {
  name: string;
  description: string;
  category: string;
  paramsSchema: Record<string, ParamDef> | null;
}

function buildShape(schema: Record<string, ParamDef>): Record<string, ZodTypeAny> {
  const shape: Record<string, ZodTypeAny> = {};
  for (const [key, def] of Object.entries(schema)) {
    let t: ZodTypeAny;
    switch (def.type) {
      case 'number':  t = z.number();  break;
      case 'boolean': t = z.boolean(); break;
      case 'array':   t = z.array(z.unknown()); break;
      default:        t = z.string();
    }
    if (def.description) t = t.describe(def.description);
    shape[key] = def.required ? t : (t as any).optional();
  }
  return shape;
}

export async function loadDynamicTools(
  server: McpServer,
  unity: McpUnityClient,
  logger: Logger,
  timeoutMs = 4000
): Promise<number> {
  let res: any;
  try {
    res = await unity.sendRequest(
      { method: 'get_registered_tools', params: {} },
      { queueIfDisconnected: false, timeout: timeoutMs }
    );
  } catch (err) {
    logger.warn(`Could not fetch custom tools from Unity: ${err instanceof Error ? err.message : String(err)}`);
    return 0;
  }

  const tools: CustomTool[] = res?.tools ?? [];
  if (tools.length === 0) {
    logger.info('No custom tools registered in Unity');
    return 0;
  }

  let registered = 0;
  for (const tool of tools) {
    try {
      const shape = tool.paramsSchema ? buildShape(tool.paramsSchema) : {};
      server.tool(
        tool.name,
        tool.description,
        shape,
        async (params) => {
          const result: any = await unity.sendRequest({ method: tool.name, params });
          const text = result?.message ?? JSON.stringify(result);
          return { content: [{ type: 'text', text }] };
        }
      );
      logger.info(`  [${tool.category}] ${tool.name}`);
      registered++;
    } catch (err) {
      logger.warn(`Failed to register dynamic tool '${tool.name}': ${err instanceof Error ? err.message : String(err)}`);
    }
  }

  logger.info(`Registered ${registered} dynamic custom tool(s) from Unity`);
  return registered;
}
