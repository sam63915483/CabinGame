# Handoff — CABIN (working title) — Foundation v1

**Date:** 2026-10-29
**For:** Claude Code, starting a brand-new Unity project and repo
**From:** Sam (design) via Claude (chat)
**Tag format:** `[EXISTS]` already there · `[BUILD]` Claude Code builds · `[AUTHOR]` Sam does it (sourcing, placing, playtesting) · `[INTEGRATE]` wire into existing · `[TEST]` must have a test · `[OPEN]` needs Sam's answer

---

## 0. TL;DR

New game, new repo, new Unity project — **not** a fork of Sound of Space. Phase 1 is a **creative-mode sandbox**:

1. A big forest map: hills, cliffs, lakes, rivers, roads. One map, one world, no planets.
2. Trees and grass reused from Sound of Space (audit which ones actually made the cut there, port only those).
3. A first-person character and a drivable truck. You spawn in the truck on the highway at the map edge and drive in looking for a place to take over.
4. Cut down trees and clear brush to make a site.
5. A realistic stick-framing building system — individual posts, plates, studs, joists, rafters, sheets — with **unlimited free parts** so Sam can test the building feel.

**Nothing else.** No hunger/thirst, no enemies, no town or hardware store, no lumber milling or progression, no story, no radio, no multiplayer. Those are later handoffs, after the sandbox is fun.

Build in order, stop at each milestone for a playtest: **M0 setup → M1 map → M2 vegetation → M3 player + truck → M4 cut + clear → M5 building sandbox.**

---

## 1. What the game is (context only — do not build any of this yet)

AI-driven blackout, society falls apart, the player drives out to a piece of land in the woods to start over. Clear the land, build a cabin from real framing, survive on fishing and filtered water, then defend the place when things start showing up after the first week. Radio transmissions, an island safe-haven ending around day 100, or just see how long you last.

Phase 1 proves the one thing everything else depends on: **is clearing a site and framing a cabin fun on its own?** If standing inside a wall you framed doesn't feel good, nothing downstream matters.

Design reference diagrams (Sam will drop them in `docs/diagrams/`): `cabin_game_systems_map.svg` (whole game, phased) and `building_system_map.svg` (materials pipeline → build layers → placement model).

---

## 2. Non-goals for this handoff

Do not build, stub, or "leave hooks for" any of these unless the hook is free (a data field, an interface) and noted in `docs/STATUS.md`:

- Survival meters, sleep, temperature, weather effects on the player
- Enemies, animals, combat, base defense
- Town, store, scavenging, inventory beyond what M4/M5 need
- Lumber milling / processing stations / material costs being **enforced** (costs are recorded on parts, never charged)
- Progression, unlocks, tool tiers
- Story, radio, endings, UI polish, main menu, settings screens
- Multiplayer (but keep generation deterministic and world state data-driven so it stays possible)
- Sound of Space's floating origin, n-body gravity, planet code — none of it applies here

---

## 3. Decisions already made

| Decision | Choice | Why |
|---|---|---|
| Engine | **Unity 6.3 LTS** (move to 6.7 LTS when it ships, late 2026). Not Unity 7 beta. | 6.3 is the current LTS; 6.x → 6.x upgrades are designed to be smooth |
| Render pipeline | **URP**, Forward+ path, GPU Resident Drawer + GPU occlusion culling on | Built-in RP is officially deprecated as of Unity 6.5 and Unity says not to use it for new titles; forests are exactly what the GPU drawer is for |
| Scripting | C#, Input System package (not legacy Input), assembly definitions from day one | — |
| Units | **Real-world metres**, lumber at real dimensions (Appendix A) | Snapping only works if the numbers are real |
| World | Unity Terrain, **4 × 4 tiles of 1000 m** (4 km × 4 km), everything generated from a seeded `MapRecipe` asset by editor tools | Big enough to drive in; regenerable; can grow |
| Trees | Unity terrain trees (instanced); swapped to a physics GameObject only when cut | 300k+ trees need instancing |
| Grass | Unity terrain details, GPU instanced | Sound of Space's custom grass shader is Built-in RP; not porting it in Phase 1 |
| Building | Parts system: every stud/plate/sheet is its own object; layout tools generate parts; typed snap sockets; dependency graph gates order and removal | See §11 |
| Creative mode | Unlimited parts, no costs charged, fly camera, undo | Test the feel first |
| Save format | Flat JSON list of pieces + world deltas, from day one | Retrofitting saves onto a build system is misery |
| Art style | Whatever Sound of Space's trees are (low-poly packs) — match them | Reuse |
| Name | `[OPEN]` working title "Cabin"; repo `CabinGame` | — |

---

## 4. Sam's pre-flight checklist `[AUTHOR]`

Claude Code cannot do these. Do them first, in order:

1. **Install Unity 6.3 LTS** via Unity Hub, with the Windows Build Support module.
2. **Create the project from the Hub** using the **Universal 3D** template. Name it `CabinGame`. Open it once so `Library/` and `ProjectSettings/` exist, then close it.
3. **Init git in that folder** (Claude Code will add `.gitignore`, `.gitattributes`, LFS config — just `git init` and make the first commit after Claude Code's M0).
4. **Tell Claude Code the local path to the Sound of Space project** (the full checkout with assets, not just the GitHub clone — the GitHub repo `sam63915483/SoundOfSpace` is public and fine as a backup for scripts, but the asset audit needs the real `Assets/` folder). `[OPEN]`
5. **Get a rigged humanoid** for the player body (Mixamo is the fast route: pick a character, download in FBX for Unity with idle/walk/run/jump plus a chop and a hammer swing). Drop it in `Assets/External/Character/`. Can be done during M1–M2; M3 uses a capsule until it arrives.
6. **Find a pickup truck model** with a licence that allows commercial use (Asset Store, Sketchfab CC-BY, Kenney). Drop it in `Assets/External/Truck/`. M3 drives a kitbashed placeholder until then — do not block on this.
7. Copy the two SVG diagrams into `docs/diagrams/`.
8. If Coplay (Unity MCP) is set up like it was for Sound of Space, connect it to the new project. If not, Claude Code works through editor scripts + batchmode (§5).

---

## 5. How to work in this project

**Division of labour.** Claude Code writes all code, editor tools, generators, shaders, prefab wiring (via scripts or Coplay), tests and docs. Sam sources art, playtests, answers `[OPEN]`s, and makes any call that changes scope. Anything Claude Code cannot do (Asset Store download, a licence decision, a visual judgement) goes into `docs/SAM_TODO.md` and work continues on something else.

**Everything generated must be regenerable.** Terrain, water, roads, tree placement, texturing — all produced by `Tools/Cabin/...` menu items reading a `MapRecipe` ScriptableObject with a seed. No hand-sculpting, no hand-edits to generated data. Change the recipe, regenerate.

**Verifying without the editor open.** Unity holds a project lock, so batchmode only works when Sam's editor is closed (or ask Sam to close it). Commands in Appendix B: compile check, run EditMode tests, run a generator. With Coplay connected, prefer it: run the menu item, read the console, inspect the hierarchy.

**Milestone discipline.**
- One branch per milestone (`m1-map`, `m2-vegetation`, …), merged to `main` after Sam's playtest. Small commits with real messages.
- At the end of each milestone update `docs/STATUS.md` (what exists, what's tested, what's not played) and `docs/SAM_TODO.md`, then stop and ask for a playtest.
- Do not start the next milestone's work early "while waiting." Polish the current one or write tests.
- Ask before: adding a package, changing URP/quality settings, importing an asset pack wholesale, adding anything from §2.

**Performance budget (Sam's machine: RX 6800 XT, 3440 × 1440 ultrawide).** 60 fps in Play Mode in the editor, with headroom. If a milestone drops below that, fixing it is part of the milestone. Tree draw distance ~250–300 m, detail (grass) distance 100 m, shadow distance 150 m / 4 cascades, GPU occlusion culling on.

**Code conventions.** Namespaces `Cabin.*` (`Cabin.World`, `Cabin.Build`, `Cabin.Player`, `Cabin.Vehicles`, `Cabin.Editor`). One class per file. No `FindObjectOfType` in hot paths. Dev-only code lives in `Cabin.Editor` or behind `#if CABIN_DEV` — nothing auto-spawns in builds. Warnings-zero from the first commit. Tests for every generator and for the build graph.

---

## 6. M0 — Project setup `[BUILD]`

**Done when:** the project opens clean on Unity 6.3 LTS with URP Forward+, compiles with zero warnings, has the folder layout and asmdefs below, git is configured (ignore, LFS, Smart Merge), and an empty `World` scene plus a `TestPlatform` scene exist.

1. **Packages:** Universal RP (from template), Input System, Splines (`com.unity.splines`), Terrain Tools (`com.unity.terrain-tools`), Burst + Collections + Mathematics (for generation jobs), Test Framework, Cinemachine (optional; only if the camera work needs it). Remove template sample content.
2. **URP:** rendering path Forward+; GPU Resident Drawer = Instanced Drawing; GPU occlusion culling on; one URP asset per quality tier (Low/Med/High), High is the dev default; shadows 150 m, 4 cascades, soft.
3. **Editor settings:** Force Text serialization, Visible Meta Files (template defaults — verify).
4. **Folders:**
   ```
   Assets/
     _Project/
       Art/        our own meshes, materials, shaders (water, ghost, road)
       Data/       ScriptableObjects: MapRecipe, PartCatalog, VegetationSet
       Prefabs/
       Scenes/     Boot, World, TestPlatform
       Scripts/
         Runtime/  asmdef Cabin.Runtime
         Editor/   asmdef Cabin.Editor (refs Runtime)
         Tests/    asmdef Cabin.Tests.EditMode, Cabin.Tests.PlayMode
       Settings/   URP assets, Input Actions asset, quality presets
     External/     third-party packs, one folder each, never edited in place
     Generated/    TerrainData, baked meshes — regenerable from MapRecipe
   docs/
     STATUS.md  SAM_TODO.md  diagrams/  handoffs/
   ```
5. **Git:** Unity `.gitignore`; `.gitattributes` with Git LFS for `*.fbx *.obj *.png *.jpg *.tga *.psd *.exr *.hdr *.wav *.mp3 *.ogg *.ttf` and for `Assets/Generated/**/*.asset`; UnityYAMLMerge configured as the merge driver for `*.unity *.prefab *.asset`.
6. **Scenes:** `Boot` (managers, loads World additively), `World` (terrain tiles, water, roads, vegetation — generated), `TestPlatform` (a flat 30 × 30 m plane for M5 building tests, loadable on its own).
7. **Input Actions asset:** Move, Look, Jump, Sprint, Interact (E), Use (LMB), AltUse (RMB), Rotate (R), FineRotate (Q/E), Remove (X), Undo (Z), Fly (Tab), Palette (B), Hotbar 1–9, EnterExitVehicle (E), Throttle/Brake/Steer/Handbrake.
8. `docs/STATUS.md` created with the milestone table; `docs/SAM_TODO.md` created.

---

## 7. M1 — Map: terrain, water, roads `[BUILD]`

**Done when:** `Tools/Cabin/Map/Generate All` builds the 4 × 4 km world from a `MapRecipe` in under ~2 minutes, deterministic per seed; the map has hills, cliff bands, 3–5 lakes, 2–3 rivers, a paved highway plus gravel and logging roads, 2–4 natural clearings, and a reserved flat town pad; a fly camera can tour it at 60 fps.

### 7.1 `MapRecipe` ScriptableObject (`Assets/_Project/Data/`)
- `seed`, tile grid (4 × 4), tile size 1000 m, heightmap res (513 for iteration, 1025 final), alphamap res 512, detail res 512, max height 300 m
- Noise stack params: base fBm (octaves, lacunarity, gain, scale), ridged layer for cliff bands (mask + strength), domain warp strength, valley-floor bias
- Erosion: thermal erosion iterations/talus angle (cheap, makes slopes natural); hydraulic optional later
- Lakes: list of `{centre, radius, waterLevel, shoreFalloff}`
- Rivers: list of `{spline control points (world XZ), widthStart, widthEnd, depth, bankFalloff}` — sources on high ground, mouths at a lake or map edge; the carve pass also lowers the channel monotonically downstream so water visibly slopes
- Roads: list of `{spline, type: Paved|Gravel|Logging, width 6/4/3 m, shoulder 8 m}`; one paved highway crosses the map edge-to-edge (entry = spawn), 3–6 gravel branches, 2–4 dead-end logging roads
- Clearings: list of `{centre, radius}` (tree density ~0, grass density high); one must sit within 150 m of the highway (first build site)
- Town pad: `{centre, size 200 m}` flattened, marker only
- Spawn: `{position on highway at map edge, heading}`

### 7.2 Generation pipeline (each stage is its own menu item too)
1. **Heightmap** — Burst job: fBm + ridged + warp → thermal erosion → valley bias → write 16 tiles with matching edge rows (stitch with `SetNeighbors`).
2. **Hydrology** — flatten lake basins below `waterLevel` with smooth shores; carve river channels along splines with bank falloff; ensure rivers end at or below their lake's water level.
3. **Roads** — flatten terrain under each road to the spline height with shoulder falloff; mark a 10 m corridor as no-tree / low-grass; generate the road mesh (custom sweep along the spline, UVs along length, slight crown, MeshCollider) with Paved/Gravel/Logging materials; paint a road-bed terrain layer under gravel and logging roads.
4. **Texturing by rules** — terrain layers: Grass, ForestFloor, Dirt, Rock, Mud, RoadBed. Slope < 25° grass/forest floor (noise-mixed), 25–45° dirt, > 45° rock; Mud within ~5 m above water level; Rock also on ridged-cliff mask.
5. **Rocks on cliffs** — scatter rock meshes (placeholder low-poly boulders if no pack exists) on slopes > 50°, instanced.
6. **Water** — one plane per lake at `waterLevel`; river mesh swept along each spline sitting slightly below the carved bed. Shader: hand-written URP HLSL (not Shader Graph, so it's reviewable): scrolling normals, depth-based colour and opacity, shoreline foam from scene depth. No swimming, no drowning in Phase 1 — the player can wade.
7. **Write** TerrainData to `Assets/Generated/Terrain/Tile_x_y.asset`, water/road meshes to `Assets/Generated/Meshes/`.

### 7.3 Notes
- Cliffs are steep slopes with rock texture and scattered rocks; Unity terrain cannot overhang. That's fine.
- Keep the world at origin; 4 km is well within float precision. No floating origin.
- `[OPEN]` Sam: want the hills to feel like Nova Scotia? Nova Scotia publishes open elevation (LiDAR DEM) data — a real heightmap could replace the noise stack later. Noise first.

---

## 8. M2 — Vegetation, audited from Sound of Space `[BUILD]` + `[AUTHOR]`

**Done when:** `docs/ASSETS_FROM_SOS.md` lists exactly which tree/grass/rock assets Sound of Space actually uses, the chosen ones are copied into `Assets/External/<PackName>/` with URP-compatible materials (no pink), and `Tools/Cabin/Map/Scatter Vegetation` populates the world by rules at 60 fps.

### 8.1 The audit (Sound of Space, path from Sam — `[OPEN]`)
Sound of Space is Unity 2022.3, Built-in RP. Its planets are spheres, so trees were **prefabs placed by spawners**, not terrain trees. Third-party packs live under `Assets/5 - External Imports/<Category>/` (several LowPolyFantasyBundle packs among them).

1. Find every tree/bush/grass/rock prefab that is actually referenced: spawner components and ScriptableObjects (deterministic seed-hash spawners), prefab lists, scene instances on the planets that shipped (Humble Abode, Cyclops, the twins, etc.), and the chop/harvest code (it names the tree prefabs it accepts).
2. For each: source pack + folder, mesh triangle count, LOD setup (LODGroup? billboard?), colliders, shader on every material (Standard/Lit → auto-convertible; custom → flag), textures.
3. Separate three lists in `docs/ASSETS_FROM_SOS.md`: **Used in SoS** (port these), **Owned but unused** (candidates, note why unused if findable), **Blocked** (custom Built-in shaders — e.g. the custom grass shader with `_GrassFillStrength`/lamp response is Built-in and is **not** being ported in Phase 1).
4. Copy only the needed folders into the new project; keep each pack's licence file with it.
5. Run Unity's **Render Pipeline Converter** (Built-in → URP) on the copied materials; verify by walking the TestPlatform scene with one of each prefab placed. Anything still pink gets a URP Lit replacement material authored by hand.

### 8.2 Terrain tree requirements
- Each tree prefab used as a terrain tree gets a `LODGroup` (LOD0 full, LOD1 reduced if available, cull at tree distance) and a `CapsuleCollider` (terrain tree colliders come from the prefab).
- A `VegetationSet` ScriptableObject lists species with weights, min/max scale, allowed slope/altitude bands, moisture preference, cluster noise scale.

### 8.3 Scatter rules (`Tools/Cabin/Map/Scatter Vegetation`)
- Density by zone: forest 0.01–0.02 trees/m² (clustered by noise, not uniform), clearings ~0, road corridors 0, water 0, slope > 40° 0, cliff bands 0.
- Mix: conifer-heavy (spruce/fir/pine equivalents) with birch/maple-ish deciduous mixed in; pick the closest low-poly equivalents from the audit.
- Grass as terrain details (Instanced render mode), density high in clearings and along roads' shoulders, lower under dense canopy, none on rock/road/water. Detail distance 100 m.
- Deterministic from the recipe seed; writes into TerrainData; re-runnable.

---

## 9. M3 — Player + truck `[BUILD]` + `[AUTHOR]`

**Done when:** Sam spawns seated in the truck at the highway entry, drives into the map at a believable speed on roads and off-road, stops anywhere, presses E to get out, walks/sprints/jumps in first person, presses E at the door to get back in, and a fly camera (Tab) exists for inspection. 60 fps.

### 9.1 First-person character (`Cabin.Player`)
- `CharacterController`-based, Input System. Walk 4 m/s, sprint 7, jump, crouch, head height 1.7 m, no head bob by default (toggle).
- Interaction: centre-screen raycast 3 m, `IInteractable` interface, minimalist prompt (the kind Sound of Space settled on after its gaze-prompt revamp — small bracket + verb, occlusion-aware).
- Body: capsule until Sam's Mixamo character arrives `[AUTHOR]`; then a full body visible from first person (head hidden from the player camera via layer), Animator with idle/walk/run/jump/chop/hammer. Shadows from the full body.
- Tools in hand are simple held prefabs (axe, hammer) swapped by hotbar; no inventory system beyond "which tool is equipped."

### 9.2 Truck (`Cabin.Vehicles`)
- Four `WheelCollider`s, rear-wheel drive, ~2000 kg, simple torque curve and gear-less speed cap (~90 km/h paved, less off-road through friction curves), handbrake, reverse, steering with speed-dependent limit. Tune so gravel feels loose and the highway feels planted.
- Enter/exit: E at either door; player is parented and hidden, camera moves to the driver seat (first-person in cab) with a third-person chase toggle. Exiting places the player beside the door, never inside geometry.
- Placeholder model: kitbashed primitives on the physics rig until Sam's model arrives `[AUTHOR]`; swap is a prefab child replacement, nothing else changes.
- Headlights, horn, engine audio: nice-to-have, after everything else in M3.

### 9.3 Fly camera
- Tab toggles a free camera (WASD + mouse, shift fast, scroll speed). Creative-mode tool, editor and `CABIN_DEV` builds only.

---

## 10. M4 — Cut and clear `[BUILD]`

**Done when:** with the axe equipped, Sam can fell any terrain tree (falls away from him, physics), buck the fallen trunk into logs, leave or pull the stump, and clear grass in a radius with the clearing tool; everything survives save/reload.

- **Terrain trees aren't GameObjects.** `TreeIndex` builds a per-tile spatial hash of `TerrainData.treeInstances` positions at load. Axe raycast → hit point → nearest instance within 1.5 m.
- Chop progress per instance keyed `(tile, instanceIndex)`; 4–6 swings for a mid-size tree. At 100%: remove the instance (reassign the tile's `treeInstances` array — batch removals if it ever stutters), spawn `FallingTree` (same mesh, `Rigidbody`, hinge at the base, falls away from the player, settles), spawn `Stump` at the base.
- `FallenTree` → interact with axe to **buck** into `Log` items (physics, 8/12/16 ft segments). Logs do nothing yet except exist and be carried (one on the shoulder, slow walk) — milling is a later handoff.
- `Stump` → "pull stump" interaction (several seconds) removes it.
- **Clearing tool:** hold to zero grass detail density in a 3 m radius (`SetDetailLayer`); also removes small bushes if they are details.
- **Persistence:** world save records removed tree instances, placed logs, stumps, and cleared-grass circles; applied on load after terrain loads.
- `[TEST]` EditMode: TreeIndex nearest-lookup; save/load roundtrip of world deltas.

---

## 11. M5 — Building sandbox (the actual game) `[BUILD]`

**Done when (TestPlatform scene first, then World):** Sam can, with unlimited parts: set posts on uneven ground that level themselves; put beams and joists on them and lay a subfloor; drag out walls of any length with a door and two windows and watch real studs appear; sheathe the walls with 4 × 8 sheets and boards; add a shed roof, then a gable roof; paint a sheathed wall; remove parts only in a legal order; undo; save; reload with everything intact. Then do the same on the terrain next to the truck.

### 11.1 Principle
**Freedom lives in the layout, realism lives in the parts.** The player decides where things go and how long they are; the engine lays out real framing; every part is a real object with a recorded cost (not charged in creative mode); dependencies enforce a real build order and a real tear-down order.

### 11.2 Core types (`Cabin.Build`)
- `PartDefinition` (ScriptableObject in a `PartCatalog`): id, category (Structural / Sheet / Finish / Opening), mesh/prefab, real dimensions, stock lengths, cost record (lumber type, count, nails), socket layout, which socket types it can attach to, whether it is terrain-placeable.
- `BuildPiece` (MonoBehaviour on every placed part): `PartDefinition`, `State` (Ghost / Built), `Length` (for cut-to-length stock), `DependsOn` (list of piece ids it is attached to), `Dependents` (pieces attached to it), `AssemblyId` (if generated by a layout tool).
- `SnapSocket` (child transforms on part prefabs): `SocketType` enum — `Ground, PostTop, PostSide, BeamEnd, BeamTop, JoistTop, PlateTop, PlateBottom, StudEnd, StudFace, RafterEnd, RidgeTop, SheetEdge, OpeningFrame` — plus compatibility table (which socket accepts which part).
- `BuildGraph`: registry of all pieces, dependency edges, `CanRemove(piece)` = no Built dependents, `Remove(piece)` (returns the part to the palette; nails are "lost" — recorded only), `Clear()`, undo stack (command pattern: Place / Remove / GenerateAssembly as reversible commands).
- `BuildPlacer`: raycast from camera; ghost preview with a translucent material (green valid / red invalid); finds the nearest compatible socket within 0.3 m and aligns; 16-in (0.4064 m) grid along plates and beams; rotate 90° (R) / 15° (Q/E); confirm places a Ghost; "hammer" (LMB with hammer) converts Ghost → Built. In creative mode a toggle auto-builds on place.
- `BuildSave`: JSON list of `{id, partId, length, position, rotation, state, dependsOn[], assemblyId}` + assemblies' parameters. Load re-creates pieces, then rebuilds the graph. `[TEST]` roundtrip equality.

### 11.3 Layout tools (generators — they output ordinary `BuildPiece`s, so hand-editing a generated wall just works)
- **PostTool**: click terrain to place a round post or 6×6; the post's top is set to a target height (default: last placed post's top, or 0.6 m above the clicked point); the post extends itself down to terrain + 0.3 m buried. Visual length changes with ground. Posts snap to a 16 in / 4 ft grid relative to the first post when a modifier is held.
- **FloorTool** (`FloorGenerator`): select 4 posts (or drag a rectangle over posts) → rim beams on the post lines, interior beams every ≤ 8 ft, 2×8 joists at 16 in OC spanning the short way, rim joists, subfloor sheets tiled 4 × 8 staggered, partial sheets auto-cut at edges.
- **WallTool** (`WallGenerator(start, end, height, openings[])`): drag along a floor edge or anywhere on a subfloor/test platform; height 8 ft default. Output: bottom plate (segmented to stock lengths), studs at 16 in OC from `start`, end stud at `end`, top plate, double top plate. **Openings** (added in edit mode by clicking the wall): door RO 38 × 82.5 in, windows 36 × 48 / 24 × 36 in → king studs, jack studs, header (doubled 2×6 up to 4 ft, doubled 2×8 up to 6 ft), cripples above (and below a window sill). Corners: when two walls meet, the second gets a corner stud pack (3-stud corner). `[TEST]` stud count and positions for 12 ft and 13 ft walls; door and window framing counts; corner detection.
- **RoofTool** (`RoofGenerator`): **Shed** first — pick the low wall and the high wall (or a pitch 2:12–4:12 and let the generator add the tall wall's extra plates), rafters 2×6 at 24 in OC, birdsmouth ignored (rafters just sit on plates), fascia boards. **Gable** second — ridge 2×8 along the long axis, pitch 4:12 / 6:12 / 8:12, rafters paired, collar ties 2×4 every other pair, gable-end studs. Roof sheathing via the SkinTool.
- **SkinTool**: on any framed surface (wall or roof, each independently): **4 × 8 sheets** snap to stud/rafter faces starting from a corner, tile across, partial sheets auto-cut at the end and around openings; or **1×6 boards** laid horizontally row by row. Boards are allowed to be gappy (early-game look).
- **PaintTool**: tint a sheathed wall/roof (per-surface colour; placeholder palette of 8 colours).
- **OpeningFillTool**: placeholder door (hinged, opens) and window (glass quad) snap into `OpeningFrame` sockets.

### 11.4 Dependency rules (the realism)
`Post → Ground` · `Beam → 2 Posts` · `Joist → Beams` · `Subfloor → Joists` · `BottomPlate → Subfloor | Beam | Ground (test platform only)` · `Stud → BottomPlate` · `TopPlate → Studs` · `DoubleTopPlate → TopPlate` · `Rafter → TopPlate (+ Ridge for gable)` · `Sheet/Board → the Studs or Rafters it spans` · `Paint → Sheet/Board` · `Door/Window → Opening frame`. Removal is refused with a one-line reason in the HUD ("3 sheets are nailed to this stud"). Creative mode keeps these rules on — they are the point — but adds a `Clear All` button.

### 11.5 Creative palette + HUD
- B opens a palette: categories (Structural / Assemblies / Sheets / Openings / Finish), hotbar 1–9 assignment, search. Ugly uGUI or IMGUI is fine.
- HUD: hovered part's recorded cost ("2×4 × 1 · 8 ft stock · 4 nails"), current tool, snap state. No art pass.
- Z undo, X remove, Tab fly, F5 quick-save, F9 quick-load.

### 11.6 Cheap realism touches (do after the above works)
- Stock lengths with offcuts: cutting an 8 ft 2×4 to 6 ft records a 2 ft offcut in an "offcut bin" that cripples and blocking draw from first.
- Sheets carried one at a time (walk slower); lumber carried in bundles of 10.
- Nails recorded per part (stud 4, plate per stud 2, sheet 32, rafter 6).

---

## 12. Lessons from Sound of Space (do not repeat them here)

- **Scene churn:** SoS had a monolithic scene with 10k+ line YAML diffs weekly and no Smart Merge. Here: Smart Merge from M0, generated content lives in `Assets/Generated/`, everything placeable is a prefab, scenes stay small.
- **Script sprawl:** 500+ files under a nested `Scripts/Scripts`. Here: asmdefs and namespaces from day one; one class per file; delete scratch scripts the day they're obsolete.
- **Diagnostics in builds:** a 600-line diagnostic auto-spawned in every SoS build via `RuntimeInitializeOnLoadMethod`. Here: dev tooling is editor-only or behind `CABIN_DEV`, reviewed before each milestone merge.
- **Docs rot:** 170+ markdown files with `(1).md` duplicates. Here: `docs/STATUS.md` (single source of truth), `docs/SAM_TODO.md`, dated handoffs under `docs/handoffs/`, nothing else without a reason.
- **"Compiles clean, not played":** many SoS features sat unplayed for weeks. Here: a milestone is not done until Sam has played it; STATUS.md says explicitly what has and hasn't been played.
- **Warnings:** SoS needed a 177 → 0 pass. Here: zero from the first commit.
- **Physics vs render frame bugs** came from floating origin + orbiting planets. None of that exists here; keep it that way — static world at origin.

---

## 13. Open questions for Sam `[OPEN]`

1. Local path to the Sound of Space project (for the asset audit).
2. Game name / repo name (default `CabinGame`).
3. Map size: 4 × 4 km default — bigger, smaller?
4. Start resolution: 513 heightmap for fast iteration (≈2 m per sample) is recommended; say if you want 1025 from the start.
5. Noise-generated terrain first, or go straight to a real Nova Scotia heightmap?
6. Truck model and character model: Sam sourcing, timing flexible.
7. Shed roof before gable (recommended — simpler, and plenty of real cabins are shed-roofed).

---

## Appendix A — Units and part catalog

| Thing | Imperial | Metres |
|---|---|---|
| 1 in / 1 ft | — | 0.0254 / 0.3048 |
| 2×4 (actual 1.5 × 3.5 in) | studs, plates, ties, cripples | 0.0381 × 0.0889 |
| 2×6 (1.5 × 5.5) | rafters, headers ≤ 4 ft | 0.0381 × 0.1397 |
| 2×8 (1.5 × 7.25) | joists, ridge, headers ≤ 6 ft | 0.0381 × 0.1842 |
| 1×6 board (0.75 × 5.5) | board sheathing, siding | 0.0191 × 0.1397 |
| 4 × 8 sheet | OSB 7/16, plywood 1/2, subfloor 3/4 | 1.2192 × 2.4384; t = 0.0111 / 0.0127 / 0.0191 |
| Round post | 8 in dia, 4–10 ft | 0.203 dia |
| Beam 6×6 (5.5 × 5.5) | hewn | 0.1397 × 0.1397 |
| Stud spacing | 16 in OC | 0.4064 |
| Rafter spacing | 24 in OC | 0.6096 |
| Precut stud (8 ft wall, 3 plates) | 92 5/8 in | 2.3528 |
| Stock lengths | 8 / 10 / 12 / 16 ft | 2.4384 / 3.048 / 3.6576 / 4.8768 |
| Door rough opening (36 × 80 door) | 38 × 82.5 in | 0.965 × 2.096 |
| Window rough openings | 36 × 48, 24 × 36 in | 0.914 × 1.219, 0.610 × 0.914 |
| Joist span limit (2×8 @ 16 in, simplified) | 12 ft | 3.66 |
| Roof pitches | 2:12 / 4:12 shed; 4:12 / 6:12 / 8:12 gable | — |

**Phase-1 part catalog:** RoundPost, Beam6x6, Lumber2x4 (8/12/16 stock), Lumber2x6 (8/12/16), Lumber2x8 (12/16), Board1x6 (8/12), SheetOSB, SheetPlywood, SubfloorSheet, DoorOpening, WindowOpening36x48, WindowOpening24x36, PlaceholderDoor, PlaceholderWindow, Paint. Assemblies: Floor, Wall, ShedRoof, GableRoof, Skin (sheet/board).

## Appendix B — Batchmode commands (editor must be closed)

```
set UNITY="C:\Program Files\Unity\Hub\Editor\6000.3.xf1\Editor\Unity.exe"   # match the installed 6.3 LTS build

# compile check (non-zero exit on compile errors; read the log)
%UNITY% -batchmode -nographics -quit -projectPath . -logFile -

# EditMode tests
%UNITY% -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults TestResults\editmode.xml -logFile -

# run a generator headlessly
%UNITY% -batchmode -nographics -quit -projectPath . -executeMethod Cabin.Editor.Cli.GenerateAll -logFile -
```

`Cabin.Editor.Cli` wraps the same methods the menu items call, so Coplay, the menu and the CLI all run identical code.
