using System;
using UnityEngine;

namespace StomachAbyss.MainMenu
{
    /// <summary>
    /// Directs all Main Menu audio:
    /// - Synchronizes the visceral double heartbeat ("TUM-tum") with <see cref="ColossalHeartController"/>.
    /// - Plays the seamless 12-second dark gothic cavern & pipe-organ drone loop.
    /// - Provides UI hover, UI click, and "Jogar" dramatic dark-fantasy impact sound effects.
    /// - Manages master volume persistence via <see cref="PlayerPrefs"/> and <see cref="AudioListener.volume"/>.
    /// - Generates procedural runtime fallback <see cref="AudioClip"/> assets if any clip reference is unassigned.
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuAudioController : MonoBehaviour
    {
        public const string MasterVolumePrefKey = "StomachAbyss_MasterVolume";
        private const string LegacyVolumePrefKey = "MasterVolume";

        [Header("Scene References")]
        public ColossalHeartController heartController;

        [Header("Audio Clips")]
        public AudioClip heartbeatClip;
        public AudioClip cavernDroneClip;
        public AudioClip uiHoverClip;
        public AudioClip uiClickPlayClip;

        [Header("Audio Sources (Auto-created if null)")]
        public AudioSource droneSource;
        public AudioSource heartbeatSource;
        public AudioSource uiSfxSource;

        [Header("Mix Levels")]
        [Range(0f, 1f)] public float droneVolume = 0.50f;
        [Range(0f, 1f)] public float heartbeatVolume = 0.90f;
        [Range(0f, 1f)] public float uiHoverVolume = 0.72f;
        [Range(0f, 1f)] public float uiClickVolume = 0.95f;
        [Range(0.1f, 3f)] public float droneFadeInDuration = 1.6f;

        private bool _isSubscribedToHeart;
        private float _lastHeartbeatPlayTime = -10f;
        private float _lastHoverPlayTime = -10f;
        private float _droneFadeElapsed;
        private bool _isPlayTransitionActive;
        private float _fallbackHeartbeatTimer;
        private float _masterVolume = 0.88f;

        public float CurrentMasterVolume => _masterVolume;

        private void Awake()
        {
            LoadMasterVolumeFromPrefs();
            EnsureAudioSources();
            EnsureAudioClipsWithProceduralFallbacks();
        }

        private void OnEnable()
        {
            TryBindHeartController();
        }

        private void Start()
        {
            TryBindHeartController();
            StartCavernDrone();
        }

        private void OnDisable()
        {
            UnbindHeartController();
        }

        private void Update()
        {
            // Smooth drone fade-in on menu entry (or slight swell on play transition)
            if (droneSource != null && droneSource.isPlaying)
            {
                float musicMult = StomachAbyss.UI.GameSettingsManager.GetCategoryVolume(StomachAbyss.UI.SoundCategory.Music);
                if (_droneFadeElapsed < droneFadeInDuration)
                {
                    _droneFadeElapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(_droneFadeElapsed / Mathf.Max(0.05f, droneFadeInDuration));
                    float smooth = t * t * (3f - 2f * t);
                    droneSource.volume = droneVolume * smooth * musicMult;
                }
                else if (_isPlayTransitionActive)
                {
                    droneSource.volume = Mathf.MoveTowards(droneSource.volume, droneVolume * 0.35f * musicMult, Time.unscaledDeltaTime * 0.45f);
                }
                else
                {
                    droneSource.volume = droneVolume * musicMult;
                }
            }

            // Re-attempt binding if heartController was instantiated after Awake/OnEnable
            if (!_isSubscribedToHeart)
            {
                TryBindHeartController();
            }

            // Fallback autonomous cardiac audio trigger only if no ColossalHeartController is present in scene
            if (heartController == null)
            {
                float bpm = _isPlayTransitionActive ? 115f : 54f;
                float interval = 60f / Mathf.Max(20f, bpm);
                _fallbackHeartbeatTimer += Time.unscaledDeltaTime;
                if (_fallbackHeartbeatTimer >= interval)
                {
                    _fallbackHeartbeatTimer -= interval;
                    HandleHeartbeatLub(_isPlayTransitionActive ? 1.25f : 1.0f);
                }
            }
        }

        /// <summary>
        /// Subscribes to <see cref="ColossalHeartController.OnHeartbeatLub"/> to synchronize the heartbeat audio.
        /// </summary>
        public void TryBindHeartController()
        {
            if (heartController == null)
            {
                heartController = FindFirstObjectByType<ColossalHeartController>();
            }

            if (heartController != null && !_isSubscribedToHeart)
            {
                heartController.OnHeartbeatLub -= HandleHeartbeatLub;
                heartController.OnHeartbeatLub += HandleHeartbeatLub;
                _isSubscribedToHeart = true;
            }
        }

        private void UnbindHeartController()
        {
            if (heartController != null && _isSubscribedToHeart)
            {
                heartController.OnHeartbeatLub -= HandleHeartbeatLub;
            }
            _isSubscribedToHeart = false;
        }

        private void HandleHeartbeatLub(float intensity)
        {
            if (heartbeatSource == null || heartbeatClip == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastHeartbeatPlayTime < 0.22f)
            {
                return;
            }
            _lastHeartbeatPlayTime = now;

            float clampedIntensity = Mathf.Clamp(intensity <= 0.001f ? 1f : intensity, 0.65f, 1.45f);
            float boostPitch = _isPlayTransitionActive ? 1.06f : 1.0f;
            heartbeatSource.pitch = Mathf.Clamp(boostPitch * UnityEngine.Random.Range(0.97f, 1.03f), 0.85f, 1.20f);
            float ambMult = StomachAbyss.UI.GameSettingsManager.GetCategoryVolume(StomachAbyss.UI.SoundCategory.Ambient);
            float vol = Mathf.Clamp01(heartbeatVolume * ambMult * Mathf.Lerp(0.85f, 1.15f, (clampedIntensity - 0.65f) / 0.8f));
            heartbeatSource.PlayOneShot(heartbeatClip, vol);
        }

        /// <summary>
        /// Starts or restarts the looping dark cavern & pipe-organ drone.
        /// </summary>
        public void StartCavernDrone()
        {
            EnsureAudioSources();
            EnsureAudioClipsWithProceduralFallbacks();

            if (droneSource == null || cavernDroneClip == null)
            {
                return;
            }

            droneSource.clip = cavernDroneClip;
            droneSource.loop = true;
            droneSource.pitch = 1.0f;
            _droneFadeElapsed = 0f;
            droneSource.volume = 0f;

            if (!droneSource.isPlaying)
            {
                droneSource.Play();
            }
        }

        /// <summary>
        /// Plays the subtle bone/flesh whisper tick when hovering a menu button.
        /// </summary>
        public void PlayHoverSfx()
        {
            EnsureAudioSources();
            EnsureAudioClipsWithProceduralFallbacks();

            if (uiSfxSource == null || uiHoverClip == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastHoverPlayTime < 0.045f)
            {
                return;
            }
            _lastHoverPlayTime = now;

            uiSfxSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
            uiSfxSource.PlayOneShot(uiHoverClip, uiHoverVolume);
        }

        /// <summary>
        /// Plays a dark tactile UI confirmation sound for standard button clicks (e.g., Opções / Voltar).
        /// </summary>
        public void PlayClickSfx()
        {
            EnsureAudioSources();
            EnsureAudioClipsWithProceduralFallbacks();

            if (uiSfxSource == null)
            {
                return;
            }

            if (uiHoverClip != null)
            {
                uiSfxSource.pitch = 0.72f;
                uiSfxSource.PlayOneShot(uiHoverClip, Mathf.Clamp01(uiHoverVolume * 1.15f));
            }
            if (heartbeatClip != null && heartbeatSource != null)
            {
                heartbeatSource.pitch = 1.12f;
                heartbeatSource.PlayOneShot(heartbeatClip, heartbeatVolume * 0.45f);
            }
        }

        /// <summary>
        /// Plays the heavy Dark Souls-style liturgical chord impact & heart surge when clicking "Jogar".
        /// </summary>
        public void PlayStartGameImpactSfx()
        {
            EnsureAudioSources();
            EnsureAudioClipsWithProceduralFallbacks();

            _isPlayTransitionActive = true;

            if (uiSfxSource != null && uiClickPlayClip != null)
            {
                uiSfxSource.pitch = 1.0f;
                uiSfxSource.PlayOneShot(uiClickPlayClip, uiClickVolume);
            }

            if (heartbeatSource != null && heartbeatClip != null)
            {
                heartbeatSource.pitch = 0.94f;
                heartbeatSource.PlayOneShot(heartbeatClip, Mathf.Clamp01(heartbeatVolume * 1.1f));
            }
        }

        /// <summary>
        /// Updates <see cref="AudioListener.volume"/> and persists the setting in <see cref="PlayerPrefs"/>.
        /// </summary>
        public void SetMasterVolume(float volume01)
        {
            _masterVolume = Mathf.Clamp01(volume01);
            AudioListener.volume = _masterVolume;
            PlayerPrefs.SetFloat(MasterVolumePrefKey, _masterVolume);
            PlayerPrefs.SetFloat(LegacyVolumePrefKey, _masterVolume);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Returns the current normalized master volume in [0, 1].
        /// </summary>
        public float GetMasterVolume()
        {
            return _masterVolume;
        }

        private void LoadMasterVolumeFromPrefs()
        {
            if (PlayerPrefs.HasKey(MasterVolumePrefKey))
            {
                _masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePrefKey, 0.88f));
            }
            else if (PlayerPrefs.HasKey(LegacyVolumePrefKey))
            {
                _masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(LegacyVolumePrefKey, 0.88f));
            }
            else
            {
                _masterVolume = 0.88f;
            }

            AudioListener.volume = _masterVolume;
        }

        private void EnsureAudioSources()
        {
            if (droneSource == null)
            {
                droneSource = CreateOrFindChildSource("AudioSource_CavernDrone", loop: true, priority: 32);
            }
            if (heartbeatSource == null)
            {
                heartbeatSource = CreateOrFindChildSource("AudioSource_Heartbeat", loop: false, priority: 16);
            }
            if (uiSfxSource == null)
            {
                uiSfxSource = CreateOrFindChildSource("AudioSource_UI", loop: false, priority: 24);
            }
        }

        private AudioSource CreateOrFindChildSource(string childName, bool loop, int priority)
        {
            Transform child = transform.Find(childName);
            GameObject go;
            if (child != null)
            {
                go = child.gameObject;
            }
            else
            {
                go = new GameObject(childName);
                go.transform.SetParent(transform, false);
            }

            AudioSource src = go.GetComponent<AudioSource>();
            if (src == null)
            {
                src = go.AddComponent<AudioSource>();
            }

            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f; // 2D atmospheric stereo mix
            src.priority = priority;
            src.dopplerLevel = 0f;
            return src;
        }

        private void EnsureAudioClipsWithProceduralFallbacks()
        {
#if UNITY_EDITOR
            if (heartbeatClip == null)
            {
                heartbeatClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MainMenu/Audio/sfx_menu_heartbeat_lubdub.wav");
            }
            if (cavernDroneClip == null)
            {
                cavernDroneClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MainMenu/Audio/amb_menu_cavern_drone.wav");
            }
            if (uiHoverClip == null)
            {
                uiHoverClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MainMenu/Audio/sfx_menu_ui_hover.wav");
            }
            if (uiClickPlayClip == null)
            {
                uiClickPlayClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MainMenu/Audio/sfx_menu_ui_click_play.wav");
            }
#endif
            if (heartbeatClip == null)
            {
                heartbeatClip = BuildFallbackHeartbeatClip();
            }
            if (cavernDroneClip == null)
            {
                cavernDroneClip = BuildFallbackCavernDroneClip();
            }
            if (uiHoverClip == null)
            {
                uiHoverClip = BuildFallbackUiHoverClip();
            }
            if (uiClickPlayClip == null)
            {
                uiClickPlayClip = BuildFallbackUiClickPlayClip();
            }
        }

        private static AudioClip BuildFallbackHeartbeatClip()
        {
            const int sr = 44100;
            const float dur = 0.90f;
            int count = Mathf.CeilToInt(sr * dur);
            float[] data = new float[count];

            float phase1 = 0f;
            float phase2 = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sr;
                float sample = 0f;

                // Lub at 0.008s
                if (t >= 0.008f)
                {
                    float dt = t - 0.008f;
                    float freq = 45f + 68f * Mathf.Exp(-dt / 0.05f);
                    phase1 += 2f * Mathf.PI * freq / sr;
                    float env = (1f - Mathf.Exp(-dt / 0.006f)) * Mathf.Exp(-dt / 0.13f);
                    sample += (Mathf.Sin(phase1) + 0.4f * Mathf.Sin(2f * phase1)) * env;
                }

                // Dub at 0.165s
                if (t >= 0.165f)
                {
                    float dt = t - 0.165f;
                    float freq = 48f + 76f * Mathf.Exp(-dt / 0.045f);
                    phase2 += 2f * Mathf.PI * freq / sr;
                    float env = (1f - Mathf.Exp(-dt / 0.005f)) * Mathf.Exp(-dt / 0.15f);
                    sample += (Mathf.Sin(phase2) + 0.45f * Mathf.Sin(2f * phase2)) * env * 0.9f;
                }

                data[i] = Mathf.Clamp((float)Math.Tanh(sample * 1.4f), -0.95f, 0.95f);
            }

            AudioClip clip = AudioClip.Create("Procedural_Heartbeat_LubDub", count, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildFallbackCavernDroneClip()
        {
            const int sr = 44100;
            const float dur = 6.0f;
            int count = Mathf.CeilToInt(sr * dur);
            float[] data = new float[count];

            float[] freqs = { 36.6667f, 55.0f, 73.3333f, 87.5f, 110.0f, 146.6667f };
            float[] amps = { 0.35f, 0.28f, 0.22f, 0.12f, 0.15f, 0.08f };

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sr;
                float lfo = 0.78f + 0.22f * Mathf.Sin(2f * Mathf.PI * (t / dur));
                float s = 0f;
                for (int k = 0; k < freqs.Length; k++)
                {
                    float ph = 2f * Mathf.PI * freqs[k] * t;
                    s += (Mathf.Sin(ph) + 0.25f * Mathf.Sin(2f * ph)) * amps[k];
                }
                data[i] = Mathf.Clamp(s * lfo * 0.7f, -0.9f, 0.9f);
            }

            AudioClip clip = AudioClip.Create("Procedural_Cavern_Drone", count, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildFallbackUiHoverClip()
        {
            const int sr = 44100;
            const float dur = 0.18f;
            int count = Mathf.CeilToInt(sr * dur);
            float[] data = new float[count];

            uint rng = 1337u;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sr;
                rng = rng * 1664525u + 1013904223u;
                float noise = ((rng & 0xFFFF) / 32768f) - 1f;

                float clickEnv = Mathf.Exp(-t / 0.008f);
                float whispEnv = (1f - Mathf.Exp(-t / 0.006f)) * Mathf.Exp(-t / 0.045f);
                float bone = (Mathf.Sin(2f * Mathf.PI * 1240f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 2380f * t) * 0.35f) * clickEnv;
                float whisp = noise * 0.18f * whispEnv;

                data[i] = Mathf.Clamp(bone + whisp, -0.85f, 0.85f);
            }

            AudioClip clip = AudioClip.Create("Procedural_UI_Hover", count, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildFallbackUiClickPlayClip()
        {
            const int sr = 44100;
            const float dur = 1.80f;
            int count = Mathf.CeilToInt(sr * dur);
            float[] data = new float[count];

            float[] chordFreqs = { 36.71f, 55.0f, 73.42f, 87.31f, 110.0f, 146.83f, 220.0f, 233.08f, 293.66f };
            float subPhase = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sr;
                float fInst = 38f + 88f * Mathf.Exp(-t / 0.065f);
                subPhase += 2f * Mathf.PI * fInst / sr;
                float subEnv = (1f - Mathf.Exp(-t / 0.005f)) * Mathf.Exp(-t / 0.35f);
                float sub = Mathf.Sin(subPhase) * subEnv * 0.65f;

                float chord = 0f;
                float chordEnv = (1f - Mathf.Exp(-t / 0.004f)) * Mathf.Exp(-t / 0.78f);
                for (int k = 0; k < chordFreqs.Length; k++)
                {
                    float ph = 2f * Mathf.PI * chordFreqs[k] * t;
                    chord += (Mathf.Sin(ph) + 0.28f * Mathf.Sin(2f * ph)) * (0.14f / (1f + k * 0.22f));
                }

                float fadeOut = Mathf.Clamp01((dur - t) / 0.12f);
                data[i] = Mathf.Clamp((float)Math.Tanh((sub + chord * chordEnv) * 1.3f) * fadeOut, -0.95f, 0.95f);
            }

            AudioClip clip = AudioClip.Create("Procedural_UI_ClickPlay", count, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
