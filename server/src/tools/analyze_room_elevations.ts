import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { requestSizeError } from "../utils/requestSize.js";
import {
  ELEVATION_SIDES,
  NON_RECTANGULAR_LIMITATION,
  TIMEOUT_ADVICE,
  TRANSPORT_LIMITATIONS,
} from "../utils/interiorElevations.js";

const COMMAND = "analyze_room_elevations";

export function registerAnalyzeRoomElevationsTool(server: McpServer) {
  server.tool(
    COMMAND,
    "Analyse Rooms for Interior Elevations. Read-only. For each Room and each requested Elevation Side " +
      "(North, East, South, West) it returns ready-to-use values: origin [x,y,z] (room centre at the room's floor), " +
      "viewDirection [dx,dy], cropWidth, cropHeight, farClip and a suggestedName, plus levelId and hostPlanId " +
      "(null when the level has no floor plan; call ensure_floor_plan then). Pass these values to " +
      "create_elevation_views unchanged, or adjust them. All lengths and coordinates are in feet, in internal " +
      "model coordinates. Sides with no usable wall are listed in skippedSides with a reason; Rooms that can't be " +
      "analysed (unplaced, not enclosed, bad id) get an error while the others still succeed. " +
      "The request must be at most 4 KiB; split long roomIds lists into several calls. " +
      `${TIMEOUT_ADVICE} ` +
      "Elevation Sides are plan directions (up/right/down/left in model coordinates), not true north. " +
      `Known limitations: ${NON_RECTANGULAR_LIMITATION} ${TRANSPORT_LIMITATIONS}`,
    {
      roomIds: z
        .array(z.number().int().positive().max(Number.MAX_SAFE_INTEGER))
        .min(1)
        .describe("Element ids of the Rooms to analyse."),
      sides: z
        .array(z.enum(ELEVATION_SIDES))
        .optional()
        .describe("Elevation Sides to analyse. Defaults to all four."),
    },
    async (args) => {
      const params = {
        roomIds: args.roomIds,
        sides: args.sides ?? [...ELEVATION_SIDES],
      };
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
              text: `${COMMAND} failed: ${error instanceof Error ? error.message : String(error)}`,
            },
          ],
        };
      }
    }
  );
}
