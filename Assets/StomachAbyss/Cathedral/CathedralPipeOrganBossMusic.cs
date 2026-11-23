using System;
using System.Collections.Generic;
using BossTenia;
using MapaEstomago;
using UnityEngine;

namespace StomachAbyss.Cathedral
{
    /// <summary>
    /// Dark Souls 3 style cathedral pipe organ & choir boss music director for Malphas.
    /// Plays an earth-shaking reverberant organ blast on entry, seamlessly schedules the
    /// bar-locked looping soundtrack, escalates in Phase 2, pulses cathedral organ uplights
    /// and colossal organ pipes via FFT spectrum analysis, and resets cleanly on defeat or respawn.
    /// </summary>
    public class CathedralPipeOrganBossMusic : MonoBehaviour
    {
        [Serializable]
        private struct ReactiveLightCache
        {
            public Light light;
            public float baseIntensity;
            public Color baseColor;
        }

        [Header("Áudio do Órgão Tubular (DS3 Style)")]
        public AudioClip introBlastClip;
        public AudioClip bossLoopClip;
        [Tooltip("Opcional: som extra de sino/acorde grave na Fase 2. Se nulo, sintetiza automaticamente.")]
        public AudioClip phase2ChimeClip;

        [Header("Volumes & Timing")]
        [Range(0f, 1.5f)] public float introVolume = 0.95f;
        [Range(0f, 1.5f)] public float phase1LoopVolume = 0.82f;
        [Range(0f, 1.5f)] public float phase2LoopVolume = 0.96f;
        [Range(0.95f, 1.10f)] public float phase2Pitch = 1.03f;
        [Tooltip("Instante (s) na intro em que o loop agenda a entrada sob a cauda de reverb.")]
        public float loopStartDelaySeconds = 3.60f;
        public float loopCrossfadeSeconds = 0.85f;
        public float defeatFadeOutSeconds = 2.40f;
        public float resetFadeOutSeconds = 1.00f;

        [Header("Referências do Encontro")]
        public TeniaBossAI bossAI;
        public TeniaBossHealth bossHealth;
        public TeniaArenaBounds arenaBounds;
        public CathedralGateDramaticTrigger dramaticGate;
        public Transform playerTarget;

        [Header("Visuais Reativos do Órgão da Catedral")]
        public Transform organPipesTransform;
        public Renderer organPipesRenderer;
        public List<Light> organReactiveLights = new List<Light>();

        [Tooltip("Intensidade máxima do pulso nas luzes do órgão durante acordes fortes.")]
        public float lightPulseMultiplier = 1.15f;
        [Tooltip("Amplitude de vibração acústica (m) nos tubos colossais do órgão.")]
        public float pipeShudderAmplitude = 0.032f;

        private AudioSource _introSource;
        private AudioSource _bossLoopSource;
        private AudioClip _fallbackPhase2Chime;

        private readonly float[] _spectrumBuffer = new float[256];
        private readonly float[] _outputBuffer = new float[128];
        private readonly List<ReactiveLightCache> _lightCaches = new List<ReactiveLightCache>(8);

        private MaterialPropertyBlock _organMpb;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private Color _organBaseEmission = new Color(0.12f, 0.09f, 0.04f, 1f);
        private Vector3 _organBaseLocalPos;
        private bool _hasOrganBasePos;

        private bool _musicStarted;
        private bool _encounterConcluded;
        private bool _sawBossEngaged;
        private bool _isPhase2;
        private bool _isFadingOut;
        private float _fadeOutTimer;
        private float _fadeOutDuration = 1.5f;
        private float _introStartFadeVolume;
        private float _loopStartFadeVolume;

        private float _elapsedSinceStart;
        private double _scheduledLoopDspTime;
        private float _currentLoopVolume;
        private float _currentPitch = 1.0f;

        private float _smoothedBass;
        private float _smoothedMid;
        private float _smoothedHigh;
        private float _phase2PulseBoost;

        private void Awake()
        {
            EnsureAudioSources();
            ResolveSceneReferences();
            CacheOrganAndLights();
            _fallbackPhase2Chime = BuildFallbackPhase2Chime();
        }

        private void OnEnable()
        {
            ResolveSceneReferences();
            BindEvents(true);
        }

        private void OnDisable()
        {
            BindEvents(false);
            RestoreOrganVisualBaseline();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float unscaledDt = Time.unscaledDeltaTime;

            if (dt <= 0f)
            {
                // Mesmo pausado (Time.timeScale == 0), atualiza volume em tempo real ao mexer no slider de Musica
                if (_musicStarted && !_isFadingOut)
                {
                    float musicMult = StomachAbyss.UI.GameSettingsManager.GetCategoryVolume(StomachAbyss.UI.SoundCategory.Music);
                    float targetVol = (_isPhase2 ? phase2LoopVolume : phase1LoopVolume) * musicMult;
                    _currentLoopVolume = targetVol;
                    if (_bossLoopSource != null && _bossLoopSource.isPlaying) _bossLoopSource.volume = targetVol;
                    if (_introSource != null && _introSource.isPlaying) _introSource.volume = introVolume * musicMult;
                }
                return;
            }

            ResolveSceneReferences();
            TrackBossReset();

            if (!_musicStarted && !_encounterConcluded)
            {
                CheckAutoStartConditions();
            }

            if (_musicStarted)
            {
                UpdatePlaybackEnvelope(dt);
            }

            UpdateAudioReactiveVisuals(dt);
        }

        public void StartBossMusic()
        {
            if (_musicStarted)
            {
                return;
            }

            EnsureAudioSources();
            CacheOrganAndLights();

            _musicStarted = true;
            _encounterConcluded = false;
            _isFadingOut = false;
            _isPhase2 = bossHealth != null && bossHealth.IsPhase2;
            _elapsedSinceStart = 0f;
            _currentLoopVolume = 0f;
            _currentPitch = _isPhase2 ? phase2Pitch : 1.0f;
            _phase2PulseBoost = 0f;

            _introSource.Stop();
            _bossLoopSource.Stop();

            double dspNow = AudioSettings.dspTime;

            if (introBlastClip != null)
            {
                float musicMult = StomachAbyss.UI.GameSettingsManager.GetCategoryVolume(StomachAbyss.UI.SoundCategory.Music);
                _introSource.clip = introBlastClip;
                _introSource.volume = introVolume * musicMult;
                _introSource.pitch = 1.0f;
                _introSource.Play();

                if (bossLoopClip != null)
                {
                    float delay = Mathf.Max(0.25f, loopStartDelaySeconds);
                    _scheduledLoopDspTime = dspNow + delay;
                    _bossLoopSource.clip = bossLoopClip;
                    _bossLoopSource.loop = true;
                    _bossLoopSource.volume = 0f;
                    _bossLoopSource.pitch = _currentPitch;
                    _bossLoopSource.PlayScheduled(_scheduledLoopDspTime);
                }
            }
            else if (bossLoopClip != null)
            {
                _scheduledLoopDspTime = dspNow;
                _bossLoopSource.clip = bossLoopClip;
                _bossLoopSource.loop = true;
                _bossLoopSource.volume = 0f;
                _bossLoopSource.pitch = _currentPitch;
                _bossLoopSource.Play();
            }
        }

        public void FadeOutAndReset(float duration)
        {
            if (!_musicStarted && !_introSource.isPlaying && !_bossLoopSource.isPlaying)
            {
                ResetStateFlags();
                return;
            }

            _isFadingOut = true;
            _fadeOutDuration = Mathf.Max(0.15f, duration);
            _fadeOutTimer = 0f;
            _introStartFadeVolume = _introSource != null ? _introSource.volume : 0f;
            _loopStartFadeVolume = _bossLoopSource != null ? _bossLoopSource.volume : 0f;
        }

        private void UpdatePlaybackEnvelope(float dt)
        {
            _elapsedSinceStart += dt;

            if (_isFadingOut)
            {
                _fadeOutTimer += dt;
                float u = Mathf.Clamp01(_fadeOutTimer / _fadeOutDuration);
                float k = 1f - u * u;

                if (_introSource != null) _introSource.volume = _introStartFadeVolume * k;
                if (_bossLoopSource != null) _bossLoopSource.volume = _loopStartFadeVolume * k;

                if (u >= 1f)
                {
                    if (_introSource != null) _introSource.Stop();
                    if (_bossLoopSource != null) _bossLoopSource.Stop();
                    ResetStateFlags();
                }
                return;
            }

            float musicMult = StomachAbyss.UI.GameSettingsManager.GetCategoryVolume(StomachAbyss.UI.SoundCategory.Music);
            float targetVol = (_isPhase2 ? phase2LoopVolume : phase1LoopVolume) * musicMult;
            float targetPitch = _isPhase2 ? phase2Pitch : 1.0f;

            _currentPitch = Mathf.MoveTowards(_currentPitch, targetPitch, dt * 0.045f);
            if (_bossLoopSource != null)
            {
                _bossLoopSource.pitch = _currentPitch;
            }

            if (bossLoopClip == null) return;

            double dspNow = AudioSettings.dspTime;
            if (dspNow >= _scheduledLoopDspTime)
            {
                float fadeElapsed = (float)(dspNow - _scheduledLoopDspTime);
                float fadeRatio = loopCrossfadeSeconds > 0.01f
                    ? Mathf.Clamp01(fadeElapsed / loopCrossfadeSeconds)
                    : 1f;
                float desired = targetVol * (fadeRatio * (2f - fadeRatio));
                _currentLoopVolume = Mathf.MoveTowards(_currentLoopVolume, desired, dt * 1.4f);
                if (_bossLoopSource != null)
                {
                    _bossLoopSource.volume = _currentLoopVolume;
                }
            }
        }

        private void CheckAutoStartConditions()
        {
            if (bossHealth != null && bossHealth.IsDead) return;

            if (dramaticGate != null && (dramaticGate.IsClosed || dramaticGate.IsAnimatingClose))
            {
                StartBossMusic();
                return;
            }

            if (bossAI != null &&
                bossAI.CurrentState != TeniaBossAI.EncounterState.Dormant &&
                bossAI.CurrentState != TeniaBossAI.EncounterState.Defeated)
            {
                StartBossMusic();
                return;
            }

            if (arenaBounds != null && playerTarget != null && arenaBounds.IsPlayerInsideCathedral(playerTarget.position))
            {
                StartBossMusic();
            }
        }

        private void TrackBossReset()
        {
            if (bossAI == null) return;

            var st = bossAI.CurrentState;
            if (st != TeniaBossAI.EncounterState.Dormant && st != TeniaBossAI.EncounterState.Defeated)
            {
                _sawBossEngaged = true;
            }
            else if (_sawBossEngaged && st == TeniaBossAI.EncounterState.Dormant && (bossHealth == null || !bossHealth.IsDead))
            {
                _sawBossEngaged = false;
                _encounterConcluded = false;
                FadeOutAndReset(resetFadeOutSeconds);
            }
        }

        private void HandleGateSlamStarted()
        {
            if (!_encounterConcluded)
            {
                StartBossMusic();
            }
        }

        private void HandlePhase2Entered()
        {
            if (_encounterConcluded) return;
            _isPhase2 = true;
            _phase2PulseBoost = 1.0f;

            if (!_musicStarted)
            {
                StartBossMusic();
            }

            AudioClip chime = phase2ChimeClip != null ? phase2ChimeClip : _fallbackPhase2Chime;
            if (chime != null && _introSource != null)
            {
                _introSource.PlayOneShot(chime, 0.78f);
            }
        }

        private void HandleBossDefeated()
        {
            _encounterConcluded = true;
            _sawBossEngaged = false;
            FadeOutAndReset(defeatFadeOutSeconds);
        }

        private void ResetStateFlags()
        {
            _musicStarted = false;
            _isFadingOut = false;
            _isPhase2 = false;
            _currentLoopVolume = 0f;
            _currentPitch = 1.0f;
            _phase2PulseBoost = 0f;
            if (_bossLoopSource != null)
            {
                _bossLoopSource.pitch = 1.0f;
                _bossLoopSource.volume = 0f;
            }
            if (_introSource != null)
            {
                _introSource.volume = introVolume;
            }
        }

        private void UpdateAudioReactiveVisuals(float dt)
        {
            if (_phase2PulseBoost > 0f)
            {
                _phase2PulseBoost = Mathf.Max(0f, _phase2PulseBoost - dt * 0.65f);
            }

            float rawBass = 0f;
            float rawMid = 0f;
            float rawHigh = 0f;

            bool anyPlaying = _musicStarted &&
                ((_introSource != null && _introSource.isPlaying && _introSource.volume > 0.02f) ||
                 (_bossLoopSource != null && _bossLoopSource.isPlaying && _bossLoopSource.volume > 0.02f));

            if (anyPlaying)
            {
                AudioSource dominant = (_bossLoopSource != null && _bossLoopSource.isPlaying && _bossLoopSource.volume >= 0.28f)
                    ? _bossLoopSource
                    : _introSource;

                if (dominant != null && dominant.isPlaying)
                {
                    dominant.GetSpectrumData(_spectrumBuffer, 0, FFTWindow.BlackmanHarris);
                    dominant.GetOutputData(_outputBuffer, 0);

                    // 256 bins at 44100 Hz -> ~86 Hz/bin
                    for (int i = 0; i <= 2; i++) rawBass += _spectrumBuffer[i];
                    for (int i = 3; i <= 10; i++) rawMid += _spectrumBuffer[i];
                    for (int i = 11; i <= 28; i++) rawHigh += _spectrumBuffer[i];

                    float rmsSq = 0f;
                    for (int i = 0; i < _outputBuffer.Length; i++)
                    {
                        float s = _outputBuffer[i];
                        rmsSq += s * s;
                    }
                    float rms = Mathf.Sqrt(rmsSq / _outputBuffer.Length);

                    rawBass = Mathf.Clamp01(rawBass * 2.4f + rms * 0.75f);
                    rawMid = Mathf.Clamp01(rawMid * 2.8f + rms * 0.45f);
                    rawHigh = Mathf.Clamp01(rawHigh * 4.2f);
                }
            }

            float smoothSpeed = anyPlaying ? 14f : 4.5f;
            _smoothedBass = Mathf.Lerp(_smoothedBass, rawBass, 1f - Mathf.Exp(-smoothSpeed * dt));
            _smoothedMid = Mathf.Lerp(_smoothedMid, rawMid, 1f - Mathf.Exp(-smoothSpeed * dt));
            _smoothedHigh = Mathf.Lerp(_smoothedHigh, rawHigh, 1f - Mathf.Exp(-smoothSpeed * dt));

            float pulse = Mathf.Clamp01(_smoothedBass * 0.65f + _smoothedMid * 0.35f + _phase2PulseBoost * 0.45f);
            float idleBreath = 0.05f * Mathf.Sin(Time.time * 1.4f);

            // 1. Pulse Cathedral Pipe Organ & Facade Uplights
            for (int i = 0; i < _lightCaches.Count; i++)
            {
                ReactiveLightCache lc = _lightCaches[i];
                if (lc.light == null) continue;

                float phaseScale = _isPhase2 ? 1.24f : 1.0f;
                float mult = anyPlaying
                    ? Mathf.Lerp(0.78f, 1.0f + lightPulseMultiplier * phaseScale, pulse)
                    : (1.0f + idleBreath);

                lc.light.intensity = lc.baseIntensity * mult;

                if (anyPlaying)
                {
                    Color hotTint = _isPhase2
                        ? new Color(1.0f, 0.38f, 0.16f, 1f)
                        : new Color(1.0f, 0.76f, 0.36f, 1f);
                    lc.light.color = Color.Lerp(lc.baseColor, hotTint, pulse * 0.38f);
                }
                else
                {
                    lc.light.color = Color.Lerp(lc.light.color, lc.baseColor, dt * 4f);
                }
            }

            // 2. Micro-shudder & harmonic emission glow on Cathedral_Colossal_Organ_Pipes
            if (organPipesTransform != null && _hasOrganBasePos)
            {
                if (anyPlaying && _smoothedBass > 0.04f)
                {
                    float amp = pipeShudderAmplitude * _smoothedBass * (_isPhase2 ? 1.35f : 1.0f);
                    float t = Time.time;
                    Vector3 shudder = new Vector3(
                        Mathf.Sin(t * 43f) * amp,
                        Mathf.Cos(t * 57f) * amp * 0.8f,
                        Mathf.Sin(t * 31f) * amp * 0.45f);
                    organPipesTransform.localPosition = _organBaseLocalPos + shudder;
                }
                else
                {
                    organPipesTransform.localPosition = Vector3.Lerp(organPipesTransform.localPosition, _organBaseLocalPos, dt * 8f);
                }
            }

            if (organPipesRenderer != null)
            {
                if (_organMpb == null) _organMpb = new MaterialPropertyBlock();
                organPipesRenderer.GetPropertyBlock(_organMpb);

                Color targetGlow = _isPhase2
                    ? new Color(1.38f, 0.46f, 0.14f, 1f)
                    : new Color(1.12f, 0.68f, 0.18f, 1f);

                float glowWeight = anyPlaying ? Mathf.Clamp01(pulse * 0.95f + _smoothedHigh * 0.25f) : 0f;
                Color finalEmission = Color.Lerp(_organBaseEmission, targetGlow, glowWeight);
                _organMpb.SetColor(EmissionColorId, finalEmission);
                organPipesRenderer.SetPropertyBlock(_organMpb);
            }
        }

        private void RestoreOrganVisualBaseline()
        {
            for (int i = 0; i < _lightCaches.Count; i++)
            {
                if (_lightCaches[i].light != null)
                {
                    _lightCaches[i].light.intensity = _lightCaches[i].baseIntensity;
                    _lightCaches[i].light.color = _lightCaches[i].baseColor;
                }
            }

            if (organPipesTransform != null && _hasOrganBasePos)
            {
                organPipesTransform.localPosition = _organBaseLocalPos;
            }

            if (organPipesRenderer != null)
            {
                if (_organMpb == null) _organMpb = new MaterialPropertyBlock();
                organPipesRenderer.GetPropertyBlock(_organMpb);
                _organMpb.SetColor(EmissionColorId, _organBaseEmission);
                organPipesRenderer.SetPropertyBlock(_organMpb);
            }
        }

        private void EnsureAudioSources()
        {
            AudioSource[] existing = GetComponents<AudioSource>();
            if (existing != null && existing.Length >= 2)
            {
                _introSource = existing[0];
                _bossLoopSource = existing[1];
            }
            else
            {
                _introSource = existing != null && existing.Length >= 1 ? existing[0] : gameObject.AddComponent<AudioSource>();
                _bossLoopSource = gameObject.AddComponent<AudioSource>();
            }

            _introSource.playOnAwake = false;
            _introSource.loop = false;
            _introSource.spatialBlend = 0f;
            _introSource.priority = 16;

            _bossLoopSource.playOnAwake = false;
            _bossLoopSource.loop = true;
            _bossLoopSource.spatialBlend = 0f;
            _bossLoopSource.priority = 8;
        }

        private void CacheOrganAndLights()
        {
            if (organPipesTransform != null && !_hasOrganBasePos)
            {
                _organBaseLocalPos = organPipesTransform.localPosition;
                _hasOrganBasePos = true;
            }

            if (organPipesRenderer == null && organPipesTransform != null)
            {
                organPipesRenderer = organPipesTransform.GetComponent<Renderer>();
            }

            if (organPipesRenderer != null && organPipesRenderer.sharedMaterial != null &&
                organPipesRenderer.sharedMaterial.HasProperty(EmissionColorId))
            {
                _organBaseEmission = organPipesRenderer.sharedMaterial.GetColor(EmissionColorId);
            }

            if (_lightCaches.Count == 0 && organReactiveLights != null)
            {
                for (int i = 0; i < organReactiveLights.Count; i++)
                {
                    Light l = organReactiveLights[i];
                    if (l == null) continue;
                    _lightCaches.Add(new ReactiveLightCache
                    {
                        light = l,
                        baseIntensity = l.intensity,
                        baseColor = l.color
                    });
                }
            }
        }

        private void BindEvents(bool subscribe)
        {
            if (dramaticGate != null)
            {
                dramaticGate.OnGateSlamStarted -= HandleGateSlamStarted;
                if (subscribe) dramaticGate.OnGateSlamStarted += HandleGateSlamStarted;
            }

            if (bossHealth != null)
            {
                bossHealth.OnPhase2Entered -= HandlePhase2Entered;
                bossHealth.OnDefeated -= HandleBossDefeated;
                if (subscribe)
                {
                    bossHealth.OnPhase2Entered += HandlePhase2Entered;
                    bossHealth.OnDefeated += HandleBossDefeated;
                }
            }
        }

        private void ResolveSceneReferences()
        {
            if (bossAI == null) bossAI = FindAnyObjectByType<TeniaBossAI>();
            if (bossHealth == null) bossHealth = bossAI != null ? bossAI.health : FindAnyObjectByType<TeniaBossHealth>();
            if (arenaBounds == null) arenaBounds = bossAI != null ? bossAI.arenaBounds : FindAnyObjectByType<TeniaArenaBounds>();
            if (dramaticGate == null) dramaticGate = FindAnyObjectByType<CathedralGateDramaticTrigger>();

            if (playerTarget == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) p = GameObject.Find("Test_Player (1)");
                if (p == null) p = GameObject.Find("Test_Player");
                if (p != null) playerTarget = p.transform;
            }

            if (organPipesTransform == null)
            {
                GameObject organGo = GameObject.Find("Cathedral_Colossal_Organ_Pipes");
                if (organGo != null) organPipesTransform = organGo.transform;
            }

            if (organReactiveLights == null || organReactiveLights.Count == 0)
            {
                organReactiveLights = new List<Light>(5);
                TryAddLightByName("PipeOrgan_GoldenUplight_Center");
                TryAddLightByName("PipeOrgan_Bellows_Left");
                TryAddLightByName("PipeOrgan_Bellows_Right");
                TryAddLightByName("Facade_RoseWindow_EyeGlow");
                TryAddLightByName("Sanctuary_BossAltar_CrimsonLight");
            }
        }

        private void TryAddLightByName(string goName)
        {
            GameObject go = GameObject.Find(goName);
            if (go != null && go.TryGetComponent<Light>(out var lightComp) && !organReactiveLights.Contains(lightComp))
            {
                organReactiveLights.Add(lightComp);
            }
        }

        private static AudioClip BuildFallbackPhase2Chime()
        {
            const int sr = 22050;
            const float dur = 2.4f;
            int count = Mathf.RoundToInt(dur * sr);
            float[] samples = new float[count];

            // Deep D2 + A2 gothic bronze bell + pipe fifth sting for Phase 2 escalation
            float[] freqs = { 73.42f, 110.00f, 146.83f, 220.00f, 293.66f, 349.23f };
            float[] amps = { 0.72f, 0.56f, 0.65f, 0.44f, 0.30f, 0.22f };

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sr;
                float env = (1f - Mathf.Exp(-t * 140f)) * Mathf.Exp(-t * 1.45f);
                float sum = 0f;
                for (int f = 0; f < freqs.Length; f++)
                {
                    sum += Mathf.Sin(2f * Mathf.PI * freqs[f] * t) * amps[f];
                }
                samples[i] = Mathf.Clamp((float)Math.Tanh(sum * env * 0.9f) * 0.85f, -0.95f, 0.95f);
            }

            AudioClip clip = AudioClip.Create("SFX_Organ_Phase2_Chime", count, 1, sr, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
