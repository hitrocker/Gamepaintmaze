using System.Collections;
using UnityEngine;
using PaintMaze.Game;

namespace PaintMaze.Services
{
    /// <summary>
    /// Game SFX (wall-impact and completion clips loaded from Resources/Sounds, with a
    /// procedurally-synthesized chime fallback) and device haptics.
    /// Attach to the persistent app object; call <see cref="Init"/> once.
    ///
    /// Haptics mirror the reference game (CandyCoded.HapticFeedback): instead of
    /// Unity's blunt <c>Handheld.Vibrate()</c>, we drive the OS-tuned
    /// <c>View.performHapticFeedback(HapticFeedbackConstants.*)</c> primitives, so a
    /// roll feels like a crisp light tick and completion gets a heavier flourish.
    /// </summary>
    public sealed class AudioService : MonoBehaviour
    {
        // android.view.HapticFeedbackConstants values (same mapping CandyCoded uses).
        private const int FB_LONG_PRESS = 0;    // "heavy"
        private const int FB_VIRTUAL_KEY = 1;   // "medium"
        private const int FB_CONTEXT_CLICK = 6; // "light"
        private const int FLAG_IGNORE_GLOBAL_SETTING = 2;
        private const int ANDROID_R_ID_CONTENT = 0x01020002;
        private AudioSource _source;   // one-shots (wall hits, completion)
        private AudioClip[] _hits;
        private AudioClip _complete;

        public static AudioService Instance { get; private set; }

        public void Init()
        {
            Instance = this;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;

            _hits = new[]
            {
                Resources.Load<AudioClip>("Sounds/hit_1"),
                Resources.Load<AudioClip>("Sounds/hit_2"),
            };
            _complete = Resources.Load<AudioClip>("Sounds/Win") ?? Chime();
        }

        /// <summary>Randomized impact clip when the ball stops against a wall.</summary>
        public void PlayWall()
        {
            PlayWall(GameFeedbackProfile.Impact(3, DeviceQualityTier.Standard));
        }

        public void PlayWall(ImpactFeedback feedback)
        {
            Perform(feedback.Haptic);
            if (!SaveService.SoundEnabled || _source == null || _hits == null) return;
            var clip = _hits[UnityEngine.Random.Range(0, _hits.Length)];
            if (clip == null) return;
            _source.pitch = feedback.Pitch;
            _source.PlayOneShot(clip, feedback.Volume);
        }

        public void PlayPaint(PaintFeedback feedback)
        {
            Perform(feedback.Haptic);
        }

        public void PlayComplete()
        {
            if (!SaveService.SoundEnabled || _source == null || _complete == null) return;
            _source.pitch = 1f;
            _source.PlayOneShot(_complete, 0.7f);
        }

        /// <summary>Crisp medium tick for each cell crossed during a roll.</summary>
        public void HapticMove() => Perform(FB_CONTEXT_CLICK);

        /// <summary>Medium tap (e.g. UI / selection).</summary>
        public void HapticMedium() => Perform(FB_VIRTUAL_KEY);

        /// <summary>Heavy thunk.</summary>
        public void HapticHeavy() => Perform(FB_LONG_PRESS);

        private void Perform(FeedbackHaptic haptic)
        {
            switch (haptic)
            {
                case FeedbackHaptic.Light:
                    Perform(FB_CONTEXT_CLICK);
                    break;
                case FeedbackHaptic.Medium:
                    Perform(FB_VIRTUAL_KEY);
                    break;
                case FeedbackHaptic.Heavy:
                    Perform(FB_LONG_PRESS);
                    break;
            }
        }

        /// <summary>Celebratory multi-pulse on level complete (light, light, heavy).</summary>
        public void HapticComplete()
        {
            if (!SaveService.HapticsEnabled) return;
            StartCoroutine(CompleteSequence());
        }

        private IEnumerator CompleteSequence()
        {
            Perform(FB_CONTEXT_CLICK);
            yield return new WaitForSecondsRealtime(0.08f);
            Perform(FB_VIRTUAL_KEY);
            yield return new WaitForSecondsRealtime(0.10f);
            Perform(FB_LONG_PRESS);
        }

        private void Perform(int feedbackConstant)
        {
            if (!SaveService.HapticsEnabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var view = ContentView;
                view?.Call<bool>("performHapticFeedback", feedbackConstant, FLAG_IGNORE_GLOBAL_SETTING);
            }
            catch { /* haptics are best-effort */ }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _contentView;
        private AndroidJavaObject ContentView
        {
            get
            {
                if (_contentView != null) return _contentView;
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                using (var decor = window.Call<AndroidJavaObject>("getDecorView"))
                {
                    _contentView = decor.Call<AndroidJavaObject>("findViewById", ANDROID_R_ID_CONTENT);
                }
                return _contentView;
            }
        }
#endif

        private static AudioClip Chime()
        {
            int rate = 44100;
            float duration = 0.5f;
            int samples = (int)(rate * duration);
            var data = new float[samples];
            float[] notes = { 523.25f, 659.25f, 783.99f }; // C5 E5 G5
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / rate;
                int seg = Mathf.Min(notes.Length - 1, (int)(t / (duration / notes.Length)));
                float env = Mathf.Clamp01(1f - (float)i / samples);
                data[i] = Mathf.Sin(2f * Mathf.PI * notes[seg] * t) * env * 0.4f;
            }
            var clip = AudioClip.Create("chime", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
