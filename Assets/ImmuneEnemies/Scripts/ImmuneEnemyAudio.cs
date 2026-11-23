using System;
using UnityEngine;

namespace ImmuneEnemies
{
    /// <summary>
    /// Clipes de audio procedurais sombrios e organicos compartilhados entre todos os inimigos imunologicos.
    /// Gera tudo em memoria a 22050 Hz sem assets externos.
    /// </summary>
    public static class ImmuneEnemyAudio
    {
        private const int SampleRate = 22050;

        public static AudioClip ClipMacrophageStomp { get; private set; }
        public static AudioClip ClipMacrophageInhale { get; private set; }
        public static AudioClip ClipMacrophageChomp { get; private set; }
        public static AudioClip ClipEosinophilBuzz { get; private set; }
        public static AudioClip ClipEosinophilSpit { get; private set; }
        public static AudioClip ClipHitFlesh { get; private set; }
        public static AudioClip ClipDeathBurst { get; private set; }

        private static bool s_initialized;

        public static void EnsureInitialized()
        {
            if (s_initialized) return;
            s_initialized = true;

            ClipMacrophageStomp = BuildStomp();
            ClipMacrophageInhale = BuildMacrophageInhale();
            ClipMacrophageChomp = BuildMacrophageChomp();
            ClipEosinophilBuzz = BuildEosinophilBuzz();
            ClipEosinophilSpit = BuildEosinophilSpit();
            ClipHitFlesh = BuildHitFlesh();
            ClipDeathBurst = BuildDeathBurst();
        }

        public static void ConfigureSpatialSource(AudioSource src, float maxDist = 28f)
        {
            if (src == null) return;
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0.72f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 4.5f;
            src.maxDistance = maxDist;
        }

        public static void Play(AudioSource src, AudioClip clip, float volume = 0.65f, float pitchMin = 0.90f, float pitchMax = 1.12f)
        {
            EnsureInitialized();
            if (src == null || clip == null) return;
            src.pitch = UnityEngine.Random.Range(pitchMin, pitchMax);
            src.PlayOneShot(clip, volume * StomachAbyss.UI.GameSettingsManager.GetCategoryVolume(StomachAbyss.UI.SoundCategory.Monsters));
        }

        private static AudioClip CreateClip(string name, float duration, Func<float, float, float> sampler)
        {
            int count = Mathf.Max(64, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float u = (float)i / (count - 1);
                data[i] = Mathf.Clamp(sampler(t, u), -0.96f, 0.96f);
            }
            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildStomp()
        {
            return CreateClip("Immune_MacroStomp", 0.24f, (t, u) =>
            {
                float thud = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(92f, 34f, u) * t) * Mathf.Exp(-t * 15f) * 0.78f;
                float squelch = Mathf.Sin(2f * Mathf.PI * 190f * t) * Mathf.Exp(-t * 28f) * 0.18f;
                return (float)Math.Tanh((thud + squelch) * 1.4f);
            });
        }

        private static AudioClip BuildMacrophageInhale()
        {
            System.Random rng = new System.Random(1201);
            float lp = 0f;
            return CreateClip("Immune_MacroInhale", 0.62f, (t, u) =>
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, w, 0.11f);
                float env = Mathf.Sin(u * Mathf.PI) * (0.4f + 0.6f * u);
                float gurgle = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(65f, 145f, u) * t + Mathf.Sin(2f * Mathf.PI * 22f * t) * 2.1f);
                return (float)Math.Tanh((lp * 0.7f + gurgle * 0.45f) * env * 1.35f) * 0.8f;
            });
        }

        private static AudioClip BuildMacrophageChomp()
        {
            System.Random rng = new System.Random(2209);
            return CreateClip("Immune_MacroChomp", 0.36f, (t, u) =>
            {
                float snapNoise = ((float)(rng.NextDouble() * 2.0 - 1.0)) * Mathf.Exp(-t * 32f) * 1.15f;
                float jawClack = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(540f, 95f, Mathf.Min(1f, t / 0.1f)) * t) * Mathf.Exp(-t * 20f) * 0.85f;
                float gutSlam = Mathf.Sin(2f * Mathf.PI * 52f * t) * Mathf.Exp(-t * 11f) * 0.72f;
                return (float)Math.Tanh((snapNoise + jawClack + gutSlam) * 1.5f) * 0.88f;
            });
        }

        private static AudioClip BuildEosinophilBuzz()
        {
            return CreateClip("Immune_EosBuzz", 0.28f, (t, u) =>
            {
                float env = Mathf.Sin(u * Mathf.PI);
                float baseF = Mathf.Lerp(170f, 225f, Mathf.Sin(u * Mathf.PI));
                float wing = Mathf.Sin(2f * Mathf.PI * baseF * t + Mathf.Sin(2f * Mathf.PI * 48f * t) * 1.3f);
                return wing * env * 0.38f;
            });
        }

        private static AudioClip BuildEosinophilSpit()
        {
            System.Random rng = new System.Random(3307);
            float prev = 0f;
            return CreateClip("Immune_EosSpit", 0.32f, (t, u) =>
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                float hp = w - prev * 0.8f;
                prev = w;
                float env = Mathf.Pow(1f - u, 0.9f) * Mathf.Min(1f, u * 10f);
                float pop = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(420f, 150f, u) * t) * 0.45f;
                return (hp * 0.55f + pop) * env * 0.85f;
            });
        }

        private static AudioClip BuildHitFlesh()
        {
            System.Random rng = new System.Random(4403);
            return CreateClip("Immune_HitFlesh", 0.18f, (t, u) =>
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                float squelch = w * Mathf.Exp(-t * 24f) * 0.65f;
                float punch = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(260f, 70f, u) * t) * Mathf.Exp(-t * 22f) * 0.85f;
                return (float)Math.Tanh((squelch + punch) * 1.4f) * 0.85f;
            });
        }

        private static AudioClip BuildDeathBurst()
        {
            System.Random rng = new System.Random(5519);
            float lp = 0f;
            return CreateClip("Immune_DeathBurst", 0.62f, (t, u) =>
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, w, t < 0.1f ? 0.5f : 0.14f);
                float boom = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(115f, 38f, u) * t) * Mathf.Exp(-t * 6.5f) * 0.9f;
                float wet = lp * Mathf.Exp(-t * 4.5f) * 1.05f;
                return (float)Math.Tanh((boom + wet) * 1.5f) * 0.92f;
            });
        }
    }
}
