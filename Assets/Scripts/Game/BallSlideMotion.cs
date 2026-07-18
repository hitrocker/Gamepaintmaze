using System;
using UnityEngine;

namespace PaintMaze.Game
{
    /// <summary>
    /// Allocation-free timing model for one ball slide and its cosmetic wall settle.
    /// Gameplay path legality remains owned by MovementSystem.
    /// </summary>
    public struct BallSlideMotion
    {
        public const float BaseSpeedWorldUnitsPerSecond = 32f;
        public const float EaseExponent = 0.85f;

        public const float SpringFrequency = 11f;
        public const float SpringDampingRatio = 0.22f;
        public const float MaximumOvershoot = 0.16f;
        public const float MaximumSquash = 0.19f;
        public const float MaximumSettleDuration = 0.50f;

        private const float SquashReference = 0.10f;
        private const float RestThreshold = 0.0015f;

        private readonly int _cellCount;
        private readonly float _worldDistance;
        private readonly float _effectiveSpeed;
        private readonly float _travelDuration;
        private readonly float _arrivalVelocity;
        private readonly float _springVelocity;
        private readonly float _settleDuration;
        private float _travelElapsed;
        private float _settleElapsed;
        private float _travelCells;
        private float _settleOffset;
        private float _squash;
        private BallSlidePhase _phase;

        public BallSlideMotion(
            int cellCount,
            float worldDistance,
            float longDistanceThreshold,
            float ballSpeedIncWhenLongDist)
        {
            if (cellCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellCount), "A slide must cross at least one cell.");
            if (worldDistance <= 0f)
                throw new ArgumentOutOfRangeException(nameof(worldDistance), "A slide must have positive world distance.");

            _cellCount = cellCount;
            _worldDistance = worldDistance;
            _effectiveSpeed = EffectiveSpeed(
                worldDistance, longDistanceThreshold, ballSpeedIncWhenLongDist);
            _travelDuration = worldDistance / _effectiveSpeed;
            _arrivalVelocity = EaseExponent * cellCount / _travelDuration;
            _springVelocity = CappedSpringVelocity(_arrivalVelocity);
            _settleDuration = SettleDurationForVelocity(_springVelocity);
            _travelElapsed = 0f;
            _settleElapsed = 0f;
            _travelCells = 0f;
            _settleOffset = 0f;
            _squash = 0f;
            _phase = BallSlidePhase.Travel;
        }

        public int CellCount => _cellCount;
        public float WorldDistance => _worldDistance;
        public float EffectiveWorldSpeed => _effectiveSpeed;
        public float TravelDuration => _travelDuration;
        public float TravelElapsed => _travelElapsed;
        public float SettleDuration => _settleDuration;
        public float SettleElapsed => _settleElapsed;
        public float TravelCells => _travelCells;
        public int EnteredCellCount => EnteredCellsForDistance(_travelCells, _cellCount);
        public float SettleOffsetCells => _settleOffset;
        public float Squash => _squash;
        public float ArrivalVelocity => _arrivalVelocity;
        public BallSlidePhase Phase => _phase;
        public bool InputLocked => _phase != BallSlidePhase.Complete;

        /// <summary>
        /// Ends the cosmetic wall settle immediately so a valid buffered move can chain.
        /// Travel cannot be skipped because gameplay state is not committed until arrival.
        /// </summary>
        public bool TrySkipSettle()
        {
            if (_phase != BallSlidePhase.Settle) return false;

            _settleElapsed = _settleDuration;
            _settleOffset = 0f;
            _squash = 0f;
            _phase = BallSlidePhase.Complete;
            return true;
        }

        public static float LongDistanceBonus(
            float worldDistance,
            float longDistanceThreshold,
            float ballSpeedIncWhenLongDist)
        {
            float threshold = Mathf.Max(0.0001f, longDistanceThreshold);
            float excessDistance = Mathf.Max(0f, worldDistance - threshold);
            if (excessDistance <= 0f) return 0f;

            // SmoothStep makes both the bonus and its slope start at zero at the
            // threshold. Beyond twice the threshold, the bonus grows linearly.
            float ramp = Mathf.SmoothStep(0f, 1f, excessDistance / threshold);
            return excessDistance * Mathf.Max(0f, ballSpeedIncWhenLongDist) * ramp;
        }

        public static float EffectiveSpeed(
            float worldDistance,
            float longDistanceThreshold,
            float ballSpeedIncWhenLongDist)
        {
            return BaseSpeedWorldUnitsPerSecond +
                   LongDistanceBonus(
                       worldDistance, longDistanceThreshold, ballSpeedIncWhenLongDist);
        }

        public static float DurationForDistance(
            float worldDistance,
            float longDistanceThreshold,
            float ballSpeedIncWhenLongDist)
        {
            if (worldDistance <= 0f) return 0f;
            return worldDistance /
                   EffectiveSpeed(
                       worldDistance, longDistanceThreshold, ballSpeedIncWhenLongDist);
        }

        public static float Progress(float elapsed, float duration)
        {
            if (duration <= 0f) return 1f;
            return Mathf.Pow(Mathf.Clamp01(elapsed / duration), EaseExponent);
        }

        /// <summary>
        /// Number of neighboring cells whose boundary has been crossed. Cell zero begins
        /// half a cell from the starting center; the result is cumulative for frame skips.
        /// </summary>
        public static int EnteredCellsForDistance(float travelCells, int cellCount)
        {
            if (cellCount <= 0 || travelCells < 0.5f) return 0;
            return Mathf.Clamp(Mathf.FloorToInt(travelCells + 0.5f), 0, cellCount);
        }

        /// <summary>Maps grid-space travel to a path segment and interpolation amount.</summary>
        public static void PathSample(float travelCells, int cellCount, out int segmentIndex, out float segmentT)
        {
            if (cellCount <= 0)
            {
                segmentIndex = 0;
                segmentT = 0f;
                return;
            }

            float clamped = Mathf.Clamp(travelCells, 0f, cellCount);
            segmentIndex = Mathf.Min(Mathf.FloorToInt(clamped), cellCount - 1);
            segmentT = Mathf.Clamp01(clamped - segmentIndex);
        }

        /// <summary>
        /// Advances using real frame time. The returned flags identify phase boundaries;
        /// all other values are available through properties without allocating a sample.
        /// </summary>
        public BallSlideStep Advance(float deltaTime)
        {
            var step = default(BallSlideStep);
            if (_phase == BallSlidePhase.Complete || deltaTime <= 0f) return step;

            float remaining = deltaTime;
            if (_phase == BallSlidePhase.Travel)
            {
                float travelRemaining = _travelDuration - _travelElapsed;
                float travelStep = Mathf.Min(remaining, travelRemaining);
                _travelElapsed += travelStep;
                remaining -= travelStep;
                _travelCells = Progress(_travelElapsed, _travelDuration) * _cellCount;

                if (_travelElapsed >= _travelDuration)
                {
                    _travelElapsed = _travelDuration;
                    _travelCells = _cellCount;
                    _phase = BallSlidePhase.Settle;
                    step.Arrived = true;
                }
            }

            if (_phase == BallSlidePhase.Settle)
            {
                _settleElapsed = Mathf.Min(_settleElapsed + remaining, _settleDuration);
                SampleSpring(_settleElapsed, out _settleOffset, out _);
                _squash = Mathf.Clamp01(_settleOffset / SquashReference) * MaximumSquash;

                if (_settleElapsed >= _settleDuration)
                {
                    _settleOffset = 0f;
                    _squash = 0f;
                    _phase = BallSlidePhase.Complete;
                    step.Settled = true;
                }
            }

            return step;
        }

        private static float AngularFrequency => 2f * Mathf.PI * SpringFrequency;

        private static float CappedSpringVelocity(float arrivalVelocity)
        {
            float omega = AngularFrequency;
            float alpha = SpringDampingRatio * omega;
            float dampedOmega = omega * Mathf.Sqrt(1f - SpringDampingRatio * SpringDampingRatio);
            float peakTime = Mathf.Atan(dampedOmega / alpha) / dampedOmega;
            float peakPerVelocity =
                Mathf.Exp(-alpha * peakTime) * Mathf.Sin(dampedOmega * peakTime) / dampedOmega;

            if (peakPerVelocity <= 0f) return arrivalVelocity;
            return Mathf.Min(arrivalVelocity, MaximumOvershoot / peakPerVelocity);
        }

        private static float SettleDurationForVelocity(float springVelocity)
        {
            float omega = AngularFrequency;
            float alpha = SpringDampingRatio * omega;
            float dampedOmega = omega * Mathf.Sqrt(1f - SpringDampingRatio * SpringDampingRatio);
            float positionEnvelope = springVelocity / dampedOmega;
            float velocityEnvelope =
                springVelocity * Mathf.Sqrt(1f + alpha * alpha / (dampedOmega * dampedOmega));

            float positionTime = positionEnvelope <= RestThreshold
                ? 0f
                : Mathf.Log(positionEnvelope / RestThreshold) / alpha;
            float velocityThreshold = RestThreshold * omega;
            float velocityTime = velocityEnvelope <= velocityThreshold
                ? 0f
                : Mathf.Log(velocityEnvelope / velocityThreshold) / alpha;
            return Mathf.Min(Mathf.Max(positionTime, velocityTime), MaximumSettleDuration);
        }

        private void SampleSpring(float time, out float offset, out float velocity)
        {
            float omega = AngularFrequency;
            float alpha = SpringDampingRatio * omega;
            float dampedOmega = omega * Mathf.Sqrt(1f - SpringDampingRatio * SpringDampingRatio);
            float decay = Mathf.Exp(-alpha * time);
            float sin = Mathf.Sin(dampedOmega * time);
            float cos = Mathf.Cos(dampedOmega * time);

            offset = decay * (_springVelocity / dampedOmega) * sin;
            velocity = _springVelocity * decay * (cos - alpha / dampedOmega * sin);
        }
    }

    public enum BallSlidePhase
    {
        Travel,
        Settle,
        Complete
    }

    public struct BallSlideStep
    {
        public bool Arrived;
        public bool Settled;
    }
}
