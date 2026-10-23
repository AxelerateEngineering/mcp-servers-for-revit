import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { buildElevationWorkflow, sidesArgument, userMessage } from "./shared/buildElevationWorkflow.js";

function findRoomsStep(levelName: string): string {
  return `Call \`export_room_data\` with \`includeUnplacedRooms\` and \`includeNotEnclosedRooms\` set to true, so that such rooms reach the analysis and are reported with a reason, and keep the rooms whose \`rooms[].level\` (the level name) matches ${JSON.stringify(levelName)}. If no room matches, list the level names that appear in \`rooms[].level\` and ask the user which one they meant.`;
}

export function registerElevationsForLevelPrompt(server: McpServer) {
  server.prompt(
    "elevations_for_level",
    "Create Interior Elevations for every Room on one level.",
    {
      levelName: z.string().describe('Level name as shown in Revit, e.g. "Level 1".'),
      sides: sidesArgument,
    },
    ({ levelName, sides }) => userMessage(buildElevationWorkflow(findRoomsStep(levelName), sides))
  );
}
