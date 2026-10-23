import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { requestSizeError } from "../utils/requestSize.js";

const COMMAND = "ensure_floor_plan";

export function registerEnsureFloorPlanTool(server: McpServer) {
  server.tool(
    COMMAND,
    `Make sure each level has a Host Plan: the floor plan that hosts Interior Elevation markers. Call it for the levels of Rooms whose hostPlanId from analyze_room_elevations is null.

For each level it reuses the first floor plan (not a template) on that level, or else creates one named "{Level name} - Elevation Host" (adding " (2)" when that name is taken). Each new plan is created in its own transaction; a level that fails gets an error and does not stop the others.

Returns, per level in input order: { levelId, planId, planName, created, error? }. Use planId as hostPlanId in create_elevation_views.

The request must be at most 4 KiB. If the call times out, the outcome is unknown (Revit may still create the plans): do not retry automatically; stop and tell the user.`,
    {
      levelIds: z
        .array(z.number().int())
        .min(1)
        .describe("Level element ids, e.g. levelId from analyze_room_elevations."),
    },
    async (args) => {
      const params = { levelIds: args.levelIds };
      const sizeError = requestSizeError(COMMAND, params);
      if (sizeError) {
        return { isError: true, content: [{ type: "text", text: sizeError }] };
      }

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(COMMAND, params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          isError: true,
          content: [
            {
              type: "text",
              text: `ensure_floor_plan failed: ${error instanceof Error ? error.message : String(error)}`,
            },
          ],
        };
      }
    }
  );
}
