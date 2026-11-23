using UnityEngine;

namespace BossTenia
{
    /// <summary>
    /// Sintetizador procedural de horror organico e catedral profana para Malphas, a Tenia Liturgica.
    /// Gera AudioClips em memoria sem depender de assets externos: drone sombrio, rugidos parasitarios,
    /// estalos de carapaca/osso, constricao sufocante, sibilo acido e impacto sismico.
    /// </summary>
    public class TeniaAudioController : MonoBehaviour
    {
        private const int SampleRate = 22050;

        [Range(0f, 1.5f)]
        public float masterVolume = 0.92f;

        private AudioSource _sfx2DSource;
        private AudioSource _sfx3DSource;
        private AudioSource _ambientLoopSource;

        private AudioClip _clipAmbientDrone;
        private AudioClip _clipRoarP1;
        private AudioClip _clipRoarP2;
        private AudioClip _clipBiteSnap;
        private AudioClip _clipTailWhip;
        private AudioClip _clipConstrictSqueeze;
        private AudioClip _clipAcidSpit;
        private AudioClip _clipLeapWindup;
        private AudioClip _clipLeapSlam;
        private AudioClip _clipClimbScrape;
        private AudioClip _clipStaggerCrash;
        private AudioClip _clipDefeatAgony;
        private AudioClip _clipLowDeathGroan;
        private AudioClip _clipAcidMeltDissolve;
        private AudioClip _clipHealOrb;

        private bool _built;

        private void Awake()
        {
            EnsureSourcesAndClips();
        }

        public void EnsureSourcesAndClips()
        {
            if (_sfx2DSource == null)
            {
                _sfx2DSource = gameObject.AddComponent<AudioSource>();
                _sfx2DSource.playOnAwake = false;
                _sfx2DSource.spatialBlend = 0.15f;
                _sfx2DSource.volume = masterVolume;
            }

            if (_sfx3DSource == null)
            {
                _sfx3DSource = gameObject.AddComponent<AudioSource>();
                _sfx3DSource.playOnAwake = false;
                _sfx3DSource.spatialBlend = 0.72f;
                _sfx3DSource.minDistance = 4f;
                _sfx3DSource.maxDistance = 42f;
                _sfx3DSource.rolloffMode = AudioRolloffMode.Linear;
                _sfx3DSource.volume = masterVolume;
            }

            if (_ambientLoopSource == null)
            {
                _ambientLoopSource = gameObject.AddComponent<AudioSource>();
                _ambientLoopSource.playOnAwake = false;
                _ambientLoopSource.loop = true;
                _ambientLoopSource.spatialBlend = 0f;
                _ambientLoopSource.volume = 0f;
            }

            if (_built) return;
            _built = true;

            _clipAmbientDrone = BuildAmbientHorrorDrone();
            _clipRoarP1 = BuildParasiteRoar(false);
            _clipRoarP2 = BuildParasiteRoar(true);
            _clipBiteSnap = BuildBiteSnap();
            _clipTailWhip = BuildTailWhip();
            _clipConstrictSqueeze = BuildConstrictSqueeze();
            _clipAcidSpit = BuildAcidSpit();
            _clipLeapWindup = BuildLeapWindup();
            _clipLeapSlam = BuildLeapSlam();
            _clipClimbScrape = BuildClimbScrape();
            _clipStaggerCrash = BuildStaggerCrash();
            _clipDefeatAgony = BuildDefeatAgony();
            _clipLowDeathGroan = BuildLowDeathGroan();
            _clipAcidMeltDissolve = BuildAcidMeltDissolve();
            _clipHealOrb = BuildHealOrbSound();

            _ambientLoopSource.clip = _clipAmbientDrone;
        }

        public void SetEncounterAmbience(bool active, bool phase2)
        {
            // Heartbeat/pulsing ambient drone disabled so only the Cathedral Boss Music plays cleanly.
            if (_ambientLoopSource != null)
            {
                if (_ambientLoopSource.isPlaying)
                {
                    _ambientLoopSource.Stop();
                }
                _ambientLoopSource.volume = 0f;
            }
        }

        public void PlayRoar(bool phase2)
        {
            Play2D(phase2 ? _clipRoarP2 : _clipRoarP1, phase2 ? 0.96f : 0.88f, phase2 ? 1.04f : 0.95f);
        }

        public void PlayBiteSnap(bool windupOnly = false)
        {
            if (windupOnly)
            {
                Play3D(_clipClimbScrape, 0.52f, Random.Range(0.75f, 0.92f));
            }
            else
            {
                Play3D(_clipBiteSnap, 0.92f, Random.Range(0.90f, 1.08f));
            }
        }

        public void PlayTailWhip()
        {
            Play3D(_clipTailWhip, 0.88f, Random.Range(0.88f, 1.06f));
        }

        public void PlayConstrictGrab()
        {
            Play2D(_clipConstrictSqueeze, 0.90f, 0.92f);
        }

        public void PlayConstrictPulse()
        {
            Play2D(_clipConstrictSqueeze, 0.72f, Random.Range(0.84f, 1.04f));
        }

        public void PlayAcidSpit()
        {
            Play3D(_clipAcidSpit, 0.78f, Random.Range(0.86f, 1.12f));
        }

        public void PlayClimbScrape()
        {
            Play3D(_clipClimbScrape, 0.68f, Random.Range(0.82f, 1.05f));
        }

        public void PlayLeapWindup()
        {
            Play2D(_clipLeapWindup, 0.85f, 1.0f);
        }

        public void PlayLeapSlam()
        {
            Play2D(_clipLeapSlam, 1.0f, Random.Range(0.92f, 1.02f));
        }

        public void PlayStaggerCrash()
        {
            Play2D(_clipStaggerCrash, 0.96f, 0.96f);
        }

        public void PlayDefeatAgony()
        {
            SetEncounterAmbience(false, false);
            Play2D(_clipDefeatAgony, 0.95f, 0.95f);
        }

        public void PlayLowDeathGroan()
        {
            Play2D(_clipLowDeathGroan, 0.34f, Random.Range(0.86f, 0.98f));
        }

        public void PlayAcidMeltDissolve()
        {
            Play2D(_clipAcidMeltDissolve, 0.72f, 1.0f);
        }

        public void PlayHealOrbPickup()
        {
            Play2D(_clipHealOrb, 0.72f, Random.Range(0.95f, 1.06f));
        }

        private void Play2D(AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return;
            EnsureSourcesAndClips();
            _sfx2DSource.pitch = pitch;
            _sfx2DSource.PlayOneShot(clip, volume * masterVolume * StomachAbyss.UI.GameSettingsManager.GetCategoryVolume(StomachAbyss.UI.SoundCategory.Monsters));
        }

        private void Play3D(AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return;
            EnsureSourcesAndClips();
            _sfx3DSource.pitch = pitch;
            _sfx3DSource.PlayOneShot(clip, volume * masterVolume * StomachAbyss.UI.GameSettingsManager.GetCategoryVolume(StomachAbyss.UI.SoundCategory.Monsters));
        }

        // =====================================================================
        // SINTETIZADORES DE HORROR ORGANICO E CATEDRAL
        // =====================================================================

        private static AudioClip BuildAmbientHorrorDrone()
        {
            float duration = 4.0f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            float lp = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float phaseCycle = t / duration;
                float pulse = 0.65f + 0.35f * Mathf.Sin(phaseCycle * Mathf.PI * 4f); // batimento visceral 1Hz

                // Dissonancia de tritone + oitava sub-grave organica
                float sub = Mathf.Sin(2f * Mathf.PI * 42f * t) * 0.42f;
                float tritone = Mathf.Sin(2f * Mathf.PI * 59.4f * t + Mathf.Sin(t * 7.2f) * 1.1f) * 0.28f;
                float choirEerie = Mathf.Sin(2f * Mathf.PI * 126f * t + Mathf.Sin(2f * Mathf.PI * 3.5f * t) * 1.8f) * 0.14f;

                // Ruido gastrico/respiracao umida filtrada
                float noise = (Mathf.PerlinNoise(t * 24f, 0.37f) * 2f - 1f);
                lp = Mathf.Lerp(lp, noise, 0.045f);
                float wet = lp * (0.28f + 0.22f * Mathf.Sin(phaseCycle * Mathf.PI * 2f));

                float sample = (sub + tritone + choirEerie) * pulse + wet;
                data[i] = Mathf.Clamp((float)System.Math.Tanh(sample * 1.1f) * 0.55f, -0.95f, 0.95f);
            }

            AudioClip clip = AudioClip.Create("Tenia_AmbientHorror", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildParasiteRoar(bool phase2)
        {
            float duration = phase2 ? 1.55f : 1.30f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            float lp = 0f;
            System.Random rng = new System.Random(phase2 ? 771 : 313);

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / duration;
                float env = Mathf.Sin(Mathf.Min(1f, u / 0.14f) * Mathf.PI * 0.5f) * Mathf.Pow(1f - u, 0.72f);

                // Guincho inharmonico rasgado de verme + sub-grave demoníaco
                float baseF = phase2 ? Mathf.Lerp(175f, 78f, u) : Mathf.Lerp(132f, 58f, u);
                float mod = Mathf.Sin(2f * Mathf.PI * (baseF * 1.414f) * t) * 4.5f;
                float screech = Mathf.Sin(2f * Mathf.PI * (baseF * 2.73f) * t + mod * 0.6f) * 0.36f;
                float growl = Mathf.Sin(2f * Mathf.PI * baseF * t + mod) * 0.52f;
                float sub = Mathf.Sin(2f * Mathf.PI * (baseF * 0.5f) * t) * 0.45f;

                // Textura umida/borbulhante
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, white, 0.14f);
                float rasp = lp * (0.45f + 0.4f * Mathf.Sin(2f * Mathf.PI * 29f * t));

                // Sino profano grave misturado no ataque
                float bell = Mathf.Sin(2f * Mathf.PI * 98f * t) * Mathf.Exp(-t * 2.6f) * 0.35f;

                float mixed = (growl + screech + sub + rasp + bell) * env;
                data[i] = Mathf.Clamp((float)System.Math.Tanh(mixed * 1.65f) * 0.92f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(phase2 ? "Tenia_RoarP2" : "Tenia_RoarP1", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildBiteSnap()
        {
            float duration = 0.34f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(101);

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float crack = Mathf.Exp(-t * 36f) * (float)(rng.NextDouble() * 2.0 - 1.0) * 1.25f;
                float boneClick = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(620f, 130f, Mathf.Min(1f, t / 0.08f)) * t) * Mathf.Exp(-t * 24f) * 0.85f;
                float thud = Mathf.Sin(2f * Mathf.PI * 64f * t) * Mathf.Exp(-t * 14f) * 0.75f;
                data[i] = Mathf.Clamp((float)System.Math.Tanh((crack + boneClick + thud) * 1.5f) * 0.9f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_BiteSnap", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildTailWhip()
        {
            float duration = 0.42f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(205);
            float bp = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / duration;
                float whooshEnv = Mathf.Sin(u * Mathf.PI) * Mathf.Exp(-t * 4.5f);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                bp = Mathf.Lerp(bp, white, Mathf.Lerp(0.08f, 0.35f, u));
                float snap = u > 0.38f ? Mathf.Exp(-(t - 0.16f) * 32f) * Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.9f : 0f;
                data[i] = Mathf.Clamp((float)System.Math.Tanh((bp * whooshEnv * 1.8f + snap) * 1.3f) * 0.88f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_TailWhip", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildConstrictSqueeze()
        {
            float duration = 0.65f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(409);
            float lp = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / duration;
                float env = Mathf.Sin(u * Mathf.PI);

                // Apertão de carne + gemido abafado tenso
                float squelchFreq = Mathf.Lerp(95f, 44f, u);
                float flesh = Mathf.Sin(2f * Mathf.PI * squelchFreq * t + Mathf.Sin(2f * Mathf.PI * 19f * t) * 2.4f) * 0.6f;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, white, 0.10f);
                float wet = lp * (0.55f + 0.35f * Mathf.Sin(2f * Mathf.PI * 13f * t));
                data[i] = Mathf.Clamp((float)System.Math.Tanh((flesh + wet) * env * 1.4f) * 0.85f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_ConstrictSqueeze", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildAcidSpit()
        {
            float duration = 0.48f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(519);
            float prev = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / duration;
                float env = Mathf.Sin(Mathf.Min(1f, u / 0.12f) * Mathf.PI * 0.5f) * Mathf.Pow(1f - u, 0.85f);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                float highPass = white - prev * 0.82f;
                prev = white;
                float bubble = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(320f, 140f, u) * t + Mathf.Sin(2f * Mathf.PI * 42f * t) * 1.5f) * 0.35f;
                data[i] = Mathf.Clamp((highPass * 0.65f + bubble) * env * 0.9f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_AcidSpit", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildLeapWindup()
        {
            float duration = 0.72f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / duration;
                float f = Mathf.Lerp(110f, 460f, u * u);
                float siren = Mathf.Sin(2f * Mathf.PI * f * t + Mathf.Sin(2f * Mathf.PI * (f * 1.61f) * t) * 1.8f);
                float env = u * Mathf.Pow(1f - Mathf.Max(0f, u - 0.85f) / 0.15f, 0.6f);
                data[i] = Mathf.Clamp((float)System.Math.Tanh(siren * env * 1.2f) * 0.75f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_LeapWindup", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildLeapSlam()
        {
            float duration = 0.95f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(888);
            float lp = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                // Sub-bass sismico decaindo em frequencia + estouro visceral + eco sombrio
                float boomFreq = Mathf.Lerp(118f, 34f, Mathf.Min(1f, t / 0.28f));
                float subBoom = Mathf.Sin(2f * Mathf.PI * boomFreq * t) * Mathf.Exp(-t * 3.8f) * 1.15f;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, white, t < 0.08f ? 0.45f : 0.12f);
                float crunch = lp * Mathf.Exp(-t * 7.5f) * 1.1f;
                float ring = Mathf.Sin(2f * Mathf.PI * 52f * t) * Mathf.Exp(-t * 2.5f) * 0.42f;
                data[i] = Mathf.Clamp((float)System.Math.Tanh((subBoom + crunch + ring) * 1.4f) * 0.95f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_LeapSlam", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildClimbScrape()
        {
            float duration = 0.42f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(612);

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / duration;
                float ticks = Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 18f * t));
                float env = Mathf.Sin(u * Mathf.PI) * Mathf.Pow(ticks, 3f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float chitin = Mathf.Sin(2f * Mathf.PI * 340f * t) * 0.4f + noise * 0.7f;
                data[i] = Mathf.Clamp(chitin * env * 0.72f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_ClimbScrape", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildStaggerCrash()
        {
            float duration = 0.85f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(909);

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / duration;
                // Guincho descendente interrompido + queda de carapaca pesada
                float shriek = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(420f, 82f, u) * t + Mathf.Sin(2f * Mathf.PI * 63f * t) * 1.9f) * Mathf.Exp(-t * 2.8f) * 0.65f;
                float thud = u > 0.15f ? Mathf.Sin(2f * Mathf.PI * 48f * t) * Mathf.Exp(-(t - 0.15f) * 6.5f) * 0.9f : 0f;
                float crack = ((float)(rng.NextDouble() * 2.0 - 1.0)) * Mathf.Exp(-t * 11f) * 0.65f;
                data[i] = Mathf.Clamp((float)System.Math.Tanh((shriek + thud + crack) * 1.3f) * 0.92f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_StaggerCrash", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildDefeatAgony()
        {
            float duration = 1.85f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(999);
            float lp = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / duration;
                float f = Mathf.Lerp(260f, 32f, Mathf.Pow(u, 0.7f));
                float agony = Mathf.Sin(2f * Mathf.PI * f * t + Mathf.Sin(2f * Mathf.PI * (f * 1.48f) * t) * 3.1f);
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, w, 0.1f);
                float env = Mathf.Pow(1f - u, 1.15f);
                data[i] = Mathf.Clamp((float)System.Math.Tanh((agony * 0.65f + lp * 0.45f) * env * 1.5f) * 0.92f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_DefeatAgony", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildHealOrbSound()
        {
            float duration = 0.45f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / duration;
                float env = Mathf.Sin(Mathf.Min(1f, u / 0.1f) * Mathf.PI * 0.5f) * Mathf.Pow(1f - u, 1.4f);
                float tone1 = Mathf.Sin(2f * Mathf.PI * 440f * t) * 0.4f;
                float tone2 = Mathf.Sin(2f * Mathf.PI * 660f * t) * 0.35f;
                float tone3 = Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.25f;
                data[i] = Mathf.Clamp((tone1 + tone2 + tone3) * env * 0.85f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_HealOrb", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildLowDeathGroan()
        {
            float duration = 1.45f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(1013);
            float lp = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / duration;
                float attack = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.22f));
                float release = Mathf.Pow(1f - u, 1.35f);
                float env = attack * release;

                float f = Mathf.Lerp(92f, 44f, Mathf.Pow(u, 0.8f)) + Mathf.Sin(2f * Mathf.PI * 3.5f * t) * 4f;
                float groan = Mathf.Sin(2f * Mathf.PI * f * t + Mathf.Sin(2f * Mathf.PI * (f * 0.52f) * t) * 1.4f);
                float breath = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, breath, 0.045f);

                data[i] = Mathf.Clamp((float)System.Math.Tanh((groan * 0.62f + lp * 0.32f) * env * 1.1f) * 0.65f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_LowDeathGroan", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildAcidMeltDissolve()
        {
            float duration = 2.4f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(2027);
            float lp = 0f;
            float hpPrev = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / duration;
                float env = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.12f)) * Mathf.Pow(1f - u, 0.85f);

                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                float hissing = white - hpPrev * 0.86f;
                hpPrev = white;

                lp = Mathf.Lerp(lp, white, 0.14f);
                float bubble = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(260f, 110f, u) * t + lp * 9f)
                             * Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 11.5f * t + lp * 4f));

                data[i] = Mathf.Clamp((float)System.Math.Tanh((hissing * 0.42f + bubble * 0.55f + lp * 0.25f) * env * 1.35f) * 0.82f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Tenia_AcidMeltDissolve", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
