# PaintMaze Level Generation

This document records the endless-level architecture, deterministic generation contract,
quality gates, bake workflow, and versioning rules. These systems took significant
iteration to stabilize; changes must preserve determinism, solvability, performance, and
player progression.

## Mental model

PaintMaze has four visible independent endless tracks:

- Easy
- Medium
- Hard
- Extra Hard

`UltraHard = 4` remains an internal legacy source identifier for old saves, baked
resource slugs, generation tuning, and deterministic seeds. It is not selectable.

Every track is one-based and can progress up to the practical integer ceiling of
`2,000,000,000`.

Levels are supplied by a hybrid system:

```text
Easy / Medium / Hard request
    ├── 1–500  → sectioned baked Resources assets
    └── 501+   → deterministic local generation
Extra Hard request
    ├── 1–500      → extrahard baked Resources assets
    ├── 501–5000   → ultrahard baked assets 1–4500
    └── 5001+      → Ultra-tuned generation at source index N-500
                    ├── memory memoization
                    ├── versioned disk cache
                    └── guaranteed fallback
```

There is no server dependency. The same generator version, difficulty, and level number
must produce the same board on every supported device.

## Canonical storage and gameplay orientation

The deterministic contract ends at the provider boundary. Baked sections, generated
caches, fallback templates, and fingerprints keep their canonical rows, columns, tiles,
and spawn. `AppRoot` then calls `PortraitLevelOrientation` immediately before gameplay.

`PortraitLevelOrientation` measures the occupied Floor bounding box. A visibly
landscape board is copied through one 90° clockwise rotation; its spawn maps to
`(oldCol, oldRows - 1 - oldRow)`. Portrait and square silhouettes are unchanged. For
example, a canonical 16×22 Extra Hard level is stored, cached, and fingerprinted as
16×22 but is passed to `GameController` as a session-only 22×16 level.

Rotation is an isometry and `LevelFingerprint` already canonicalizes rotations. This
presentation policy therefore does not require a generator-version bump, catalog
rebake, cache migration, or golden refresh. Never serialize the session copy back into
the cache.

## Provider stack

`Assets/Scripts/Game/AppRoot.cs` builds:

```text
ScalableLevelProvider
    ├── SectionedLevelProvider
    └── DeterministicBatchLevelProvider
            └── LevelCacheStore
```

Core interfaces:

- `ILevelProvider`: reports baked count and resolves a level.
- `IPrefetchLevelProvider`: warms a future level and reports when it can be returned
  without blocking.

`ScalableLevelProvider` routes at the per-mode baked boundary: 500 for Easy, Medium, and
Hard; 5,000 for Extra Hard.

`AppRoot` prefetches the selected level while Home is visible. A sequential look-ahead
coordinator then keeps `N+1` and `N+2` ready, beginning after the active board's first
rendered frame and rolling forward after every activation. It waits for `N+1` before
requesting `N+2`, so a low-tier phone never launches both expensive searches together.
Baked section assets are acquired on the main thread, then their fifty-level text is
parsed on a worker. Generated levels load, validate, and persist through `Task.Run`.
Prepared generated levels remain in the versioned disk cache across relaunches. No
loading, spinner, or “preparing” screen is shown.

## Difficulty configuration

`Assets/Scripts/Domain/DifficultyConfig.cs` owns board dimensions, obstacle fraction,
display name, and baked count.

| Difficulty | Rows | Columns | Obstacle fraction | Baked |
| --- | --- | --- | ---: | ---: |
| Easy | 7→8 | 6→7 | 0.14 | 500 |
| Medium | 8→9 | 7→8 | 0.18 | 500 |
| Hard | 9→10 | 8→9 | 0.22 | 500 |
| Extra Hard | 9→16 | 9→22 | 0.26 / legacy 0.30 | 5,000 |

Easy, Medium, and Hard retain their version-3 one-step growth every 125 levels. Extra
Hard uses explicit source bands:

| Visible levels | Source tuple | Dimensions |
| --- | --- | --- |
| 1–125 | ExtraHard 1–125 | 9×9 |
| 126–250 | ExtraHard 126–250 | 10×10 |
| 251–375 | ExtraHard 251–375 | 12×14 |
| 376–500 | ExtraHard 376–500 | 14×16 |
| 501–625 | UltraHard 1–125 | 14×16 |
| 626–750 | UltraHard 126–250 | 14×18 |
| 751–875 | UltraHard 251–375 | 16×18 |
| 876–1000 | UltraHard 376–500 | 16×20 |
| 1001–5000 | UltraHard 501–4500 | 16×22 |
| 5001+ | UltraHard 4501+ | 16×22 |

Board growth is capped at 16×22. Later difficulty comes from topology and movement
complexity, not unbounded board size.

Each visible difficulty has independent progress in `SaveService`. Extra Hard 501+
continues through the former Ultra Hard catalog and tuning.

## Level data model

`Assets/Scripts/Domain/Level.cs` contains:

- a rectangular `Tile[,]` grid
- one Floor spawn position
- difficulty
- one-based level index

`Assets/Scripts/Domain/Tile.cs` defines:

- `Floor`: traversable and paintable.
- `Wall`: blocks movement.
- `Void`: exterior space outside the board.

Serialization characters:

```text
S  spawn Floor
.  Floor
#  Wall
_  Void
```

Rendering is not part of this model. `BoardView3D` currently presents Floor as raised
blocks, Wall as recessed gaps, and Void as absent geometry, but solver and generator
logic depend only on the tile values.

## Exterior-only Void topology

Void represents background connected to the outside of the grid. It must never form an
enclosed pocket inside a level.

`Assets/Scripts/Core/LevelTopology.cs` enforces this contract:

- `FindExteriorVoid`: flood-fill from border Void cells.
- `HasExteriorVoid`: requires at least one border Void.
- `HasOnlyExteriorVoid`: rejects enclosed Void.
- `SealEnclosedVoid`: deterministically converts enclosed Void to Wall.

Generation seals enclosed pockets before spawn selection and validation. Cache loading
also rejects and deletes files containing invalid topology.

This distinction matters:

- an internal blocked cell is `Wall`
- unrendered space outside the generated silhouette is `Void`

Do not create interior Void merely to change rendering. Use Wall.

## Deterministic contract

The source runtime board is a pure function of:

```text
(GeneratorVersion, Difficulty, LevelNumber)
```

For visible Extra Hard N ≥ 1001, routing resolves this tuple to
`(GeneratorVersion, UltraHard, N - 500)` and restamps the returned level as Extra Hard N.

Current deterministic versions are scoped so lower tracks preserve their existing
outputs:

```text
Easy / Medium / Hard seed stream  = 3
ExtraHard / UltraHard seed stream = 6
LevelFingerprint format           = 3
```

`LevelGenerator.VersionFor` is the authority for generation and cache headers. The
fingerprint version is a canonical format version and remains 3 so unchanged lower-mode
fingerprints remain stable and cross-mode duplicate detection stays structural.

### Seeds

`LevelSeed.For` mixes:

- difficulty
- level number
- generator version
- attempt number

`DeterministicRng` is the project-owned random number generator. Do not replace it with
`System.Random`, `UnityEngine.Random`, device time, frame state, or unordered collection
iteration.

It provides deterministic:

- bounded integers using rejection sampling
- doubles using a 53-bit mantissa
- Fisher–Yates shuffling

Golden tests protect both seed and PRNG behavior.

## Generator pipeline

`Assets/Scripts/Core/LevelGenerator.cs` is shared by runtime generation, editor baking,
and generator tests.

### Top-level search

`Generate`:

1. Resolves dimensions and obstacle fraction from `DifficultyConfig`.
2. Searches a deterministic set of attempts.
3. Generates multiple candidates around the target obstacle fraction.
4. Scores candidates that pass quality floors.
5. Returns the highest-scoring candidate.
6. Uses the guaranteed fallback if no candidate survives.

Default options:

```text
AttemptCount         12
EvaluateMinMoves     false
MinMovesMaxStates    200,000
GeneratorVersion     6 (resolved to 3 for lower modes)
```

### Candidate generation

`TryGenerateBoard`:

1. Builds an irregular mask using edge-connected cuts.
2. Converts occupied mask cells to Floor and exterior cuts to Void.
3. Scatters short Wall bars.
4. Preserves Floor connectivity.
5. Seals enclosed Void to Wall.
6. Requires at least one exterior Void.
7. tries deterministic spawn candidates.
8. accepts only a board that is always solvable.

Important constants:

```text
Minimum mask occupancy       0.58
Inner candidates/fraction    32
Fraction schedule offsets    0, ±0.02, ±0.04, ±0.06, ±0.08
```

Wall scatter favors clusters of length two or three while retaining a limited number of
isolated walls. This legacy candidate path is used only by Easy, Medium, and Hard.

### Extra Hard structural generation

ExtraHard and UltraHard use `ExtraHardLevelBuilder` for every size:

1. Select a certified arena root for the requested dimensions.
2. Begin with one broad, connected Floor field.
3. Apply deterministic compound mutations: compact rectangular/L/T Wall islands,
   shifted islands, Floor/Wall swaps, edge carving, and exterior notches.
4. Keep bounded intermediate states even when they are temporarily unsafe.
5. Rank safety deficits from `Solver.AnalyzeNeverStuck` before arena and movement
   quality; targeted repair addresses uncovered sweep lines and stranded stops.
6. Select a spatially useful spawn and publish only candidates passing safety, quality,
   and the scale-aware arena contract.

The roots cover 9×9, 10×10, 12×14, 14×16, 14×18, 16×18, 16×20, and 16×22.
Generation uses deterministic beam limits and canonical-byte tie breaking. The old
stripe, mosaic, serpentine, and Hamiltonian families remain diagnostic negative
controls only; they are not Extra Hard publication paths.

Lower difficulties do not enter this path, preserving their generated fingerprints.

## Never-stuck guarantee

`Assets/Scripts/Core/Solver.cs` defines `IsAlwaysSolvable`.

A valid level must satisfy both:

1. Every Floor can be reached and painted from the spawn through slide moves.
2. The reachable stop graph can return to the spawn.

This is stronger than checking whether one route reaches a goal. PaintMaze has no single
goal tile; every traversable tile must remain paintable regardless of legal movement
history.

Do not weaken this validation to improve generation speed.

Hints use `Solver.SuggestProgressMove`, not the painted-state shortest-solution search.
It breadth-first searches only reachable stop positions and returns the first move on
the shortest route that crosses an unpainted cell. Its state count is bounded by board
area, and the never-stuck contract guarantees reachable progress while tiles remain.
`SolutionFrom` remains available for bounded offline difficulty analysis.

## Quality scoring

`Assets/Scripts/Core/LevelQualityScorer.cs` calculates:

- Floor, Wall, and Void counts
- stop positions
- branching
- isolated wall ratio
- perimeter complexity
- optional minimum moves
- serpentine-pattern detection

`Assets/Scripts/Core/LevelLayoutAnalyzer.cs` supplies the Extra Hard arena contract:

- 3×3 open-core count and covered-cell ratio
- maximum Floor clearance
- degree-four Floor ratio
- largest connected eroded-open region
- interior Wall-island count and cell ratio
- repeated parallel-separator ratio
- exterior cut depth and silhouette concavity
- reachable choice-stop count

Core quality floor:

```text
Always solvable
Floor fraction ≥ 0.45
Not a repetitive serpentine-shaped board
```

The weighted score rewards useful walls, branches, stops, perimeter variation, and
difficulty-appropriate complexity while penalizing isolated-wall noise.

`LevelDifficultyAnalyzer` adds movement-based signals for Extra Hard:

- bounded exact minimum moves on boards with at most 100 paintable cells
- turns, reversals, and revisited stop positions
- average newly painted cells per move
- forced-move ratio across the stop graph
- seeded rollout best, average, worst, and failure count

Exact search is capped at 30,000 states during baking and 12,000 states at runtime.
Larger boards skip exact search and use a bounded number of deterministic rollouts.
Analyzer randomness is seeded with `LevelSeed.For`; metrics are therefore reproducible.
Candidate selection minimizes distance from a progression target rather than maximizing
wall count. `Solver.IsAlwaysSolvable` remains the authoritative safety gate.

`Assets/Scripts/Core/LevelBakeValidator.cs` is the shared bake/runtime acceptance gate:

1. always solvable
2. passes quality floors
3. at least one Void
4. exterior-only Void
5. isolated wall ratio no greater than `0.5`
6. Extra Hard arena floor
7. Extra Hard movement-difficulty floor when detailed analysis is supplied

## Guaranteed fallback

Generation must return a level even if normal search exhausts its budget.

`OpenExtraHardFallbackBank` preloads one certified arena root for every physical size.
`DeterministicBatchLevelProvider` selects the matching root and a deterministic
orientation, then restamps the visible difficulty and index. Every bank entry is covered
by safety, exterior-topology, arena-contract, dimension, and orientation-diversity
tests.

If a resource root is unavailable, `GenerateCertifiedOpenFallback` runs a bounded arena
repair search and throws rather than publishing a corridor. `GenerateGuaranteedFallback`
still exposes Hamiltonian/serpentine construction for lower-mode recovery and negative
tests, but Extra Hard runtime, bake, and preview paths never call it.

## Baked catalog

The four visible tracks expose 6,500 baked boards:

```text
Easy 500 + Medium 500 + Hard 500 + Extra Hard 5,000 = 6,500 boards
```

Assets live in:

```text
Assets/Resources/Levels/Baked/
```

Each physical source file contains 50 levels. Easy, Medium, Hard, and the physical
Extra Hard source retain ten sections each; Ultra Hard contains ninety sections. The
catalog therefore contains 130 files:

```text
easy_0001_0050.txt
...
ultrahard_4451_4500.txt
```

`SectionedLevelProvider` loads only the required 50-level section and caches parsed
sections in memory.

The generator-version comment in a pack is metadata describing the bake that produced
that file. Runtime routing is based on the index, not that comment. Historical baked
sections may retain an older header when their board layouts remain intentionally
unchanged.

## Runtime generation

`Assets/Scripts/Core/DeterministicBatchLevelProvider.cs` generates and persists one
requested slot at a time. `SequentialLevelPrefetchBuffer` schedules two future slots,
but requests them serially: `N+2` does not begin until `N+1` is ready. Per-slot `Lazy`
publication deduplicates racing requests, and stale in-memory slots are trimmed while
their disk files remain available.

Runtime search budgets:

| Difficulty | Attempts per offset | Maximum offset blocks |
| --- | ---: | ---: |
| Easy / Medium / Hard | 1 | 12 |
| Extra Hard / Ultra Hard | 1 | 1 |

Cold `GetLevel`:

1. checks in-memory slots
2. attempts to hydrate that index's disk file
3. generates only the requested slot
4. applies the same safety, topology, quality, and bounded difficulty contract as baking
5. atomically persists that slot
6. returns it

Generation and cache writes can finish while Home or the current board remains visible.
Only one slot in the two-level window is requested at a time. The 16×22 arena path
performs one deterministic search and then uses the certified bank if the candidate
misses a publication gate. On the Galaxy S22 Ultra, final development-build
cold launches for visible Extra Hard 1001, 2000, and 10500 reached a rendered board in
8.7–9.7 seconds; the final release build reached cold Extra Hard 2002 in 4.4 seconds.
The work occurred during Home prefetch. A cached 10500 development reload reached the
board in 3.7 seconds.

The 5,000-level runway validation measured the runtime boundary from a clean install:

- Galaxy S22 Ultra: level 5000 `GetLevel` 0.0 ms, board build 53.6 ms; sequential
  5001/5002 preparation took 5.36/5.20 seconds.
- Galaxy A05: level 5000 `GetLevel` 0.0 ms, board build 80.5 ms; sequential 5001/5002
  preparation took 16.97/15.49 seconds. A swipe was accepted while 5001 was generating.
- On relaunch at 5001, disk hydration took 4.2 ms on S22 and 8.7 ms on A05; activation
  `GetLevel` took 1.1/1.3 ms because hydration had already completed off the main thread.

The two requests were visibly sequential in telemetry: each `N+2` start followed the
matching `N+1` ready event. Post-buffer PSS was about 504 MB on S22 and 291 MB on A05,
with empty Android crash buffers.

## Cache

`Assets/Scripts/Core/LevelCacheStore.cs` stores generated levels under:

```text
{Application.persistentDataPath}/generated-levels/v3/{mode}/
```

Each file contains exactly one level and its name contains that visible level index.

Cache properties:

- lower modes retain version-3 headers and paths
- Extra Hard uses version-6 headers, so only incompatible Extra Hard files are rejected
- text format shared with level packs
- atomic save through a temporary file
- wrong-size, unparsable, or invalid-topology files are deleted
- cache failure is non-fatal because deterministic regeneration is authoritative

Never treat cache contents as player progression.

## Fingerprints and uniqueness

`Assets/Scripts/Core/LevelFingerprint.cs` creates a canonical identity:

1. serialize all four rotations
2. serialize mirrored forms
3. choose the lexicographically smallest representation
4. hash it with 64-bit FNV-1a

Canonical bytes include:

- fingerprint/generator version
- dimensions
- transformed spawn
- tile values

`LevelFingerprintRegistry` uses both the hash and canonical byte equality to prevent
false-positive duplicate rejection during baking.

The bake requires global uniqueness across all 6,500 accepted source levels.

## Bake workflow

`Assets/Editor/LevelBakeTool.cs` is the canonical offline bake implementation.

Editor menu:

```text
Paint Maze → Bake All Levels
Paint Maze → Bake Extra Hard Levels
```

Headless:

```bash
Unity -batchmode -quit -projectPath . \
  -executeMethod PaintMaze.EditorTools.LevelBakeTool.BakeAll

# Selective rebuild; preserves Easy/Medium/Hard assets and fingerprints
Unity -batchmode -quit -projectPath . \
  -executeMethod PaintMaze.EditorTools.LevelBakeTool.BakeExtraHardCatalogs
```

`tools/genpacks.py` invokes this Unity bake; pass `--extra-hard` for the selective path.
It is not a separate production generator. The selective bake seeds its global
fingerprint registry from all existing Easy/Medium/Hard sections before generating the
physical `extrahard` and `ultrahard` catalogs.

The packaged runway extension is a separate resumable operation:

```bash
python3 tools/genpacks.py --extra-hard-extension
```

It leaves the existing Extra Hard and Ultra Hard 1–500 files untouched, seeds uniqueness
from all 2,500 existing boards, and materializes visible Extra Hard 1001–5000 through the
exact runtime provider path. It writes Ultra Hard source sections 501–4500 atomically,
one verified 50-level section at a time, and safely resumes completed sections.

Global deduplication exposed one v6 grid collision: visible Extra Hard 3767 matched 2632
under rotation/reflection with the same spawn. `ExtraHardRuntimePatches` keeps the 3767
grid unchanged and applies a solver-certified alternate Floor spawn at `(0,0)` in both
runtime publication and cache hydration. The packaged/runtime parity test covers this
patched index.

The bake:

1. generates each mode from 1 through 500
2. applies shared validation
3. rejects canonical duplicates
4. writes 50-level sections
5. writes dimensions, structural quality, and movement-difficulty metrics
6. creates preview contact sheets
7. verifies the expected globally unique accepted fingerprints

The approved 2026-07-14 Extra-Hard-only v6 bake completed all 1,000 rebuilt source
levels in 19.1 minutes. It used 106 duplicate-offset retries and zero validation-offset
retries. The global registry finished with 2,500 unique boards. Easy, Medium, and Hard
pack hashes remained byte-for-byte unchanged.

The 5,000-level extension added 80 Ultra Hard source sections and finished with 6,500
globally unique source boards. Pack verification accepted all 6,500 levels, 236/236
EditMode tests passed, and pre/post SHA-256 checks proved the original 20 Extra Hard and
Ultra Hard source files remained byte-identical. The release APK grew from 23,023,988 to
23,236,064 bytes (+212,076 bytes, about 0.92%).

Before that production bake, the preview checkpoint generated 24 unique boards for each
of the eight physical size bands (192 total), per-band metric distributions, and a
worst-arena montage. Production was allowed only after the six required boards were
reviewed on the S22 Ultra and explicitly approved.

Outputs outside Resources:

```text
build/level-reports/level_metrics.csv
build/level-previews/
```

## Pack verification

Run:

```bash
python3 tools/verify_packs.py
```

It verifies:

- exactly 130 section files
- exactly 500 Easy, Medium, Hard, and physical Extra Hard source levels
- exactly 4,500 Ultra Hard source levels
- exactly 6,500 total source levels
- one valid spawn per level
- never-stuck solvability
- Floor fraction at least `0.45`
- isolated wall ratio at most `0.5`
- exterior-only Void
- no canonical duplicates
- every Extra Hard board passes the shared arena predicate
- every Extra Hard 50-level section passes median open-core and separator gates
- average Floor count and cycle rank increase across physical size bands

`tools/neverstuck_test.py` mirrors the C# never-stuck check for offline pack validation.

Legacy Python generator experiments remain in `tools/`, but production content must come
from the C# `LevelGenerator`.

## Progression and save migration

`Assets/Scripts/Services/SaveService.cs` stores the highest unlocked index independently
for each of the four visible difficulties.

Completing level N unlocks N+1 and never wraps to level 1.

Current save schema:

```text
SaveService.CurrentSchemaVersion = 3
```

The schema-3 migration:

- maps old Ultra Hard progress N to Extra Hard `500 + N`
- keeps higher existing Extra Hard progress
- removes the old Ultra unlock key and maps selected mode 4 to Extra Hard
- deletes generated-level caches created with the old 501 boundary

The UI always presents progression as endless. It must not display finite text such as
`OF 60`.

## Versioning rules

### Bump the affected generator version when

A change can alter the generated grid, spawn, or deterministic candidate selection for
the same difficulty/index, including:

- seed or PRNG changes
- mask generation changes
- wall placement changes
- topology repair changes
- scoring or validation changes that select a different candidate
- fallback output changes
- difficulty dimensions or obstacle fractions

Change the scoped version returned by `LevelGenerator.VersionFor`. Keep lower-mode
version 3 when lower generated bytes are intentionally unchanged. Change
`LevelFingerprint.GeneratorVersion` only when the canonical fingerprint byte format
changes, not for every generation tuning change.

```text
LevelGenerator.VersionFor(difficulty)
LevelFingerprint.GeneratorVersion  # canonical format changes only
```

### After a version bump

1. Re-evaluate which baked source catalogs must be rebuilt.
2. Run `tools/genpacks.py --extra-hard` for an Extra-Hard-only change.
3. Run `tools/verify_packs.py`.
4. Refresh affected runtime fingerprint goldens intentionally.
5. Run the complete EditMode suite.
6. Verify high-level cold-generation performance.
7. Decide whether shipped player progression must reset through a save-schema migration.
8. Confirm incompatible files carry a new mode-specific cache header.

Never silently change deterministic output while keeping the same generator version.

## Tests

Important EditMode fixtures:

- `DifficultyConfigTests`
- `DeterministicRngTests` and `LevelSeedTests`
- `LevelParserTests`
- `LevelTopologyTests`
- `GeneratedLevelProviderTests`
- `LevelFingerprintTests`
- `GeneratedFingerprintGoldenTests`
- `BakedLevelCatalogTests`
- `SectionedLevelProviderTests`
- `DeterministicBatchLevelProviderTests`
- `SequentialLevelPrefetchBufferTests`
- `BakedRuntimeParityTests`
- `LevelCacheStoreTests`
- `DifficultyQualityTrendTests`
- `PerformanceBenchmarkTests`
- `PortraitLevelOrientationTests`
- `SolverTests` (bounded progress hints)
- `SaveServiceMigrationTests`

Golden runtime snapshots cover representative generated levels in all four visible
modes, including Extra Hard source mappings 1001→legacy Ultra 501 and beyond.

Before shipping a generator change:

```bash
# Complete Unity EditMode suite
Unity -batchmode -nographics -projectPath . \
  -runTests -testPlatform editmode \
  -testResults build/editmode-results.xml

# Baked catalog
python3 tools/verify_packs.py
```

Also test cold levels above 500 (standard modes) and 5000 (Extra Hard), plus cached
reloads on the target Android device.

## Change checklist

Before editing generation code:

- Identify whether output bytes can change.
- Preserve `DeterministicRng`; never use ambient randomness.
- Keep each runtime slot independent of request order.
- Keep Void exterior-connected.
- Preserve the never-stuck guarantee.
- Keep generation off the main thread above each mode's baked boundary.
- Keep cache optional and versioned.

Before accepting the change:

- Update generator and fingerprint versions when required.
- Re-bake affected content.
- Refresh intentional goldens.
- Run all tests and pack validation.
- Check uniqueness and quality trends.
- Benchmark Ultra-tuned Extra Hard cold generation above level 5000.
- Validate progression and migration behavior.

## Key files

```text
Assets/Scripts/Domain/Difficulty.cs
Assets/Scripts/Domain/DifficultyConfig.cs
Assets/Scripts/Domain/Level.cs
Assets/Scripts/Domain/Tile.cs
Assets/Scripts/Core/ILevelProvider.cs
Assets/Scripts/Core/IPrefetchLevelProvider.cs
Assets/Scripts/Core/ILevelMemoryWindow.cs
Assets/Scripts/Core/SequentialLevelPrefetchBuffer.cs
Assets/Scripts/Core/ScalableLevelProvider.cs
Assets/Scripts/Core/SectionedLevelProvider.cs
Assets/Scripts/Core/DeterministicBatchLevelProvider.cs
Assets/Scripts/Core/GeneratedLevelProvider.cs
Assets/Scripts/Core/LevelGenerator.cs
Assets/Scripts/Core/DeterministicRng.cs
Assets/Scripts/Core/LevelTopology.cs
Assets/Scripts/Core/LevelQualityScorer.cs
Assets/Scripts/Core/LevelLayoutAnalyzer.cs
Assets/Scripts/Core/LevelDifficultyAnalyzer.cs
Assets/Scripts/Core/ExtraHardLevelBuilder.cs
Assets/Scripts/Core/ArenaExtraHardTemplateBank.cs
Assets/Scripts/Core/OpenExtraHardFallbackBank.cs
Assets/Scripts/Core/ExtraHardRuntimePatches.cs
Assets/Scripts/Core/LevelBakeValidator.cs
Assets/Scripts/Core/LevelFingerprint.cs
Assets/Scripts/Core/LevelFingerprintRegistry.cs
Assets/Scripts/Core/LevelCacheStore.cs
Assets/Scripts/Core/LevelParser.cs
Assets/Scripts/Core/LevelPackSerializer.cs
Assets/Scripts/Core/Solver.cs
Assets/Scripts/Core/MovementSystem.cs
Assets/Scripts/Game/PortraitLevelOrientation.cs
Assets/Scripts/Services/SaveService.cs
Assets/Scripts/Services/LevelLabel.cs
Assets/Editor/LevelBakeTool.cs
tools/genpacks.py
tools/verify_packs.py
Assets/Tests/EditMode/
```
