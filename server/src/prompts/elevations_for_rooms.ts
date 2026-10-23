import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { buildElevationWorkflow, sidesArgument, userMessage } from "./shared/buildElevationWorkflow.js";

function findRoomsStep(roomNumbers: string): string {
  return `The user asked for these room numbers: ${JSON.stringify(roomNumbers)} (a comma-separated list). Call \`export_room_data\` with \`includeUnplacedRooms\` and \`includeNotEnclosedRooms\` set to true, and match each number against \`rooms[].number\`. If a number matches several rooms, list each candidate's \`id\`, \`name\`, \`level\` and \`phase\` and ask the user which to use; don't pick the first one and don't take them all. Tell the user which numbers weren't found.`;
}

export function registerElevationsForRoomsPrompt(server: McpServer) {
  server.prompt(
    "elevations_for_rooms",
    "Create Interior Elevations for the Rooms with the given room numbers.",
    {
      roomNumbers: z.string().describe('Room numbers, comma-separated, e.g. "101, 102".'),
      sides: sidesArgument,
    },
    ({ roomNumbers, sides }) =>
      userMessage(buildElevationWorkflow(findRoomsStep(roomNumbers), sides))
  );
}
