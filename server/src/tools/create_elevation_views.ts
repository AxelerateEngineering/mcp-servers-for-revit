import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { requestSizeError } from "../utils/requestSize.js";
import {
  DEFAULT_ELEVATION_SCALE,
  MAX_VIEWS_PER_CALL as MAX_VIEWS,
  NON_RECTANGULAR_LIMITATION,
  TIMEOUT_ADVICE,
  TRANSPORT_LIMITATIONS,
} from "../utils/interiorElevations.js";

const COMMAND = "create_elevation_views";

// Only the shape is checked here. Value rules (finite numbers, non-zero direction, positive
// sizes, positive ids) are checked per item in Revit, so one bad item does not reject the batch.
const viewSchema = z.object({
  hostPlanId: z
    .number()
    .int()
    .describe("Host Plan id: room.hostPlanId from analyze_room_elevations, or planId from ensure_floor_plan when that was null."),
  origin: z
    .array(z.number())
    .length(3)
    .describe("[x, y, z] in feet, internal model coordinates: side.origin, copied unchanged."),
  viewDirection: z
    .array(z.number())
    .length(2)
    .describe("[dx, dy]: the horizontal direction the view looks in: side.viewDirection, copied unchanged."),
  cropWidth: z.number().describe("Crop width in feet: side.cropWidth."),
  cropHeight: z.number().describe("Crop height in feet, from origin.z up: side.cropHeight."),
  farClip: z.number().describe("Far clip offset in feet: side.farClip. Increase it to see further."),
  name: z.string().describe("View name: side.suggestedName. ' (2)', ' (3)'… is added if the name is taken."),
  roomId: z
    .number()
    .int()
    .optional()
    .describe("room.roomId; the view takes this Room's phase."),
});

export function registerCreateElevationViewsTool(server: McpServer) {
  server.tool(
    COMMAND,
    `Create Interior Elevations in Revit, 1 to ${MAX_VIEWS} per call, normally from the sides returned by analyze_room_elevations.

Mapping per side: hostPlanId = room.hostPlanId (or planId from ensure_floor_plan when it was null), roomId = room.roomId, name = side.suggestedName; origin, viewDirection, cropWidth, cropHeight and farClip are copied unchanged. All values are in feet, in internal model coordinates.

For each item, in its own transaction, the tool places an elevation marker on the Host Plan, applies the view template (if given), turns the view to look along viewDirection, sets far clipping ("clip without line") to farClip, sets a rectangular crop (cropWidth wide, centred on origin, from origin.z up by cropHeight), copies the Room's phase and names the view. If the template controls the crop or far clip, the template's setting is kept and a warning is returned.

Returns, per item in input order: { index, viewId, finalName, warnings, error? }. A failed item has viewId and finalName null and leaves no view or marker behind; the other items are still created. An unknown viewTypeName or viewTemplateName fails the whole call before anything is created, and the error lists the valid names.

Limits: at most ${MAX_VIEWS} views and 4 KiB per request; split larger sets into several calls. ${TIMEOUT_ADVICE} Calling again with the same items creates a fresh set of views named "… (2)".

Known limitations: ${NON_RECTANGULAR_LIMITATION} ${TRANSPORT_LIMITATIONS}`,
    {
      views: z
        .array(viewSchema)
        .min(1)
        .max(MAX_VIEWS)
        .describe(`1 to ${MAX_VIEWS} Interior Elevations to create.`),
      viewTypeName: z
        .string()
        .optional()
        .describe("Elevation view type name. Defaults to the first Elevation type in the model."),
      viewTemplateName: z
        .string()
        .optional()
        .describe("Elevation view template name. Defaults to no template."),
      scale: z
        .number()
        .int()
        .min(1)
        .max(24000)
        .optional()
        .describe(`View scale denominator, e.g. 48 for 1/4" = 1'-0". Defaults to ${DEFAULT_ELEVATION_SCALE}.`),
    },
    async (args) => {
      const params = {
        views: args.views,
        viewTypeName: args.viewTypeName,
        viewTemplateName: args.viewTemplateName,
        scale: args.scale ?? DEFAULT_ELEVATION_SCALE,
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
