# Terrain and Water Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the stamped-water M1 pipeline with one simulation: eroded terrain, drainage-derived lakes and rivers, waterfalls on hard strata, contour shorelines, ribbon river meshes.

**Architecture:** Coarse 8 m grid for geology (base relief → stream-power erosion → glacial basins → drainage → lakes/rivers/falls as data), then a fine 2 m heightmap (bicubic upsample + damped detail + shore shelves + channel and bed carving) feeding the unchanged painter, vegetation, grass and spawn. Water meshes: lakes as today, rivers and falls as ribbons; the water shader gains a falling mode.

**Tech Stack:** Unity 6.3 editor scripts (C#, `Cabin.Editor.Map`), `Parallel.For`, `MinHeap`, NUnit EditMode tests (`Cabin.Tests.EditMode`), HLSL URP shader. Spec: `docs/superpowers/specs/2026-10-31-terrain-water-redesign-design.md`.

**Verifying:** Unity is open with Coplay. Compile: `mcp__coplay-mcp__get_unity_editor_state` → `hasCompilationErrors`, and `get_unity_logs` with `show_errors`. Tests: run a Coplay script that calls `Cabin.Editor.TestResultLog.Run(TestMode.EditMode)`, then read `Temp/cabin_test_results.txt` (last line `DONE pass=N fail=0`). Generate: a Coplay script calling `Cabin.Editor.Map.MapGenerator.Generate()`, then read the console and `Temp/map_preview.png`.

---

## File structure

Create (all `Assets/_Project/Scripts/Editor/Map/` unless noted, each with `.meta`):
- `Drainage.cs` — priority-flood fill, receivers, topological order, accumulation. Pure.
- `Erosion.cs` — implicit stream-power incision + hardness-weighted diffusion + incision cap. Pure.
- `BaseTerrain.cs` — coarse base heights, highland, inland/offshore, terraces, hardness. Replaces `Relief.cs`.
- `Basins.cs` — glacial lake scoops after erosion. Pure.
- `Lake.cs`, `River.cs`, `Fall.cs` — data.
- `WaterNetwork.cs` — lakes, rivers (traced from mouths upstream), falls; `Downhill` moves here. Replaces `Hydrology.cs`.
- `FineTerrain.cs` — upsample, detail, shore shelf, lake beds, channels, fine water/lakes grids.
- `MapPreview.cs` — hillshade PNG + stats file.
- Tests in `Assets/_Project/Scripts/Tests/EditMode/`: `DrainageTests.cs`, `ErosionTests.cs`, `WaterNetworkTests.cs`, `BasinsTests.cs`, `FineTerrainTests.cs`, `WaterMeshesTests.cs`.

Modify: `Grid.cs` (+`SampleCubic`), `WaterMeshes.cs` (ribbons with falls; drop `RiverLevels`, `Refine`), `Art/Shaders/Water.shader` (fall mode), `Runtime/Map/MapRecipe.cs`, `MapGenerator.cs`, `Tests/EditMode/ForestryTests.cs` (Downhill test → WaterNetwork), `docs/SAM_TODO.md`, `docs/STATUS.md`.

Delete: `Hydrology.cs`, `Relief.cs`, `OsmWater.cs`, `RiverPath.cs` (+ metas), `Assets/_Project/Data/MapRecipe.asset` (regenerated).

Tests: synthetic grids (≤ 64 × 64), no scene, no recipe asset (construct `ScriptableObject.CreateInstance<MapRecipe>()` for defaults).

---

### Task 1: Drainage

**Files:** Create `Drainage.cs`, `Tests/EditMode/DrainageTests.cs`.

- [ ] **Step 1: Tests**

```csharp
[Test] public void TiltedPlane_EverythingDrainsToLowEdge()
// 16×16, h = x (east high), outlet = x == 0. Every Receiver chain ends at x == 0; Accumulation[x==0 column] sums to 16*16.
[Test] public void Pit_FillsToSpill()
// 16×16 plane h = 10, outlet = edge; pit of 3×3 at h = 2 in the middle with one rim cell at h = 6. Filled over the pit == 6 (±2e-3). Filled never below h.
[Test] public void Filled_NeverRisesAlongReceivers()
// seeded random bumps; for every non-outlet cell Filled[Receiver[c]] < Filled[c].
[Test] public void Order_PutsReceiverBeforeCell()
```

- [ ] **Step 2: Implementation**

```csharp
public sealed class Drainage
{
    public readonly int W, H; public readonly float Cell;
    public readonly float[] Filled; public readonly int[] Receiver; public readonly int[] Order; public readonly float[] Accumulation;
    static readonly int[] DX = {1,-1,0,0,1,1,-1,-1}, DZ = {0,0,1,-1,1,-1,1,-1};
    /// Priority flood (Barnes 2014) with epsilon so flats drain; receiver = steepest descent on Filled (always exists: the flood parent).
    public static Drainage Fill(Grid h, Func<int,bool> outlet, float eps = 1e-3f)
    // 1. push outlets with Filled = h; pop lowest; for each 8-neighbour not done: Filled[n] = max(h[n], Filled[c] + eps), Receiver[n] = c (parent), push.
    // 2. Order = pop sequence. 3. Receiver refinement: steepest descent among neighbours with Filled[n] < Filled[c] (drop / distance), else keep parent.
    // 4. Accumulation: all 1, iterate Order backwards: Accumulation[Receiver[c]] += Accumulation[c].
    public bool IsOutlet(int i) => Receiver[i] < 0;
}
```
Note on step 3: steepest-descent receivers are still topologically before the cell in `Order` because `Filled[rc] < Filled[c]` and the heap pops in increasing `Filled`.

- [ ] **Step 3: Compile, run EditMode tests, commit** `git commit -m "Drainage: priority-flood fill, receivers, accumulation"`

---

### Task 2: Erosion

**Files:** Create `Erosion.cs`, `Tests/EditMode/ErosionTests.cs`.

- [ ] **Step 1: Tests**

```csharp
[Test] public void FixedCells_Unchanged()      // sea cells (h<0) keep their height exactly
[Test] public void Channel_EndsLowerThanRidges() // 48×48 tilted plane + random bumps; after 30 iterations the max-accumulation land cell is lower than the mean of cells 6 away either side perpendicular to flow
[Test] public void Deterministic()              // same input twice → identical arrays
[Test] public void Land_NeverBelowMin()         // no land cell below minLand (0.5)
[Test] public void IncisionCap_Holds()          // with maxIncision 3: base - result ≤ 3 + 1e-3 everywhere
```

- [ ] **Step 2: Implementation**

```csharp
public static class Erosion
{
    public struct Settings { public int Iterations; public float Rate; public float Diffusion; public float MinLand; }
    /// h: coarse heights (modified). hardness 0–1. fixedCell: sea / edge. maxIncision: per-cell cap on total lowering (base − result).
    public static void Run(Grid h, Grid hardness, Func<int,bool> fixedCell, Grid maxIncision, Settings s)
    // base = copy of h. Each iteration:
    //  d = Drainage.Fill(h, fixedCell)
    //  for c in d.Order: if fixed skip; rc = d.Receiver[c]; L = diag? 1.41421 : 1 (cells); soft = 1.15 - hardness[c];
    //     k = s.Rate * soft * sqrt(d.Accumulation[c]) / L;  h[c] = (h[c] + k*h[rc]) / (1+k);  h[c] = max(h[c], s.MinLand)
    //  diffusion (double buffer, parallel): h[c] += s.Diffusion * soft * (sum4(h[n]) - 4h[c]); fixed cells skipped
    //  cap: h[c] = max(h[c], base[c] - maxIncision[c])
}
```

- [ ] **Step 3: Compile, tests, commit** `"Erosion: implicit stream power with hardness and diffusion"`

---

### Task 3: Water network data and tracing

**Files:** Create `Lake.cs`, `River.cs`, `Fall.cs`, `WaterNetwork.cs`, `Tests/EditMode/WaterNetworkTests.cs`. Modify `ForestryTests.cs` (move the `Downhill` test, now `WaterNetwork.Downhill`).

- [ ] **Step 1: Data**

```csharp
public sealed class Lake { public int Id; public float Level; public List<int> Cells = new(); public int Outlet = -1; public float WidthMetres; }
public sealed class River {
    public List<Vector2> Points = new(); public List<float> Levels = new(); public float Width, Depth; public float CatchmentKm2;
    public int EndsInLake = -1; public bool EndsAtSea; public int JoinsRiver = -1; public List<Fall> Falls = new(); }
public sealed class Fall { public Vector2 Lip, Base; public float LipLevel, BaseLevel, Width; public float Height => LipLevel - BaseLevel; }
```

- [ ] **Step 2: Tests**

```csharp
[Test] public void BowlWithNotch_OneLakeOneOutletRiver()   // 32×32 plane h=10 sloping to sea at z==0 (h<0 row); bowl depth 6 radius 5 in the middle; a notch of h=7 cut on its downhill rim. Expect: 1 lake, Level ≈ 7 (±0.01), every lake cell h < Level; ≥1 river starting at the lake's Outlet and EndsAtSea; its Levels never increase.
[Test] public void SmallHollow_NoWaterAndGroundRaised()     // a 2×2 hollow below lakeMinArea → 0 lakes, h there == filled + 0.1
[Test] public void StepChannel_OneFallOfStepHeight()        // river over a 5 m step → 1 Fall, Height within 0.3 of 5
[Test] public void Smoothing_KeepsLevelsNonIncreasing()
[Test] public void Downhill_PoolsAdjacentViolators()        // moved from ForestryTests verbatim
```

- [ ] **Step 3: Implementation**

```csharp
public sealed class WaterNetwork
{
    public struct Settings { public float LakeMinDepth /*0.25*/, RiverCatchmentKm2 /*0.3*/, RealMetresPerMetre, MaxRiverSlope /*0.12*/, FallMinHeight /*2.5*/, FallRun /*24*/; public int LakeMinCells /*20*/; }
    public readonly List<Lake> Lakes = new(); public readonly List<River> Rivers = new(); public readonly int[] LakeOf; public readonly Drainage Drain;
    public static WaterNetwork Build(Grid h, Func<int,bool> sea, Settings s)
    // 1. d = Drainage.Fill(h, sea). 2. Lakes: BFS components of (Filled - h ≥ LakeMinDepth) with |Filled - seed Filled| < 3e-3; small → raise h to Filled+0.1 (and redo nothing: those cells no longer count); each lake: Level = min Filled in it; Outlet = first cell reached by following receivers out of the lake.
    // 3. River cells: Accumulation*cellKm2 ≥ RiverCatchmentKm2 && h ≥ 0.2 && LakeOf < 0 && !sea. Mouths: river cells whose receiver is sea / a lake cell / outlet. From each mouth walk upstream: donors = river cells with Receiver == c; main = max Accumulation; others → tributary mouths (JoinsRiver = this). Points = cell centres (mouth last), Levels = Filled (eps-filled ⇒ non-increasing downstream).
    // 4. Width = clamp(2 + 4√A_km2, 3, 18), Depth = 0.6 + 0.12·Width. Tributary end level := main level at join, propagated upstream at ≤ MaxRiverSlope (JoinTributaries).
    // 5. Smooth: Chaikin ×2 on Points (keep endpoints), Levels resampled by arc length, then running minimum from the mouth. 6. Falls: windowed drop over FallRun ≥ FallMinHeight → one Fall per steep stretch (Lip = first steep point, Base = last).
    public static void Downhill(List<float> v) // from Hydrology
}
```
Direction note: rivers are stored **source → mouth** (index 0 = head), matching the old `RiverPath` and `WaterMeshes.Rivers`.

- [ ] **Step 4: Compile, tests, commit** `"WaterNetwork: lakes from drainage, rivers traced from mouths, falls"`

---

### Task 4: Base terrain and basins

**Files:** Create `BaseTerrain.cs` (from `Relief.cs`), `Basins.cs`, `Tests/EditMode/BasinsTests.cs`. Modify `MapRecipe.cs` (new fields, see Task 7 list).

- [ ] **Step 1: Tests (Basins)**

```csharp
[Test] public void NoScoop_NearSeaOrOnHighland()   // cells with h<1.5 or highland>0.6 unchanged
[Test] public void Scoops_AreDepressionsOnGentleLowland() // some cells lowered, none raised, lowering ≤ lakeDepth
[Test] public void Deterministic()
```

- [ ] **Step 2: BaseTerrain**

```csharp
public sealed class BaseTerrain
{
    public readonly Grid Heights, Hardness, Highland, Inland, Offshore, Slope;  // coarse
    public static BaseTerrain Build(MapRecipe r, Grid real /*coarse, masked*/)
    // highland = blur(max(real,0), regionScale)/regionHigh clamped; inland/offshore chamfers (from Relief).
    // base = Curve(real), sea shelf as MapGenerator.Heights did. Relief (per Relief.At without the dry/waterCalm terms):
    //   rise = lowBroad·fbm(900); hill = lowHill·lerp(1, highHillGain, highland)·fbm(230); ridge = mountainHeight·mountainous·ridged^2.2
    //   terrace: t = floor(hRel/terraceStep + warp)·terraceStep; h = lerp(h, t, terraceBlend·highland)
    //   shore fade smoothstep(shore/shoreCalm); sea cliffs as before; floor 0.8 + base·0.3.
    // hardness: lowland 0.35 + 0.15·fbm; highland: phase = frac((h + warp)/terraceStep); hard where phase > 1 − hardBand/terraceStep → 0.95 else 0.25; blended by highland.
}
public static class Basins
{
    public static void Scoop(Grid h, BaseTerrain b, MapRecipe r, int seed)
    // density = (1−highland)·smoothstep(12°,6°,slope)·(0.5+0.5 fbm(2500))·bias(SW interior: smoothstep on normalised x,z towards the recipe's lakeCountryCentre)
    // ponds: f = fbm(warped, pondScale); e = (f − pondThreshold)/(1 − pondThreshold); if e>0: h −= density·pondDepth·smoothstep(0,1,e·2)
    // lakes: same with lakeScale/lakeThreshold/lakeDepth. Skip h<1.5 or offshore-adjacent (inland<20).
}
```

- [ ] **Step 3: Compile, tests, commit** `"Base terrain with terraces and hardness; glacial lake basins"`

---

### Task 5: Fine terrain

**Files:** Modify `Grid.cs` (+`SampleCubic`). Create `FineTerrain.cs`, `Tests/EditMode/FineTerrainTests.cs`.

- [ ] **Step 1: Tests**

```csharp
[Test] public void SampleCubic_ReproducesLinearRamp()  // exact on h = x
[Test] public void LakeCells_BelowLevelAndConnected()  // small synthetic network → every fine water cell with LakeOf ≥ 0 has h < L and is BFS-reachable from the seed
[Test] public void RiverBed_BelowSurfaceAlongPath()    // sample 50 points along each river: ground < level − 0.2
[Test] public void NoNaNHeights()
[Test] public void ShoreIsGentle()                     // ≥ 90 % of cells within 2 m of a lake shoreline have slope < 20°
```

- [ ] **Step 2: Implementation**

```csharp
public sealed class FineTerrain
{
    public readonly Grid Heights, Water /*lakes+rivers level, NaN dry*/, Lakes /*lakes only*/;
    public struct Settings { public float DetailHeight, DetailScale, RockDetailHeight, RockDetailScale, RockFrom, RockFull, ShelfBand /*1.2*/, ShelfKeep /*0.35*/, BankWidth /*4*/, LakeDepthSlope /*0.12*/, LakeMaxDepth /*7*/; public int Seed; }
    public static FineTerrain Build(Grid coarse, WaterNetwork net, Settings s, int fineW, int fineH, float fineCell)
    // 1. heights[x,z] = coarse.SampleCubic(wx, wz) (parallel).
    // 2. levelNear (coarse): seeds = lake cells (Level) and river cells (Filled); chamfer spread ≤ 40 m carrying the level.
    // 3. detail: land only; damp = smoothstep(0.3, 2.5, |h − levelNear|) (1 if NaN); h += damp·(fbm detail + rocky ridged).
    // 4. shelf: for lake levels L within ShelfBand: d = h − L; h = L + sign(d)·(|d| ≤ band ? band·ShelfKeep·(|d|/band) blended smoothly to |d| at band) — implement as h = L + d·lerp(ShelfKeep, 1, smoothstep(0.5, 1, |d|/band)).
    // 5. lake mask per lake: BFS on fine cells h < L seeded from the lake's coarse cells' fine indices; LakeOf[fine]. dShore = chamfer. h = min(h, L − min(LakeMaxDepth, 0.3 + dShore·LakeDepthSlope)). Water = L, Lakes = L.
    // 6. channels: fine arrays dist/level/half/depth/fallness (float, init +inf); rasterise each river segment (and each fall segment with the stepped level rule) over its box of half+BankWidth. Then parallel pass:
    //    d<half: bed = s − depth·(1 − (d/half)²)·(fall base pool: ×1.8 within 1.5·half of Base); h = min(h, bed); Water = s.
    //    half ≤ d < half+BankWidth: profile = lerp(s+0.15, h, smoothstep(0,1,(d−half)/BankWidth)); h = min(h, profile); if d ≤ half+1.5: h = max(h, s + 0.2).
    //    Skip cells with LakeOf ≥ 0.
}
```
`Grid.SampleCubic`: Catmull-Rom on the 4×4 neighbourhood, clamped indices.

- [ ] **Step 3: Compile, tests, commit** `"Fine terrain: cubic upsample, damped detail, shore shelves, lake beds, channels"`

---

### Task 6: Ribbons, falls, shader

**Files:** Modify `WaterMeshes.cs` (replace `Rivers(List<RiverPath>, Grid)` with `Rivers(List<River>, Grid ground, Rect tile)`; delete `RiverLevels`, `Refine`), `Art/Shaders/Water.shader`. Create `Tests/EditMode/WaterMeshesTests.cs`.

- [ ] **Step 1: Tests**

```csharp
[Test] public void Ribbon_NoNaN_LevelsMatch()  // straight river 10 points: vertices.y == Levels, no NaN, 3 verts per point
[Test] public void Fall_VerticesCarryFallness() // river with one Fall: depth.y == 1 on vertices between Lip and Base, 0 well away
```

- [ ] **Step 2: WaterMeshes**
Keep the existing ribbon logic (`Side`, no-step-back edges, flow from fall). Changes: input `River`; half = `Width/2 + 0.6`; `depths` y = fallness: 1 between Lip and Base of any Fall, fading over 3 m before the Lip and 6 m after the Base; extra plunge-pool foam is the shader's job from depth. Only points whose x/z fall in `tile` (grown by 20 m) are emitted so each tile gets its own mesh (`River_{tx}_{tz}`).

- [ ] **Step 3: Shader**
`Attributes.depth.y` → `Varyings.fall`. In `Frag`: `float fall = i.fall;` falling normal: `Waves(float2(xz.x*3, xz.y*3 + t*_FallSpeed/_WaveScale))` with `_FallSpeed ("Fall speed (m/s)", Float) = 3`; `nTS = lerp(nTS, fallN, fall)`; `foam = max(foam, fall·(0.55 + 0.45·broken))`; `alpha = max(alpha, fall·0.95)`; colour lerped towards `_FoamColor` by `fall·0.6`.

- [ ] **Step 4: Compile, tests, commit** `"River ribbons per tile with waterfall sheets; falling-water shader mode"`

---

### Task 7: Recipe, generator, preview, cleanup

**Files:** Modify `MapRecipe.cs`, `MapGenerator.cs`, `docs/SAM_TODO.md`. Create `MapPreview.cs`. Delete `Hydrology.cs`, `Relief.cs`, `OsmWater.cs`, `RiverPath.cs` and metas, `Assets/_Project/Data/MapRecipe.asset`.

- [ ] **Step 1: MapRecipe**
Remove: `lakeMinSamples, lakeDepthSlope, lakeMaxDepth, riverCatchmentKm2, riverMinWidth, riverMaxWidth, riverMinDepth, riverMaxDepth, waterCalm, riverSmoothing, valleySlope, broadHeight, hillHeight`.
Add:
```csharp
[Header("Base relief")] public float coarseCell = 8f, lowBroad = 20f, lowHill = 8f, highHillGain = 1.6f, terraceStep = 30f, terraceBlend = 0.35f, terraceWarp = 40f, hardBand = 10f;
[Header("Erosion")] public int erosionIterations = 60; public float erosionRate = 0.02f, diffusion = 0.08f, maxIncisionLow = 15f, maxIncisionHigh = 70f;
[Header("Lake basins")] public float pondScale = 250f, pondThreshold = 0.55f, pondDepth = 4f, lakeScale = 900f, lakeThreshold = 0.68f, lakeDepth = 8f; public Vector2 lakeCountryCentre = new Vector2(-0.3f, -0.35f) /* normalised map coords, SW interior */; public float lakeCountryBias = 0.6f;
[Header("Rivers and falls")] public float riverCatchmentKm2 = 0.3f, maxRiverSlope = 0.12f, fallMinHeight = 2.5f, fallRun = 24f; public int lakeMinCells = 20;
[Header("Shores")] public float shelfBand = 1.2f, shelfKeep = 0.35f, bankWidth = 4f, lakeDepthSlope = 0.12f, lakeMaxDepth = 7f;
```
Keep: source/world/curve/smoothing/detail/rock fields, `regionScale, regionHigh, warpAmount, mountainHeight, shoreCalm, seaCliffHeight, landAbove, seed, dirtSlope, rockSlope, beachHeight`.

- [ ] **Step 2: MapGenerator.Generate**
Stages with progress bar: real elevation on the coarse grid (sample + blur + ProvinceMask) → `BaseTerrain.Build` → `Erosion.Run` (fixed = sea cells h<0 or edge; maxIncision = lerp(low, high, highland)) → `Basins.Scoop` → `WaterNetwork.Build` → `FineTerrain.Build` → `Tiles` (unchanged, takes `fine.Heights`, `fine.Water`) → `Spawn` (prefers within 80 m of a lake ≥ 150 m: use `net.Lakes` with `WidthMetres = 2·sqrt(cells·cell²/π)`) → vegetation/grass unchanged → `BuildScene(r, datas, fine.Lakes, net.Rivers, fine.Heights, spawn)` with per-tile `WaterMeshes.Rivers`. Then `MapPreview.Write(fine, net, r)`. Log stats.

- [ ] **Step 3: MapPreview**
`Tools/Cabin/Map/Preview PNG` and `Write(...)`: 1 px per 4 fine cells (1024 × 896). Hillshade from the fine slope (light from NW), land tinted by height (green low → brown high), sea dark blue, lakes/rivers `(0.2, 0.5, 0.9)`, falls 3 px red dots, spawn a yellow dot. `File.WriteAllBytes("Temp/map_preview.png", tex.EncodeToPNG())`. Stats to `Temp/map_stats.txt`: lakes by size band, rivers, km of river, falls (with heights), % land < 25°, generation seconds.

- [ ] **Step 4: Delete old files, fix references, SAM_TODO** (remove the OpenStreetMap credit item). Compile clean, all EditMode tests pass. Commit `"Generate World from the simulation pipeline; preview PNG; OSM pipeline removed"`.

---

### Task 8: Generate and tune

- [ ] Delete `Assets/_Project/Data/MapRecipe.asset`; run `MapGenerator.Generate()` via Coplay; read the console for timing and stats; read `Temp/map_preview.png`.
- [ ] Tune against targets: 15–30 lakes > 300 m, many ponds, 5–15 falls, highland valleys 40–70 m deep, lowland 8–15 m, generation < 120 s, no NaN/NRE. Adjust `erosionRate`, thresholds, `terraceStep`, `hardBand`. Each pass: edit defaults in `MapRecipe.cs`, delete the asset, regenerate, look. Commit each accepted pass.
- [ ] Sanity in the editor via Coplay: hierarchy has Tile_x_z, Water/Sea, Water_x_z, River_x_z; `get_unity_logs` has no errors; open the World scene so Sam can press Play.

### Task 9: Docs

- [ ] Update `docs/STATUS.md` M1 section (pipeline, how to play, what to look at, not played), `docs/SAM_TODO.md` playtest item for the map. Commit `"STATUS: terrain and water redesign"`.
