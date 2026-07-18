# PaintMaze UI and Rendering

This document records the visual architecture and the decisions that must remain stable
when the UI or board presentation changes. PaintMaze builds both its UI and its 2.5D
board at runtime; there are no screen prefabs to edit.

## Visual goals

- Keep the game readable in portrait orientation on the Galaxy S22 Ultra.
- Preserve the dark and light palettes through shared `Theme` tokens.
- Present the maze as a raised block slab with background-coloured recessed gaps.
- Keep UI and rendering changes separate from level data and gameplay rules.
- Use the screenshots in `refrences/` as composition and depth references, not as a
  source of colors.

## Runtime composition

The game has two independent visual layers:

1. A perspective world camera renders the background, board, ball, trail, and particles.
2. A `ScreenSpaceOverlay` uGUI canvas renders the home screen, gameplay HUD, and
   completion screen.

```text
Background quad
    └── BoardView3D, ball, shadows, and effects
            └── ScreenSpaceOverlay canvas and HUD
```

`Assets/Scripts/Game/AppRoot.cs` creates and coordinates both layers. The scene only
needs the `AppRoot` component.

## Screen lifecycle

`AppRoot` owns three canvas roots:

- `HomeScreen`: menu, difficulty selector, preview, settings, and play dock.
- `GameScreen`: transparent input surface and gameplay HUD.
- `CompleteScreen`: temporary completion overlay.

The selected level is prefetched while Home remains fully interactive. If preparation is
still running after Play, Home stays unchanged and the game opens automatically when the
level is ready; there is no loading screen or busy-button state. The 3D board is disabled
on Home, enabled during gameplay, and remains visible behind the completion overlay.

The normal flow is:

```text
Home → Load level → Frame camera → Gameplay → Complete → Next level
```

Do not put gameplay state into a UI component. `HomeController`, `UIManager`, and
`CompleteController` only raise events; `AppRoot` and `GameController` own the flow.

## uGUI foundation

### Canvas

`AppRoot.BuildCanvas()` creates:

- `RenderMode.ScreenSpaceOverlay`
- reference resolution `1080 × 1920`
- `CanvasScaler.MatchWidthOrHeight = 0.5`

Home and gameplay content use normalized `Screen.safeArea` anchors. Full-screen loading,
completion, and modal scrims intentionally cover the whole canvas.

### Runtime primitives

`Assets/Scripts/Services/UiKit.cs` provides:

- `Anchor` and `Stretch`
- runtime `Image`, `Text`, and `Button` creation
- the shared Fredoka font

`Assets/Scripts/Services/SpriteFactory.cs` generates and caches:

- circles
- nine-sliced rounded rectangles
- vertical gradients
- completion stars

The project currently uses legacy `UnityEngine.UI.Text`, not TextMeshPro. `UiKit.Font`
loads `Assets/Resources/Fonts/Fredoka.ttf` and falls back safely if it is missing.

## Home screen

`Assets/Scripts/Menu/HomeController.cs` builds the complete home screen inside its safe
area:

- title and subtitle
- settings and theme buttons
- decorative `HomeMazePreview`
- four-segment `DifficultySlider` (Easy, Medium, Hard, Extra Hard)
- current level and endless progression dock
- primary play/continue button

`HomeMazePreview` is decorative. It must never read or mutate `GameState`, level
progression, or the real `BoardView3D`.

The selected difficulty and highest unlocked level come from `SaveService`. The home
screen emits a `Play` event; it does not load levels itself.

`UltraHard = 4` is retained only for legacy save/resource routing. It must not appear in
the slider, home labels, gameplay HUD, or normal save access.

## Gameplay HUD

`Assets/Scripts/Game/UIManager.cs` implements the `Hud` contract.

### Header

- Circular translucent back button at the top-left.
- Soft sibling shadow behind the button.
- Centered chevron icon.
- Muted rounded difficulty pill above the title.
- Bold Fredoka `Level N` title.

`HeaderBottomScreenY` measures the title's real screen-space lower edge. `AppRoot` uses
that value when framing the board, so changing header dimensions can move the board.

### Hint button

- Gold circular floating button at the bottom-right.
- Soft circular drop shadow.
- White count badge overlapping the top-right.
- Optional video badge at the bottom-right.

An unlimited hint count is represented by `"+"`. Styling changes must not alter the
`Back` or `Hint` events.

### Input surface

The game screen contains a transparent full-screen `SwipeSurface`. `SwipeInput` uses a
minimum gesture length of the greater of 35 pixels or 3.5% of screen height. UI buttons
remain above the swipe surface in the canvas hierarchy.

## Theme system

`Assets/Scripts/Services/Theme.cs` is the only source of visual colors. Never duplicate
theme hex values in renderers.

Important board tokens:

- `Background`: camera, decorative background, and recessed-hole fill.
- `PlayableTop`: unpainted tile top.
- `PlayableSide`: derived by darkening `PlayableTop` by 36%.
- `Hole`: aliases `Background` exactly.
- `HoleInner`: derived darker edge for recessed gaps.
- `BoardRim`: derived dark perimeter and base skirt.
- `BoardShadow`: derived from the current background.
- `Ball`, `Gold`, and rotating paint colors.

Important UI tokens:

- `Ink` and `InkSoft`
- `CardBg` and `TrackBg`
- `CircleBg` and `OnCircle`
- `Gold`
- `Accent(Difficulty)`

Theme switching occurs on the home screen. It updates `Theme.Mode`, persists through
`SaveService`, rebuilds home content, refreshes the world background, and calls
`UIManager.ApplyTheme()`.

## Board visual model

`Assets/Scripts/Game/BoardView3D.cs` is presentation-only. The domain still has three
tile states:

| Tile | Visual treatment | Gameplay meaning |
| --- | --- | --- |
| `Floor` | Raised extruded block | Paintable and traversable |
| `Wall` | Background-colored recessed pit | Blocks movement |
| `Void` | No geometry | Exterior space |

This mapping is deliberate. Do not change `Tile`, grid data, solver behavior, or movement
rules to achieve a visual result.

### Floor geometry

Each Floor cell has:

- an offset under-plate contributing to the board silhouette shadow
- a dark base skirt
- an extruded body using `PlayableSide`
- an inset top cap
- perimeter bevel strips wherever its neighbor is not Floor

Internal cap insets create the tile grid. Exposed bevels create the thicker outer and
inner-hole outlines.

Top faces use five deterministic shade bands derived from row and column. The variation
is intentionally small and stable. Painted cells retain the same shade band and swap to
the current level's paint material.

### Wall pits

Wall cells do not create a raised body, cap, skirt, or board shadow.

Their recessed floor uses `Theme.Hole`, which is the exact same color value as
`Theme.Background`. Slightly darker `HoleInner` strips are emitted only along boundaries
with Floor cells. The adjacent Floor body's exposed side supplies the main depth cue.

### Void

Void emits no mesh at all. The world background remains visible directly.

### Key geometry constants

```text
Cell size                  1.00
Board top height           0.40
Top cap height             0.06
Base skirt height          0.10
Hole depth                 0.28
Perimeter bevel            0.075 wide × 0.022 high
Top shade bands            5
Ball scale                 0.62 cells
```

The Z-axis cap and edge widths use `DepthStretch = 1.1034`, compensating for the
25-degree camera tilt so horizontal and vertical grid lines appear similarly thick.

## Ball and feedback

The ball is a `Standard`-shader sphere with:

- theme-aware color
- smoothness `0.62`
- a bright top-left highlight child
- a soft elliptical contact-shadow quad

The shadow follows the ball slightly above the board surface and widens during squash.

Existing movement feedback includes:

- visible rolling through `BallSpin`
- squash and spring settle on impact
- paint-colored tile splashes on every crossed cell: a full burst for newly painted
  cells and a lighter burst for revisits
- additive gold trail and sparks
- burst sparks and camera shake on wall impacts
- haptic feedback

These effects are visual feedback only. Do not move timing or collision decisions from
`GameController` into `BoardView3D`.

Tile splashes reuse one board-level particle system. Do not create particle systems,
materials, or coroutines per crossed tile. They default on and can be disabled with the
Paint Splashes switch in Settings; the preference persists through `SaveService`.

## Camera, lighting, and framing

`AppRoot.Build3DWorld()` creates:

- perspective camera, FOV `32`
- camera pitch `65°`
- board yaw `0°`
- directional light intensity `0.75`, rotation `(50, -35, 0)`
- flat ambient light at `0.55`
- 4× MSAA

Tiles use explicit unlit materials for stable palette rendering. The directional light
primarily rounds the glossy ball.

`FrameCamera()`:

1. Fits the Floor-only rendered bounds into the perspective frustum.
2. Aims at the exact rendered bounding-box center.
3. Zooms out enough to preserve symmetric room around the physical screen center while
   respecting the HUD header and lower viewport limit.
4. Measures the projected board bounds.
5. Corrects both horizontal and vertical camera offsets until the projected silhouette
   is centered at viewport `(0.5, 0.5)`.

`BoardView3D.ProjectedViewportBounds` projects the eight corners of the occupied Floor
bounding box. Its cost is constant with board area; it does not project every rendered
cell. This is required for the 16×22 Extra Hard band.

Do not return to framing from the rectangular source grid. Wall and Void regions are
visually background and would create misleading margins.

### Portrait normalization

Providers, baked packs, caches, and fingerprints retain their canonical level
orientation. Immediately before gameplay, `PortraitLevelOrientation` measures the
occupied Floor bounds. If that silhouette is wider than tall, it creates a session-only
90° clockwise copy and rotates the spawn with it. A canonical 16×22 board therefore
plays and renders as 22×16.

The complete gameplay level is rotated before `GameController.Load`, so screen-up
remains `SwipeDirection.Up` and movement, hints, paint, VFX, restart, and camera framing
all use one coordinate system. Do not replace this with a rotated board transform plus
input remapping. Portrait/square Floor bounds are returned unchanged, and applying the
normalizer twice is a no-op.

## Mobile quality and batching rules

- Portrait only.
- Safe-area anchors are mandatory for home and gameplay controls.
- Low tier (4 GB-class devices such as Galaxy A05): 60 FPS, MSAA off, reduced particle
  limits, lazy motion VFX, and no per-cell silhouette shadow.
- Standard tier: 60 FPS and 2× MSAA.
- High tier (including Galaxy S22 Ultra): 120 FPS and 4× MSAA so LTPO does not settle at
  30 Hz and procedural block edges remain clean.
- Unknown hardware defaults to Standard. Sustained poor frame rate may downgrade a
  session by one tier; quality never upgrades mid-session.
- Paintable caps and bodies are pooled per cell. Immutable skirts, bevels, holes, hole
  edges, and shadows are combined into one mesh per material to reduce draw calls.
- Home prewarms 704 dynamic cubes (`16 × 22 × body/cap`) over multiple frames so the
  largest board does not instantiate paintable geometry during activation.
- Extra Hard 1–5000 is packaged. Beyond that boundary, a sequential background buffer
  keeps `N+1` and `N+2` ready and persisted without launching both searches together.
  No loading or preparation screen is part of the transition.
- Android builds use ARM64 IL2CPP.
- Runtime shaders referenced through `Shader.Find` must stay in
  `BuildTool.EnsureAlwaysIncluded`.

## Rendering invariants

Before changing visuals, preserve these rules:

1. `Theme.Hole == Theme.Background`.
2. Floor is raised; Wall is recessed; Void emits nothing.
3. Board shadows and visual bounds use Floor cells only.
4. Paint changes materials but not geometry or tile state.
5. Spawn is painted without the pop animation when a level loads.
6. Home preview remains cosmetic.
7. HUD controls only raise existing events.
8. Board framing remains coupled to `HeaderBottomScreenY`.
9. Dark and light modes use the same geometry.
10. No rendering change may modify level packs, generation, solver, movement, or win
    conditions.
11. Portrait normalization may rotate only the in-memory gameplay copy; canonical
    provider and cache bytes remain unchanged.

## Testing and validation

`Assets/Tests/EditMode/BoardRenderingTests.cs` covers:

- background-linked recessed Wall pits in both themes
- no geometry for Void
- raised Floor extrusion and derived darker sides
- deterministic non-uniform top shading
- Floor-only silhouette shadow and boundary bevels
- glossy ball and elliptical contact shadow
- 16×22 pool sizing, static batching, and projected camera bounds
- Floor-bounds-based 90° portrait normalization and 22×16 rendered bounds

For any meaningful UI/rendering change:

1. Run the complete EditMode suite.
2. Build `build/PaintMaze.apk`.
3. Install on the S22 Ultra.
4. Inspect a small, large, irregular, and wall-dense board.
5. Check dark and light themes.
6. Check unpainted, partially painted, and completed states.
7. Verify header spacing, back interaction, swipe input, hint interaction, and safe areas.

The reference-style pass is validated by the complete EditMode suite, all 6,500 physical
baked source levels passing pack validation, and the production boards being inspected
on the S22 Ultra.

### Adaptive performance validation (2026-07-13)

Release IL2CPP measurements for prefetched Medium level 1:

- Galaxy A05 (`SM-A055F`, 3,661 MB reported): selected Low; `GetLevel` 0.0 ms,
  board build 38.0 ms, camera framing 2.8 ms, and the first automated swipe was accepted
  349 ms after gameplay became ready.
- Galaxy S22 Ultra (`SM-S908U1`, 11,204 MB reported): selected High; `GetLevel` 0.0 ms,
  board build 20.6 ms, camera framing 1.4 ms, and the first automated swipe was accepted
  322 ms after gameplay became ready.

The APK installed and launched successfully on both devices.

### Arena production validation (2026-07-14)

The approved v6 production catalog was replayed on the Galaxy S22 Ultra at visible Extra
Hard levels 1, 251, 501, 751, 1001, and 2002. Landscape source levels were normalized as
12×14→14×12, 14×16→16×14, 16×18→18×16, and 16×22→22×16. The square 9×9 control remained
unchanged. Development replay measurements were:

- board build: 33.5–58.1 ms
- camera framing: 20.7–29.7 ms
- `GetLevel` after prefetch: 0.0 ms

For Extra Hard 1001, the rendered board bounds changed from roughly 1320×837 pixels to
1344×1562 pixels. Tile width increased from about 60 to 84 pixels while the board stayed
clear of the title and hint control. Real Down, Up, Left, and Right swipes painted the
matching screen-space lanes. An activity restart restored the unpainted portrait board,
and completing the square control advanced level 1 to level 2.

Large-board hints now use `Solver.SuggestProgressMove`, a stop-position search bounded by
board area, rather than the painted-state shortest-solution search. On Extra Hard 1001,
the hint visibly leaned the ball in the suggested direction. S22 memory measured 496,977
KB PSS before the hint and 505,181 KB afterward; the former search had climbed from
471,521 KB to 888,862 KB within four seconds.

Cold development launches for generated Extra Hard 1001, 2000, and 10500 reached a
rendered board in 8.7–9.7 seconds. The final release build reached cold Extra Hard 2002
in 4.4 seconds, including process launch and Home prefetch; its board build was 35.2 ms
and camera framing was 0.9 ms. A cached development reload reached the board in 3.7
seconds.

The final portrait release installed over the development build, preserved player
progress, and opened Home at Extra Hard level 2003. Its 22×16 gameplay board built in
26.7 ms and framed in 1.6 ms. Android reported 401,132 KB total PSS before the release
hint and 404,325 KB afterward. The captured release log contained no fatal exception,
ANR, out-of-memory, or low-memory-killer entry.

The original arena production pass finished with 225/225 EditMode tests and 2,500/2,500
baked boards accepted before the packaged runway was extended.

### Instant-transition validation (2026-07-14)

The final 5,000-level Extra Hard runway contains 6,500 physical source boards in 130
files. Independent pack verification accepted all boards and the complete EditMode suite
passed. SHA-256 verification confirmed that the original 20 Extra Hard/Ultra Hard source
files were byte-identical. The release APK increased by 212,076 bytes (about 0.92%).

Clean development-build boundary measurements at Extra Hard 5000:

- Galaxy S22 Ultra: `GetLevel` 0.0 ms, board build 53.6 ms, camera 21.8 ms; 5001 and
  5002 prepared sequentially in 5.36 and 5.20 seconds.
- Galaxy A05: `GetLevel` 0.0 ms, board build 80.5 ms; 5001 and 5002 prepared
  sequentially in 16.97 and 15.49 seconds. Input was accepted during background work.

After relaunch, prepared level 5001 activated with `GetLevel` at 1.1 ms on S22 and
1.3 ms on A05. Board build was 46.6/76.1 ms. PSS after the rolling buffer settled was
approximately 504/291 MB, and both Android crash buffers were empty. Final release APKs
installed and launched on both devices without a loading UI.

## Key files

```text
Assets/Scripts/Game/AppRoot.cs
Assets/Scripts/Game/BoardView3D.cs
Assets/Scripts/Game/GameController.cs
Assets/Scripts/Game/PortraitLevelOrientation.cs
Assets/Scripts/Game/UIManager.cs
Assets/Scripts/Game/Hud.cs
Assets/Scripts/Game/SwipeInput.cs
Assets/Scripts/Game/CameraShake.cs
Assets/Scripts/Game/UiPressFx.cs
Assets/Scripts/Menu/HomeController.cs
Assets/Scripts/Menu/HomeMazePreview.cs
Assets/Scripts/Menu/DifficultySlider.cs
Assets/Scripts/Menu/SettingsPanel.cs
Assets/Scripts/Menu/CompleteController.cs
Assets/Scripts/Services/Theme.cs
Assets/Scripts/Services/UiKit.cs
Assets/Scripts/Services/SpriteFactory.cs
Assets/Scripts/Services/LevelLabel.cs
Assets/Tests/EditMode/BoardRenderingTests.cs
Assets/Editor/BuildTool.cs
```
