# Handoff — CABIN — Free Build v2

**Date:** 2026-10-29
**From:** Sam (design, after playtesting the layout-tool build slice) · **For:** Claude Code
**Supersedes:** Foundation v1 §11 (M5 building sandbox). Everything else in v1 stands.

## Verdict on the layout-tool slice
"Not very good or what I want." Tools that generate whole floors/walls hold your hand. Replace them with a free,
physical building system: real pieces, carried, placed anywhere, cut, nailed.

## The design (Sam's words, condensed)
- **Materials, to start:** 2×4s and 4×8 plywood sheets, plus **4×4s**. 2×4 and 4×4 stock from **8 ft to 20 ft**.
  Stock lives on **tables/racks** by length; take a piece from them.
- **Holding:** an equipped piece floats in your view where you look. **Scroll** = distance from your head.
  **Hold RMB + move mouse** = rotate the piece instead of the camera, to line things up.
- **Place anywhere:** flat on the ground, leaning, in the air, against other pieces.
- **E = freeze** the held piece where it is. **Only one frozen piece at a time** — freezing another unfreezes the
  previous one, and it falls unless it was nailed.
- **Nail gun:** shoot nails through one piece into another; nailed pieces hold together as one structure.
- **Posts:** only a **4×4 placed vertically into the ground** can start a structure (anchor). 2×4s can't be posts.
  After that, 4×4s can also be used for framing like 2×4s.
- **Cutting:** a **cutting table** — put any piece on it and cut it however you like. Placed pieces can still be cut
  too (e.g. cut posts down to height).
- **Exact colliders** on every piece, kept after attaching — later, structures will break and fall apart
  (enemies, demolition).

## The intended flow
Plant four 4×4 posts in a square → cut them to height → take a 2×4, float it so each end touches a post → E to
freeze → nail gun it to both posts → repeat around → fill in the floor frame with more boards → lay plywood on
top, nail it → floor done → frame walls and roof however you like.

## Change after playtest 5 (2026-10-29)
Freezing is gone. **LMB clamps** the held piece to whatever it's pressed against (needs contact); clamps appear
at each contact; **E on a clamp** removes them. Still one clamped piece at a time. Nails pull small gaps flush.

## Implementation notes (Claude Code)
- Every piece is a convex polyhedron (box to start). A planar cut of a convex shape gives two convex shapes, so
  any sequence of straight cuts keeps exact convex `MeshCollider`s.
- Nails are graph edges between pieces. Connected pieces share one `Rigidbody` (compound collider). A structure is
  kinematic while it contains a planted post or the frozen piece; otherwise it's dynamic and falls. Removing nails
  or cutting later re-splits structures, which is the hook for breakage/demolition.
- The old layout tools (FloorLayout, WallLayout, roof, sheathing generators, BuildGraph) are removed; they're in
  git history (`m5-build-slice` commits before this handoff).
