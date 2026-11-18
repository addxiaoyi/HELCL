import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { CallToolRequestSchema, ListToolsRequestSchema } from '@modelcontextprotocol/sdk/types.js';
import { MinecraftPlayer, configFromEnv } from './game.js';
import { toolList } from './tools.js';
import { ZodError } from 'zod';
// stdout belongs to MCP; library diagnostics must never corrupt its framing.
console.log = (...values) => console.error(...values);
const player = new MinecraftPlayer(configFromEnv(process.env));
const server = new Server({ name: 'hell-minecraft', version: '1.0.0' }, { capabilities: { tools: {} } });
server.setRequestHandler(ListToolsRequestSchema, async () => ({ tools: toolList() }));
server.setRequestHandler(CallToolRequestSchema, async (request) => {
    try {
        const value = await player.call(request.params.name, request.params.arguments);
        return { content: [{ type: 'text', text: JSON.stringify({ ok: true, data: value }) }] };
    }
    catch (error) {
        const message = error instanceof ZodError ? '工具参数不符合定义：' + error.issues.map(issue => (issue.path.join('.') || '参数') + ' ' + issue.message).join('; ')
            : error instanceof Error ? error.message : 'Minecraft 操作失败。';
        return { isError: true, content: [{ type: 'text', text: JSON.stringify({ ok: false, error: message }) }] };
    }
});
process.stdin.on('end', () => { player.disconnect(); void server.close().finally(() => process.exit(0)); });
for (const signal of ['SIGINT', 'SIGTERM'])
    process.on(signal, () => { player.disconnect(); process.exit(0); });
await server.connect(new StdioServerTransport());
