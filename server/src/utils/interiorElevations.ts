/**
 * Values shared by the Interior Elevation tools (analyze_room_elevations, ensure_floor_plan,
 * create_elevation_views) and the prompts that chain them.
 */

/** The four Elevation Sides: plan directions (up, right, down, left in model coordinates), not true north. */
export const ELEVATION_SIDES = ["North", "East", "South", "West"] as const;

/** Default view scale denominator for create_elevation_views: 1/4" = 1'-0". */
export const DEFAULT_ELEVATION_SCALE = 48;

/** create_elevation_views accepts at most this many views per call. */
export const MAX_VIEWS_PER_CALL = 12;

/** Advice every Interior Elevation tool gives for a timed-out call. */
export const TIMEOUT_ADVICE =
  "If the call times out, the outcome is unknown: Revit may still finish the work. Do not retry automatically; stop and tell the user.";

/** Transport limitations from spec #7 that apply to every Interior Elevation tool. */
export const TRANSPORT_LIMITATIONS =
  "Rarely, a small request can arrive split by the socket. A multi-byte character in the response " +
  "(e.g. in a non-English Room or view name) can be corrupted if it is split across socket chunks. After a timeout " +
  "the server accepts new calls while Revit may still be finishing the timed-out one.";

/** Geometry limitation of the Interior Elevation analysis, repeated where its values are used. */
export const NON_RECTANGULAR_LIMITATION =
  "Non-rectangular Rooms (such as L-shaped ones) may give views that look into an inside corner.";
