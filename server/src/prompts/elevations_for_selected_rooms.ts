import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { buildElevationWorkflow, sidesArgument, userMessage } from "./shared/buildElevationWorkflow.js";

const SELECTION_LIMIT = 1000;

const FIND_ROOMS_STEP = `Call \`get_selected_elements\` with \`limit\` ${SELECTION_LIMIT}; it returns an array of elements, each with a PascalCase \`Id\`. If it returns exactly ${SELECTION_LIMIT} elements, tell the user that only the first ${SELECTION_LIMIT} selected elements were considered. Then call \`export_room_data\` with \`includeUnplacedRooms\` and \`includeNotEnclosedRooms\` set to true. The rooms are the selected \`Id\`s that also appear in \`rooms[].id\`; ignore every other selected element. Don't filter on the element's \`Category\`, because the category name depends on the Revit language. If no selected element is a room, ask the user to select some rooms in Revit and stop.`;

export function registerElevationsForSelectedRoomsPrompt(server: McpServer) {
  server.prompt(
    "elevations_for_selected_rooms",
    "Create Interior Elevations for the Rooms currently selected in Revit.",
    { sides: sidesArgument },
    ({ sides }) => userMessage(buildElevationWorkflow(FIND_ROOMS_STEP, sides))
  );
}
