# Interior Elevations via composable tools and scoped prompts

We build Interior Elevations from three low-level MCP tools (`analyze_room_elevations`, `ensure_floor_plan`, `create_elevation_views`) that scoped MCP prompts chain together, not from one all-in-one `create_room_elevations` tool. We chose this so the AI's steps stay visible and controllable, and so the same tools can be reused for other workflows. Geometry is still calculated in Revit by the analysis tool, because the AI should not do the trigonometry; the AI only chooses which rooms and sides to create and passes the analysed values on, adjusting them if it needs to.

## Considered Options

- **One all-in-one tool:** quickest path for the AI, but opaque, and its logic can't be reused.
- **Raw geometry, with the AI calculating the views:** most flexible, but unreliable, because LLMs make arithmetic mistakes.
- **One general prompt:** rejected in favour of one prompt per room scope (selected rooms, a level, room numbers), which are more predictable and share a single workflow builder.
