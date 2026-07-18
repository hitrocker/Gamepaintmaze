using System;
using UnityEngine;

namespace PaintMaze.Services
{
    public enum DeviceQualityTier
    {
        Low,
        Standard,
        High
    }

    /// <summary>
    /// Resolves a conservative hardware tier and owns the frame settings for that tier.
    /// Unknown hardware stays on Standard instead of assuming either extreme.
    /// </summary>
    public static class DeviceQualityProfile
    {
        public static DeviceQualityTier Current { get; private set; } = DeviceQualityTier.Standard;

        public static DeviceQualityTier ResolveCurrent()
        {
            return Resolve(SystemInfo.systemMemorySize, SystemInfo.processorCount);
        }

        public static DeviceQualityTier Resolve(int memoryMb, int processorCount)
        {
            // Android reports installed memory with some variation. The upper bound keeps
            // common 4 GB devices such as the Galaxy A05 in the Low tier.
            if (memoryMb > 0 && memoryMb <= 4608)
                return DeviceQualityTier.Low;

            if (memoryMb >= 6144 && processorCount >= 8)
                return DeviceQualityTier.High;

            return DeviceQualityTier.Standard;
        }

        public static void Apply(DeviceQualityTier tier)
        {
            Current = tier;
            QualitySettings.vSyncCount = 0;

            switch (tier)
            {
                case DeviceQualityTier.Low:
                    Application.targetFrameRate = 60;
                    QualitySettings.antiAliasing = 0;
                    break;
                case DeviceQualityTier.High:
                    Application.targetFrameRate = 120;
                    QualitySettings.antiAliasing = 4;
                    break;
                default:
                    Application.targetFrameRate = 60;
                    QualitySettings.antiAliasing = 2;
                    break;
            }
        }

        public static bool TryDowngrade()
        {
            if (Current == DeviceQualityTier.Low) return false;
            Apply(Current == DeviceQualityTier.High
                ? DeviceQualityTier.Standard
                : DeviceQualityTier.Low);
            return true;
        }
    }

    /// <summary>Session-only, one-way fallback when the static hardware estimate was optimistic.</summary>
    public sealed class DeviceQualityMonitor : MonoBehaviour
    {
        public event Action<DeviceQualityTier> TierChanged;

        private const float SampleSeconds = 2f;
        private float _elapsed;
        private float _frameTime;
        private int _frames;
        private float _grace = 3f;

        private void Update()
        {
            if (_grace > 0f)
            {
                _grace -= Time.unscaledDeltaTime;
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            _frameTime += Time.unscaledDeltaTime;
            _frames++;
            if (_elapsed < SampleSeconds) return;

            float fps = _frameTime > 0.001f ? _frames / _frameTime : 60f;
            float threshold = DeviceQualityProfile.Current == DeviceQualityTier.High ? 48f : 42f;
            if (fps < threshold && DeviceQualityProfile.TryDowngrade())
            {
                TierChanged?.Invoke(DeviceQualityProfile.Current);
                Debug.Log($"[Perf] adaptive tier={DeviceQualityProfile.Current} fps={fps:F1}");
                _grace = 5f;
            }

            _elapsed = 0f;
            _frameTime = 0f;
            _frames = 0;
        }
    }
}
