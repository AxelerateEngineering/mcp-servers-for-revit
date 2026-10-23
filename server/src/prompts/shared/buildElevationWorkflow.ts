import { z } from "zod";
import { GetPromptResult } from "@modelcontextprotocol/sdk/types.js";
import { ELEVATION_SIDES, MAX_VIEWS_PER_CALL } from "../../utils/interiorElevations.js";

/** Rooms above this count need the user's yes before anything changes in the model. */
const CONFIRM_ROOM_COUNT = 10;

/**
 * Room ids per analyze_room_elevations call. 200 ids of up to 16 digits keep the request
 * under the tool's 4 KiB limit.
 */
const MAX_ROOM_IDS_PER_CALL = 200;

const ALL_SIDES = ELEVATION_SIDES.join(", ");
const QUOTED_SIDES = ELEVATION_SIDES.map((side) => JSON.stringify(side)).join(", ");

/** The optional `sides` argument every Interior Elevation prompt takes. */
export const sidesArgument = z
  .string()
  .optional()
  .describe(
    `Elevation Sides to create, as free text, e.g. "north, east". Defaults to all four (${ALL_SIDES}).`
  );

/** Wraps prompt text in the message list an MCP prompt returns. */
export function userMessage(text: string): GetPromptResult {
  return { messages: [{ role: "user", content: { type: "text", text } }] };
}

function sidesStep(sides?: string): string {
  const request =
    sides && sides.trim()
      ? `The user asked for these Elevation Sides: ${JSON.stringify(sides.trim())}. Turn this free text into a list of ${QUOTED_SIDES} (the values analyze_room_elevations takes in \`sides\`). If a word is not one of those four directions, ask the user what they meant before going on.`
      : `No sides were given, so use all four: ${ALL_SIDES}.`;
  return `${request} Elevation Sides are plan directions (up, right, down and left on the plan in model coordinates), not true north; say so if the user seems to expect true north.`;
}

/**
 * Builds the prompt text for the Interior Elevation workflow shared by every room scope.
 * `findRoomsStep` tells the AI client how to find the room ids; `sides` is the user's free text.
 */
export function buildElevationWorkflow(findRoomsStep: string, sides?: string): string {
  return `Create Interior Elevations in the open Revit model by following these steps in order. Use only the tools named here.

1. **Find the rooms.** ${findRoomsStep}

2. **Sides.** ${sidesStep(sides)}

3. **Confirm large jobs.** If there are more than ${CONFIRM_ROOM_COUNT} distinct rooms, tell the user how many rooms there are and the most views that could be created (number of rooms × number of sides). Stop and wait for the user to say yes before calling \`ensure_floor_plan\` or \`create_elevation_views\`.

4. **Analyse.** Call \`analyze_room_elevations\` with \`roomIds\` (the room ids) and \`sides\` (the list from step 2). It is read-only. Keep each request under the tool's 4 KiB limit: send at most ${MAX_ROOM_IDS_PER_CALL} room ids per call, and split further if the tool says the request is too large. The result is \`{ rooms: [...] }\`, one entry per room with \`roomId\`, \`number\`, \`name\`, \`levelId\`, \`hostPlanId\`, \`sides\`, \`skippedSides\`, \`warnings\` and \`error\`. A room with an \`error\` is a failure; skip it from here on.

5. **Host Plans.** Collect the distinct \`levelId\`s of the rooms whose \`hostPlanId\` is null and call \`ensure_floor_plan\` with them as \`levelIds\`. It returns \`{ levelId, planId, planName, created, error? }\` per level; use \`planId\` as the \`hostPlanId\` of every room on that level. If a level has an \`error\`, the rooms on that level fail with that reason.

6. **Create.** Call \`create_elevation_views\` in batches of at most ${MAX_VIEWS_PER_CALL} views. Map each entry of a room's \`sides\` to one view as the tool describes: \`hostPlanId\` = the room's \`hostPlanId\` (or the \`planId\` from step 5), \`roomId\` = \`room.roomId\`, \`name\` = \`side.suggestedName\`, and \`origin\`, \`viewDirection\`, \`cropWidth\`, \`cropHeight\` and \`farClip\` copied unchanged. Pass \`viewTypeName\`, \`viewTemplateName\` or \`scale\` only if the user asked for them. Each result is \`{ index, viewId, finalName, warnings, error? }\`, where \`index\` is the position within that batch, so keep track of which room and side each index belongs to. If the whole call fails because of an unknown view type or view template name, show the user the valid names the error lists and ask which to use.

7. **Timeouts.** If any tool call times out, stop the whole workflow at once. Do not retry and do not call Revit again. Tell the user the outcome of that call is unknown and should be checked in Revit, then give the report below for what is known so far.

8. **Report per room.** For each room (number and name), list:
   - views created, by their final names (\`finalName\`);
   - sides skipped, with the reason (\`skippedSides\`);
   - warnings, from the analysis and from creation (for example a view template that kept its own crop or far clip);
   - failures, with the error.
   Also mention any Host Plans that \`ensure_floor_plan\` created.`;
}
