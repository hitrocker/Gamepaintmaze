using System.Reflection;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;
using PaintMaze.Game;
using PaintMaze.Services;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class BoardRenderingTests
    {
        private GameObject _root;
        private ThemeMode _themeBefore;

        [SetUp]
        public void SetUp()
        {
            _themeBefore = Theme.Mode;
            Theme.Mode = ThemeMode.Dark;
            _root = new GameObject("BoardRenderingTest");
        }

        [TearDown]
        public void TearDown()
        {
            Theme.Mode = _themeBefore;
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [TestCase(ThemeMode.Dark)]
        [TestCase(ThemeMode.Light)]
        public void Wall_IsBackgroundLinkedRecessedHole_AndVoidEmitsNothing(ThemeMode mode)
        {
            Theme.Mode = mode;
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.Build(TestHelpers.Make("S#", "._"), Theme.Paint(1));

            Assert.IsNotNull(_root.transform.Find("body_0_0"));
            Assert.IsNotNull(_root.transform.Find("cap_0_0"));
            Assert.IsNull(_root.transform.Find("body_0_1"));
            Assert.IsNull(_root.transform.Find("cap_0_1"));
            Assert.IsNull(_root.transform.Find("skirt_0_1"));

            Transform hole = Require("Static_Hole");
            float holeTop = hole.GetComponent<MeshFilter>().sharedMesh.bounds.max.y;
            Assert.Less(holeTop, board.SurfaceHeight);
            Assert.AreEqual(Theme.Background, hole.GetComponent<MeshRenderer>().sharedMaterial.color);
            Assert.AreEqual(Theme.Hole, hole.GetComponent<MeshRenderer>().sharedMaterial.color);
            Assert.AreEqual(Theme.HoleInner,
                Require("Static_HoleInner").GetComponent<MeshRenderer>().sharedMaterial.color);

            Assert.IsNull(_root.transform.Find("body_1_1"));
            Assert.IsNull(_root.transform.Find("cap_1_1"));
            Assert.IsNull(_root.transform.Find("hole_1_1"));
            Assert.IsNull(_root.transform.Find("boardShadow_1_1"));
        }

        [Test]
        public void Floor_IsExtrudedWithDerivedDarkerSides()
        {
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.Build(TestHelpers.Make("S#", "._"), Theme.Paint(1));

            Transform body = Require("body_0_0");
            Transform cap = Require("cap_0_0");
            Color side = body.GetComponent<MeshRenderer>().sharedMaterial.color;
            Color top = cap.GetComponent<MeshRenderer>().sharedMaterial.color;

            Assert.Greater(body.localScale.y, cap.localScale.y);
            Assert.AreEqual(board.SurfaceHeight, board.TopHeight, 0.0001f);
            Assert.Less(side.grayscale, top.grayscale);
            Assert.AreEqual(Theme.PlayableSide, side);
        }

        [Test]
        public void TopFaceVariation_IsSubtleDeterministicAndNonUniform()
        {
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.Build(TestHelpers.Make("S..", "...", "..."), Theme.Paint(1));

            Color first = Require("cap_0_0").GetComponent<MeshRenderer>().sharedMaterial.color;
            bool foundVariation = false;
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    Color color = Require($"cap_{r}_{c}").GetComponent<MeshRenderer>().sharedMaterial.color;
                    if (color != first) foundVariation = true;
                    Assert.Less(Vector4.Distance(color, Theme.PlayableTop), 0.08f);
                }
            }
            Assert.IsTrue(foundVariation);

            var otherRoot = new GameObject("BoardRenderingRepeat");
            try
            {
                BoardView3D repeat = otherRoot.AddComponent<BoardView3D>();
                repeat.Build(TestHelpers.Make("S..", "...", "..."), Theme.Paint(1));
                Color repeated = otherRoot.transform.Find("cap_0_0")
                    .GetComponent<MeshRenderer>().sharedMaterial.color;
                Assert.AreEqual(first, repeated);
            }
            finally
            {
                Object.DestroyImmediate(otherRoot);
            }
        }

        [Test]
        public void FloorOnlyShadowAndBoundaryBevel_FollowRaisedSilhouette()
        {
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.Build(TestHelpers.Make("S#", "._"), Theme.Paint(1));

            Assert.AreEqual(Theme.BoardShadow,
                Require("Static_BoardShadow").GetComponent<MeshRenderer>().sharedMaterial.color);
            Assert.IsNull(_root.transform.Find("boardShadow_0_1"));
            Assert.IsNull(_root.transform.Find("boardShadow_1_1"));
            Color rim = Require("Static_BoardRim")
                .GetComponent<MeshRenderer>().sharedMaterial.color;
            Color playableSide = Require("body_0_0")
                .GetComponent<MeshRenderer>().sharedMaterial.color;
            Assert.AreEqual(Theme.BoardRim, rim);
            Assert.AreEqual(playableSide, rim);
        }

        [Test]
        public void Ball_IsGlossyAndHasEllipticalContactShadow()
        {
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.Build(TestHelpers.Make("S#", "._"), Theme.Paint(1));

            var ballRenderer = Require("Ball").GetComponent<MeshRenderer>();
            Assert.AreEqual("Standard", ballRenderer.sharedMaterial.shader.name);
            Assert.Greater(ballRenderer.sharedMaterial.GetFloat("_Glossiness"), 0.5f);
            Assert.IsNotNull(_root.transform.Find("Ball/BallSpin/Highlight"));

            Transform shadow = Require("BallShadow");
            Assert.Greater(shadow.localScale.x, shadow.localScale.y);
            Assert.Less(shadow.localPosition.y, board.Ball.localPosition.y);
        }

        [Test]
        public void PaintSplash_ReusesOnePaintColoredEmitter_WithFullAndLightBursts()
        {
            SaveService.PaintSplashesEnabled = true;
            Color paint = Theme.Paint(17);
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.Build(TestHelpers.Make("S.", ".."), paint);

            ParticleSystem splash = Require("PaintSplashFx").GetComponent<ParticleSystem>();
            Assert.IsNotNull(splash);
            Assert.AreEqual(Color.Lerp(paint, Color.white, 0.12f),
                splash.main.startColor.color);
            var splashRenderer = splash.GetComponent<ParticleSystemRenderer>();
            Assert.AreEqual(ParticleSystemRenderMode.Mesh, splashRenderer.renderMode);
            Assert.IsNotNull(splashRenderer.mesh);
            Assert.AreEqual(0, splash.particleCount, "spawn setup should not splash");

            board.EmitPaintSplash(new Position(0, 1), true);
            Assert.AreEqual(BoardView3D.NewTileSplashCount, splash.particleCount);

            splash.Clear();
            board.EmitPaintSplash(new Position(0, 1), false);
            Assert.AreEqual(BoardView3D.RevisitedTileSplashCount, splash.particleCount);
            Assert.AreEqual(1, CountChildrenNamed(_root.transform, "PaintSplashFx"));

            splash.Clear();
            SaveService.PaintSplashesEnabled = false;
            board.EmitPaintSplash(new Position(0, 1), true);
            Assert.AreEqual(0, splash.particleCount);
            SaveService.PaintSplashesEnabled = true;
        }

        [Test]
        public void CompletionPulse_ReusesCapTransformsAndSharedSparkEmitter()
        {
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.Build(TestHelpers.Make("S.", ".."), Theme.Paint(2));
            board.SetPainted(new Position(0, 0), false);
            board.SetPainted(new Position(0, 1), false);
            board.SetPainted(new Position(1, 0), false);
            board.SetPainted(new Position(1, 1), false);

            var pulse = board.PlayCompletionPulse();
            Assert.IsTrue(pulse.MoveNext());

            Assert.AreEqual(4, board.ActiveCapPopCount);
            Assert.AreEqual(1, CountChildrenNamed(_root.transform, "SparkFx"));
        }

        [Test]
        public void Rebuild_ReusesPooledPaintableCubes()
        {
            BoardView3D board = _root.AddComponent<BoardView3D>();
            Level level = TestHelpers.Make("S.", "..");
            board.Build(level, Theme.Paint(1));
            int pooledAfterFirst = board.PooledCubeCount;

            board.Build(level, Theme.Paint(2));

            Assert.AreEqual(pooledAfterFirst, board.PooledCubeCount);
            Assert.AreEqual(8, board.ActivePooledCubeCount);
            Assert.IsNotNull(_root.transform.Find("cap_0_0"));
        }

        [Test]
        public void ImmutableGeometry_IsCombinedIntoMaterialBatches()
        {
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.Build(TestHelpers.Make("S#", ".."), Theme.Paint(1));

            Assert.LessOrEqual(board.StaticBatchCount, 4);
            Assert.IsNull(_root.transform.Find("skirt_1_0"));
            Assert.IsNotNull(_root.transform.Find("Static_BoardRim"));
            Assert.IsNotNull(_root.transform.Find("Static_Hole"));
        }

        [Test]
        public void ReferenceSizeBoard_UsesPoolAndConstantTimeProjectedBounds()
        {
            const int rows = 16;
            const int cols = 22;
            var grid = new Tile[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    grid[r, c] = Tile.Floor;
            var level = new Level(grid, new Position(0, 0), Difficulty.UltraHard, 501);

            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.ConfigureQuality(DeviceQualityTier.Low);
            board.Build(level, Theme.Paint(1));

            Assert.AreEqual(rows * cols * 2, board.ActivePooledCubeCount);
            Assert.LessOrEqual(board.StaticBatchCount, 4);

            var cameraRoot = new GameObject("LargeBoardCamera", typeof(Camera));
            cameraRoot.transform.SetParent(_root.transform, false);
            cameraRoot.transform.position = new Vector3(0f, 18f, -24f);
            cameraRoot.transform.LookAt(Vector3.zero);
            Camera camera = cameraRoot.GetComponent<Camera>();
            board.ProjectedViewportBounds(
                camera, out float minX, out float maxX, out float minY, out float maxY);

            Assert.IsFalse(float.IsInfinity(minX));
            Assert.Greater(maxX, minX);
            Assert.Greater(maxY, minY);
        }

        [Test]
        public void LandscapeReferenceBoard_NormalizesToPortraitRenderedBounds()
        {
            const int rows = 16;
            const int cols = 22;
            var grid = new Tile[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    grid[r, c] = Tile.Floor;
            var source = new Level(
                grid, new Position(rows - 1, cols - 1),
                Difficulty.UltraHard, 501);

            Level portrait = PortraitLevelOrientation.Apply(source);
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.ConfigureQuality(DeviceQualityTier.Low);
            board.Build(portrait, Theme.Paint(1));

            Assert.AreEqual(16, source.Rows);
            Assert.AreEqual(22, source.Cols);
            Assert.AreEqual(22, portrait.Rows);
            Assert.AreEqual(16, portrait.Cols);
            Assert.AreEqual(22, board.RenderedRows);
            Assert.AreEqual(16, board.RenderedCols);
            Assert.Greater(board.RenderedRows, board.RenderedCols);
            Assert.AreEqual(rows * cols * 2, board.ActivePooledCubeCount);
        }

        [Test]
        public void LowTier_DefersMotionVfxUntilRequested()
        {
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.ConfigureQuality(DeviceQualityTier.Low);
            board.Build(TestHelpers.Make("S.", ".."), Theme.Paint(1));

            Assert.IsNull(_root.transform.Find("SparkFx"));
            Assert.IsNull(_root.transform.Find("PaintSplashFx"));
            Assert.IsNull(_root.transform.Find("Ball").GetComponent<TrailRenderer>());

            board.EnsureMotionVfx();

            Assert.IsNotNull(_root.transform.Find("SparkFx"));
            Assert.IsNotNull(_root.transform.Find("PaintSplashFx"));
            Assert.IsNotNull(_root.transform.Find("Ball").GetComponent<TrailRenderer>());
            Assert.AreEqual(160,
                _root.transform.Find("SparkFx").GetComponent<ParticleSystem>().main.maxParticles);
        }

        [Test]
        public void RouteHint_ShowsThinWhiteFlowingChevronsAndClearsWithoutDiscardingPool()
        {
            Level level = TestHelpers.Make(
                "S..",
                "...",
                "...");
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.Build(level, Theme.Paint(1));
            var route = HintRouteBuilder.Build(
                level, level.Spawn, new[] { SwipeDirection.Right });

            board.ShowRouteHint(route);

            Assert.IsTrue(board.RouteHintVisible);
            Assert.AreEqual(2, board.ActiveHintChevronCount);
            Assert.AreEqual(2, board.PooledHintChevronCount);
            LineRenderer arrow = Require("RouteHintOverlay/HintChevron_0/Arrow")
                .GetComponent<LineRenderer>();
            Assert.Greater(arrow.GetPosition(1).x, arrow.GetPosition(0).x);
            Assert.That(arrow.startWidth, Is.EqualTo(0.045f).Within(0.0001f));
            Assert.AreEqual(5, arrow.numCapVertices);
            Assert.AreEqual(5, arrow.numCornerVertices);
            Assert.That(arrow.sharedMaterial.color, Is.EqualTo(Color.white));
            Assert.IsNull(
                _root.transform.Find("RouteHintOverlay/HintChevron_0/Shadow"));

            Vector3 beforeFlow = arrow.GetPosition(1);
            typeof(BoardView3D).GetMethod(
                "AdvanceHintFlow",
                BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(
                board, new object[] { 0.2f });
            Assert.Greater(arrow.GetPosition(1).x, beforeFlow.x);

            board.ClearRouteHint();

            Assert.IsFalse(board.RouteHintVisible);
            Assert.AreEqual(0, board.ActiveHintChevronCount);
            Assert.AreEqual(2, board.PooledHintChevronCount);
            Assert.IsFalse(Require("RouteHintOverlay/HintChevron_0").gameObject.activeSelf);
        }

        [Test]
        public void RouteHint_ReplacementAndBoardRebuildReuseAndCleanOverlay()
        {
            Level level = TestHelpers.Make(
                "S..",
                "...",
                "...");
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.Build(level, Theme.Paint(1));
            board.ShowRouteHint(HintRouteBuilder.Build(
                level, level.Spawn, new[] { SwipeDirection.Right }));
            int pooled = board.PooledHintChevronCount;

            board.ShowRouteHint(HintRouteBuilder.Build(
                level, new Position(0, 2), new[] { SwipeDirection.Down }));

            Assert.AreEqual(pooled, board.PooledHintChevronCount);
            LineRenderer arrow = Require("RouteHintOverlay/HintChevron_0/Arrow")
                .GetComponent<LineRenderer>();
            Assert.Less(arrow.GetPosition(1).z, arrow.GetPosition(0).z);

            board.Build(level, Theme.Paint(2));

            Assert.IsFalse(board.RouteHintVisible);
            Assert.AreEqual(0, board.ActiveHintChevronCount);
            Assert.AreEqual(pooled, board.PooledHintChevronCount);
        }

        [Test]
        public void RouteHint_WithMultipleSegmentsDisplaysOnlyTheFirstSlide()
        {
            Level level = TestHelpers.Make(
                "S..",
                "...",
                "...");
            BoardView3D board = _root.AddComponent<BoardView3D>();
            board.Build(level, Theme.Paint(1));
            var route = HintRouteBuilder.Build(
                level,
                level.Spawn,
                new[] { SwipeDirection.Right, SwipeDirection.Down });

            board.ShowRouteHint(route);

            Assert.AreEqual(2, board.ActiveHintChevronCount);
            LineRenderer arrow = Require("RouteHintOverlay/HintChevron_0/Arrow")
                .GetComponent<LineRenderer>();
            Assert.Greater(arrow.GetPosition(1).x, arrow.GetPosition(0).x);
        }

        private Transform Require(string childName)
        {
            Transform child = _root.transform.Find(childName);
            Assert.IsNotNull(child, childName);
            return child;
        }

        private static int CountChildrenNamed(Transform root, string childName)
        {
            int count = root.name == childName ? 1 : 0;
            foreach (Transform child in root)
                count += CountChildrenNamed(child, childName);
            return count;
        }
    }
}
