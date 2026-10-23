import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

const COMMAND = "analyze_room_elevations";

// The Revit plugin reads one request per 8 KiB socket read; stay well inside it.
const MAX_REQUEST_BYTES = 4096;

// SocketClient request ids are Date.now() (13 digits) plus 6 random digits.
const REQUEST_ID_PLACEHOLDER = "0".repeat(19);

function requestBytes(params: unknown): number {
  const envelope = { jsonrpc: "2.0", method: COMMAND, params, id: REQUEST_ID_PLACEHOLDER };
  return Buffer.byteLength(JSON.stringify(envelope), "utf8");
}

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
      "Limitations: Elevation Sides are plan directions (up/right/down/left in model coordinates), not true north. " +
      "Non-rectangular Rooms (such as L-shaped ones) may give views that look into an inside corner. " +
      `The serialized request must not exceed ${MAX_REQUEST_BYTES} bytes; split long roomIds lists into several calls.`,
    {
      roomIds: z
        .array(z.number().int().positive().max(Number.MAX_SAFE_INTEGER))
        .min(1)
        .describe("Element ids of the Rooms to analyse."),
      sides: z
        .array(z.enum(["North", "East", "South", "West"]))
        .optional()
        .describe("Elevation Sides to analyse. Defaults to all four."),
    },
    async (args) => {
      const params = {
        roomIds: args.roomIds,
        sides: args.sides ?? ["North", "East", "South", "West"],
      };

      const size = requestBytes(params);
      if (size > MAX_REQUEST_BYTES) {
        return {
          isError: true,
          content: [
            {
              type: "text",
              text: `Request too large (${size} bytes, limit ${MAX_REQUEST_BYTES}). Split roomIds into several smaller calls.`,
            },
          ],
        };
      }

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(COMMAND, params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        return {
          isError: true,
          content: [
            {
              type: "text",
              text: `Analyse room elevations failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
