// Session management tools
import { deleteSandbox, getSandboxStatus } from "./sandbox-client.js";

export function registerSessionTools(server, z) {
    server.tool("destroy_session", "Destroy a sandbox session and free resources. Irreversible.", {
        session_id: z.string().describe("Session ID to destroy"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id }) => {
        await deleteSandbox(session_id);
        return { content: [{ type: "text", text: JSON.stringify({ success: true, session_id, destroyed: true }) }] };
    });

    server.tool("get_session_status", "Check sandbox session state.", {
        session_id: z.string().describe("Session ID"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id }) => {
        const result = await getSandboxStatus(session_id);
        const props = result.properties || {};
        return { content: [{ type: "text", text: JSON.stringify({ session_id, state: props.state || "Unknown" }) }] };
    });
}
