# CLAUDE.md — CabinGame

Solo indie project by Sam (BIGBOI STUDIOS). First-person forest survival/building game; Phase 1 is a creative-mode building sandbox. Read `docs/handoffs/Handoff_CabinGame_Foundation_v1.md` for the plan and `docs/STATUS.md` for where things stand before doing anything.

## Stack
- Unity **6.3 LTS**, **URP** (Forward+, GPU Resident Drawer on, GPU occlusion culling on). Never Built-in RP.
- C#, Input System package, Splines, Terrain Tools, Burst/Collections/Mathematics, Test Framework.
- Real-world units: metres, lumber at real dimensions (handoff Appendix A). 16-in centres = 0.4064 m.

## Working agreement
- Claude Code: all code, editor tools, generators, shaders, prefab wiring (via scripts or the Coplay MCP), tests, docs.
- Sam: sources art (character, truck, packs), playtests, answers `[OPEN]` items, decides scope.
- Anything Claude Code can't do goes in `docs/SAM_TODO.md`; keep working on something else.
- Current milestone only. Nothing from the handoff's non-goals (survival, enemies, town, milling, progression, story, multiplayer) without a new handoff.
- Ask before: adding packages, changing URP/quality settings, importing an asset pack wholesale, anything that widens scope.
- A milestone is done when Sam has played it. `docs/STATUS.md` says what has and has not been played.

## Spec tags
`[EXISTS]` already there · `[BUILD]` Claude Code builds · `[AUTHOR]` Sam does it · `[INTEGRATE]` wire into existing · `[TEST]` needs a test · `[OPEN]` needs Sam's answer

## Rules
- Everything generated is regenerable from a seeded `MapRecipe` via `Tools/Cabin/...` menu items. No hand-sculpting, no hand-edits to `Assets/Generated/`.
- Namespaces `Cabin.*`. Asmdefs: `Cabin.Runtime`, `Cabin.Editor`, `Cabin.Tests.EditMode`, `Cabin.Tests.PlayMode`. One class per file.
- Dev-only code lives in `Cabin.Editor` or behind `#if CABIN_DEV`. Nothing auto-spawns in builds.
- Zero compiler warnings. Tests for every generator and for `BuildGraph` (placement, dependency, removal, save/load roundtrip).
- Third-party packs go in `Assets/External/<Pack>/` with their licence file, never edited in place. Ours go in `Assets/_Project/`.
- Scenes stay small: `Boot`, `World`, `TestPlatform`. Everything placeable is a prefab.
- Static world at origin, no floating origin. 4 × 4 km fits float precision.
- Performance budget: 60 fps in editor Play Mode at 3440 × 1440 on an RX 6800 XT. If a milestone breaks it, fixing it is part of the milestone.

## Git
- One branch per milestone (`m1-map`, `m2-vegetation`, ...), merged to `main` after Sam's playtest. Small commits, real messages.
- Git LFS for binaries (`.gitattributes`), UnityYAMLMerge for `*.unity *.prefab *.asset`. Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`.
- Every `.cs` ships with its `.meta`.

## Verifying
- Coplay connected: run the menu item, read the console, inspect the hierarchy.
- Otherwise batchmode (Sam's editor must be closed — the project lock): compile check, EditMode tests, `Cabin.Editor.Cli.*` generators. Commands in the handoff, Appendix B.

## Docs
- `docs/STATUS.md` — single source of truth for state; update at the end of every milestone.
- `docs/SAM_TODO.md` — things only Sam can do.
- `docs/handoffs/` — dated handoffs. `docs/diagrams/` — the SVG system maps.
- `docs/ASSETS_FROM_SOS.md` — the Sound of Space asset audit (M2).
- No other docs without a reason. No `(1).md` duplicates, ever.
