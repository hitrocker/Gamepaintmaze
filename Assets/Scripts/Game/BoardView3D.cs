using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PaintMaze.Domain;
using PaintMaze.Services;

namespace PaintMaze.Game
{
    /// <summary>
    /// Reference-style 2.5D presentation. Floor cells form the raised paintable slab,
    /// Wall cells are background-coloured recessed cutouts, and exterior Void cells emit
    /// no geometry. All distinctions here are visual only; gameplay remains in
    /// GameController and the domain layer.
    /// </summary>
    public sealed class BoardView3D : MonoBehaviour
    {
        public const float Size = 1f;
        private const float CapInset = 0.05f;     // top cap inset -> dark rim = grid line (both axes)
        // The camera is pitched 65deg (25deg off vertical, see AppRoot.CamPitch), which
        // foreshortens the depth (row/Z) axis by cos(25deg). Pre-stretching the Z insets
        // by 1/cos(25deg) makes the depth- and width-direction grid lines read equally.
        private const float DepthStretch = 1.1034f; // 1 / cos(25 deg)
        private const float BoardHeight = 0.40f;
        private const float CapHeight = 0.06f;    // thin colored top cap on the dark body
        private const float BoardRimHeight = 0.10f;
        private const float HoleDepth = 0.28f;
        private const float HoleFloorHeight = 0.025f;
        private const float HoleEdgeWidth = 0.075f;
        private const float HoleEdgeHeight = 0.025f;
        private const float PerimeterBevelWidth = 0f;
        private const float PerimeterBevelHeight = 0.022f;
        private const float SilhouetteShadowHeight = 0.035f;
        private static readonly Vector3 SilhouetteShadowOffset = new(0.10f, -0.055f, -0.10f);
        private const float BallScale = 0.62f;
        private const int ShadeBandCount = 5;

        public float SurfaceHeight => BoardHeight;
        public float TopHeight => BoardHeight;

        private readonly Dictionary<Position, MeshRenderer> _caps = new();
        private readonly Dictionary<Position, MeshRenderer> _bodies = new();
        private readonly Dictionary<Position, int> _shadeBands = new();
        private readonly List<(Vector3 center, float height)> _renderedCellBounds = new();
        private readonly List<CapPop> _activeCapPops = new();
        private readonly List<GameObject> _cubePool = new();
        private readonly List<GameObject> _transientRoots = new();
        private readonly List<Mesh> _generatedMeshes = new();
        private readonly Dictionary<string, StaticBatch> _staticBatches = new();
        private Material[] _matPlayableTops, _matPaintTops;
        private Material _matPlayableSide, _matHole, _matHoleInner;
        private Material _matPaintSide, _matBall, _matBoardRim, _matBoardShadow;
        private GameObject _ball;
        private Transform _ballSpin;
        private Vector3 _ballBaseScale = Vector3.one;
        private int _rows, _cols;
        private int _minRow, _maxRow, _minCol, _maxCol;
        private Vector3 _opticalCenterLocal;
        private int _cubeUseCount;
        private DeviceQualityTier _qualityTier = DeviceQualityTier.High;
        private Color _paintForVfx;

        // ---- Presentation extras (grounding shadow + spark & trail feedback) ----
        private Transform _shadow;
        private Vector3 _shadowBaseScale;
        private ParticleSystem _sparkFx;
        private ParticleSystem _paintSplashFx;
        private Color _paintSplashColor;
        private Vector3 _sparkPrevPos;
        private bool _sparkTracking;
        private TrailRenderer _trail;
        private bool _completionPulsePlaying;
        private readonly List<HintChevron> _hintChevronPool = new();
        private Transform _routeHintRoot;
        private Coroutine _routeHintRoutine;
        private HintRouteSegment _routeHintSegment;
        private Vector3 _routeHintForward;
        private float _hintFlowOffsetCells;
        private int _activeHintChevronCount;

        private const float CapPopPeak = 1.32f;
        private const float CapPopDuration = 0.17f;
        private const float HintChevronLength = 0.17f;
        private const float HintChevronHalfWidth = 0.13f;
        private const float HintChevronWidth = 0.045f;
        private const float HintEdgeInset = 0.21f;
        private const float HintEdgeFadeLength = 0.30f;
        private const float HintChevronHeight = BoardHeight + 0.065f;
        private const float HintFlowSpeedCells = 0.85f;
        public const int NewTileSplashCount = 8;
        public const int RevisitedTileSplashCount = 3;

        private struct CapPop
        {
            public Transform Target;
            public Vector3 BaseScale;
            public float Elapsed;
            public float Peak;
            public float Duration;
            public float Delay;
        }

        private sealed class HintChevron
        {
            public GameObject Root;
            public LineRenderer Foreground;
        }

        private sealed class StaticBatch
        {
            public Material Material;
            public readonly List<CombineInstance> Instances = new();
        }

        public int Rows => _rows;
        public int Cols => _cols;
        public float BoardExtent => Mathf.Max(RenderedRows, RenderedCols);
        public int RenderedRows => _maxRow - _minRow + 1;
        public int RenderedCols => _maxCol - _minCol + 1;
        public Vector3 RenderedCenterLocal => new(
            (_minCol + _maxCol) * 0.5f - (_cols - 1) * 0.5f,
            0f,
            (_rows - 1) * 0.5f - (_minRow + _maxRow) * 0.5f);
        public Vector3 OpticalCenterLocal => _opticalCenterLocal;
        public DeviceQualityTier QualityTier => _qualityTier;

        public void ConfigureQuality(DeviceQualityTier tier)
        {
            _qualityTier = tier;
        }

        private void OnDisable()
        {
            ClearRouteHint();
        }

        public void Build(Level level, Color paint)
        {
            ClearRouteHint();
            _completionPulsePlaying = false;
            _activeCapPops.Clear();
            ResetBuildObjects();
            _caps.Clear();
            _bodies.Clear();
            _shadeBands.Clear();
            _renderedCellBounds.Clear();
            _staticBatches.Clear();
            _rows = level.Rows;
            _cols = level.Cols;
            _paintForVfx = paint;
            ComputeRenderedBounds(level);

            // The top retains the theme palette while all depth colors are derived from
            // it. Quantized material bands avoid a unique material for every tile.
            _matPlayableSide = FlatMat(Theme.PlayableSide);
            _matPaintSide = FlatMat(Color.Lerp(paint, Color.black, 0.32f));
            _matPlayableTops = BuildShadeMaterials(Theme.PlayableTop);
            _matPaintTops = BuildShadeMaterials(paint);
            _matHole = FlatMat(Theme.Hole);
            _matHoleInner = FlatMat(Theme.HoleInner);
            _matBall = MakeMat(Theme.Ball, 0.62f);
            _matBoardRim = FlatMat(Theme.BoardRim);
            _matBoardShadow = FlatMat(Theme.BoardShadow);

            float stateBodyHeight = BoardHeight - BoardRimHeight - CapHeight;
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    var pos = new Position(r, c);
                    var cell = CellLocal(pos);

                    Tile tile = level.Grid[r, c];
                    if (tile == Tile.Void)
                        continue;

                    if (tile == Tile.Wall)
                    {
                        BuildHoleCell(level, r, c, cell);
                        continue;
                    }

                    _renderedCellBounds.Add((cell, BoardHeight));
                    int shadeBand = ShadeBand(r, c);
                    _shadeBands[pos] = shadeBand;
                    BuildFloorCell(level, r, c, pos, cell, stateBodyHeight, shadeBand);
                }
            }
            BuildStaticGeometry();
            if (_activeCapPops.Capacity < _caps.Count)
                _activeCapPops.Capacity = _caps.Count;

            _ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _transientRoots.Add(_ball);
            _ball.name = "Ball";
            _ball.transform.SetParent(transform, false);
            _ballBaseScale = Vector3.one * (Size * BallScale);
            _ball.transform.localScale = _ballBaseScale;
            DestroyImmediate(_ball.GetComponent<Collider>());
            var br = _ball.GetComponent<MeshRenderer>();
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            br.sharedMaterial = _matBall;

            // Spin/scale are applied to an inner pivot so squash and roll stay
            // independent. A bright top-left glint reinforces the glossy sphere.
            _ballSpin = new GameObject("BallSpin").transform;
            _ballSpin.SetParent(_ball.transform, false);
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Highlight";
            dot.transform.SetParent(_ballSpin, false);
            dot.transform.localScale = new Vector3(0.23f, 0.12f, 0.23f);
            dot.transform.localPosition = new Vector3(-0.15f, 0.43f, 0.11f);
            DestroyImmediate(dot.GetComponent<Collider>());
            var dr = dot.GetComponent<MeshRenderer>();
            dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dr.receiveShadows = false;
            dr.sharedMaterial = FlatMat(Color.Lerp(Theme.Ball, Color.white, 0.88f));

            BuildShadow();
            if (MotionVfxProfile.For(_qualityTier).EagerBuild)
                EnsureMotionVfx();

            _sparkTracking = false; // don't spark on the spawn teleport
            SetBallCell(level.Spawn);
        }

        private void BuildFloorCell(Level level, int row, int col, Position pos, Vector3 cell,
            float stateBodyHeight, int shadeBand)
        {
            // Overlapping Floor-only under-plates visually merge into one soft shadow
            // following the exact irregular slab silhouette.
            if (_qualityTier != DeviceQualityTier.Low)
            {
                AddStaticCube("BoardShadow", _matBoardShadow,
                    cell + SilhouetteShadowOffset + Vector3.up * (SilhouetteShadowHeight * 0.5f),
                    new Vector3(Size * 1.035f, SilhouetteShadowHeight, Size * 1.035f));
            }

            AddStaticCube("BoardRim", _matBoardRim,
                cell + Vector3.up * (BoardRimHeight * 0.5f),
                new Vector3(Size, BoardRimHeight, Size));

            var body = MakeCube($"body_{row}_{col}", _matPlayableSide);
            body.transform.localScale = new Vector3(Size, stateBodyHeight, Size);
            body.transform.localPosition =
                cell + Vector3.up * (BoardRimHeight + stateBodyHeight * 0.5f);

            var cap = MakeCube($"cap_{row}_{col}", _matPlayableTops[shadeBand]);
            cap.transform.localScale =
                new Vector3(Size - CapInset, CapHeight, Size - CapInset * DepthStretch);
            cap.transform.localPosition =
                cell + Vector3.up * (BoardHeight - CapHeight * 0.5f);

            _bodies[pos] = body.GetComponent<MeshRenderer>();
            _caps[pos] = cap.GetComponent<MeshRenderer>();
            BuildPerimeterBevel(level, row, col, cell);
        }

        private void BuildHoleCell(Level level, int row, int col, Vector3 cell)
        {
            // The pit fill is the exact shared background token. Slight overlap removes
            // seams between neighbouring Wall cells so a large cutout reads as one gap.
            float holeSurfaceY = BoardHeight - HoleDepth;
            AddStaticCube("Hole", _matHole,
                cell + Vector3.up * (holeSurfaceY - HoleFloorHeight * 0.5f),
                new Vector3(Size * 1.015f, HoleFloorHeight, Size * 1.015f));

            // A restrained dark strip where the pit meets a playable block supplies the
            // recessed inner shadow without giving the hole a raised tile of its own.
            if (IsFloor(level, row - 1, col))
                BuildHoleEdge(cell, Vector3.forward);
            if (IsFloor(level, row + 1, col))
                BuildHoleEdge(cell, Vector3.back);
            if (IsFloor(level, row, col - 1))
                BuildHoleEdge(cell, Vector3.left);
            if (IsFloor(level, row, col + 1))
                BuildHoleEdge(cell, Vector3.right);
        }

        private void BuildHoleEdge(Vector3 cell, Vector3 direction)
        {
            float holeSurfaceY = BoardHeight - HoleDepth;
            bool horizontal = Mathf.Abs(direction.z) > 0.5f;
            Vector3 scale = horizontal
                ? new Vector3(Size, HoleEdgeHeight, HoleEdgeWidth * DepthStretch)
                : new Vector3(HoleEdgeWidth, HoleEdgeHeight, Size);
            Vector3 position = cell
                + direction * (Size * 0.5f - HoleEdgeWidth * 0.5f)
                + Vector3.up * (holeSurfaceY + HoleEdgeHeight * 0.5f);
            AddStaticCube("HoleInner", _matHoleInner, position, scale);
        }

        private void BuildPerimeterBevel(Level level, int row, int col, Vector3 cell)
        {
            if (!IsFloor(level, row - 1, col))
                BuildBevelEdge(cell, Vector3.forward);
            if (!IsFloor(level, row + 1, col))
                BuildBevelEdge(cell, Vector3.back);
            if (!IsFloor(level, row, col - 1))
                BuildBevelEdge(cell, Vector3.left);
            if (!IsFloor(level, row, col + 1))
                BuildBevelEdge(cell, Vector3.right);
        }

        private void BuildBevelEdge(Vector3 cell, Vector3 direction)
        {
            if (PerimeterBevelWidth <= Mathf.Epsilon) return;

            bool horizontal = Mathf.Abs(direction.z) > 0.5f;
            Vector3 scale = horizontal
                ? new Vector3(Size, PerimeterBevelHeight, PerimeterBevelWidth * DepthStretch)
                : new Vector3(PerimeterBevelWidth, PerimeterBevelHeight, Size);
            Vector3 position = cell
                + direction * (Size * 0.5f - PerimeterBevelWidth * 0.5f)
                + Vector3.up * (BoardHeight - PerimeterBevelHeight * 0.5f);
            AddStaticCube("BoardRim", _matBoardRim, position, scale);
        }

        private static bool IsFloor(Level level, int row, int col) =>
            row >= 0 && row < level.Rows && col >= 0 && col < level.Cols &&
            level.Grid[row, col] == Tile.Floor;

        private static int ShadeBand(int row, int col)
        {
            unchecked
            {
                uint hash = (uint)(row * 73856093) ^ (uint)(col * 19349663) ^ 0x9E3779B9u;
                hash ^= hash >> 16;
                return (int)(hash % ShadeBandCount);
            }
        }

        private static Material[] BuildShadeMaterials(Color baseColor)
        {
            var materials = new Material[ShadeBandCount];
            for (int i = 0; i < materials.Length; i++)
            {
                float signed = i - (ShadeBandCount - 1) * 0.5f;
                Color shade = signed < 0f
                    ? Color.Lerp(baseColor, Color.black, -signed * 0.018f)
                    : Color.Lerp(baseColor, Color.white, signed * 0.014f);
                materials[i] = FlatMat(shade);
            }
            return materials;
        }

        public void ProjectedViewportBounds(
            Camera camera, out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = float.PositiveInfinity;
            maxX = float.NegativeInfinity;
            minY = float.PositiveInfinity;
            maxY = float.NegativeInfinity;

            if (_renderedCellBounds.Count == 0 || _maxRow < _minRow || _maxCol < _minCol)
            {
                minX = maxX = minY = maxY = 0.5f;
                return;
            }

            // Project the occupied board AABB rather than eight corners for every floor
            // cell. This keeps activation framing constant-time on 16x22 boards.
            float localMinX = _minCol - (_cols - 1) * 0.5f - Size * 0.5f;
            float localMaxX = _maxCol - (_cols - 1) * 0.5f + Size * 0.5f;
            float localMinZ = (_rows - 1) * 0.5f - _maxRow - Size * 0.5f;
            float localMaxZ = (_rows - 1) * 0.5f - _minRow + Size * 0.5f;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 local = new Vector3(
                            x == 0 ? localMinX : localMaxX,
                            y == 0 ? 0f : BoardHeight,
                            z == 0 ? localMinZ : localMaxZ);
                        Vector3 viewport = camera.WorldToViewportPoint(
                            transform.TransformPoint(local));
                        minX = Mathf.Min(minX, viewport.x);
                        maxX = Mathf.Max(maxX, viewport.x);
                        minY = Mathf.Min(minY, viewport.y);
                        maxY = Mathf.Max(maxY, viewport.y);
                    }
                }
            }

            if (float.IsInfinity(minX) || float.IsInfinity(maxX) ||
                float.IsInfinity(minY) || float.IsInfinity(maxY))
            {
                minX = maxX = minY = maxY = 0.5f;
            }
        }

        private void ComputeRenderedBounds(Level level)
        {
            _minRow = level.Rows;
            _maxRow = -1;
            _minCol = level.Cols;
            _maxCol = -1;
            float rowSum = 0f;
            float colSum = 0f;
            int occupiedCount = 0;
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    if (level.Grid[r, c] != Tile.Floor) continue;
                    _minRow = Mathf.Min(_minRow, r);
                    _maxRow = Mathf.Max(_maxRow, r);
                    _minCol = Mathf.Min(_minCol, c);
                    _maxCol = Mathf.Max(_maxCol, c);
                    rowSum += r;
                    colSum += c;
                    occupiedCount++;
                }
            }

            // Defensive fallback for malformed all-void data (valid levels always have a
            // floor spawn, so normal builds never take this branch).
            if (_maxRow < _minRow || _maxCol < _minCol)
            {
                _minRow = _maxRow = 0;
                _minCol = _maxCol = 0;
            }

            // Frame from the raised playable silhouette, not the rectangular source grid
            // or the background-coloured recessed cutouts.
            // A restrained centroid correction makes heavily asymmetric shapes look
            // optically centred without sacrificing the equal margins of their bounds.
            Vector3 boundsCenter = RenderedCenterLocal;
            if (occupiedCount == 0)
            {
                _opticalCenterLocal = boundsCenter;
                return;
            }

            float averageRow = rowSum / occupiedCount;
            float averageCol = colSum / occupiedCount;
            var centroid = new Vector3(
                averageCol - (_cols - 1) * 0.5f,
                0f,
                (_rows - 1) * 0.5f - averageRow);
            Vector3 correction = (centroid - boundsCenter) * 0.35f;
            correction.x = Mathf.Clamp(correction.x, -0.35f, 0.35f);
            correction.z = Mathf.Clamp(correction.z, -0.35f, 0.35f);
            _opticalCenterLocal = boundsCenter + correction;
        }

        // A soft, tapering motion streak behind the ball (world-space). White-hot at the
        // head cooling to gold at the tail, additive so it glows with the sparks.
        private void BuildBallTrail()
        {
            if (_trail != null || _ball == null) return;
            _trail = _ball.AddComponent<TrailRenderer>();
            _trail.time = 0.16f;
            _trail.minVertexDistance = 0.02f;
            _trail.widthMultiplier = Size * BallScale * 0.72f;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            _trail.numCapVertices = 4;
            _trail.textureMode = LineTextureMode.Stretch;
            _trail.alignment = LineAlignment.View;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.sharedMaterial = SparkMat();
            _trail.sortingOrder = 4;

            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Theme.Gold, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = g;
            _trail.Clear();
        }

        // A soft dark disc that tracks the ball on the tile surface so it reads as
        // resting on the board instead of floating. Cheap fake (no shadow maps).
        private void BuildShadow()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _transientRoots.Add(quad);
            quad.name = "BallShadow";
            quad.transform.SetParent(transform, false);
            DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lie flat, facing up
            _shadowBaseScale = new Vector3(
                Size * BallScale * 1.18f,
                Size * BallScale * 0.62f,
                1f);
            quad.transform.localScale = _shadowBaseScale;

            var mr = quad.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = BlobMat();
            _shadow = quad.transform;
        }

        // Additive gold sparks that stream off the rolling ball. Emitted by distance
        // (rateOverDistance), so faster/longer slides throw more sparks; a white-hot
        // core cools to gold and fades. Sim space is World so sparks trail behind.
        private void BuildSparkFx()
        {
            if (_sparkFx != null) return;
            var go = new GameObject("SparkFx");
            _transientRoots.Add(go);
            go.transform.SetParent(transform, false);
            _sparkFx = go.AddComponent<ParticleSystem>();
            _sparkFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _sparkFx.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.085f);
            main.gravityModifier = 3.2f;
            main.startColor = Color.white;
            main.maxParticles = MotionVfxProfile.For(_qualityTier).SparkMaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _sparkFx.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 16f;

            var shape = _sparkFx.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.07f;

            // White-hot core -> gold -> fade.
            var col = _sparkFx.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Theme.Gold, 0.45f),
                    new GradientColorKey(Theme.Gold, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = _sparkFx.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = SparkMat();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 2.5f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 6; // draw over tiles and shadow
            _sparkFx.Play();
        }

        /// <summary>A radial burst of sparks at the ball (e.g. on wall impact).</summary>
        public void BurstSparks(int count = 14)
        {
            if (_sparkFx == null) EnsureMotionVfx();
            if (_sparkFx == null) return;
            _sparkFx.Emit(count);
        }

        public IEnumerator PlayCompletionPulse()
        {
            if (_completionPulsePlaying || _caps.Count == 0) yield break;
            _completionPulsePlaying = true;
            EnsureMotionVfx();
            BurstSparks(MotionVfxProfile.For(_qualityTier).CompletionSparkCount);

            Vector3 origin = _ball != null
                ? _ball.transform.localPosition
                : Vector3.zero;
            float maxDistance = 0.001f;
            foreach (MeshRenderer cap in _caps.Values)
            {
                if (cap == null) continue;
                maxDistance = Mathf.Max(maxDistance,
                    Vector3.Distance(origin, cap.transform.localPosition));
            }

            foreach (MeshRenderer cap in _caps.Values)
            {
                if (cap == null) continue;
                float distance = Vector3.Distance(origin, cap.transform.localPosition);
                float delay = distance / maxDistance *
                    GameFeedbackProfile.CompletionPulseSpread;
                float duration = GameFeedbackProfile.CompletionPulseDuration -
                    GameFeedbackProfile.CompletionPulseSpread;
                StartCapPop(cap.transform, 1.14f, duration, delay);
            }

            float elapsed = 0f;
            while (elapsed < GameFeedbackProfile.CompletionPulseDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _completionPulsePlaying = false;
        }

        // One shared emitter serves every tile crossing. A short, gravity-driven burst
        // reads as wet paint without creating a ParticleSystem per tile.
        private void BuildPaintSplashFx(Color paint)
        {
            if (_paintSplashFx != null) return;
            var go = new GameObject("PaintSplashFx");
            _transientRoots.Add(go);
            go.transform.SetParent(transform, false);
            _paintSplashFx = go.AddComponent<ParticleSystem>();
            _paintSplashFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _paintSplashColor = Color.Lerp(paint, Color.white, 0.12f);

            var main = _paintSplashFx.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.42f, 0.68f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 2.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.25f);
            main.gravityModifier = 1.4f;
            main.startColor = _paintSplashColor;
            main.maxParticles = MotionVfxProfile.For(_qualityTier).SplashMaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _paintSplashFx.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;

            var shape = _paintSplashFx.shape;
            shape.enabled = false;

            var color = _paintSplashFx.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = new ParticleSystem.MinMaxGradient(fade);

            var size = _paintSplashFx.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.65f),
                new Keyframe(0.18f, 1f),
                new Keyframe(1f, 0f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = FlatMat(_paintSplashColor);
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = SphereMesh();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 7;

            // Keep simulation alive with both automatic rates at zero; Emit() supplies
            // allocation-free bursts at explicit tile-entry times.
            _paintSplashFx.Play();
        }

        public void EmitPaintSplash(Position tile, bool newlyPainted)
        {
            EmitPaintSplash(tile, GameFeedbackProfile.Paint(
                0, newlyPainted, _qualityTier));
        }

        public void EmitPaintSplash(Position tile, PaintFeedback feedback)
        {
            if (_paintSplashFx == null && SaveService.PaintSplashesEnabled)
                EnsureMotionVfx();
            if (!SaveService.PaintSplashesEnabled ||
                _paintSplashFx == null || !_caps.ContainsKey(tile))
                return;

            int count = feedback.SplashCount;
            Vector3 worldPosition = transform.TransformPoint(
                CellLocal(tile) + Vector3.up * (BoardHeight + 0.16f));
            float phase = ((tile.Row * 17 + tile.Col * 31) & 7) * (Mathf.PI * 0.25f);

            for (int i = 0; i < count; i++)
            {
                float angle = phase + i * (Mathf.PI * 2f / count);
                float horizontalSpeed = 1.35f + (i % 3) * 0.3f;
                var localVelocity = new Vector3(
                    Mathf.Cos(angle) * horizontalSpeed,
                    2f + (i % 2) * 0.45f,
                    Mathf.Sin(angle) * horizontalSpeed);
                var emit = new ParticleSystem.EmitParams
                {
                    position = worldPosition,
                    velocity = transform.TransformDirection(localVelocity),
                    startColor = _paintSplashColor,
                    startLifetime = 0.48f + (i % 3) * 0.07f,
                    startSize = 0.17f + (i % 2) * 0.05f
                };
                _paintSplashFx.Emit(emit, 1);
            }
        }

        public void EnsureMotionVfx()
        {
            if (_ball == null) return;
            BuildSparkFx();
            if (SaveService.PaintSplashesEnabled)
                BuildPaintSplashFx(_paintForVfx);
            BuildBallTrail();
        }

        private void LateUpdate()
        {
            UpdateCapPops(Time.unscaledDeltaTime);
            if (_ball == null) return;
            var b = _ball.transform.localPosition;

            if (_shadow != null)
            {
                _shadow.localPosition = new Vector3(b.x + 0.035f, BoardHeight + 0.012f, b.z - 0.025f);
                // Widen slightly with the ball's squash so the contact stays believable.
                float squashK2 = _ballBaseScale.x > 0f ? _ball.transform.localScale.x / _ballBaseScale.x : 1f;
                float k2 = Mathf.Lerp(1f, squashK2, 0.5f);
                _shadow.localScale = new Vector3(
                    _shadowBaseScale.x * k2,
                    _shadowBaseScale.y * k2,
                    _shadowBaseScale.z);
            }

            // Move the spark emitter to the ball's contact point. rateOverDistance turns
            // the emitter's travel into a spark trail; suppress emission on teleports
            // (spawn/reset) by disabling the module for that single frame.
            if (_sparkFx != null)
            {
                Vector3 contact = new Vector3(b.x, BoardHeight + 0.04f, b.z);
                bool teleport = !_sparkTracking || (contact - _sparkPrevPos).magnitude > 1.2f;
                var em = _sparkFx.emission;
                em.enabled = !teleport;
                _sparkFx.transform.localPosition = contact;
                _sparkPrevPos = contact;
                _sparkTracking = true;
            }
        }

        public void ShowRouteHint(IReadOnlyList<HintRouteSegment> segments)
        {
            ClearRouteHint();
            if (segments == null || segments.Count == 0) return;

            HintRouteSegment segment = null;
            for (int i = 0; i < segments.Count; i++)
            {
                HintRouteSegment candidate = segments[i];
                if (candidate != null && candidate.Cells != null && candidate.Cells.Count > 0)
                {
                    segment = candidate;
                    break;
                }
            }
            if (segment == null) return;

            ShowHintSegment(segment);
            if (Application.isPlaying && isActiveAndEnabled)
                _routeHintRoutine = StartCoroutine(AnimateRouteHint());
        }

        public void ClearRouteHint()
        {
            if (_routeHintRoutine != null)
            {
                StopCoroutine(_routeHintRoutine);
                _routeHintRoutine = null;
            }
            _routeHintSegment = null;
            _hintFlowOffsetCells = 0f;
            HideHintChevrons();
        }

        private IEnumerator AnimateRouteHint()
        {
            while (_routeHintSegment != null)
            {
                AdvanceHintFlow(Time.unscaledDeltaTime);
                yield return null;
            }

            _routeHintRoutine = null;
        }

        private void ShowHintSegment(HintRouteSegment segment)
        {
            HideHintChevrons();
            EnsureHintChevronPool(segment.Cells.Count);

            _routeHintSegment = segment;
            _routeHintForward = new(
                segment.Direction.DCol(), 0f, -segment.Direction.DRow());
            _hintFlowOffsetCells = 0f;
            for (int i = 0; i < segment.Cells.Count; i++)
            {
                HintChevron chevron = _hintChevronPool[i];
                chevron.Root.SetActive(true);
            }
            _activeHintChevronCount = segment.Cells.Count;
            LayoutHintChevrons();
        }

        private void AdvanceHintFlow(float deltaTime)
        {
            if (_routeHintSegment == null || _routeHintSegment.Cells.Count == 0) return;
            float travelLength = HintTravelLength(_routeHintSegment.Cells.Count);
            _hintFlowOffsetCells = Mathf.Repeat(
                _hintFlowOffsetCells + Mathf.Max(0f, deltaTime) * HintFlowSpeedCells,
                travelLength);
            LayoutHintChevrons();
        }

        private void LayoutHintChevrons()
        {
            if (_routeHintSegment == null) return;

            int count = _routeHintSegment.Cells.Count;
            Vector3 start = CellLocal(_routeHintSegment.Start);
            float routeLength = count * Size;
            float inset = Mathf.Min(HintEdgeInset, routeLength * 0.32f);
            float travelLength = HintTravelLength(count);
            float spacing = travelLength / count;
            for (int i = 0; i < count; i++)
            {
                float phase = (i + 0.5f) * spacing;
                float distance = inset +
                    Mathf.Repeat(phase + _hintFlowOffsetCells, travelLength);
                float edgeDistance = Mathf.Min(
                    distance - inset,
                    routeLength - inset - distance);
                float alpha = Mathf.SmoothStep(
                    0f, 0.94f, Mathf.Clamp01(edgeDistance / HintEdgeFadeLength));
                SetHintChevron(
                    _hintChevronPool[i].Foreground,
                    start,
                    _routeHintForward,
                    distance * Size,
                    HintChevronHeight,
                    alpha);
            }
        }

        private static float HintTravelLength(int cellCount)
        {
            float routeLength = Mathf.Max(1, cellCount) * Size;
            float inset = Mathf.Min(HintEdgeInset, routeLength * 0.32f);
            return Mathf.Max(0.01f, routeLength - inset * 2f);
        }

        private void HideHintChevrons()
        {
            for (int i = 0; i < _hintChevronPool.Count; i++)
            {
                HintChevron chevron = _hintChevronPool[i];
                if (chevron?.Root != null) chevron.Root.SetActive(false);
            }
            _activeHintChevronCount = 0;
        }

        private void EnsureHintChevronPool(int count)
        {
            if (_routeHintRoot == null)
            {
                var root = new GameObject("RouteHintOverlay");
                root.transform.SetParent(transform, false);
                _routeHintRoot = root.transform;
            }

            while (_hintChevronPool.Count < count)
                _hintChevronPool.Add(CreateHintChevron(_hintChevronPool.Count));
        }

        private HintChevron CreateHintChevron(int index)
        {
            var root = new GameObject($"HintChevron_{index}");
            root.transform.SetParent(_routeHintRoot, false);

            LineRenderer foreground = CreateHintLine(
                root.transform, "Arrow", HintLineMaterial(), 5);

            root.SetActive(false);
            return new HintChevron
            {
                Root = root,
                Foreground = foreground
            };
        }

        private static LineRenderer CreateHintLine(
            Transform parent, string name, Material material, int sortingOrder)
        {
            var go = new GameObject(name, typeof(LineRenderer));
            go.transform.SetParent(parent, false);
            var line = go.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = 3;
            line.alignment = LineAlignment.View;
            line.numCornerVertices = 5;
            line.numCapVertices = 5;
            line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = sortingOrder;
            return line;
        }

        private static void SetHintChevron(
            LineRenderer line,
            Vector3 routeStart,
            Vector3 forward,
            float centerDistance,
            float height,
            float alpha)
        {
            Vector3 side = new(-forward.z, 0f, forward.x);
            Vector3 center = routeStart + forward * centerDistance;
            Vector3 tip = center + forward * HintChevronLength;
            Vector3 back = center - forward * HintChevronLength;
            Vector3 left = back + side * HintChevronHalfWidth;
            Vector3 right = back - side * HintChevronHalfWidth;
            tip.y = height;
            left.y = height;
            right.y = height;
            line.startWidth = HintChevronWidth;
            line.endWidth = HintChevronWidth;
            Color color = new(1f, 1f, 1f, alpha);
            line.startColor = color;
            line.endColor = color;
            line.SetPosition(0, left);
            line.SetPosition(1, tip);
            line.SetPosition(2, right);
        }

        public IEnumerator PrewarmPool(int targetCount, int perFrame)
        {
            targetCount = Mathf.Max(0, targetCount);
            perFrame = Mathf.Max(1, perFrame);
            while (_cubePool.Count < targetCount)
            {
                int end = Mathf.Min(targetCount, _cubePool.Count + perFrame);
                while (_cubePool.Count < end)
                {
                    GameObject cube = CreatePooledCube();
                    cube.SetActive(false);
                    _cubePool.Add(cube);
                }
                yield return null;
            }
        }

        public int PooledCubeCount => _cubePool.Count;
        public int ActivePooledCubeCount => _cubeUseCount;
        public int StaticBatchCount => _staticBatches.Count;
        public int ActiveCapPopCount => _activeCapPops.Count;
        public int ActiveHintChevronCount => _activeHintChevronCount;
        public int PooledHintChevronCount => _hintChevronPool.Count;
        public bool RouteHintVisible => _activeHintChevronCount > 0;

        private void ResetBuildObjects()
        {
            _cubeUseCount = 0;
            foreach (GameObject cube in _cubePool)
            {
                if (cube == null) continue;
                cube.SetActive(false);
                cube.transform.localRotation = Quaternion.identity;
                cube.transform.localScale = Vector3.one;
            }

            foreach (GameObject root in _transientRoots)
                DestroyOwned(root);
            _transientRoots.Clear();

            foreach (Mesh mesh in _generatedMeshes)
                DestroyOwned(mesh);
            _generatedMeshes.Clear();

            _ball = null;
            _ballSpin = null;
            _shadow = null;
            _sparkFx = null;
            _paintSplashFx = null;
            _trail = null;
        }

        private void AddStaticCube(string batchName, Material material,
            Vector3 localPosition, Vector3 localScale)
        {
            if (!_staticBatches.TryGetValue(batchName, out StaticBatch batch))
            {
                batch = new StaticBatch { Material = material };
                _staticBatches[batchName] = batch;
            }

            batch.Instances.Add(new CombineInstance
            {
                mesh = CubeMesh(),
                transform = Matrix4x4.TRS(localPosition, Quaternion.identity, localScale)
            });
        }

        private void BuildStaticGeometry()
        {
            foreach (var pair in _staticBatches)
            {
                StaticBatch batch = pair.Value;
                if (batch.Instances.Count == 0) continue;

                var go = new GameObject($"Static_{pair.Key}",
                    typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                var mesh = new Mesh
                {
                    name = $"Board_{pair.Key}",
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
                };
                mesh.CombineMeshes(batch.Instances.ToArray(), true, true, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = batch.Material;
                _generatedMeshes.Add(mesh);
                _transientRoots.Add(go);
            }
        }

        private GameObject MakeCube(string name, Material mat)
        {
            GameObject cube;
            if (_cubeUseCount < _cubePool.Count)
            {
                cube = _cubePool[_cubeUseCount];
            }
            else
            {
                cube = CreatePooledCube();
                _cubePool.Add(cube);
            }
            _cubeUseCount++;
            cube.name = name;
            cube.transform.SetParent(transform, false);
            cube.transform.localPosition = Vector3.zero;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = Vector3.one;
            var mr = cube.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = mat;
            cube.SetActive(true);
            return cube;
        }

        private GameObject CreatePooledCube()
        {
            var cube = new GameObject("PooledCube", typeof(MeshFilter), typeof(MeshRenderer));
            cube.transform.SetParent(transform, false);
            cube.GetComponent<MeshFilter>().sharedMesh = CubeMesh();
            return cube;
        }

        private static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private Vector3 CellLocal(Position p) =>
            new Vector3(p.Col - (_cols - 1) / 2f, 0f, (_rows - 1) / 2f - p.Row);

        public Vector3 BallLocalFor(Position p)
        {
            var v = CellLocal(p);
            v.y = BoardHeight + (Size * BallScale) / 2f;
            return v;
        }

        public void SetBallCell(Position p)
        {
            _ball.transform.localPosition = BallLocalFor(p);
            if (_trail != null) _trail.Clear(); // no streak across a teleport
        }
        public void SetBallLocal(Vector3 local) => _ball.transform.localPosition = local;
        public Transform Ball => _ball.transform;

        /// <summary>Base (unsquashed) ball scale, for restoring after squash/stretch.</summary>
        public Vector3 BallBaseScale => _ballBaseScale;

        /// <summary>Sets the ball's overall scale (used for squash/stretch on impact).</summary>
        public void SetBallScale(Vector3 scale)
        {
            if (_ball != null) _ball.transform.localScale = scale;
        }

        public void ResetBallScale()
        {
            if (_ball != null) _ball.transform.localScale = _ballBaseScale;
        }

        /// <summary>Sets the inner spin pivot's local rotation (visible rolling).</summary>
        public void SetBallSpin(Quaternion rot)
        {
            if (_ballSpin != null) _ballSpin.localRotation = rot;
        }

        public void ResetBallSpin()
        {
            if (_ballSpin != null) _ballSpin.localRotation = Quaternion.identity;
        }

        public void SetPainted(Position p) => SetPainted(p, true);

        // <paramref name="pop"/> false paints the tile with no scale-punch — used for
        // the spawn cell at load, which is already counted as painted (see GameState)
        // and shouldn't animate before the player has moved.
        public void SetPainted(Position p, bool pop)
        {
            if (_caps.TryGetValue(p, out var cap))
            {
                int shadeBand = _shadeBands.TryGetValue(p, out int band)
                    ? band
                    : (ShadeBandCount - 1) / 2;
                cap.sharedMaterial = _matPaintTops[shadeBand];
                if (_bodies.TryGetValue(p, out var body)) body.sharedMaterial = _matPaintSide;

                // Juice: a quick scale-punch on the cap (color-independent cue). Active
                // punches use a pre-sized list so crossing tiles does not allocate.
                if (pop && isActiveAndEnabled) StartCapPop(cap.transform);
            }
        }

        private void StartCapPop(Transform target)
        {
            StartCapPop(target, CapPopPeak, CapPopDuration, 0f);
        }

        private void StartCapPop(
            Transform target, float peak, float duration, float delay)
        {
            if (target == null) return;

            for (int i = 0; i < _activeCapPops.Count; i++)
            {
                CapPop active = _activeCapPops[i];
                if (active.Target != target) continue;
                active.Elapsed = -Mathf.Max(0f, delay);
                active.Peak = peak;
                active.Duration = Mathf.Max(0.01f, duration);
                active.Delay = Mathf.Max(0f, delay);
                _activeCapPops[i] = active;
                target.localScale = active.BaseScale;
                if (active.Delay <= 0f)
                    SetCapPopScale(target, active.BaseScale, active.Peak);
                return;
            }

            Vector3 baseScale = target.localScale;
            _activeCapPops.Add(new CapPop
            {
                Target = target,
                BaseScale = baseScale,
                Elapsed = -Mathf.Max(0f, delay),
                Peak = peak,
                Duration = Mathf.Max(0.01f, duration),
                Delay = Mathf.Max(0f, delay)
            });
            if (delay <= 0f) SetCapPopScale(target, baseScale, peak);
        }

        private void UpdateCapPops(float deltaTime)
        {
            for (int i = _activeCapPops.Count - 1; i >= 0; i--)
            {
                CapPop active = _activeCapPops[i];
                if (active.Target == null)
                {
                    RemoveCapPopAt(i);
                    continue;
                }

                active.Elapsed += deltaTime;
                if (active.Elapsed < 0f)
                {
                    _activeCapPops[i] = active;
                    continue;
                }
                if (active.Elapsed >= active.Duration)
                {
                    active.Target.localScale = active.BaseScale;
                    RemoveCapPopAt(i);
                    continue;
                }

                float k = Mathf.Clamp01(active.Elapsed / active.Duration);
                float easeOut = 1f - (1f - k) * (1f - k);
                float scale = Mathf.Lerp(active.Peak, 1f, easeOut);
                SetCapPopScale(active.Target, active.BaseScale, scale);
                _activeCapPops[i] = active;
            }
        }

        private void RemoveCapPopAt(int index)
        {
            int last = _activeCapPops.Count - 1;
            _activeCapPops[index] = _activeCapPops[last];
            _activeCapPops.RemoveAt(last);
        }

        private static void SetCapPopScale(Transform target, Vector3 baseScale, float scale)
        {
            target.localScale = new Vector3(baseScale.x * scale, baseScale.y, baseScale.z * scale);
        }

        private static readonly Dictionary<(Color32 color, int smoothness), Material> StandardMaterials = new();
        private static readonly Dictionary<Color32, Material> FlatMaterials = new();
        private static Material _hintLineMaterial;
        private static Material _blobMaterial;
        private static Material _sparkMaterial;

        private static Material MakeMat(Color color, float smoothness = 0.05f)
        {
            var key = ((Color32)color, Mathf.RoundToInt(smoothness * 1000f));
            if (StandardMaterials.TryGetValue(key, out Material cached)) return cached;
            var m = new Material(Shader.Find("Standard")) { color = color };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            StandardMaterials[key] = m;
            return m;
        }

        private static Material FlatMat(Color color)
        {
            Color32 key = color;
            if (FlatMaterials.TryGetValue(key, out Material cached)) return cached;
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) return MakeMat(color); // safety fallback
            var m = new Material(shader);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            m.color = color;
            FlatMaterials[key] = m;
            return m;
        }

        private static Material HintLineMaterial()
        {
            if (_hintLineMaterial != null) return _hintLineMaterial;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _hintLineMaterial = new Material(shader)
            {
                color = Color.white,
                mainTexture = Texture2D.whiteTexture
            };
            return _hintLineMaterial;
        }

        // Soft black radial disc for the fake ball shadow (color baked into the texture
        // since Unlit/Transparent ignores material tint).
        private static Material BlobMat()
        {
            if (_blobMaterial != null) return _blobMaterial;
            var shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            _blobMaterial = new Material(shader) { mainTexture = ShadowTex() };
            return _blobMaterial;
        }

        // Additive material for the ball sparks: adds light to whatever's behind so the
        // gold reads against any tile/paint color. Legacy additive respects per-particle
        // (colorOverLifetime) vertex color; _TintColor 0.5 grey = neutral (×2 -> ×1).
        private static Material SparkMat()
        {
            if (_sparkMaterial != null) return _sparkMaterial;
            var shader = Shader.Find("Legacy Shaders/Particles/Additive")
                         ?? Shader.Find("Mobile/Particles/Additive")
                         ?? Shader.Find("Sprites/Default");
            var m = new Material(shader) { mainTexture = DotTex() };
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            _sparkMaterial = m;
            return _sparkMaterial;
        }

        private static Mesh _cubeMesh, _sphereMesh;
        private static Mesh CubeMesh()
        {
            if (_cubeMesh != null) return _cubeMesh;
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(primitive);
            return _cubeMesh;
        }

        private static Mesh SphereMesh()
        {
            if (_sphereMesh != null) return _sphereMesh;
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(primitive);
            return _sphereMesh;
        }

        private static Texture2D _shadowTex, _dotTex;
        private static Texture2D ShadowTex() =>
            _shadowTex != null ? _shadowTex : (_shadowTex = SoftDot(128, 0.27f, 0f));
        private static Texture2D DotTex() =>
            _dotTex != null ? _dotTex : (_dotTex = SoftDot(64, 1f, 1f));

        // Radial gradient texture: rgb=<gray>, alpha opaque(ish) center -> transparent edge.
        private static Texture2D SoftDot(int size, float maxAlpha, float gray)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float r = size / 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - r) / r;
                    float dy = (y + 0.5f - r) / r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * maxAlpha; // squared falloff = soft edge
                    px[y * size + x] = new Color(gray, gray, gray, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
