using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace StomachAbyss.Combat
{
    public enum CombatSfxId
    {
        SwordSlashLight = 0,
        SwordSlamHeavy = 1,
        CrossThrust = 2,
        RazorThrow = 3,
        RazorReturnLock = 4,
        HitFleshCleave = 5,
        HitWeakspotCrit = 6,
        PlayerDash = 7,
        PlayerJump = 8,
        PlayerHurt = 9,
        BloodSplatter = 10,
        WhipCrackLight = 11,
        WhipCrackHeavy = 12,
        WhipAcidSpit = 13,
        WhipHeavyLob = 14,
        AcidPoolSplash = 15,
        WhipFatigueEnter = 16,
        WhipFatigueReady = 17,
        FlameSlashLight = 18,
        FlameChargeIgnite = 19,
        FlameThrustDash = 20,
        FlameBurnBurst = 21,
        FlameSlashLightAlt = 22,
        KatanaIaidoHeavy = 23,
        WeaponSwap = 24,
        PlayerAcidBurn = 25,
        PlayerHeal = 26,
        PlayerDeath = 27,
        PlayerFootstepFlesh = 28,
        CheckpointLit = 29,
        VirgilWhisper = 30,
        EyePillarBurst = 31,
        BossTeniaDeath = 32
    }

    /// <summary>
    /// Centralized combat SFX bank with serialized studio-grade WAV clips,
    /// Editor AssetDatabase auto-loading, procedural runtime fallbacks,
    /// multi-channel AudioSource pooling, and anti-stacking cooldowns.
    /// </summary>
    public class CombatSfxBank : MonoBehaviour
    {
        public const string SfxFolder = "Assets/StomachAbyss/Audio/SFX";
        private const int ChannelPoolSize = 8;
        private const int ClipCount = 33;

        public static CombatSfxBank Instance { get; private set; }

        [Header("Weapon & Attack SFX")]
        public AudioClip swordSlashLight;
        public AudioClip swordSlamHeavy;
        public AudioClip crossThrust;
        public AudioClip razorThrow;
        public AudioClip razorReturnLock;

        [Header("Whip & Acid Weapon SFX (Opcional - Fallback Procedural)")]
        public AudioClip whipCrackLight;
        public AudioClip whipCrackHeavy;
        public AudioClip whipAcidSpit;
        public AudioClip whipHeavyLob;
        public AudioClip acidPoolSplash;
        public AudioClip whipFatigueEnter;
        public AudioClip whipFatigueReady;

        [Header("Lamina Ardente (Flame Saber) SFX (Opcional - Fallback Procedural)")]
        public AudioClip flameSlashLight;
        public AudioClip flameSlashLightAlt;
        public AudioClip katanaIaidoHeavy;
        public AudioClip flameChargeIgnite;
        public AudioClip flameThrustDash;
        public AudioClip flameBurnBurst;

        [Header("Impact & Gore SFX")]
        public AudioClip hitFleshCleave;
        public AudioClip hitWeakspotCrit;
        public AudioClip bloodSplatter;

        [Header("Player Movement & Damage SFX")]
        public AudioClip playerDash;
        public AudioClip playerJump;
        public AudioClip playerHurt;
        public AudioClip playerAcidBurn;
        public AudioClip playerHeal;
        public AudioClip playerDeath;
        public AudioClip playerFootstepFlesh;
        public AudioClip weaponSwap;

        [Header("World & Boss SFX")]
        public AudioClip checkpointLit;
        public AudioClip virgilWhisper;
        public AudioClip eyePillarBurst;
        public AudioClip bossTeniaDeath;

        [Header("Master Gain")]
        [Range(0f, 1.5f)]
        public float masterVolume = 0.92f;

        private readonly AudioClip[] _runtimeFallbacks = new AudioClip[ClipCount];
        private readonly float[] _lastClipPlayTime = new float[ClipCount];
        private AudioSource[] _pool2D;
        private AudioSource[] _pool3D;
        private int _next2DIndex;
        private int _next3DIndex;

        private static int s_heavyScopeDepth;
        private static bool s_impactClaimedInScope;
        private static float s_lastGlobalImpactTime = -10f;

        public static bool IsHeavyAttackContext => s_heavyScopeDepth > 0;
        public static bool WasImpactClaimedInScope => s_impactClaimedInScope;

        public static void BeginImpactScope(bool isHeavyAttack)
        {
            if (isHeavyAttack) s_heavyScopeDepth++;
            s_impactClaimedInScope = false;
        }

        public static void EndImpactScope(bool isHeavyAttack)
        {
            if (isHeavyAttack && s_heavyScopeDepth > 0) s_heavyScopeDepth--;
            s_impactClaimedInScope = false;
        }

        public static CombatSfxBank EnsureExists()
        {
            if (Instance != null) return Instance;

            Instance = FindAnyObjectByType<CombatSfxBank>();
            if (Instance != null)
            {
                Instance.EnsureInitialized();
                return Instance;
            }

            GameObject container = GameObject.Find("Combat_Juice_Systems");
            if (container == null)
            {
                container = new GameObject("Combat_Juice_Systems");
            }

            Instance = container.GetComponent<CombatSfxBank>();
            if (Instance == null)
            {
                Instance = container.AddComponent<CombatSfxBank>();
            }
            Instance.EnsureInitialized();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            LoadEditorClipsIfMissing();
        }

        public void LoadEditorClipsIfMissing()
        {
            if (swordSlashLight == null) swordSlashLight = LoadClip("SFX_Sword_Slash_Light.wav");
            if (swordSlamHeavy == null) swordSlamHeavy = LoadClip("SFX_Sword_Slam_Heavy.wav");
            if (crossThrust == null) crossThrust = LoadClip("SFX_Cross_Thrust.wav");
            if (razorThrow == null) razorThrow = LoadClip("SFX_Razor_Throw.wav");
            if (razorReturnLock == null) razorReturnLock = LoadClip("SFX_Razor_Return_Lock.wav");
            if (hitFleshCleave == null) hitFleshCleave = LoadClip("SFX_Hit_Flesh_Cleave.wav");
            if (hitWeakspotCrit == null) hitWeakspotCrit = LoadClip("SFX_Hit_Weakspot_Crit.wav");
            if (playerDash == null) playerDash = LoadClip("SFX_Player_Dash.wav");
            if (playerJump == null) playerJump = LoadClip("SFX_Player_Jump.wav");
            if (playerHurt == null) playerHurt = LoadClip("SFX_Player_Hurt.wav");
            if (bloodSplatter == null) bloodSplatter = LoadClip("SFX_Blood_Splatter.wav");
        }

        private static AudioClip LoadClip(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxFolder}/{fileName}");
        }
#endif

        public void LoadRuntimeResourcesIfMissing()
        {
            if (swordSlashLight == null) swordSlashLight = Resources.Load<AudioClip>("Audio/SFX/SFX_Weapon_Greatsword_Swing");
            if (swordSlamHeavy == null) swordSlamHeavy = Resources.Load<AudioClip>("Audio/SFX/SFX_Weapon_Greatsword_HeavySlam");
            if (whipCrackLight == null) whipCrackLight = Resources.Load<AudioClip>("Audio/SFX/SFX_Weapon_Whip_Crack");
            if (whipCrackHeavy == null) whipCrackHeavy = Resources.Load<AudioClip>("Audio/SFX/SFX_Weapon_Whip_Crack");
            if (flameSlashLight == null) flameSlashLight = Resources.Load<AudioClip>("Audio/SFX/SFX_Weapon_Katana_Slash_1");
            if (flameSlashLightAlt == null) flameSlashLightAlt = Resources.Load<AudioClip>("Audio/SFX/SFX_Weapon_Katana_Slash_2");
            if (katanaIaidoHeavy == null) katanaIaidoHeavy = Resources.Load<AudioClip>("Audio/SFX/SFX_Weapon_Katana_Iaido_HeavyCut");
            if (flameChargeIgnite == null) flameChargeIgnite = Resources.Load<AudioClip>("Audio/SFX/SFX_Weapon_Katana_FireSpecial_Slash");
            if (flameThrustDash == null) flameThrustDash = Resources.Load<AudioClip>("Audio/SFX/SFX_Weapon_Katana_FireSpecial_Wave");
            if (flameBurnBurst == null) flameBurnBurst = Resources.Load<AudioClip>("Audio/SFX/SFX_Weapon_Katana_FireSpecial_Slash");
            if (weaponSwap == null) weaponSwap = Resources.Load<AudioClip>("Audio/SFX/SFX_Weapon_Swap_Equip");
            if (playerHurt == null) playerHurt = Resources.Load<AudioClip>("Audio/SFX/SFX_Player_Hurt_Impact");
            if (playerAcidBurn == null) playerAcidBurn = Resources.Load<AudioClip>("Audio/SFX/SFX_Player_Acid_Burn");
            if (playerHeal == null) playerHeal = Resources.Load<AudioClip>("Audio/SFX/SFX_Player_Heal_Pulse");
            if (playerDeath == null) playerDeath = Resources.Load<AudioClip>("Audio/SFX/SFX_Player_Death_Collapse");
            if (playerFootstepFlesh == null) playerFootstepFlesh = Resources.Load<AudioClip>("Audio/SFX/SFX_Player_Footstep_WetFlesh");
            if (checkpointLit == null) checkpointLit = Resources.Load<AudioClip>("Audio/SFX/SFX_World_Checkpoint_Lit");
            if (virgilWhisper == null) virgilWhisper = Resources.Load<AudioClip>("Audio/SFX/SFX_World_Virgil_Whisper");
            if (eyePillarBurst == null) eyePillarBurst = Resources.Load<AudioClip>("Audio/SFX/SFX_World_EyePillar_Burst");
            if (bossTeniaDeath == null) bossTeniaDeath = Resources.Load<AudioClip>("Audio/SFX/SFX_Boss_Tenia_Death_Agony");
        }

        public void EnsureInitialized()
        {
#if UNITY_EDITOR
            LoadEditorClipsIfMissing();
#endif
            LoadRuntimeResourcesIfMissing();
            if (_pool2D == null || _pool2D.Length != ChannelPoolSize)
            {
                _pool2D = new AudioSource[ChannelPoolSize];
                Transform root = CreateOrFindChild("CombatSfx_2D_Pool");
                for (int i = 0; i < ChannelPoolSize; i++)
                {
                    AudioSource src = root.gameObject.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.loop = false;
                    src.spatialBlend = 0f;
                    _pool2D[i] = src;
                }
            }

            if (_pool3D == null || _pool3D.Length != ChannelPoolSize)
            {
                _pool3D = new AudioSource[ChannelPoolSize];
                Transform root = CreateOrFindChild("CombatSfx_3D_Pool");
                for (int i = 0; i < ChannelPoolSize; i++)
                {
                    GameObject emitter = new GameObject($"Emitter3D_{i}");
                    emitter.transform.SetParent(root, false);
                    AudioSource src = emitter.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.loop = false;
                    src.spatialBlend = 0.55f;
                    src.rolloffMode = AudioRolloffMode.Linear;
                    src.minDistance = 5f;
                    src.maxDistance = 38f;
                    _pool3D[i] = src;
                }
            }
        }

        private Transform CreateOrFindChild(string childName)
        {
            Transform existing = transform.Find(childName);
            if (existing != null) return existing;
            GameObject go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        public static AudioClip GetClip(CombatSfxId id)
        {
            CombatSfxBank bank = EnsureExists();
            return bank != null ? bank.ResolveClipInternal(id) : null;
        }

        public static void Play(
            CombatSfxId id,
            float volume = 0.85f,
            float pitchMin = 0.96f,
            float pitchMax = 1.04f,
            float minCooldown = 0.03f)
        {
            if (!Application.isPlaying) return;
            CombatSfxBank bank = EnsureExists();
            if (bank == null) return;
            bank.PlayInternal(id, false, Vector3.zero, volume, pitchMin, pitchMax, 0f, minCooldown);
        }

        public static void PlayAt(
            CombatSfxId id,
            Vector3 worldPos,
            float volume = 0.85f,
            float pitchMin = 0.95f,
            float pitchMax = 1.05f,
            float spatialBlend = 0.55f,
            float minCooldown = 0.045f)
        {
            if (!Application.isPlaying) return;
            CombatSfxBank bank = EnsureExists();
            if (bank == null) return;
            bank.PlayInternal(id, true, worldPos, volume, pitchMin, pitchMax, spatialBlend, minCooldown);
        }

        /// <summary>
        /// Plays flesh cleave or weakspot crit SFX while deduplicating rapid multi-segment impacts.
        /// </summary>
        public static void PlayImpact(
            bool isCriticalOrHeavy,
            Vector3 worldPos,
            float volume = 0.88f,
            bool forceUpgrade = false)
        {
            if (!Application.isPlaying) return;

            float now = Time.unscaledTime;
            s_impactClaimedInScope = true;

            if (!forceUpgrade && (now - s_lastGlobalImpactTime) < 0.055f)
            {
                return;
            }

            s_lastGlobalImpactTime = now;
            CombatSfxId id = (isCriticalOrHeavy || IsHeavyAttackContext)
                ? CombatSfxId.HitWeakspotCrit
                : CombatSfxId.HitFleshCleave;

            PlayAt(id, worldPos, volume, 0.94f, 1.06f, 0.35f, 0.03f);
        }

        private void PlayInternal(
            CombatSfxId id,
            bool positional,
            Vector3 worldPos,
            float volume,
            float pitchMin,
            float pitchMax,
            float spatialBlend,
            float minCooldown)
        {
            EnsureInitialized();
            int idx = (int)id;
            if (idx < 0 || idx >= ClipCount) return;

            float now = Time.unscaledTime;
            if (minCooldown > 0f && (now - _lastClipPlayTime[idx]) < minCooldown)
            {
                return;
            }
            _lastClipPlayTime[idx] = now;

            AudioClip clip = ResolveClipInternal(id);
            if (clip == null) return;

            float finalVol = Mathf.Clamp01(volume * masterVolume);
            float pitch = Mathf.Approximately(pitchMin, pitchMax)
                ? pitchMin
                : UnityEngine.Random.Range(Mathf.Min(pitchMin, pitchMax), Mathf.Max(pitchMin, pitchMax));

            if (positional)
            {
                AudioSource src = _pool3D[_next3DIndex];
                _next3DIndex = (_next3DIndex + 1) % _pool3D.Length;
                if (src != null)
                {
                    src.transform.position = worldPos;
                    src.spatialBlend = Mathf.Clamp01(spatialBlend);
                    src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
                    src.PlayOneShot(clip, finalVol * StomachAbyss.UI.GameSettingsManager.GetCategoryVolume(StomachAbyss.UI.SoundCategory.Actions));
                }
            }
            else
            {
                AudioSource src = _pool2D[_next2DIndex];
                _next2DIndex = (_next2DIndex + 1) % _pool2D.Length;
                if (src != null)
                {
                    src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
                    src.PlayOneShot(clip, finalVol * StomachAbyss.UI.GameSettingsManager.GetCategoryVolume(StomachAbyss.UI.SoundCategory.Actions));
                }
            }
        }

        private AudioClip ResolveClipInternal(CombatSfxId id)
        {
            AudioClip assigned = id switch
            {
                CombatSfxId.SwordSlashLight => swordSlashLight,
                CombatSfxId.SwordSlamHeavy => swordSlamHeavy,
                CombatSfxId.CrossThrust => crossThrust,
                CombatSfxId.RazorThrow => razorThrow,
                CombatSfxId.RazorReturnLock => razorReturnLock,
                CombatSfxId.HitFleshCleave => hitFleshCleave,
                CombatSfxId.HitWeakspotCrit => hitWeakspotCrit,
                CombatSfxId.PlayerDash => playerDash,
                CombatSfxId.PlayerJump => playerJump,
                CombatSfxId.PlayerHurt => playerHurt,
                CombatSfxId.BloodSplatter => bloodSplatter,
                CombatSfxId.WhipCrackLight => whipCrackLight,
                CombatSfxId.WhipCrackHeavy => whipCrackHeavy,
                CombatSfxId.WhipAcidSpit => whipAcidSpit,
                CombatSfxId.WhipHeavyLob => whipHeavyLob,
                CombatSfxId.AcidPoolSplash => acidPoolSplash,
                CombatSfxId.WhipFatigueEnter => whipFatigueEnter,
                CombatSfxId.WhipFatigueReady => whipFatigueReady,
                CombatSfxId.FlameSlashLight => flameSlashLight,
                CombatSfxId.FlameSlashLightAlt => flameSlashLightAlt,
                CombatSfxId.KatanaIaidoHeavy => katanaIaidoHeavy,
                CombatSfxId.FlameChargeIgnite => flameChargeIgnite,
                CombatSfxId.FlameThrustDash => flameThrustDash,
                CombatSfxId.FlameBurnBurst => flameBurnBurst,
                CombatSfxId.WeaponSwap => weaponSwap,
                CombatSfxId.PlayerAcidBurn => playerAcidBurn,
                CombatSfxId.PlayerHeal => playerHeal,
                CombatSfxId.PlayerDeath => playerDeath,
                CombatSfxId.PlayerFootstepFlesh => playerFootstepFlesh,
                CombatSfxId.CheckpointLit => checkpointLit,
                CombatSfxId.VirgilWhisper => virgilWhisper,
                CombatSfxId.EyePillarBurst => eyePillarBurst,
                CombatSfxId.BossTeniaDeath => bossTeniaDeath,
                _ => null
            };

            if (assigned != null) return assigned;

            int idx = (int)id;
            if (_runtimeFallbacks[idx] == null)
            {
                _runtimeFallbacks[idx] = BuildProceduralFallback(id);
            }
            return _runtimeFallbacks[idx];
        }

        private static AudioClip BuildProceduralFallback(CombatSfxId id)
        {
            const int sr = 22050;
            float dur = id switch
            {
                CombatSfxId.SwordSlamHeavy => 0.36f,
                CombatSfxId.HitWeakspotCrit => 0.34f,
                CombatSfxId.RazorReturnLock => 0.30f,
                CombatSfxId.HitFleshCleave => 0.26f,
                CombatSfxId.PlayerJump => 0.16f,
                CombatSfxId.CrossThrust => 0.18f,
                CombatSfxId.WhipCrackLight => 0.30f,
                CombatSfxId.WhipCrackHeavy => 0.38f,
                CombatSfxId.WhipAcidSpit => 0.28f,
                CombatSfxId.WhipHeavyLob => 0.36f,
                CombatSfxId.AcidPoolSplash => 0.52f,
                CombatSfxId.WhipFatigueEnter => 0.42f,
                CombatSfxId.WhipFatigueReady => 0.28f,
                CombatSfxId.FlameSlashLight => 0.16f,
                CombatSfxId.FlameChargeIgnite => 0.34f,
                CombatSfxId.FlameThrustDash => 0.36f,
                CombatSfxId.FlameBurnBurst => 0.24f,
                _ => 0.24f
            };

            int len = Mathf.Max(64, Mathf.RoundToInt(sr * dur));
            float[] data = new float[len];
            System.Random rng = new System.Random(1900 + (int)id * 97);
            float lp = 0f;
            float lpPrev = 0f;
            float ph = 0f;

            for (int i = 0; i < len; i++)
            {
                float t = (float)i / sr;
                float u = (float)i / (len - 1);
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                float env = Mathf.Sin(Mathf.Min(1f, u / 0.08f) * Mathf.PI * 0.5f) * Mathf.Exp(-u * 3.8f);

                float sample;
                switch (id)
                {
                    case CombatSfxId.WhipCrackLight:
                    case CombatSfxId.WhipCrackHeavy:
                    {
                        bool heavy = id == CombatSfxId.WhipCrackHeavy;
                        float whooshEnv = Mathf.Sin(Mathf.Clamp01(u / 0.65f) * Mathf.PI);
                        lp = Mathf.Lerp(lp, w, Mathf.Lerp(0.10f, 0.52f, u));
                        float snapTime = heavy ? 0.14f : 0.10f;
                        float snapTransient = t >= snapTime
                            ? Mathf.Exp(-(t - snapTime) * (heavy ? 36f : 48f)) *
                              (w * 1.1f + Mathf.Sin(2f * Mathf.PI * (heavy ? 480f : 760f) * (t - snapTime)) * 0.85f)
                            : 0f;
                        float fleshSnap = t >= snapTime
                            ? Mathf.Exp(-(t - snapTime) * 20f) * Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(260f, 95f, Mathf.Clamp01((t - snapTime) / 0.12f)) * t) * (heavy ? 0.95f : 0.65f)
                            : 0f;
                        sample = lp * whooshEnv * (heavy ? 0.95f : 0.80f) + snapTransient * (heavy ? 1.25f : 1.05f) + fleshSnap;
                        break;
                    }
                    case CombatSfxId.WhipAcidSpit:
                    {
                        float hp = w - lpPrev * 0.82f;
                        lpPrev = w;
                        float pitchHz = Mathf.Lerp(420f, 180f, u);
                        ph += 2f * Mathf.PI * pitchHz / sr;
                        float bubbleMod = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 52f * t);
                        float spitEnv = Mathf.Sin(Mathf.Min(1f, u / 0.10f) * Mathf.PI * 0.5f) * Mathf.Pow(1f - u, 1.15f);
                        sample = (hp * 0.68f + Mathf.Sin(ph) * bubbleMod * 0.48f) * spitEnv;
                        break;
                    }
                    case CombatSfxId.WhipHeavyLob:
                    {
                        lp = Mathf.Lerp(lp, w, 0.25f);
                        float pitchHz = Mathf.Lerp(190f, 88f, u);
                        ph += 2f * Mathf.PI * pitchHz / sr;
                        float gurgle = Mathf.Sin(2f * Mathf.PI * 26f * t);
                        sample = (Mathf.Sin(ph + gurgle * 1.8f) * 0.65f + lp * 0.55f) * env;
                        break;
                    }
                    case CombatSfxId.AcidPoolSplash:
                    {
                        float hp = w - lpPrev * 0.72f;
                        lpPrev = w;
                        lp = Mathf.Lerp(lp, w, t < 0.09f ? 0.42f : 0.14f);
                        float splatThud = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(145f, 48f, Mathf.Min(1f, t / 0.18f)) * t) * Mathf.Exp(-t * 10f) * 0.85f;
                        float fizz = hp * (0.45f + 0.55f * Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 34f * t))) * Mathf.Exp(-t * 4.2f) * 0.72f;
                        sample = splatThud + lp * Mathf.Exp(-t * 6f) * 0.65f + fizz;
                        break;
                    }
                    case CombatSfxId.WhipFatigueEnter:
                    {
                        lp = Mathf.Lerp(lp, w, 0.18f);
                        float f = Mathf.Lerp(240f, 75f, u);
                        ph += 2f * Mathf.PI * f / sr;
                        float deflEnv = Mathf.Sin(Mathf.Min(1f, u / 0.12f) * Mathf.PI * 0.5f) * (1f - u * 0.85f);
                        sample = (lp * 0.72f + Mathf.Sin(ph) * 0.35f) * deflEnv;
                        break;
                    }
                    case CombatSfxId.WhipFatigueReady:
                    {
                        float f = Mathf.Lerp(380f, 920f, u);
                        ph += 2f * Mathf.PI * f / sr;
                        float readyPing = u > 0.48f ? Mathf.Sin(2f * Mathf.PI * 1120f * t) * Mathf.Exp(-(t - dur * 0.48f) * 18f) * 0.65f : 0f;
                        sample = (Mathf.Sin(ph) * 0.45f + w * 0.15f) * env + readyPing;
                        break;
                    }
                    case CombatSfxId.FlameSlashLight:
                    {
                        float hp = w - lpPrev * 0.88f;
                        lpPrev = w;
                        float f = Mathf.Lerp(920f, 340f, u);
                        ph += 2f * Mathf.PI * f / sr;
                        float sliceEnv = Mathf.Sin(Mathf.Min(1f, u / 0.18f) * Mathf.PI * 0.5f) * Mathf.Exp(-u * 4.4f);
                        float steelPing = Mathf.Sin(2f * Mathf.PI * 1560f * t) * Mathf.Exp(-t * 28f) * 0.35f;
                        sample = (hp * 0.82f + Mathf.Sin(ph) * 0.34f) * sliceEnv + steelPing;
                        break;
                    }
                    case CombatSfxId.FlameChargeIgnite:
                    {
                        float hp = w - lpPrev * 0.80f;
                        lpPrev = w;
                        lp = Mathf.Lerp(lp, w, Mathf.Lerp(0.12f, 0.46f, u));
                        float f = Mathf.Lerp(135f, 440f, u * u);
                        ph += 2f * Mathf.PI * f / sr;
                        float crackle = Mathf.Pow(Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 37f * t)), 3f);
                        float igniteEnv = Mathf.Sin(Mathf.Min(1f, u / 0.25f) * Mathf.PI * 0.5f) * (1f - u * 0.55f);
                        sample = (lp * 0.62f + hp * (0.35f + crackle * 0.65f) + Mathf.Sin(ph) * 0.36f) * igniteEnv;
                        break;
                    }
                    case CombatSfxId.FlameThrustDash:
                    {
                        float hp = w - lpPrev * 0.76f;
                        lpPrev = w;
                        lp = Mathf.Lerp(lp, w, 0.22f);
                        float pierceHz = Mathf.Lerp(1320f, 540f, Mathf.Clamp01(t / 0.10f));
                        ph += 2f * Mathf.PI * pierceHz / sr;
                        float pierceRing = Mathf.Sin(ph) * Mathf.Exp(-t * 16f) * 0.68f;
                        float jetEnv = Mathf.Sin(Mathf.Min(1f, u / 0.08f) * Mathf.PI * 0.5f) * Mathf.Exp(-u * 2.9f);
                        sample = (lp * 0.78f + hp * 0.58f) * jetEnv + pierceRing;
                        break;
                    }
                    case CombatSfxId.FlameBurnBurst:
                    {
                        float hp = w - lpPrev * 0.84f;
                        lpPrev = w;
                        float snapHz = Mathf.Lerp(1480f, 320f, Mathf.Clamp01(u * 2.2f));
                        ph += 2f * Mathf.PI * snapHz / sr;
                        float snapPop = Mathf.Sin(ph) * Mathf.Exp(-t * 22f) * 0.72f;
                        float flareCrackle = hp * (0.5f + 0.5f * Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 46f * t))) * env * 0.85f;
                        sample = snapPop + flareCrackle;
                        break;
                    }
                    case CombatSfxId.SwordSlashLight:
                    case CombatSfxId.CrossThrust:
                    case CombatSfxId.PlayerDash:
                    {
                        lp = Mathf.Lerp(lp, w, id == CombatSfxId.PlayerDash ? 0.28f : 0.35f);
                        float f = Mathf.Lerp(260f, 95f, u);
                        ph += 2f * Mathf.PI * f / sr;
                        sample = (lp * 0.72f + Mathf.Sin(ph) * 0.28f) * Mathf.Sin(u * Mathf.PI);
                        break;
                    }
                    case CombatSfxId.SwordSlamHeavy:
                    {
                        lp = Mathf.Lerp(lp, w, 0.22f);
                        float f = Mathf.Lerp(145f, 38f, u);
                        ph += 2f * Mathf.PI * f / sr;
                        float chime = Mathf.Sin(2f * Mathf.PI * 520f * t) * Mathf.Exp(-t * 8f) * 0.35f;
                        sample = (Mathf.Sin(ph) * 0.72f + lp * 0.45f) * env + chime;
                        break;
                    }
                    case CombatSfxId.RazorThrow:
                    case CombatSfxId.RazorReturnLock:
                    {
                        float fStart = id == CombatSfxId.RazorThrow ? 680f : 540f;
                        float fEnd = id == CombatSfxId.RazorThrow ? 1320f : 1680f;
                        float f = Mathf.Lerp(fStart, fEnd, u);
                        ph += 2f * Mathf.PI * f / sr;
                        float lockPing = (id == CombatSfxId.RazorReturnLock && u > 0.55f)
                            ? Mathf.Sin(2f * Mathf.PI * 1840f * t) * Mathf.Exp(-(t - dur * 0.55f) * 16f) * 0.7f
                            : 0f;
                        sample = (Mathf.Sin(ph) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 38f * t)) * 0.5f + w * 0.25f) * env + lockPing;
                        break;
                    }
                    case CombatSfxId.HitFleshCleave:
                    case CombatSfxId.HitWeakspotCrit:
                    case CombatSfxId.PlayerHurt:
                    {
                        lp = Mathf.Lerp(lp, w, 0.32f);
                        float f = Mathf.Lerp(id == CombatSfxId.HitWeakspotCrit ? 220f : 280f, 52f, u);
                        ph += 2f * Mathf.PI * f / sr;
                        float ring = id == CombatSfxId.HitWeakspotCrit
                            ? Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 7f) * 0.42f
                            : 0f;
                        sample = (lp * 0.65f + Mathf.Sin(ph) * 0.65f) * env + ring;
                        break;
                    }
                    case CombatSfxId.PlayerJump:
                    {
                        float f = Mathf.Lerp(160f, 520f, u);
                        ph += 2f * Mathf.PI * f / sr;
                        sample = Mathf.Sin(ph) * Mathf.Sin(u * Mathf.PI) * 0.6f;
                        break;
                    }
                    default: // BloodSplatter
                    {
                        lp = Mathf.Lerp(lp, w, 0.42f);
                        float drops = Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 24f * t));
                        sample = lp * drops * env * 0.75f;
                        break;
                    }
                }

                data[i] = Mathf.Clamp((float)Math.Tanh(sample * 1.35) * 0.85f, -0.95f, 0.95f);
            }

            AudioClip clip = AudioClip.Create($"Fallback_{id}", len, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
