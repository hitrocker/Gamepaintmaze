using PaintMaze.Services;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PaintMaze.Menu
{
    /// <summary>
    /// Renders the selected catalog skin on a slowly rotating real sphere.
    /// Its isolated scene is displayed inside the shop through a RenderTexture.
    /// </summary>
    public sealed class BallSkinLivePreview : MonoBehaviour
    {
        public const int PreviewLayer = 31;

        private const int TextureSize = 512;
        private static readonly Vector3 SceneOrigin =
            new(10000f, 10000f, 10000f);
        private static readonly Quaternion InitialSpin =
            Quaternion.Euler(-14f, 26f, 8f);

        private RawImage _output;
        private GameObject _sceneRoot;
        private Camera _camera;
        private RenderTexture _target;
        private Transform _spin;
        private MeshRenderer _ballRenderer;
        private GameObject _rings;
        private Mesh _ringMesh;
        private bool _disposed;

        public string CurrentSkinId { get; private set; }
        public bool IsRendering => _camera != null && _camera.enabled;
        public bool RingsVisible => _rings != null && _rings.activeSelf;
        public Transform SpinTransform => _spin;
        public Transform RingTransform =>
            _rings != null ? _rings.transform : null;
        public Camera PreviewCamera => _camera;
        public RenderTexture TargetTexture => _target;

        public void Build(RawImage output)
        {
            _output = output;
            _output.raycastTarget = false;

            _target = new RenderTexture(
                TextureSize,
                TextureSize,
                24,
                RenderTextureFormat.ARGB32)
            {
                name = "BallSkinLivePreview",
                antiAliasing = 4,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.DontSave
            };
            _target.Create();
            _output.texture = _target;

            _sceneRoot = new GameObject("BallSkinPreviewScene")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = PreviewLayer
            };
            _sceneRoot.transform.position = SceneOrigin;

            BuildCamera();
            BuildLight();
            BuildBall();
            SetSkin(SaveService.DefaultSkinId);
            _camera.enabled = false;
        }

        public void SetRendering(bool rendering)
        {
            if (_camera != null) _camera.enabled = rendering;
        }

        public void SetSkin(string skinId)
        {
            if (_ballRenderer == null) return;

            BallSkinDefinition skin = CosmeticCatalog.GetOrClassic(skinId);
            CurrentSkinId = skin.Id;
            _ballRenderer.sharedMaterial = SportsBallArt.MaterialFor(skin.Id);
            _spin.localRotation = InitialSpin;

            bool ringed = SportsBallArt.HasRings(skin.Id);
            _rings.SetActive(ringed);
            _camera.orthographicSize = ringed ? 1.05f : 0.62f;
        }

        private void BuildCamera()
        {
            var cameraObject = new GameObject("Camera")
            {
                layer = PreviewLayer
            };
            cameraObject.transform.SetParent(_sceneRoot.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -4f);
            cameraObject.transform.localRotation = Quaternion.identity;

            _camera = cameraObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 0.62f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 10f;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;
            _camera.cullingMask = 1 << PreviewLayer;
            _camera.targetTexture = _target;
        }

        private void BuildLight()
        {
            var lightObject = new GameObject("Light")
            {
                layer = PreviewLayer
            };
            lightObject.transform.SetParent(_sceneRoot.transform, false);
            lightObject.transform.localRotation =
                Quaternion.Euler(50f, -35f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 0.75f;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << PreviewLayer;
        }

        private void BuildBall()
        {
            var ballRoot = new GameObject("Ball")
            {
                layer = PreviewLayer
            };
            ballRoot.transform.SetParent(_sceneRoot.transform, false);

            _spin = new GameObject("Spin")
            {
                layer = PreviewLayer
            }.transform;
            _spin.SetParent(ballRoot.transform, false);
            _spin.localRotation = InitialSpin;

            GameObject sphere =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Surface";
            sphere.layer = PreviewLayer;
            sphere.transform.SetParent(_spin, false);
            DestroyOwned(sphere.GetComponent<Collider>());
            _ballRenderer = sphere.GetComponent<MeshRenderer>();
            _ballRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _ballRenderer.receiveShadows = false;

            _rings = new GameObject(
                "Rings",
                typeof(MeshFilter),
                typeof(MeshRenderer))
            {
                layer = PreviewLayer
            };
            _rings.transform.SetParent(ballRoot.transform, false);
            _rings.transform.localRotation = Quaternion.Euler(20f, 0f, 9f);
            _ringMesh = SportsBallArt.BuildRingMesh(0.62f, 0.95f, 96);
            _rings.GetComponent<MeshFilter>().sharedMesh = _ringMesh;
            MeshRenderer ringRenderer =
                _rings.GetComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = SportsBallArt.RingMaterial();
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
        }

        private void Update()
        {
            if (_spin == null || !IsRendering) return;
            _spin.Rotate(
                new Vector3(8f, 24f, 0f) * Time.unscaledDeltaTime,
                Space.World);
        }

        private void OnEnable()
        {
            if (_camera != null) SetRendering(true);
        }

        private void OnDisable()
        {
            SetRendering(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_output != null && _output.texture == _target)
                _output.texture = null;
            if (_camera != null) _camera.targetTexture = null;
            if (_camera != null) DestroyOwned(_camera.gameObject);
            if (_target != null)
            {
                _target.Release();
                DestroyOwned(_target);
            }
            if (_ringMesh != null) DestroyOwned(_ringMesh);
            if (_sceneRoot != null) DestroyOwned(_sceneRoot);

            _target = null;
            _ringMesh = null;
            _sceneRoot = null;
            _camera = null;
        }

        private void OnDestroy()
        {
            Dispose();
        }

        private static void DestroyOwned(Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
        }
    }
}
