using System;
using System.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Firebase.Auth;
using PaintMaze.Core;
using PaintMaze.Domain;
using PaintMaze.Menu;
using PaintMaze.Services;

namespace PaintMaze.Game
{
    /// <summary>
    /// Single entry point. Build a scene with one empty GameObject and add this
    /// component (see README). It creates the tilted 2.5D board camera/light, the
    /// UI canvas/EventSystem, loads the level packs, wires the providers, and
    /// drives Home → Game → Complete — so no prefabs or per-scene wiring are needed.
    /// </summary>
    public sealed class AppRoot : MonoBehaviour
    {
        // Camera pitch in degrees (90 = straight down). A little under 90 gives a
        // subtle 3D tilt so the extruded block sides and recessed holes read.
        private const float CamPitch = 65f;   // degrees down from horizontal (25 deg off vertical)
        private const float CamFov = 32f;
        // In-plane spin of the whole board. 0 = axis-aligned (no yaw).
        private const float BoardYaw = 0f;
        // The title RectTransform includes generous font ascender/descender padding;
        // 12dp from its box produces a visible glyph-to-board gap of roughly 28dp.
        private const float HeaderBoardGapDp = 12f;
        private const float BoardBottomViewportLimit = 0.16f;
        private const float BoardSideViewportLimit = 0.04f;
        private static readonly ProfilerMarker GetLevelMarker = new("PaintMaze.GetLevel");
        private static readonly ProfilerMarker BoardLoadMarker = new("PaintMaze.BoardLoad");
        private static readonly ProfilerMarker FrameCameraMarker = new("PaintMaze.FrameCamera");

        private Canvas _canvas;
        private RectTransform _canvasRt;
        private Camera _cam;
        private Light _sun;
        private BoardView3D _board;
        private Renderer _bgRenderer;
        private Material _bgMat;

        private ILevelProvider _provider;
        private IPrefetchLevelProvider _prefetch;
        private ILevelMemoryWindow _memoryWindow;
        private SequentialLevelPrefetchBuffer _aheadPrefetch;
        private DeviceQualityTier _qualityTier;

        private GameObject _homeRoot, _gameRoot, _completeRoot, _authRoot, _accountRoot;
        private GameObject _leaderboardRoot;
        private GameObject _revealRoot;
        private UiPage _activePage = UiPage.Home;
        private HomeController _home;
        private CompleteController _complete;
        private SignInController _signInController;
        private AccountController _accountController;
        private LeaderboardController _leaderboardController;
        private ScreenRevealController _screenReveal;
        private GameController _game;
        private UIManager _hud;
        private AuthService _authService;
        private GoogleCredentialBridge _googleCredential;
        private LeaderboardService _leaderboardService;
        private ProgressSyncService _progressSyncService;
        private AccountDeletionService _accountDeletionService;
        private EconomySyncService _economySyncService;
        private CosmeticService _cosmeticService;
        private HintService _hintService;
        private HintPurchaseDialog _hintPurchaseDialog;
        private ActiveLevelSessionStore _activeLevelSessions;
        private DailyPuzzleService _dailyPuzzleService;
        private DailyLeaderboardService _dailyLeaderboardService;
        private Coroutine _loadRoutine;
        private Coroutine _aheadRoutine;
        private string _podiumClaimDayId;

        private Difficulty _currentDifficulty;
        private int _currentIndex;
        private bool _isDailyLevel;
        private DailyPuzzleStatus _activeDailyPuzzle;
        private string _dailySessionOwner;
        private float _dailyElapsedSeconds;
        private float _dailyTimerStartedAt;
        private bool _dailyTimerRunning;
        private bool _resumeDailyTimerAfterPause;
        private bool _applicationPaused;
        private int _displayedDailyMillisecond = -1;
        private bool _hasDevelopmentOverride;
        private int _developmentIndex;

        private void Awake()
        {
            Screen.orientation = ScreenOrientation.Portrait;
            _qualityTier = DeviceQualityProfile.ResolveCurrent();
            DeviceQualityProfile.Apply(_qualityTier);

            Theme.Mode = SaveService.ThemeMode;
            SaveService.RestoreLastProgressOwner();
            _activeLevelSessions = new ActiveLevelSessionStore();

            EnsureEventSystem();
            Build3DWorld();
            BuildCanvas();
            _board.ConfigureQuality(_qualityTier);

            var qualityMonitor = gameObject.AddComponent<DeviceQualityMonitor>();
            qualityMonitor.TierChanged += ApplyRuntimeQuality;

            var audio = gameObject.AddComponent<AudioService>();
            audio.Init();

            var firebase = gameObject.AddComponent<FirebaseService>();
            _authService = gameObject.AddComponent<AuthService>();
            _googleCredential = gameObject.AddComponent<GoogleCredentialBridge>();
            _leaderboardService = gameObject.AddComponent<LeaderboardService>();
            _economySyncService = gameObject.AddComponent<EconomySyncService>();
            _cosmeticService = new CosmeticService(_economySyncService);
            _hintService = new HintService(_economySyncService);
            _progressSyncService = gameObject.AddComponent<ProgressSyncService>();
            _accountDeletionService = gameObject.AddComponent<AccountDeletionService>();
            _authService.Init(firebase);
            _authService.UserChanged += OnAuthUserChanged;
            _leaderboardService.Init(firebase, _authService);
            _economySyncService.Init(firebase, _authService);
            _economySyncService.EconomyApplied += OnEconomyApplied;
            _progressSyncService.Init(
                _authService,
                _leaderboardService,
                _economySyncService);
            _progressSyncService.ProgressApplied += OnProgressApplied;
            bool storeScreenshotPlayer = ScreenshotPlayerMode.IsActive;
            string[] launchArgs = Environment.GetCommandLineArgs();
            bool captureAutomation = storeScreenshotPlayer &&
                                     (ScreenshotPlayerMode.HasCommandLineArgument(
                                          launchArgs, "-captureOutput") ||
                                      ScreenshotPlayerMode.HasCommandLineArgument(
                                          launchArgs, "-demoVideoOutput"));
            _dailyPuzzleService = new DailyPuzzleService();
            _dailyLeaderboardService =
                gameObject.AddComponent<DailyLeaderboardService>();
            _dailyLeaderboardService.Init(firebase, _authService);
            if (storeScreenshotPlayer)
                _progressSyncService.Suspend();
            _accountDeletionService.Init(
                _authService,
                _leaderboardService,
                _progressSyncService,
                _dailyLeaderboardService,
                _economySyncService);
            if (!captureAutomation)
                firebase.Init();

            _hasDevelopmentOverride =
                TryGetDevelopmentLevelOverride(out _developmentIndex);
            ApplyDevelopmentCoinGrant();
            _provider = BuildProvider();
            _prefetch = _provider as IPrefetchLevelProvider;
            _memoryWindow = _provider as ILevelMemoryWindow;
            if (_prefetch != null)
                _aheadPrefetch = new SequentialLevelPrefetchBuffer(_prefetch);

            BuildHome();
            BuildGameScreen();
            BuildComplete();
            BuildAuth();
            BuildAccount();
            BuildLeaderboard();
            BuildScreenReveal();

            ApplyTheme();
            Debug.Log($"[Perf] tier={_qualityTier} memoryMb={SystemInfo.systemMemorySize} " +
                      $"cores={SystemInfo.processorCount}");
            ShowHome();
            if (_hasDevelopmentOverride)
            {
                _hasDevelopmentOverride = false;
                Debug.Log($"[DeviceTest] Opening Extra Hard #{_developmentIndex}");
                StartLevel(Difficulty.ExtraHard, _developmentIndex);
            }
            if (!captureAutomation)
                _authService.EnsureSessionSilently();
        }

        private static bool TryGetDevelopmentLevelOverride(out int index)
        {
            index = 0;
            if (!Debug.isDebugBuild) return false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var properties = new AndroidJavaClass("android.os.SystemProperties");
                string raw = properties.CallStatic<string>(
                    "get", "debug.paintmaze.level", string.Empty);
                return int.TryParse(raw, out index) &&
                       index >= 1 &&
                       index <= DifficultyConfig.PracticalMaxLevel;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DeviceTest] Could not read level override: " + ex.Message);
            }
#endif
            return false;
        }

        private static void ApplyDevelopmentCoinGrant()
        {
            if (!Debug.isDebugBuild) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            string raw = string.Empty;
            try
            {
                using var properties = new AndroidJavaClass("android.os.SystemProperties");
                raw = properties.CallStatic<string>(
                    "get", "debug.paintmaze.coins", string.Empty);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DeviceTest] Could not read coin override: " + ex.Message);
            }

            int next = DeviceTestSupport.ResolveDebugCoinGrant(raw, SaveService.Coins);
            if (next != SaveService.Coins)
                SaveService.Coins = next;
            Debug.Log("[DeviceTest] coins=" + SaveService.Coins);
#endif
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<StandaloneInputModule>();
        }

        private void Build3DWorld()
        {
            foreach (var existing in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                Destroy(existing.gameObject);

            var camGo = new GameObject("BoardCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            _cam = camGo.GetComponent<Camera>();
            _cam.orthographic = false;
            _cam.fieldOfView = CamFov;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 500f;
            _cam.cullingMask &= ~(1 << BallSkinLivePreview.PreviewLayer);
            _cam.transform.rotation = Quaternion.Euler(CamPitch, 0, 0);

            var lightGo = new GameObject("Sun", typeof(Light));
            _sun = lightGo.GetComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.color = Color.white;
            _sun.intensity = 0.75f;
            _sun.shadows = LightShadows.None;
            _sun.cullingMask &= ~(1 << BallSkinLivePreview.PreviewLayer);
            // The board uses one explicit flat-shaded treatment for color stability;
            // this restrained light rounds the ball without washing out board colors.
            _sun.transform.rotation = Quaternion.Euler(50f, -35f, 0);

            var boardGo = new GameObject("BoardRoot");
            boardGo.transform.rotation = Quaternion.Euler(0f, BoardYaw, 0f);
            _board = boardGo.AddComponent<BoardView3D>();

            BuildGradientBackground();
        }

        // A full-frame decorative layer, deliberately separate from the board geometry.
        // Sprites/Default is double-sided (no facing worries) and depth-tests behind the
        // opaque board. (Texture-driven, so a gradient can be reintroduced via the stops.)
        private void BuildGradientBackground()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "BackgroundGradient";
            quad.transform.SetParent(_cam.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 250f);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(400f, 400f, 1f);
            Destroy(quad.GetComponent<Collider>());

            _bgRenderer = quad.GetComponent<Renderer>();
            _bgRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _bgRenderer.receiveShadows = false;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Texture");
            _bgMat = new Material(shader);
            _bgRenderer.sharedMaterial = _bgMat;
            RefreshGradient();
        }

        private void RefreshGradient()
        {
            if (_bgMat == null) return;
            // Flat decorative fill. Recessed Wall pits use this exact Theme.Background
            // token, while their derived inner edges provide the cutout depth.
            Color bg = Theme.Background;

            const int h = 256;
            var tex = new Texture2D(4, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var col = new Color[4 * h];
            for (int i = 0; i < col.Length; i++) col[i] = bg;
            tex.SetPixels(col);
            tex.Apply();
            _bgMat.mainTexture = tex;
        }

        private void ApplyTheme()
        {
            if (_cam != null) _cam.backgroundColor = Theme.Background;
            RefreshGradient();
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white * 0.55f;
        }

        private void FrameCamera()
        {
            float aspect = _cam.aspect > 0.01f ? _cam.aspect : (float)Screen.width / Mathf.Max(1, Screen.height);
            // Tight reference-style framing: enough breathing room for tall side faces,
            // but the occupied silhouette remains the dominant screen element.
            float margin = 0.45f;

            // Half-angles of the perspective frustum (vertical from FOV, horizontal via aspect).
            float halfVRad = CamFov * 0.5f * Mathf.Deg2Rad;
            float tanV = Mathf.Tan(halfVRad);
            float tanH = tanV * aspect;

            // Reserve symmetric room around the physical screen centre. If the header
            // leaves less room above than the bottom safe limit leaves below, zoom out
            // until the board can remain centred without touching either one.
            Canvas.ForceUpdateCanvases();
            float screenHeight = Mathf.Max(1f, Screen.height);
            float dpi = Screen.dpi >= 80f ? Screen.dpi : 160f;
            float gapPixels = HeaderBoardGapDp * dpi / 160f;
            float headerBottom = _hud != null ? _hud.HeaderBottomScreenY : screenHeight * 0.86f;
            float topLimit = Mathf.Clamp01((headerBottom - gapPixels) / screenHeight);
            float allowedHalfY = Mathf.Max(0.05f,
                Mathf.Min(0.5f - BoardBottomViewportLimit, topLimit - 0.5f));
            float allowedHalfX = 0.5f - BoardSideViewportLimit;

            // The board is spun in-plane by BoardYaw, so its axis-aligned footprint grows.
            // Use the yaw-rotated bounding half-extents (X = screen width, Z = depth).
            float yaw = Mathf.Abs(BoardYaw) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);
            float halfW = _board.RenderedCols * 0.5f;
            float halfD = _board.RenderedRows * 0.5f;
            float halfExtentX = halfW * cos + halfD * sin;
            float halfExtentZ = halfW * sin + halfD * cos;

            // Depth foreshortens with the tilt; the tall blocks also add a little vertical
            // screen coverage, so fold their height into the depth extent.
            float tiltFromVertical = (90f - CamPitch) * Mathf.Deg2Rad;
            float vScreenHalf = halfExtentZ * Mathf.Cos(tiltFromVertical)
                              + _board.TopHeight * Mathf.Sin(tiltFromVertical);
            float dWidth = (halfExtentX + margin) /
                Mathf.Max(0.001f, 2f * allowedHalfX * tanH);
            float dHeight = (vScreenHalf + margin) /
                Mathf.Max(0.001f, 2f * allowedHalfY * tanV);
            float dist = Mathf.Max(dWidth, dHeight);

            // Use the exact rendered-bounds centre. Centroid correction made asymmetric
            // boards feel shifted even when their outer margins were mathematically even.
            Vector3 target = _board.transform.TransformPoint(
                _board.RenderedCenterLocal + Vector3.up * (_board.TopHeight * 0.5f));
            Quaternion rotation = Quaternion.Euler(CamPitch, 0, 0);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 cameraUp = rotation * Vector3.up;
            Vector3 cameraRight = rotation * Vector3.right;

            _cam.transform.position = target - forward * dist;
            _cam.transform.rotation = rotation;

            // Perspective makes the projected centre differ slightly from the world-space
            // bounds centre. Correct both axes from the measured silhouette.
            for (int pass = 0; pass < 3; pass++)
            {
                _board.ProjectedViewportBounds(
                    _cam, out float minX, out float maxX, out float minY, out float maxY);
                float centerX = (minX + maxX) * 0.5f;
                float centerY = (minY + maxY) * 0.5f;
                float shiftX = 0.5f - centerX;
                float shiftY = 0.5f - centerY;
                if (Mathf.Abs(shiftX) < 0.0005f && Mathf.Abs(shiftY) < 0.0005f) break;

                float cameraOffsetX = shiftX * 2f * dist * tanH;
                float cameraOffsetY = shiftY * 2f * dist * tanV;
                _cam.transform.position -=
                    cameraRight * cameraOffsetX + cameraUp * cameraOffsetY;
            }
        }

        private void BuildCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRt = (RectTransform)_canvas.transform;
        }

        private ILevelProvider BuildProvider()
        {
            var baked = new SectionedLevelProvider();
            var cache = new LevelCacheStore();
            var generated = new DeterministicBatchLevelProvider(cache);
            return new ScalableLevelProvider(baked, generated);
        }

        private GameObject NewScreen(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_canvasRt, false);
            UiKit.Stretch((RectTransform)go.transform);
            return go;
        }

        private void ActivatePage(UiPage page)
        {
            _activePage = page;
            _homeRoot?.SetActive(UiFlowPolicy.IsVisible(page, UiPage.Home));
            _gameRoot?.SetActive(UiFlowPolicy.IsVisible(page, UiPage.Game));
            _completeRoot?.SetActive(UiFlowPolicy.IsVisible(page, UiPage.Complete));
            _authRoot?.SetActive(UiFlowPolicy.IsVisible(page, UiPage.SignIn));
            _accountRoot?.SetActive(UiFlowPolicy.IsVisible(page, UiPage.Account));
            _leaderboardRoot?.SetActive(UiFlowPolicy.IsVisible(page, UiPage.Leaderboard));
        }

        private void BuildHome()
        {
            _homeRoot = NewScreen("HomeScreen");
            _home = _homeRoot.AddComponent<HomeController>();
            _home.Build(
                (RectTransform)_homeRoot.transform,
                _authService,
                _dailyPuzzleService,
                _cosmeticService);
            _home.Play += StartLevel;
            _home.DailyPlay += StartDailyPuzzle;
            _home.SelectionChanged += PrefetchHomeWindow;
            _home.ThemeChanged += OnThemeChanged;
            _home.AccountRequested += ShowAccount;
            _home.SignInRequested += ShowAuth;
            _home.LeaderboardRequested += ShowLeaderboard;
            _home.BallSkinChanged += OnBallSkinChanged;
        }

        private void BuildGameScreen()
        {
            _gameRoot = NewScreen("GameScreen");
            var root = (RectTransform)_gameRoot.transform;

            // No background image: the 3D board shows through the overlay canvas.
            var swipeImg = UiKit.Image(root, "SwipeSurface", new Color(0, 0, 0, 0));
            swipeImg.raycastTarget = true;
            UiKit.Stretch(swipeImg.rectTransform);
            var swipe = swipeImg.gameObject.AddComponent<SwipeInput>();

            _hud = root.gameObject.AddComponent<UIManager>();
            _hud.Build(root, SaveService.SelectedDifficulty);
            _hud.Back += ShowHome;
            _hud.Hint += HandleHintPressed;

            _game = _gameRoot.AddComponent<GameController>();
            _game.Setup(_board, swipe, _hud);
            _game.Completed += OnLevelComplete;
            _game.MoveStarted += OnMoveStarted;
            _game.StateCommitted += PersistActiveLevelSession;
            _game.Restarted += OnLevelRestarted;

            var dialogObject = new GameObject(
                "HintPurchaseDialog",
                typeof(RectTransform));
            _hintPurchaseDialog =
                dialogObject.AddComponent<HintPurchaseDialog>();
            _hintPurchaseDialog.Build(root);
        }

        private void BuildComplete()
        {
            _completeRoot = NewScreen("CompleteScreen");
            _complete = _completeRoot.AddComponent<CompleteController>();
            _complete.Build((RectTransform)_completeRoot.transform);
            _complete.Next += OnNext;
            _complete.Home += ShowHome;
            _complete.Replay += ReplayDaily;
        }

        private void BuildAuth()
        {
            _authRoot = NewScreen("SignInScreen");
            _signInController = _authRoot.AddComponent<SignInController>();
            _signInController.Build((RectTransform)_authRoot.transform,
                _authService, _googleCredential, _progressSyncService);
            _signInController.BackRequested += ShowHome;
            _signInController.Completed += OnAuthenticated;
        }

        private void BuildAccount()
        {
            _accountRoot = NewScreen("AccountScreen");
            _accountController = _accountRoot.AddComponent<AccountController>();
            _accountController.Build(
                (RectTransform)_accountRoot.transform, _authService, _googleCredential,
                _progressSyncService, _accountDeletionService);
            _accountController.SignedOut += OnSignedOut;
            _accountController.Closed += ShowHome;
            _accountController.SignInRequested += ShowAuth;
        }

        private void ShowAccount()
        {
            ActivatePage(UiPage.Account);
            _accountController.Show();
        }

        private void BuildLeaderboard()
        {
            _leaderboardRoot = NewScreen("LeaderboardScreen");
            _leaderboardController = _leaderboardRoot.AddComponent<LeaderboardController>();
            _leaderboardController.Build(
                (RectTransform)_leaderboardRoot.transform, _leaderboardService, _authService);
            _leaderboardController.Closed += ShowHome;
            _leaderboardController.SignInRequested += ShowAuth;
        }

        private void ShowLeaderboard()
        {
            ActivatePage(UiPage.Leaderboard);
            _leaderboardController.Show(_home.SelectedDifficulty);
        }

        private void BuildScreenReveal()
        {
            _revealRoot = NewScreen("ScreenReveal");
            _screenReveal = _revealRoot.AddComponent<ScreenRevealController>();
            _screenReveal.Build((RectTransform)_revealRoot.transform);
        }

        private void OnSignedOut()
        {
            _accountRoot.SetActive(false);
            SaveService.SetProgressOwner(null);
            ShowHome();
            _authService.EnsureSessionSilently();
        }

        private void ShowAuth()
        {
            ActivatePage(UiPage.SignIn);
            if (_board != null) _board.gameObject.SetActive(false);
            _signInController.Show(true);
        }

        private void OnAuthenticated()
        {
            _authRoot.SetActive(false);
            ShowHome();
        }

        private void OnAuthUserChanged(FirebaseUser user)
        {
            if (user == null) return;
            if (_homeRoot != null && _homeRoot.activeSelf)
            {
                _home.OnShown();
                TryClaimYesterdayPodium();
            }
        }

        private void OnProgressApplied()
        {
            if (_homeRoot != null && _homeRoot.activeSelf) _home.OnShown();
        }

        private void OnEconomyApplied()
        {
            ApplyDevelopmentCoinGrant();
            if (_homeRoot != null && _homeRoot.activeSelf)
                _home.RefreshCosmetics();
            _board?.RefreshBallSkin();
            RefreshHintHud();
        }

        private void OnBallSkinChanged()
        {
            _board?.RefreshBallSkin();
        }

        private void TryClaimYesterdayPodium()
        {
            if (_dailyLeaderboardService == null || _home == null) return;
            string dayId = DailyPuzzleService.DayId(DateTime.UtcNow.Date.AddDays(-1));
            if (SaveService.WasDailyPodiumGranted(dayId)) return;
            if (_podiumClaimDayId == dayId) return;
            if (!_dailyLeaderboardService.IsReady) return;

            _podiumClaimDayId = dayId;
            _dailyLeaderboardService.ForceRefresh(dayId, result =>
            {
                if (_home == null) return;
                if (result == null ||
                    (!string.IsNullOrEmpty(result.Error) &&
                     result.OwnEntry == null &&
                     !result.OwnRank.HasValue))
                {
                    // Offline / failed load — retry on a later home show.
                    if (_podiumClaimDayId == dayId)
                        _podiumClaimDayId = null;
                    return;
                }

                int rank = int.MaxValue;
                if (result.OwnRank.HasValue)
                    rank = result.OwnRank.Value;
                else if (result.OwnEntry != null && result.Entries != null)
                {
                    for (int i = 0; i < result.Entries.Count; i++)
                    {
                        if (result.Entries[i].UserId == result.OwnEntry.UserId)
                        {
                            rank = i + 1;
                            break;
                        }
                    }
                }

                int granted = CoinService.TryClaimPodium(dayId, rank);
                if (granted > 0)
                {
                    _economySyncService?.PushLocal();
                    _home.RefreshCoins();
                    _home.ShowToast($"Daily #{rank}  ·  +{granted}");
                }
            });
        }

        private void OnThemeChanged()
        {
            // Home already re-themed its own UI; refresh the world background/light.
            ApplyTheme();
            _hud?.ApplyTheme();
            if (_accountRoot != null)
            {
                Destroy(_accountRoot);
                BuildAccount();
            }
            if (_authRoot != null)
            {
                Destroy(_authRoot);
                BuildAuth();
            }
            if (_leaderboardRoot != null)
            {
                Destroy(_leaderboardRoot);
                BuildLeaderboard();
            }
        }

        private void StartLevel(Difficulty difficulty, int index)
        {
            BeginLevel(difficulty, index, false);
        }

        private void StartDailyPuzzle()
        {
            if (_dailyPuzzleService == null) return;

            DailyPuzzleStatus previousStatus = _dailyPuzzleService.Today;
            bool resumingAttempt = previousStatus.IsAttemptInProgress;
            if (!_dailyPuzzleService.TryBeginToday(out _activeDailyPuzzle))
            {
                _home?.OnShown();
                return;
            }
            _dailySessionOwner =
                _dailyPuzzleService.PrepareSessionOwner(out string expiredOwner);
            if (!string.IsNullOrEmpty(expiredOwner))
                _activeLevelSessions?.ClearOwner(expiredOwner);
            if (!resumingAttempt)
            {
                _activeLevelSessions?.ClearOwner(_dailySessionOwner);
                _dailyPuzzleService.ResetRunElapsed(
                    _activeDailyPuzzle.DayId);
            }
            _dailyElapsedSeconds =
                _dailyPuzzleService.GetRunElapsedSeconds(
                    _activeDailyPuzzle.DayId);
            _dailyTimerRunning = false;
            _displayedDailyMillisecond = -1;
            BeginLevel(
                _activeDailyPuzzle.Difficulty,
                _activeDailyPuzzle.LevelIndex,
                true);
        }

        private void BeginLevel(
            Difficulty difficulty,
            int index,
            bool daily)
        {
            if (!daily) ResetDailyTimerRuntime();
            _isDailyLevel = daily;
            _currentDifficulty = difficulty;
            _currentIndex = Mathf.Clamp(index, 1, DifficultyConfig.PracticalMaxLevel);
            if (_loadRoutine != null) StopCoroutine(_loadRoutine);

            _prefetch?.Prefetch(difficulty, _currentIndex);
            if (_prefetch != null && !_prefetch.IsReady(difficulty, _currentIndex))
            {
                // Keep the current Home/Complete screen fully responsive. No loading UI
                // is introduced; the prepared level opens as soon as its worker finishes.
                _loadRoutine = StartCoroutine(
                    WaitForPreparedLevel(difficulty, _currentIndex, daily));
                return;
            }

            ActivatePreparedLevel(difficulty, _currentIndex, daily);
        }

        private IEnumerator WaitForPreparedLevel(
            Difficulty difficulty,
            int index,
            bool daily)
        {
            while (_prefetch != null && !_prefetch.IsReady(difficulty, index))
                yield return null;
            _loadRoutine = null;
            if (_currentDifficulty != difficulty ||
                _currentIndex != index ||
                _isDailyLevel != daily)
                yield break;
            ActivatePreparedLevel(difficulty, index, daily);
        }

        private void ActivatePreparedLevel(
            Difficulty difficulty,
            int index,
            bool daily)
        {
            Level level;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            using (GetLevelMarker.Auto())
                level = _provider.GetLevel(difficulty, index);
            timer.Stop();
            Debug.Log($"[Perf] getLevel={timer.Elapsed.TotalMilliseconds:F1}ms " +
                      $"difficulty={difficulty} index={index}");
            ActivateLevel(level, daily);
        }

        private void ActivateLevel(Level level, bool daily)
        {
            if (level == null) { ShowHome(); return; }
            _isDailyLevel = daily;

            Level gameplayLevel = PortraitLevelOrientation.Apply(level);
            if (!ReferenceEquals(gameplayLevel, level))
            {
                Debug.Log($"[Board] portraitRotation=90cw source={level.Rows}x{level.Cols} " +
                          $"gameplay={gameplayLevel.Rows}x{gameplayLevel.Cols}");
            }

            ActivatePage(UiPage.Game);
            _board.gameObject.SetActive(true);

            var timer = System.Diagnostics.Stopwatch.StartNew();
            GameState restoredState = null;
            string sessionOwner = CurrentSessionOwner;
            if (_activeLevelSessions.TryLoad(
                    sessionOwner,
                    gameplayLevel,
                    out _,
                    out GameState savedState))
            {
                restoredState = savedState;
                Debug.Log(
                    $"[Session] Resuming {gameplayLevel.Difficulty} " +
                    $"#{gameplayLevel.Index} at {savedState.ProgressPercent}%");
            }
            using (BoardLoadMarker.Auto())
            {
                _game.SetPaintIndexOverride(
                    daily
                        ? Theme.PaintIndexForAttempt(
                            gameplayLevel.Index,
                            _activeDailyPuzzle?.AttemptsUsed ?? 1)
                        : null);
                _game.Load(gameplayLevel, restoredState);
            }
            RefreshHintHud();
            if (daily)
            {
                _hud.SetDailyPuzzle(
                    _dailyElapsedSeconds,
                    _activeDailyPuzzle?.AttemptsUsed ?? 1);
                if (restoredState != null && restoredState.MoveCount > 0)
                    StartDailyTimer();
            }
            double boardMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            using (FrameCameraMarker.Auto())
                FrameCamera();
            timer.Stop();
            Debug.Log($"[Perf] boardBuild={boardMs:F1}ms frameCamera={timer.Elapsed.TotalMilliseconds:F1}ms");
            StartCoroutine(WarmMotionVfx());
            if (!daily)
                StartAheadPrefetch(level.Difficulty, level.Index, deferOneFrame: true);
            _screenReveal?.Reveal();
        }

        private void OnLevelComplete()
        {
            ClearCurrentLevelSession();
            if (_isDailyLevel && _dailyPuzzleService != null)
            {
                PauseDailyTimer(true);
                DateTime completedDate =
                    _activeDailyPuzzle?.Date ?? DateTime.UtcNow.Date;
                DailyPuzzleCompletion completion =
                    _dailyPuzzleService.Complete(
                        completedDate,
                        _dailyElapsedSeconds,
                        _game.MoveCount);
                int dailyCoins = CoinService.TryGrantDailyBase(
                    completion.Status?.DayId,
                    completion.WasNewCompletion);
                if (dailyCoins > 0)
                    _economySyncService?.PushLocal();
                _completeRoot.SetActive(true);
                _complete.ShowDaily(completion, dailyCoins);
                _dailyLeaderboardService?.LoadForCompletion(
                    completion,
                    result =>
                    {
                        if (_completeRoot != null &&
                            _completeRoot.activeSelf &&
                            _isDailyLevel)
                            _complete.SetDailyLeaderboardResult(result);
                    });
                return;
            }

            int campaignCoins = CoinService.TryGrantCampaignFirstClear(
                _currentDifficulty, _currentIndex);
            SaveService.MarkCompleted(_currentDifficulty, _currentIndex);
            if (campaignCoins > 0)
                _economySyncService?.PushLocal();
            _leaderboardService.TryRaiseCompletedLevel(_currentDifficulty, _currentIndex);
            PrefetchLevel(_currentDifficulty, LevelLabel.NextLevel(_currentIndex));
            _completeRoot.SetActive(true);
            _complete.Show(_currentDifficulty, _currentIndex, campaignCoins);
        }

        private void OnNext()
        {
            if (_isDailyLevel)
            {
                ReplayDaily();
                return;
            }
            _completeRoot.SetActive(false);
            StartLevel(_currentDifficulty, LevelLabel.NextLevel(_currentIndex));
        }

        private void ReplayDaily()
        {
            if (_dailyPuzzleService == null) return;
            _completeRoot.SetActive(false);
            StartDailyPuzzle();
        }

        private void ShowHome()
        {
            _hintPurchaseDialog?.Hide();
            bool reveal = _board != null && _board.gameObject.activeSelf;
            if (_gameRoot != null && _gameRoot.activeInHierarchy)
                PersistActiveLevelSession();
            PauseDailyTimer(false);
            _isDailyLevel = false;
            _activeDailyPuzzle = null;
            _dailySessionOwner = null;
            ResetDailyTimerRuntime();
            CancelAheadPrefetch();
            if (_loadRoutine != null)
            {
                StopCoroutine(_loadRoutine);
                _loadRoutine = null;
            }
            ActivatePage(UiPage.Home);
            if (_board != null) _board.gameObject.SetActive(false);
            _home.OnShown();
            TryClaimYesterdayPodium();
            DifficultyConfig peak = DifficultyConfig.For(Difficulty.UltraHard);
            int peakDynamicCells = peak.MaxRows * peak.MaxCols * 2;
            StartCoroutine(_board.PrewarmPool(peakDynamicCells, 32));
            if (reveal) _screenReveal?.Reveal();
        }

        private void PersistActiveLevelSession()
        {
            if (_activeLevelSessions == null || _game == null ||
                _game.Level == null)
                return;

            if (_game.IsComplete)
            {
                PauseDailyTimer(true);
                ClearCurrentLevelSession();
                return;
            }

            ActiveLevelSnapshot snapshot =
                _game.CaptureSnapshot(CurrentSessionOwner);
            if (snapshot != null)
                _activeLevelSessions.Save(snapshot);
            SaveDailyTimer();
        }

        private void ClearCurrentLevelSession()
        {
            if (_activeLevelSessions == null || _game?.Level == null) return;
            _activeLevelSessions.Clear(
                CurrentSessionOwner,
                _game.Level.Difficulty);
        }

        private string CurrentSessionOwner =>
            _isDailyLevel && !string.IsNullOrEmpty(_dailySessionOwner)
                ? _dailySessionOwner
                : SaveService.CurrentProgressOwner;

        private void OnApplicationPause(bool paused) =>
            SetApplicationPaused(paused);

        private void OnApplicationFocus(bool focused) =>
            SetApplicationPaused(!focused);

        private void SetApplicationPaused(bool paused)
        {
            if (_applicationPaused == paused) return;
            _applicationPaused = paused;
            if (paused)
            {
                _resumeDailyTimerAfterPause = _dailyTimerRunning;
                if (_gameRoot != null && _gameRoot.activeInHierarchy)
                    PersistActiveLevelSession();
                PauseDailyTimer(true);
                return;
            }

            if (_resumeDailyTimerAfterPause &&
                _isDailyLevel &&
                _gameRoot != null &&
                _gameRoot.activeInHierarchy)
                StartDailyTimer();
            _resumeDailyTimerAfterPause = false;
        }

        private void OnApplicationQuit()
        {
            if (_gameRoot != null && _gameRoot.activeInHierarchy)
                PersistActiveLevelSession();
            PauseDailyTimer(true);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                HandleSystemBack();

            if (!_isDailyLevel || _hud == null) return;
            float elapsed = CurrentDailyElapsed;
            int displayed = Mathf.FloorToInt(elapsed * 1000f);
            if (displayed == _displayedDailyMillisecond) return;
            _displayedDailyMillisecond = displayed;
            _hud.SetDailyElapsed(elapsed);
        }

        private void HandleSystemBack()
        {
            if (_activePage == UiPage.Game &&
                _hintPurchaseDialog != null &&
                _hintPurchaseDialog.IsVisible)
            {
                _hintPurchaseDialog.Hide();
                return;
            }

            bool hasHomeOverlay =
                _activePage == UiPage.Home &&
                _home != null &&
                _home.HasDismissibleOverlay;
            switch (UiFlowPolicy.BackAction(_activePage, hasHomeOverlay))
            {
                case SystemBackAction.DismissHomeOverlay:
                    _home.DismissActiveOverlay();
                    break;
                case SystemBackAction.ReturnHome:
                    ShowHome();
                    break;
            }
        }

        private void HandleHintPressed()
        {
            if (_hintService == null || _game == null ||
                (_hintPurchaseDialog != null && _hintPurchaseDialog.IsVisible))
                return;
            if (!_game.CanShowHint()) return;

            if (_hintService.FreeHintsRemaining > 0)
            {
                if (_game.TryShowHint() &&
                    _hintService.TryConsumeFreeHint())
                    RefreshHintHud();
                return;
            }

            int balance = SaveService.Coins;
            int missing = CoinEconomy.HintCost - balance;
            if (missing > 0)
            {
                _hud?.ShowHintMessage(
                    $"You need {missing} more coin{(missing == 1 ? string.Empty : "s")}.");
                return;
            }

            _hintPurchaseDialog?.Show(
                balance,
                CoinEconomy.HintCost,
                ConfirmPaidHint);
        }

        private void ConfirmPaidHint()
        {
            int missing = CoinEconomy.HintCost - SaveService.Coins;
            if (missing > 0)
            {
                _hud?.ShowHintMessage(
                    $"You need {missing} more coin{(missing == 1 ? string.Empty : "s")}.");
                RefreshHintHud();
                return;
            }

            // Gameplay validates and displays the route first, so unavailable hints
            // can never mutate the player's balance.
            if (!_game.TryShowHint()) return;
            if (!_hintService.TryPurchaseHint())
            {
                _hud?.ShowHintMessage("Your coin balance changed. Try again.");
                return;
            }
            RefreshHintHud();
        }

        private void RefreshHintHud()
        {
            if (_hud == null || _hintService == null) return;
            _hud.UpdateHints(_hintService.FreeHintsRemaining, false);
        }

        private void OnMoveStarted()
        {
            if (_isDailyLevel) StartDailyTimer();
        }

        private void OnLevelRestarted()
        {
            ClearCurrentLevelSession();
            if (!_isDailyLevel || _activeDailyPuzzle == null) return;
            PauseDailyTimer(false);
            _dailyPuzzleService?.EndAttempt(_activeDailyPuzzle.Date);
            if (_dailyPuzzleService == null ||
                !_dailyPuzzleService.TryBeginAttempt(
                    _activeDailyPuzzle.Date,
                    out _activeDailyPuzzle))
            {
                if (_gameRoot != null) _gameRoot.SetActive(false);
                ShowHome();
                return;
            }
            _dailyElapsedSeconds = 0f;
            _displayedDailyMillisecond = -1;
            _game?.SetPaintIndexOverride(
                Theme.PaintIndexForAttempt(
                    _activeDailyPuzzle.LevelIndex,
                    _activeDailyPuzzle.AttemptsUsed));
            StartCoroutine(RestoreDailyHudAfterRestart());
        }

        private IEnumerator RestoreDailyHudAfterRestart()
        {
            yield return null;
            if (_isDailyLevel)
                _hud?.SetDailyPuzzle(
                    0f,
                    _activeDailyPuzzle?.AttemptsUsed ?? 1);
        }

        private float CurrentDailyElapsed =>
            _dailyElapsedSeconds +
            (_dailyTimerRunning
                ? Mathf.Max(
                    0f,
                    Time.realtimeSinceStartup - _dailyTimerStartedAt)
                : 0f);

        private void StartDailyTimer()
        {
            if (_dailyTimerRunning ||
                !_isDailyLevel ||
                _activeDailyPuzzle == null ||
                _applicationPaused)
                return;
            _dailyTimerStartedAt = Time.realtimeSinceStartup;
            _dailyTimerRunning = true;
        }

        private void PauseDailyTimer(bool persist)
        {
            if (_dailyTimerRunning)
            {
                _dailyElapsedSeconds = CurrentDailyElapsed;
                _dailyTimerRunning = false;
            }
            if (persist) SaveDailyTimer();
        }

        private void SaveDailyTimer()
        {
            if (!_isDailyLevel ||
                _dailyPuzzleService == null ||
                _activeDailyPuzzle == null)
                return;
            _dailyPuzzleService.SetRunElapsedSeconds(
                _activeDailyPuzzle.DayId,
                CurrentDailyElapsed);
        }

        private void ResetDailyTimerRuntime()
        {
            _dailyElapsedSeconds = 0f;
            _dailyTimerStartedAt = 0f;
            _dailyTimerRunning = false;
            _resumeDailyTimerAfterPause = false;
            _displayedDailyMillisecond = -1;
        }

        private void PrefetchLevel(Difficulty difficulty, int index)
        {
            _prefetch?.Prefetch(difficulty,
                Mathf.Clamp(index, 1, DifficultyConfig.PracticalMaxLevel));
        }

        private void PrefetchHomeWindow(Difficulty difficulty, int index)
        {
            int current = Mathf.Clamp(index, 1, DifficultyConfig.PracticalMaxLevel);
            PrefetchLevel(difficulty, current);
            StartAheadPrefetch(difficulty, current, deferOneFrame: false);
        }

        private void StartAheadPrefetch(
            Difficulty difficulty,
            int currentIndex,
            bool deferOneFrame)
        {
            if (_aheadPrefetch == null) return;
            if (_aheadRoutine != null)
            {
                StopCoroutine(_aheadRoutine);
                _aheadRoutine = null;
            }

            int current = Mathf.Clamp(
                currentIndex, 1, DifficultyConfig.PracticalMaxLevel);
            _memoryWindow?.RetainMemoryWindow(
                difficulty,
                current,
                levelsBehind: 1,
                levelsAhead: SequentialLevelPrefetchBuffer.DefaultDepth);
            SequentialLevelPrefetchBuffer.Request request =
                _aheadPrefetch.Begin(difficulty, current);
            _aheadRoutine = StartCoroutine(
                RunAheadPrefetch(request, deferOneFrame));
        }

        private IEnumerator RunAheadPrefetch(
            SequentialLevelPrefetchBuffer.Request request,
            bool deferOneFrame)
        {
            if (deferOneFrame)
                yield return null;
            while (!_aheadPrefetch.Pump(request))
                yield return null;
            _aheadRoutine = null;
        }

        private void CancelAheadPrefetch()
        {
            if (_aheadRoutine != null)
            {
                StopCoroutine(_aheadRoutine);
                _aheadRoutine = null;
            }
            _aheadPrefetch?.Cancel();
        }

        private IEnumerator WarmMotionVfx()
        {
            yield return null;
            _board.EnsureMotionVfx();
        }

        private void ApplyRuntimeQuality(DeviceQualityTier tier)
        {
            _qualityTier = tier;
            _board?.ConfigureQuality(tier);
        }
    }
}
