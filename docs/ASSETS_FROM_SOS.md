# Sound of Space vegetation audit (read-only), 2026-10-30

Source: `C:\2.0 SOUND OF SPACE\1aughhh1` (Unity 2022.3.62f3, **Built-in RP**: `m_CustomRenderPipeline: 0`). Nothing in that folder was modified.
Paths below are relative to `…\1aughhh1\Assets\`. "FF" = `5 - External Imports/Nature & Trees/FantasyForest`, "FV" = `…/FantasyValley`, "CG" = `…/CartoonGrass`, "LPNP" = `…/Low Poly Nature Pack`.

## How I checked
- Built a GUID index: every `.meta` GUID mapped to its asset, and every YAML `.unity/.prefab/.asset/.mat/...` mapped to the GUIDs it references (12,677 assets, 4,733 files with references).
- **Used** means reachable through the reference graph from a scene in `EditorBuildSettings` (31 scenes; main gameplay = `1.6.7.7.7.unity`, Humble Abode). That covers nested prefabs, variants, FBX meshes, materials and textures. References from a pack's own demo scenes and from `TreeGallery.unity` (Sam's picker scene) don't count. I also checked FBX-GUID references, because some builder scripts unpack prefabs.
- Read the spawner and chop code: `3 - Scripts/World/TreeSpawner.cs`, `SpawnedTree.cs`, `TreeRegistry.cs`, `Editor/HumbleAbodeTreeVariants.cs`, `Editor/TreeGalleryBuilder.cs`, the Grass/Mushroom/Crystal spawners, and the docs `docs/TREE_GALLERY.md` and `docs/CURRENT_STATE_AUDIT.md` §16.4.
- Triangle and vertex counts come from parsing the binary FBX files directly. Vertex counts are FBX control points; Unity will split more at seams. Species calls are based on silhouette (crown radius per height band) plus the bark and crown colour sampled from the atlas at the mesh's UVs.

**Headline:** Sam already did this selection once. On 2026-09-04 his Tree Gallery laid out all **99** tree prefabs in the project and he picked **18**, all 8 FantasyForest trees and all 10 FantasyValley trees, and ranked them. `HumbleAbodeTreeVariants.cs` wraps each one in a variant (`1 - samsPrefabs/Trees/HA_FF_Tree_0N` / `HA_FV_Tree_NN`) and wires them with rank weights into the `TreeSpawner` in `1.6.7.7.7.unity`. 18 of 99 is roughly the "10%" Sam remembered. No other tree is used anywhere in the shipped game.

---

## 1. USED in Sound of Space (port these)

### 1a. Trees: 18, all spawned by `TreeSpawner` (seed-hash cells, 34 m, chance 0.75, ×1.3 density)
Where used: `1.6.7.7.7.unity` (Humble Abode, the main gameplay scene) and `1 - samsPrefabs/TutorialSpawners/TreeSpawner.prefab` (used by `Tutorial.unity`, `Proto_Junker.unity` and `Proto_Junker_Compact`). Planets excluded: Sun, Constant Companion, Watchful Eye, Tumbling Bean. Every other body gets the same 18-tree mix.

**Common to all 18:**
- Each prefab has a **LODGroup with 3 LODs**. Pack transitions are 0.60 / 0.27 / cull. Sam's HA variants retune them to 0.20 / 0.06 / 0.01 with cross-fade, because the pack defaults culled trees at 100–150 m.
- Each has one **BoxCollider** on the trunk (1–2 m square).
- Each uses **one material per pack**: `FF/Materials/FantasyForest_MAIN.mat` and `FV/Materials/FantasyValley_MAIN.mat`. The shader is **Built-in Standard** (fileID 46): opaque, no alpha cutout. The only texture is a **1024² gradient palette atlas** (`FantasyForest_Texture_01.png` / `FantasyValley_Texture_01.png`, imported at max 1024). This converts to URP trivially.
- HA variant scale: FF ×(1.45, 1.8, 1.45), FV ×1.4 uniform. The spawner adds a per-tree 0.85–1.25 size factor and a 0.9–1.15 stretch.
- **Chopping:** `SpawnedTree` is added at runtime to whatever prefab is in `treePrefabs`, with 4–8 HP. Felling is **procedural** (`FallAndShrink` coroutine, 0.7 s): the tree falls, shrinks and drops Wood plus 1–3 species saplings. **No stump, log or falling-tree prefabs exist.** `TreeRegistry` keys species by prefab name.

| Rank (Sam) | SoS variant → source prefab | Type (my read) | Size w×h m (pack scale) | Tris LOD0 / LOD1 / LOD2 | Bark / crown colour |
|---|---|---|---|---|---|
| FF #1 (wt 8) | HA_FF_Tree_05 → FF/Prefabs/Vegetation/Tree_05 | **Tall conifer, mature spruce/fir.** Bare lower trunk, cone above | 4.6 × 13.0 | 14,734 / 5,784 / 474 | brown / dark olive |
| FF #2 (7) | HA_FF_Tree_04 → FF Tree_04 | **Tall conifer.** Same form, slightly slimmer | 3.8 × 12.3 | 12,654 / 5,095 / 516 | brown / dark olive |
| FF #3 (6) | HA_FF_Tree_01 → FF Tree_01 | **Conifer, full skirt to the ground** (young spruce) | 4.3 × 8.3 | 9,141 / 5,212 / 454 | / olive |
| FF #4 (5) | HA_FF_Tree_02 → FF Tree_02 | Conifer, full cone | 3.5 × 7.6 | 9,024 / 3,004 / 366 | |
| FF #5 (4) | HA_FF_Tree_03 → FF Tree_03 | Conifer, cone on a short trunk | 3.9 × 8.1 | 11,204 / 4,104 / 304 | |
| FF #6 (3) | HA_FF_Tree_06 → FF Tree_06 | Squat, bushy conifer (young or understory) | 3.8 × 4.7 | 10,552 / 2,615 / 112 | |
| FF #7 (2) | HA_FF_Tree_07 → FF Tree_07 | **Narrow full cone** (balsam-fir look) | 2.7 × 8.1 | 9,804 / 1,628 / 524 | |
| FF #8 (1) | HA_FF_Tree_08 → FF Tree_08 | **Very narrow spire** (black-spruce look); cheapest FF | 1.8 × 7.0 | 4,240 / 1,380 / 253 | |
| FV #1 (10) | HA_FV_Tree_03 → FV/Prefabs/Vegetation/Tree_03 | Big spreading broadleaf (oak/maple). **Heaviest asset** | 9.1 × 10.0 | 18,002 / 6,544 / 772 | dark brown / green |
| FV #2 (9) | HA_FV_Tree_08 → FV Tree_08 | **Birch** (white-grey bark #bfbfbf). Tall, narrow, crown up high | 3.3 × 9.4 | 5,318 / 2,341 / 208 | **white** / light green |
| FV #3 (8) | HA_FV_Tree_06 → FV Tree_06 | Round-crown broadleaf (maple stand-in) | 5.3 × 7.2 | 5,960 / 2,444 / 344 | brown / yellow-olive |
| FV #4 (7) | HA_FV_Tree_09 → FV Tree_09 | **Bare / dead tree** (crown is brown branches, no leaves) | 5.3 × 7.4 | 3,614 / 2,150 / 846 | brown / brown |
| FV #5 (6) | HA_FV_Tree_01 → FV Tree_01 | Round-crown broadleaf | 3.5 × 7.5 | 10,534 / 4,844 / 263 | brown / green |
| FV #6 (5) | HA_FV_Tree_02 → FV Tree_02 | Narrow broadleaf | 2.8 × 7.4 | 7,826 / 2,723 / 282 | brown / green |
| FV #7 (4) | HA_FV_Tree_04 → FV Tree_04 | Round-crown broadleaf | 4.7 × 7.3 | 7,304 / 2,776 / 378 | |
| FV #8 (3) | HA_FV_Tree_05 → FV Tree_05 | Round-crown broadleaf | 3.8 × 7.2 | 3,924 / 2,000 / 262 | |
| FV #9 (2) | HA_FV_Tree_10 → FV Tree_10 | **Bare / dead snag**, crown only at the top | 3.4 × 6.4 | 2,150 / 844 / 212 | brown / brown |
| FV #10 (1) | HA_FV_Tree_07 → FV Tree_07 | **Birch** (white-grey bark). Tall and narrow | 3.5 × 9.3 | 7,176 / 3,035 / 362 | **white** / light green |

FBX paths: `FF/Models/Vegetation/Tree_0N.fbx` and `FV/Models/Vegetation/Tree_NN.fbx`. Each holds `Tree_NN_LOD0/1/2` meshes.

### 1b. Grass (used, but the shader is blocked; see §3)
| Asset | Used by | Tris | Material / shader |
|---|---|---|---|
| `CG/Models/Grass_01.fbx`, `Grass_02.fbx`, `Grass_03.fbx` | `GrassSpawner` + `InstancedGrassRenderer` (GPU instanced, compute cull) in `1.6.7.7.7.unity` and `TutorialSpawners/Grass.prefab`. Baked placement in `BakedPlanets/Humble_Abode_Grass.bytes` | 672 / 686 / 1,210 (LOD0) | `CG/CG_GameGrass.mat` → **custom `CartoonGrass/SimpleGrass`** (`CG/Shaders/CG_SimpleGrass.shader`). Colour is a vertex-colour-R gradient; no texture |

### 1c. Mushrooms (used)
`Nature & Trees/Low Poly Mushrooms Pack/Prefabs/Mushrooms/*`: all **23** prefabs, listed in `MushroomSpawner` (scene and `TutorialSpawners/MushroomSpawner.prefab`). Materials are embedded in the FBX, so they import as Standard and become URP Lit automatically. About 240–470 tris each, no LODs. Documentation is at `…/Low Poly Mushrooms Pack/Documentation/Documentation.pdf`. The pack also has unused `Other/log, log2, log3` (≈300 tris) and `Rock1–6`.

### 1d. Rocks (used)
| Asset | What | Tris | Shader |
|---|---|---|---|
| `1 - samsPrefabs/Wooden Survival Tools/Stone Ore - Boulder Cluster / Layered Outcrop / Split Formation.prefab` | Sam's own flat-shaded rock formations; "stone formations spawn naturally on Humble Abode". Mesh `.asset` files are regenerable via `tools/WoodenToolBuilder.cs` | 536 / 636 / 532 | Standard (`Weathered Flint`, `Stone Crevices`, `Pale Mineral Seams`, `Chipped Stone Faces`) |
| `1 - samsPrefabs/Cave/Pebble/Cave_Pebble.prefab` (+ `Cave_01`, `Cave/Moon/Cave_Moon`) | Generated cave-mouth rocks | ~6k per rock mesh | `Cave_Rock.mat` is Standard; the Moon cave uses custom `Shaders/CaveMoonRock.shader` |
| `Nature & Trees/Stylized Crystal/Prefab/crystal_17_2.prefab` | `CrystalSpawner` | n/a | not relevant |

### 1e. Used only in the `4 - Scenes/BasskProShops_Test.unity` test scene (not in build settings; instances unpacked by `Editor/BasskProShopsBuilder.cs`)
- LPNP `Trees/Tree4A`, `Tree5A`, `Tree9A`, `Tree13A`: Sam's conifer picks from LPNP. 4A and 5A are a pole trunk with a cone crown.
- LPNP `Boulders/Boulder01–08`; `Plants/Plant01A, 02A, 05A, 06A, 07A`.
- FF `Props/Campfire_01`, `Props/Firewood_01`.

### 1f. Other used assets that matter for a cabin game
- `1 - samsPrefabs/Buildings/house.prefab`: built from LPNP `Prefabs/Cabin/` log-cabin pieces (Floor01, Roof01, Wall01/03/09/10, Window02). Standard / `Atlas_Mat`.
- `1 - samsPrefabs/Axe.prefab` uses `Village & Buildings/Low Poly Medieval Environment Pack (35+ Props)/Models/Axe_Log.fbx`. `Wooden Axe` / `Stone Axe` are Sam-generated.
- `Low Poly Survival Pack/Models/Torch1.fbx` (torch); the medieval `Bonfire.prefab`.

---

## 2. OWNED BUT UNUSED: worth a look

**Why unused:** Sam walked all 99 trees in the Tree Gallery (2026-09-04) and picked only FF and FV. The bundle had only been fully imported on 2026-08-31 as "raw imports, nothing placed". The docs record no other reasons.

| Asset | Pack | Why it's interesting for CabinGame | Notes |
|---|---|---|---|
| **FF `Prefabs/Vegetation/Stump_01, Stump_02`** | FantasyForest | **M4 stump** in the same style and atlas as the conifers | 828 / 556 tris, MeshCollider, Standard |
| **FV `Stump_01, Stump_02`** | FantasyValley | Broadleaf stumps | 696 / 774 tris |
| **FF `Log_01–03`, FV `Log_01–02`** | FF / FV | Fallen-log forest-floor dressing. Bucked lumber should still be generated at real dimensions | 322–942 tris |
| **FV `Branch_01–03`** | FV | Ground debris / slash | 382–542 tris |
| **FV `BigRock_01–05`** (4–6.5 m), `Rock_01–05` (0.6–1.6 m), `SmallRock_01–05` | FV | Rocky-hill boulders sharing the FV atlas, so one material for all | 146–350 / 80–108 / ~80 tris, with LODs |
| FF `Landscape/Rock_01–06`, `Mountains_01–06`, `MagicRock_*` (skip MagicRock) | FF | Small rocks; low-poly backdrop mountains | Rocks 88–138 tris; Mountains 620–2,960 |
| FF `Plant_01–08`, `Grass_01–02`, `Flower_01–05`; FV `Plant_01–12`, `Grass_01–02`, `Flower_01–05`, `PondPlant_01–06`, `Mushroom_01–06` | FF / FV | Understory, ground cover, lake-edge plants. The FV atlas has fern fronds | Mostly under 2k tris. **Avoid FV `Plant_06` (7,690 tris)** and FF `Plant_04` (3,724). Flowers, FF Plant_07–10, FF Log_01 and both packs' Stump_01 also use `*_Nature.mat`, which is Standard with `_ALPHATEST_ON` (cutout). It still converts to URP Lit, but set Alpha Clipping |
| CG `Prefabs/Grass_04–08`, `Plant_01–13`, `Flower_01–06`, `Rock_01–02`; `Terrain/CartoonGrass_GRASS/MUD.terrainlayer` | CartoonGrass | More grass and detail meshes; terrain layers | Grass materials point at the blocked SimpleGrass shader (§3); plant materials are Standard |
| Terrain textures: FF/FV/CG `*_Grass`, `*_Mud`, `*_Sand` (+`_Normal`), all 1024² | FF / FV / CG | Stylised ground that matches the atlas look | Plain PNGs |
| **LowPolyFantasyArena2 `Prefabs/Nature/Tree_01–03`** | Arena2 | **Lower-poly copies of FF Tree_01 / 03 / 05** (same silhouette and height): 2,245 / 2,350 / 3,379 tris LOD0 vs 9–15k. Use as cheap far LODs or a perf fallback. `Tree_04` ≈ FV Tree_09 (bare) at 1,338 tris | 3 LODs, Standard; FBX in cm, scaled by the prefab |
| CartoonMedievalTown `Prefabs/Nature/Tree_01–02` | CMT ("LowPolyFantasyVillage2") | **Exact duplicates** of FV Tree_03 / Tree_04 (identical tri counts). Ignore | |
| LPNP `Trees/Tree01A–13C` (39), `Cliffs/Cliff01–04` + `Cliffs`, `Boulders/Boulder01–08`, `Rocks/Rock01–05`, `Plants/Plant01A–17D` | LPNP (BD Studios) | Conifers **Tree4A/5A/9A/13A** (Sam used these in the Bassk test); low-poly cliffs (≈146 tris) for cliff bands | Brighter, flatter, saturated style that clashes with FF/FV. Trunk and leaves are separate meshes. All Standard `Atlas_Mat` (2000² atlas), all have LODGroups. Readme: `LPNP/Readme.txt` |
| LowPolyFantasyVillage `Prefabs/Nature/Tree_01–10` | Village | Tree_07/08/10 are conical (conifer-ish) | Older, simpler style. 160–3,400 tris |
| Medieval Env Pack `Tree_01–03.2`, Medieval Market `tree_01–03`, LowPolyDungeons `Tree_01–03`, FantasyArena1 `Tree_01–02` | various | Small stylised trees (2–7 m) | Off-style. Skip |
| TropicalEnvironment palms, bananas, bamboo | Tropical | Not Nova Scotia | Skip |
| `5 - External Imports/Prefabs/Tree1, Tree2, Rock1, Rock2` | Celestial-body starter | Placeholders | Skip |
| FF `Props/Ax_01`, `Firewood_01`, `Campfire_01`, `Tent_*`, `Boat_01`, `Paddle_01`, `Fence_*` | FF | Cabin-life props in the same style (axe in hand, firewood pile, canoe/paddle by the lake) | Out of M2 scope, but noted |

**Licence files:** none of these packs ship one. That covers FantasyForest, FantasyValley, CartoonGrass, Arena2, CMT and Village (LowPolyFantasyBundle), plus LPNP and the Mushrooms pack. They are Unity Asset Store purchases under the standard Asset Store EULA. The only docs are `_Pack Installers/LowPolyFantasyBundle/README_LowPolyFantasyBundle.pdf` (a Canva PDF; text not extractable), `LPNP/Readme.txt` (BD Studios, publisher 60190) and the Mushrooms `Documentation.pdf`. CabinGame's rule ("licence file with each pack") therefore needs a **SAM_TODO**: add an EULA note with the order/invoice reference to each `Assets/External/<Pack>/`.

---

## 3. BLOCKED (custom Built-in shaders; do not port)

| Shader | Used by | Why blocked |
|---|---|---|
| `CG/Shaders/CG_SimpleGrass.shader` ("CartoonGrass/SimpleGrass") | `CG/CG_GameGrass.mat` (the live grass) and the CG `Grass_*_LOD0/LOD2` materials | Hand-written Built-in surface shader with SoS-only globals: `_GrassFillStrength`/`_GrassFillColor`, flashlight cookie, lantern, concert spot and planet-terminator terms, `GRASS_MAX_POINT_LIGHTS`. Written because the pack's Shader Graphs rendered black in Built-in |
| `CG/Shaders/CG_GrassDepth.shader` | `CG/CG_GrassDepth.mat` | Built-in depth pre-pass for the grass system |
| `3 - Scripts/World/InstancedGrassRenderer.cs` + `GrassCull.compute`, `GrassSpawner.cs` | SoS grass system | Built for a sphere planet with baked `.bytes`. The handoff wants terrain details instead |
| `LPNP/Shaders/Wind Shader/BDStudiosWind.shader` | `LPNP/Materials/Wind.mat` only (**no prefab uses it**) | Built-in surface shader. Irrelevant: every LPNP prefab uses Standard `Atlas_Mat` |
| `Shaders/CaveRock.shader`, `CaveMoonRock.shader` | SoS cave rocks | SoS-specific |
| `Celestial Body/*` planet shaders | Planet terrain | Sphere-planet specific |

The Bassk / Football / Firefly custom shaders are not vegetation.

---

## 4. Recommendation

### 4.0 Biggest finding: the URP versions are already on disk
Next to each imported pack in SoS are the publisher's **URP installers**:
- `FF/FantasyForest_2021.3_URP_1.02.unitypackage` (27 MB)
- `FV/FantasyValley_2021.3_URP_1.02.unitypackage` (37 MB)
- `CG/CartoonGrass_2021.3_URP_1.02.unitypackage` (6 MB)
- `TropicalEnvironment/..._URP_1.05.unitypackage`

SoS imported the Built-in ("SRP") builds from `_Pack Installers/LowPolyFantasyBundle/`. The URP packages contain Shader Graphs: `FantasyForest_Tree_LOD0/1/2`, `_Vegetation_*`, `_Grass_*`, and the FV and CG equivalents. The tree graph uses Time + GradientNoise + Position + VertexColor nodes, i.e. **vertex-colour wind sway**. They also fix CartoonGrass grass, which only went black under Built-in.

There are two ways to bring them into CabinGame:
- **(A, preferred)** Import the URP packages, *selectively*: Models, Prefabs, Materials, Shaders and Textures, under `Assets/External/<Pack>/`. **Exclude** `Scenes/Settings/URP_*.asset` and the `*_Renderer.asset` files, because CLAUDE.md requires asking before URP settings change. The graphs are 2021.3-era (legacy `PBRMasterNode`), so Unity 6 will auto-upgrade them; verify nothing renders pink or black.
- **(B, fallback)** Copy the Built-in prefabs and FBX files and convert Standard to URP/Lit. That is trivial here: one material and one 1024² atlas per pack, no alpha on the trees. You lose wind.

Either way, importing a pack needs Sam's OK (CLAUDE.md: "ask before importing an asset pack wholesale").

**For CabinGame terrain trees (handoff §8.2):**
- Swap each BoxCollider for a CapsuleCollider.
- Keep the 3-LOD LODGroup and adopt Sam's SoS transitions as a starting point: 0.20 / 0.06 / cull 0.01.
- There is no billboard LOD. LOD2 is 110–850 tris and can carry 250–300 m.
- Rough budget: a 300 m radius at 0.015 trees/m² is about 4k trees, mostly at LOD2, so about 1–2M tris. That is plausible, but needs measuring.
- Real heights: the FF trees at ×1.5–1.8 come out 12–23 m, which matches NS spruce and fir. FV birch at ×1.4 is about 13 m.

### 4.1 Ranked picks for a Nova Scotia forest
**Conifers (most of the forest):**
1. FF Tree_05: tall mature spruce/fir. Sam's #1.
2. FF Tree_04: tall spruce/fir.
3. FF Tree_07: narrow full cone, the balsam-fir look.
4. FF Tree_01 and FF Tree_02: full-skirt young spruce for stand edges and clearing margins.
5. FF Tree_08: narrow spire at 4.2k tris. Black spruce for bogs and lake edges; cheap filler.
6. FF Tree_03: filler.
7. FF Tree_06: squat regrowth and understory conifer.
- Arena2 Tree_01/02/03 are low-poly twins of FF 01/03/05, for far rings or a perf fallback.

**Broadleaf mixed in:**
1. FV Tree_08 and FV Tree_07: **birch**, white bark. These are the only true birch in the collection.
2. FV Tree_06, 01, 04, 05, 02: round-crown "maple-ish". A red/orange atlas variant would give autumn maple.
3. FV Tree_03: a big spreading landmark tree. Use sparingly, at 18k tris.
4. FV Tree_09 and FV Tree_10: bare snags for bogs, burns and windthrow.

**Bushes and understory:** FV `Plant_*` (fern fronds in the atlas) and FF `Plant_*`, plus LPNP `Plant*` if the brighter style is acceptable. Watch FV Plant_06 and FF Plant_04 (high tris).

**Rocks and cliffs:**
- FV `BigRock_01–05`, `Rock_01–05` and `SmallRock_01–05`, plus FF `Rock_01–06`. These share an atlas, have low tris and LODs. Best match.
- LPNP `Cliff01–04` for cliff bands; restyle the material.
- Sam's `Stone Ore ×3`: regenerable, flat-shaded.

**Grass:** the meshes SoS actually used are `CG/Models/Grass_01–03.fbx`. Use them as Instanced terrain detail meshes with the URP package's CartoonGrass graph. Don't port SimpleGrass.

**Chop-related (M4):**
- FF `Stump_01/02` and FV `Stump_01/02` for stumps.
- FF and FV `Log_*` and FV `Branch_*` as forest-floor deadfall.
- The falling tree can reuse the tree's own LOD0 mesh, the way SoS did.
- SoS's chop code (`SpawnedTree.FallAndShrink`, wood plus sapling drops) is planet-specific; use it as reference only.

**Style note:** FF and FV are "stylised low-poly fantasy" with a gradient-palette atlas, so they lean more cartoon than PS2-realism. Because each pack's colour lives in a single 1024² atlas, re-tinting (darker, bluer spruce needles; autumn maple) is one texture edit per pack.

**Out of current scope, but owned:** `polyperfect/Low Poly Animated Animals` has Deer, Fox, Wolf, Bear, Beaver, Rabbit, Squirrel, Goose and Eagle (Sam used them as statics in the Bassk test). FF also has props: canoe, paddle, tent, firewood, axe.

---

## Summary: top picks
- **Already chosen by Sam and used in SoS:** all 8 **FantasyForest** trees (conifers) and all 10 **FantasyValley** trees (broadleaf, including **2 birches**, FV 07 and 08, and 2 bare snags, FV 09 and 10). They spawn via `TreeSpawner` from the `HA_*` variants in `1 - samsPrefabs/Trees/`. All are Standard-shader, single-atlas, 3-LOD prefabs: easy URP.
- **Port route:** use the **URP .unitypackage installers already sitting in the SoS pack folders** (FF, FV, CartoonGrass). They include wind Shader Graphs. Exclude their URP pipeline assets.
- **Forest mix:** conifers FF 05 > 04 > 07 > 01/02 > 08 > 03 > 06; birch FV 08/07; maple stand-ins FV 06/01/04/05/02; FV 03 as a rare landmark; FV 09/10 as snags.
- **Also bring:** FV/FF rocks (BigRock, Rock, SmallRock), FF/FV **stumps and logs** for M4, the CartoonGrass `Grass_01–03` meshes, FF/FV/CG terrain textures, and LPNP cliffs.
- **Blocked:** `CG_SimpleGrass` (`_GrassFillStrength`), `CG_GrassDepth`, the SoS instanced-grass system, cave and planet shaders. The LPNP wind shader is unused anyway.
- **SAM_TODO:** none of these packs has a licence file (Asset Store EULA); add an EULA/order note per pack. Approve a selective import of the FF, FV and CG URP packages.
