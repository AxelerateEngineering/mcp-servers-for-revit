# Revit MCP

An MCP server and Revit add-in that let an AI application read and change an open Revit model through tools.

## Language

### Room elevations

**Interior Elevation**:
An elevation view that looks from inside a Room toward one of its Elevation Sides.
_Avoid_: Room elevation, wall elevation, room view

**Elevation Side**:
One of four directions around a Room, North, East, South or West, meaning up, right, down and left on the plan in model coordinates. It does not mean true north.
_Avoid_: Left/Top/Right/Bottom, face side, direction index

**Side Face**:
A vertical boundary face of a Room. It belongs to whichever Elevation Side its outward direction falls in.
_Avoid_: Wall face, room face

**Host Plan**:
The floor plan on a Room's level where the Interior Elevation's marker is placed.
_Avoid_: Associated plan, parent view
