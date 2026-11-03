# Terrain and water redesign — design

Date: 2026-10-31. Branch: `m1-map`. Replaces the M1 relief/hydrology pipeline. Approved by Sam in conversation.

## Why

The map shrinks Nova Scotia 68× so the province fits in 8 × 7 km. At that scale OpenStreetMap's real lakes are
20–80 m ponds (Kejimkujik Lake ≈ 75 m across) and rivers are one or two 4 m cells wide. The terrain is noise and the
water is stamped onto it afterwards, so banks, valleys and falls never agree with the land, and lake edges follow the
grid. Sam's playtest: lakes and rivers "square, jagged, unrealistic", terrain "not interesting", the dramatic version
"buggy waterfalls". Polishing the stamping cannot fix a scale mismatch.

## Decisions (Sam, 2026-10-31)

1. **Nova Scotia character, not the real map.** Keep the real coastline and the province's broad highs and lows so it
   still reads as Nova Scotia from above. Generate the interior terrain, lakes and rivers.
2. **Drama mixed by region.** Lowlands and the valley rolling and buildable; highlands (Cape Breton, Cobequids, North
   and South Mountain) steep, with gorges, 30–60 m cliff bands and waterfalls.
3. **Lake country with some big ones.** Many 50–150 m ponds in the lowlands, 15–30 lakes of 300–800 m, long rivers
   running lake to lake to the sea. The spawn area (Kejimkujik) is lake country.
4. **Proper waterfalls, no particles.** A falling sheet with its own shader look and a plunge pool. No spray, no sound.
5. **One simulation.** Terrain erodes its own valleys; water comes from drainage of that terrain; shorelines are
   terrain contours. Nothing is stamped from an outline.

## Non-goals

Roads, hand-placed anything, swimming, particles/sound at falls, floating origin, changes to the painter's look, to
vegetation rules or to the axe. Those systems keep their inputs and are not touched except where a signature forces it.

## Pipeline

All on `Grid` (floats, x east / z north). Two resolutions: **coarse** 8 m (1025 × 897 cells for the 8 × 7 km map;
geology and drainage) and **fine** 2 m (4097 × 3585; the heightmap, as today). Everything seeded from the recipe.
Target: `Tools/Cabin/Map/Generate World` under 2 minutes.

### 1. Base (`BaseTerrain`, coarse)

Inputs: `ElevationSource`, `MapProjection`, `ProvinceMask` (unchanged).

- `real` elevation sampled on the coarse grid, blurred by `smoothing`, province-masked (non-NS sunk to −40).
- `highland` 0–1 = `real` blurred over `regionScale` / `regionHigh`, clamped (today's `region` field).
- `inland`, `offshore` metre distances (today's chamfer).
- `base` = `Curve(real)` (land lifted 0.8 m, linear + quadratic exaggeration), sea floor shelving offshore as today.
- Relief noise, warped by `warpAmount`, faded in over `shoreCalm` inland:
  - lowland: broad rises ± `lowBroad` (20 m, ~900 m) + hills ± `lowHill` (8 m, ~230 m);
  - highland: hills × `highHillGain` (1.6) + ridged mountains `mountainHeight` (65 m, ~420 m) + **terraces**: the
    highland height is blended `terraceBlend` (0.35) towards a staircase of `terraceStep` (30 m) steps whose edges are
    warped by noise (`terraceWarp` 40 m), so ledges and cliff bands exist off the rivers too;
  - rugged-coast sea cliffs as today (`seaCliffHeight`).
- Never below 0.8 m on land (today's floor).
- `hardness` 0–1 field: lowland `0.35 ± 0.15` fbm; highland strata — a hard band `hardBand` (10 m) at the top of each
  terrace step (hardness 0.95) grading to soft (0.25) below it, band edges warped with the terraces. Hardness scales
  erodibility: K = K0 × (1.15 − hardness).

Output: `base` (coarse heights), `hardness`, `highland`, `inland` grids.

### 2. Erosion (`Erosion`, coarse, land only)

Stream-power incision with the implicit scheme of Braun & Willett (2013), `erosionIterations` (60) times:

1. `Drainage.Fill` (priority flood with ε = 1e-3 m so flats drain), receivers by steepest descent on the filled
   surface, topological order, accumulation A (cells).
2. For cells in order from outlets upward: `h = (h + c·h_rcv) / (1 + c)` with `c = K·A^m·dt / L` where L is the
   receiver distance (8 or 11.3 m), m = 0.5, n = 1, `dt·K0` = `erosionRate` (tuned so the biggest highland rivers cut
   40–70 m and lowland rivers 8–15 m in 60 iterations; recipe default 0.02).
3. Hillslope diffusion `h += D·(1.15 − hardness)·∇²h·dt`, `diffusion` 0.08 m²/iter: domed soft hills, sharp hard caps.
4. Sea cells (base < 0) and the map edge are fixed boundaries. Land never erodes below 0.5 m.

Knickpoints survive where a hard band is crossed → waterfalls. Deterministic: fill is serial, updates are per-cell
with double buffering.

### 3. Lake basins (`Basins`, coarse, after erosion)

Glacial scours, not river lakes, so they come after erosion.

- `lakeDensity` = (1 − highland) × gentle(slope < 8°) × fbm mask (~2.5 km) biased by `lakeCountryBias` towards the
  south-west interior (the Kejimkujik spawn); low on the Annapolis Valley floor by the same mask's low end.
- Two blob fields of warped fbm: **ponds** at `pondScale` 250 m, threshold `pondThreshold` 0.55; **lakes** at
  `lakeScale` 900 m, threshold `lakeThreshold` 0.68. Where a field exceeds its threshold (weighted by `lakeDensity`),
  the ground is lowered by `depth × smoothstep(excess)`: ponds 3–5 m, lakes 6–10 m (`pondDepth`, `lakeDepth`).
- Basin scoops do not touch cells below 1.5 m above sea (no basins at the beach) nor cells within 20 m of the sea.

### 4. Water network (`WaterNetwork`, coarse → data)

A final `Drainage.Fill` on the scooped terrain.

- **Lakes**: connected components of cells with `filled − h ≥ 0.25 m` sharing one filled level (± 2ε). Level L = that
  value. Components under `lakeMinArea` (20 cells ≈ 36 m across) are not lakes: their ground is raised to
  `filled + 0.1` so nothing holds water there. Each `Lake { Id, Level, Cells, Outlet cell }`.
- **Rivers**: cells with A ≥ `riverCatchment` (0.3 km² game) and h ≥ 0.2. Heads = river cells with no river upstream.
  Each head is traced down the receivers until it reaches the sea (filled ≤ 0), joins an already-traced river (ends
  there, recorded as a tributary join), or enters a lake (ends at the shore; the lake's outlet cell starts its own
  river if A qualifies). Each `River { Points (coarse cells → smoothed curve), Levels, Widths, Depths, EndsIn }`.
  - Level = filled at the cell: non-increasing downstream by construction. Width `w = clamp(2 + 4·√A_km², 3, 18)` m,
    depth `0.6 + 0.12·w`.
  - Planform smoothing: two rounds of Chaikin on the point list; levels re-sampled along the curve and then forced
    non-increasing (running minimum from the mouth) so smoothing never creates an uphill step.
  - At a join the tributary's last level is set to the main river's level at the join and the change propagated upstream
    at no more than `maxRiverSlope` (0.12) — today's `JoinTributaries` rule.
- **Falls**: along each river, where the surface drops ≥ `fallMinHeight` (2.5 m) within `fallRun` (24 m) of run, the
  steep stretch is one `Fall { River, Lip point/level, Base point/level, Height, Width }`. Shallower drops are rapids.
  Count logged; tuning target 5–15 falls per map.
- Logged: lakes by size band (<100 m, 100–300 m, >300 m), rivers, total river length, falls, % land under 25° slope.

### 5. Fine terrain (`FineTerrain`, 2 m)

- Upsample coarse `h` with bicubic (Catmull-Rom) interpolation → fine `heights`. Grid gains `SampleCubic`.
- `waterLevelNear` (coarse): each cell's nearest lake level or river surface within 40 m (chamfer spread), NaN else.
- Detail: fbm `detailHeight` (1.6 m, 45 m) + ridged rock on high/steep ground as today, multiplied by
  `shoreDamp = smoothstep(0.3, 2.5, |h − waterLevelNear|)` so the waterline is clean.
- Shore shelf: within |h − L| < `shelfBand` (1.2 m) of a lake level, `h = L + sign·shelf(|h − L|)` where shelf
  compresses small differences to 35 % (a gentle beach and shallows); the band's outer edge eases smoothly.
- Lake beds: fine lake mask = flood fill of `h < L` seeded from the lake's coarse cells (not a plain threshold, so
  unconnected dips outside the lake stay dry). Within it, `h = min(h, L − min(lakeMaxDepth, 0.3 + dShore·lakeDepthSlope))`
  with dShore the fine chamfer distance to the mask edge.
- River channels: for each river not inside a lake, fine cells within `w/2 + bankWidth` (4 m) of the smoothed curve
  (nearest-segment distance d via `EdgeIndex`, level s interpolated along it): bed `s − depth·(1 − (2d/w)²)` for
  d < w/2; bank from `s + 0.15` at d = w/2 blending to the existing ground over `bankWidth`; the ground is `min` with
  that profile, and `max(s + 0.2)` for d ≤ w/2 + 1.5 so the water never sits below a dip in its bank.
- Falls: the channel's s along a fall segment steps from lip level to base level at the lip point (not a ramp), so the
  face is as steep as the terrain allows; a plunge pool at the base deepens the bed to `1.8 × depth` within `1.5 × w/2`.
- Fine water grid (`water`, 2 m, NaN dry): L on lake mask cells; river surface on cells within w/2 of a river. `lakes`
  grid: lakes only. These feed the painter, vegetation, grass and spawn exactly as the old grids did.

### 6. Meshes and shader (`WaterMeshes`, `Cabin/Water`)

- Lakes: today's per-tile `Build` on `Extend(lakes, heights, 12)`. Unchanged.
- Rivers: a ribbon per river: cross-section at every curve point, half-width `w/2 + 0.6` m (under the banks), vertex
  level s, `TEXCOORD2.x` depth = s − ground, `TEXCOORD1` flow = tangent × speed (today's `FlowPerSlope` rule), split per
  tile for culling. Replaces the 2 m river grid (`RiverLevels`, `Refine`).
- Falls: the ribbon continues from lip to base along the segment, hugging the face 0.3 m out; `TEXCOORD2.y = 1` on
  fall vertices, grading to 0 over 3 m above the lip and 6 m past the plunge pool.
- Shader: `TEXCOORD2.y` (fallness) selects a falling look — normal map scrolled down the sheet at `_FallSpeed` (3 m/s),
  streak contrast, opacity and foam raised, foam also boosted where depth > 1.5 × normal within the plunge pool.
- Sea unchanged.

### 7. Orchestration and recipe

- `MapGenerator.Generate`: Base → Erosion → Basins → WaterNetwork → FineTerrain → Tiles → Vegetation → Grass → Scene.
  Progress bar per stage; `GC.Collect` between coarse and fine as today.
- `MapRecipe`: remove `lakeMinSamples`, `riverCatchmentKm2`, `riverMinWidth/MaxWidth/MinDepth/MaxDepth`,
  `waterCalm`, `riverSmoothing`, `valleySlope`, `lakeDepthSlope/MaxDepth` (moves to Fine); add sections **Base**,
  **Erosion**, **Basins**, **Rivers and falls**, **Shores** with the fields named above. Delete and regenerate
  `Assets/_Project/Data/MapRecipe.asset` so the defaults apply.
- Deleted: `Hydrology`, `Relief`, `OsmWater`, `RiverPath`, the Library/CabinOsm cache note. `ForestryTests`' use of
  `Hydrology.Downhill` moves to a `WaterNetwork` test. `docs/SAM_TODO.md` loses the OpenStreetMap credit item.
- Spawn: from the Kejimkujik coordinates, the first spot that is dry, under 12° and within 80 m of a lake ≥ 150 m
  across; fallback as today.

### 8. Preview and stats (`MapPreview`)

`Tools/Cabin/Map/Preview PNG` writes `Temp/map_preview.png`: hillshaded fine heights, lakes and rivers blue, falls as
red dots, sea dark. Also written automatically at the end of Generate World. Claude Code reads it after each tuning
pass. Stats from §4 go to the console and to `Temp/map_stats.txt`.

## Tests (EditMode, synthetic grids, no scene)

- `DrainageTests`: tilted plane drains to the low edge; a pit fills to its spill with the right level; outlet
  accumulation equals the catchment cell count; filled level never rises along receivers.
- `ErosionTests`: sea cells unchanged; the highest-accumulation channel ends lower than the ridges beside it; same
  seed → identical grid hash; land never below 0.5 m.
- `WaterNetworkTests`: bowl with a notch → one lake at the notch level and one outlet river that ends at the sea;
  stepped channel → one fall with the step's height ± 0.2; hollow under `lakeMinArea` → no water and ground raised;
  river levels non-increasing after smoothing; `Downhill` (moved) still pools adjacent violators.
- `FineTerrainTests`: every lake-mask cell is below its level and connected to its lake; every river bed cell is below
  its surface; shore slope under 20° at 90 % of cells within 2 m of a waterline; no NaN heights.
- `WaterMeshesTests`: ribbon has no NaN, vertex levels match the river, fall vertices carry fallness 1.
- Existing `ProjectSetupTests`, `ForestryTests` still pass.

## Risks

- Look needs tuning by eye: the preview PNG and stats are the loop; budget two or three tuning passes before Sam plays.
- Erosion time: 60 iterations × (serial flood ≈ 0.3 s + parallel updates) ≈ 30 s on the coarse grid. If over budget,
  drop to 40 iterations with a larger `erosionRate` (the scheme is stable).
- Too few falls if hard bands erode anyway: raise the hard/soft K ratio before adding any artificial step.
- Unity terrain cannot overhang: a fall's face is a 45–60° slope of rock; the sheet hugs it.

## Pass 2 — after Sam's first playtest (2026-10-31, later)

Sam: "decent"; terrain still mild, wants cliffs and rock formations played up; almost every shore jagged where water
meets land; trees vanish beyond a few hundred metres so hills look bare; asks what URP/6.3 buys. Decisions (Sam): full
look pass including renderer settings; noticeably bolder terrain but still buildable; far trees in this pass.

1. **Shores.** The symmetric shelf (ground flattened to 35 % slope within ±1.2 m of the level) is the cause: on a 2 m
   heightmap a near-flat shore lets the waterline wander across triangle edges and the paint cells. Replaced by a
   **beach**: from the waterline out `beachWidth` (10 m) the land rises at least `beachSlope` (9°), rings out from
   each lake carrying its id; below the water the bed still deepens from the shore. River banks rise at the same angle
   from `level + 0.2` at the channel edge. Painter's stony band widened to 1.3 m and smoothstepped. Trees from
   0.9 m above water (was 1.6). Terrain `heightmapPixelError` 3 (was 4).
2. **Drama.** Recipe: `lowBroad` 30, `lowHill` 13, `maxIncisionLow` 25, `maxIncisionHigh` 80, `mountainHeight` 80,
   `terraceStep` 28, `terraceBlend` 0.5, `seaCliffHeight` 22, `diffusion` 0.12. New **bedrock ribs** (`ribHeight`
   7 m): ridged whaleback crests in lowland patches, hard rock so erosion leaves them. **Rocks** (`RockLibrary`,
   `RockScatter`): FantasyValley BigRock/Rock/SmallRock as terrain-tree prototypes with root capsules, scattered by
   `RockScatter.Density` (steep ground, barren patches, high country, stony shores, a few strays), bedded into the
   ground; `rockDensity` in the recipe.
3. **Far trees.** The pack prefabs have 3 LODs culling at 8 % screen height (≈ 150 m), and the tree distance was 450 m.
   `TreeLibrary` now sets the LOD transitions to 0.20 / 0.06 / 0.006 (Sam's Sound of Space values) and the terrain's
   tree distance is 1800 m, full-LOD count 1500. No impostors; if fps suffers at 3440 × 1440, impostors are the next step.
4. **Look** (`WorldLook`, also `Tools/Cabin/Map/Apply Look`): afternoon sun from the south-west (2.1, warm, soft
   shadows) set as the scene sun; Skybox/Procedural sky lighting the scene (ambient Skybox); exp² haze 0.00045 in the
   horizon colour; a global Volume (`Settings/WorldLook.asset`): ACES tonemapping, bloom 0.28 @ 1.05, exposure +0.15,
   contrast 12, saturation 10, white balance +6, vignette 0.22; all URP assets to HDR colour grading; the renderer's
   existing SSAO to intensity 0.7, radius 0.45, downsampled.
