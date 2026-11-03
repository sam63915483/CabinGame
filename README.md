# CABIN (working title)

A first-person survival and building game set in the forests of Nova Scotia. Society has fallen apart after an AI-driven blackout. You drive out to a piece of land in the woods, clear it, and build a cabin from real timber framing, one stud at a time.

By **Sam McNeil** (BIGBOI STUDIOS). Built in Unity 6.3 LTS with URP.

> **Status:** early development. Phase 1, the creative-mode building sandbox, is being built and playtested milestone by milestone. Nothing here is a finished game yet.

---

## What the game is going to be

The full game, built in phases after the sandbox proves the core is fun:

- **Get out.** Spawn in your pickup truck on the highway at the edge of the map and drive into the woods looking for a place to take over.
- **Clear a site.** Fell trees with an axe, buck them, pull stumps, clear brush.
- **Build for real.** A realistic stick-framing system: individual posts, plates, studs, joists, rafters and sheathing, at real lumber dimensions, clamped, nailed and cut by hand. Standing inside a wall you framed yourself should feel good.
- **Survive.** Fishing, filtered water, staying warm.
- **Hold on.** After the first week, things start showing up. Defend the place you built.
- **Find a way out, or don't.** Radio transmissions, an island safe-haven ending around day 100, or just see how long you last.

## What Phase 1 is

Phase 1 answers one question: **is clearing a site and framing a cabin fun on its own?** It is a creative-mode sandbox with unlimited free parts and none of the survival systems:

1. A big forest map: hills, cliffs, lakes, rivers, waterfalls.
2. Forests and meadows to walk through.
3. A first-person lumberjack (and later a drivable truck).
4. Cutting trees and clearing land.
5. The building sandbox.

## What's built so far

| Milestone | What | State |
|---|---|---|
| M0 | Project setup: Unity 6.3 LTS, URP Forward+, GPU Resident Drawer, asmdefs, tests | Done |
| M1 | **The map.** 4 × 4 km of Nova Scotia's real coastline and broad elevation (open AWS terrain tiles), relief by region, simulated river erosion, glacial lake basins, lakes and rivers from real drainage, waterfalls, 2 m fine terrain. Everything generated from a seeded `MapRecipe` | Built, in playtesting |
| M2 | **Vegetation.** ~240k trees (conifers and broadleaf, by forest patches, clearings and slope), grass streamed in chunks with per-tuft tinting and edge fade, far-tree impostors | Built, in playtesting |
| M3 | **The player.** A full-body first-person lumberjack: real finger bones, the body's own arms gripping tools by IK, procedural legs that plant on slopes, the camera at the eyes. *Truck not started* | Body built, in playtesting |
| M4 | **Cut and clear.** Axe chopping with notches that deepen, chips, and trees that fall away from you. *Bucking, stumps and brush not yet* | Axe built |
| M5 | **Free build.** Carry, clamp, nail gun, saw, deck blocks, posts, plywood, pilot mode for lining up pieces, spray paint | Built, in playtesting |

**The look:** a custom eight-layer terrain shader (height-blended edges, triplanar rock, slope-driven cliffs, macro colour drift), atmosphere pass with sun shafts, height and distance haze, drifting clouds and cloud shadows, all switchable in the settings menu.

**Tests:** about 120 EditMode and 31 PlayMode tests covering the generators, the build graph, forestry and the player rig.

## Opening the project

You need **Unity 6.3 LTS** (6000.3.x). The project uses URP and several Unity packages, which Unity installs from `Packages/manifest.json`.

**Third-party art is not in this repository.** The trees and grass come from three paid Unity Asset Store packs, which can't be redistributed. To get the full world:

1. Buy and import **FantasyForest**, **FantasyValley** and **CartoonGrass** from the Unity Asset Store.
2. Put each one in `Assets/External/<PackName>/` (for example `Assets/External/FantasyForest/`).
3. Run `Tools/Cabin/Map/Generate World` to rebuild the trees, impostors and the world from the recipe.

Without the packs the project opens, but the trees and grass are missing references until the packs are imported and the world is regenerated. The `TestPlatform` scene, the building system and the tools don't need them.

Useful menu items, all under `Tools/Cabin/`:

- `Map/Generate World`: builds the whole map from `Assets/_Project/Data/MapRecipe.asset`.
- `Map/Apply Look`: lighting, post-processing and the atmosphere pass.
- `Build/Generate Sandbox Assets`: materials, tools, the player and the build system.
- `Build/Generate Player`: just the player character.
- `Scenes/Open World` and `Scenes/Open TestPlatform`.

Press Play from the `Boot` scene, or open `World` or `TestPlatform` directly.

## Repository layout

```
Assets/
  _Project/      our own code, art, shaders, prefabs, scenes, data
    Scripts/     Runtime (Cabin.Runtime), Editor (Cabin.Editor), Tests (EditMode, PlayMode)
    Art/         shaders, generated textures, the lumberjack, materials
  Generated/     terrain tiles and meshes built from the MapRecipe (regenerable)
  External/      third-party packs go here (not included, see above)
docs/
  STATUS.md      where every milestone stands, in detail
  SAM_TODO.md    what's waiting on playtesting
  handoffs/      the design handoffs the game is built from
```

`docs/STATUS.md` is the detailed record of how each system works and what changed after each playtest.

## How it's made

Code, generators, shaders and tests are written with Claude Code. Design, art direction, sourcing and playtesting are Sam's. The lumberjack was generated with Meshy and rigged with Meshy and Mixamo. Sound effects are generated recordings.

## Rights

© Sam McNeil. All rights reserved. The source is public to read and follow along; it is not licensed for reuse. The Asset Store packs the game uses belong to their publishers and are not included.
