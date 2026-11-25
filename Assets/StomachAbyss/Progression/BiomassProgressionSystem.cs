using MapaEstomago;
using StomachAbyss.Combat;
using StomachAbyss.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StomachAbyss.Progression
{
    public enum MutationType
    {
        Carapace = 0,
        Metabolism = 1,
        Virulence = 2,
        Gland = 3
    }

    /// <summary>
    /// Core Biomass progression currency, mutation upgrades, recoverable death cocoon,
    /// and PS1 obsidian-and-bone HUD counter for Stomach_Abyss.
    /// </summary>
    public class BiomassProgressionSystem : MonoBehaviour
    {
        public const int MaxMutationLevel = 5;
        private const string GameplaySceneName = "Stomach_Abyss";
        private const string MainMenuSceneName = "MainMenu";

        public static BiomassProgressionSystem Instance { get; private set; }
        public static int CurrentBiomass => Instance != null ? Instance._currentBiomass : 0;

        private int _currentBiomass;
        private readonly int[] _mutationLevels = new int[4];

        private int _recentGainAmount;
        private float _recentGainTimer;
        private float _recoveredBannerTimer;

        private BiomassCocoonPickup _activeCocoon;

        private static Texture2D s_whiteTex;
        private static GUIStyle s_labelStyle;
        private static GUIStyle s_gainStyle;
        private static GUIStyle s_bannerStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            HandleScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            HandleScene(scene);
        }

        private static void HandleScene(Scene scene)
        {
            if (string.Equals(scene.name, MainMenuSceneName, System.StringComparison.OrdinalIgnoreCase))
            {
                if (Instance != null)
                {
                    if (Instance._activeCocoon != null)
                    {
                        Destroy(Instance._activeCocoon.gameObject);
                        Instance._activeCocoon = null;
                    }
                    Destroy(Instance.gameObject);
                    Instance = null;
                }
                return;
            }

            if (string.Equals(scene.name, GameplaySceneName, System.StringComparison.OrdinalIgnoreCase))
            {
                EnsureExists();
            }
        }

        public static BiomassProgressionSystem EnsureExists()
        {
            if (Instance != null) return Instance;

            Instance = FindAnyObjectByType<BiomassProgressionSystem>();
            if (Instance != null) return Instance;

            GameObject go = new GameObject("BiomassProgressionSystem");
            Instance = go.AddComponent<BiomassProgressionSystem>();
            return Instance;
        }

        public static float GetDamageMultiplier()
        {
            return 1f + GetMutationLevel(MutationType.Virulence) * 0.12f;
        }

        public static int GetMutationLevel(MutationType type)
        {
            if (Instance == null) return 0;
            int idx = (int)type;
            if (idx < 0 || idx >= Instance._mutationLevels.Length) return 0;
            return Instance._mutationLevels[idx];
        }

        public static int GetUpgradeCost(MutationType type)
        {
            int lvl = GetMutationLevel(type);
            // Level 0: 60, 1: 105, 2: 165, 3: 240, 4: 330
            return 60 + lvl * 45 + (lvl > 1 ? (lvl * (lvl - 1) / 2) * 15 : 0);
        }

        public static bool TryPurchaseUpgrade(MutationType type, SimplePlayerController player)
        {
            BiomassProgressionSystem inst = EnsureExists();
            if (inst == null) return false;

            int idx = (int)type;
            if (idx < 0 || idx >= inst._mutationLevels.Length) return false;

            int lvl = inst._mutationLevels[idx];
            if (lvl >= MaxMutationLevel) return false;

            int cost = GetUpgradeCost(type);
            if (inst._currentBiomass < cost) return false;

            inst._currentBiomass -= cost;
            inst._mutationLevels[idx] = lvl + 1;

            if (player != null)
            {
                switch (type)
                {
                    case MutationType.Carapace:
                        player.maxHealth += 15f;
                        player.Heal(15f);
                        break;
                    case MutationType.Metabolism:
                        player.maxStamina += 15f;
                        player.staminaRegenRate += 5f;
                        player.currentStamina = player.maxStamina;
                        break;
                    case MutationType.Virulence:
                        // Automatically applied via GetDamageMultiplier() in weapons
                        break;
                    case MutationType.Gland:
                        player.maxEstusCharges += 1;
                        player.estusHealAmount += 10f;
                        player.RefillEstus();
                        break;
                }
            }

            CombatSfxBank.Play(CombatSfxId.PlayerHeal, 0.90f, 1.05f, 1.12f, 0.05f);
            CombatCameraJuice.PunchFov(-1.8f, 0.25f);
            return true;
        }

        public static void AwardBiomass(int amount, Vector3 worldSourcePos)
        {
            if (amount <= 0) return;

            BiomassProgressionSystem inst = EnsureExists();
            if (inst == null) return;

            inst._currentBiomass += amount;
            inst._recentGainAmount = inst._recentGainTimer > 0f ? inst._recentGainAmount + amount : amount;
            inst._recentGainTimer = 2.2f;

            SpawnBiomassBurstParticles(worldSourcePos, 14);
        }

        public static void DropBiomassCocoon(Vector3 deathWorldPos)
        {
            if (Instance == null || Instance._currentBiomass <= 0) return;

            if (Instance._activeCocoon != null)
            {
                Destroy(Instance._activeCocoon.gameObject);
                Instance._activeCocoon = null;
            }

            Vector3 spawnPos = deathWorldPos + Vector3.up * 0.55f;
            if (Physics.Raycast(
                    deathWorldPos + Vector3.up * 2.5f,
                    Vector3.down,
                    out RaycastHit hit,
                    14f,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                spawnPos = hit.point + Vector3.up * 0.55f;
            }

            int droppedAmount = Instance._currentBiomass;
            Instance._currentBiomass = 0;
            Instance._recentGainAmount = 0;
            Instance._recentGainTimer = 0f;

            Instance._activeCocoon = BiomassCocoonPickup.Spawn(spawnPos, droppedAmount);
        }

        internal static void RecoverCocoonBiomass(BiomassCocoonPickup cocoon, int amount, Vector3 worldPos)
        {
            BiomassProgressionSystem inst = EnsureExists();
            if (inst == null) return;

            if (inst._activeCocoon == cocoon)
            {
                inst._activeCocoon = null;
            }

            if (amount > 0)
            {
                inst._currentBiomass += amount;
                inst._recentGainAmount = inst._recentGainTimer > 0f ? inst._recentGainAmount + amount : amount;
                inst._recentGainTimer = 2.4f;
            }

            inst._recoveredBannerTimer = 2.6f;
            CombatSfxBank.Play(CombatSfxId.CheckpointLit, 0.92f, 0.98f, 1.06f, 0.05f);
            SpawnBiomassBurstParticles(worldPos, 24);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (_activeCocoon != null)
            {
                Destroy(_activeCocoon.gameObject);
                _activeCocoon = null;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_recentGainTimer > 0f)
            {
                _recentGainTimer = Mathf.Max(0f, _recentGainTimer - dt);
                if (_recentGainTimer <= 0f)
                {
                    _recentGainAmount = 0;
                }
            }

            if (_recoveredBannerTimer > 0f)
            {
                _recoveredBannerTimer = Mathf.Max(0f, _recoveredBannerTimer - dt);
            }
        }

        private void OnGUI()
        {
            if (SceneManager.GetActiveScene().name != GameplaySceneName) return;
            if (PauseMenuController.IsPaused) return;

            EnsureGuiAssets();

            float boxW = 196f;
            float boxH = 38f;
            float margin = 20f;
            float x = Screen.width - boxW - margin;
            float y = Screen.height - boxH - margin;

            // Outer aged-bone border
            GUI.color = new Color(0.66f, 0.58f, 0.46f, 0.92f);
            GUI.DrawTexture(new Rect(x - 2f, y - 2f, boxW + 4f, boxH + 4f), s_whiteTex);

            // Inner obsidian fill
            GUI.color = new Color(0.06f, 0.05f, 0.06f, 0.90f);
            GUI.DrawTexture(new Rect(x, y, boxW, boxH), s_whiteTex);

            // Cyan-emerald biomass accent bar on the left
            GUI.color = new Color(0.18f, 0.92f, 0.72f, 0.95f);
            GUI.DrawTexture(new Rect(x + 4f, y + 5f, 4f, boxH - 10f), s_whiteTex);

            // Biomass counter text
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 14f, y, boxW - 22f, boxH), $"BIOMASSA: {_currentBiomass}", s_labelStyle);

            // Recent gain floating popup
            if (_recentGainTimer > 0f && _recentGainAmount > 0)
            {
                float alpha = Mathf.Clamp01(_recentGainTimer / 0.55f);
                float rise = (1f - Mathf.Clamp01(_recentGainTimer / 2.2f)) * 14f;
                GUI.color = new Color(0.32f, 1.0f, 0.68f, alpha);
                GUI.Label(new Rect(x, y - 24f - rise, boxW - 6f, 22f), $"+{_recentGainAmount}", s_gainStyle);
            }

            // Recovered cocoon banner
            if (_recoveredBannerTimer > 0f)
            {
                float alpha = _recoveredBannerTimer > 2.2f
                    ? Mathf.Clamp01((2.6f - _recoveredBannerTimer) / 0.4f)
                    : Mathf.Clamp01(_recoveredBannerTimer / 0.65f);

                float bw = 340f;
                float bh = 46f;
                float bx = (Screen.width - bw) * 0.5f;
                float by = Screen.height * 0.24f;

                GUI.color = new Color(0.18f, 0.92f, 0.72f, 0.85f * alpha);
                GUI.DrawTexture(new Rect(bx - 2f, by - 2f, bw + 4f, bh + 4f), s_whiteTex);

                GUI.color = new Color(0.05f, 0.06f, 0.06f, 0.90f * alpha);
                GUI.DrawTexture(new Rect(bx, by, bw, bh), s_whiteTex);

                GUI.color = new Color(0.75f, 1.0f, 0.88f, alpha);
                GUI.Label(new Rect(bx, by, bw, bh), "BIOMASSA RECUPERADA", s_bannerStyle);
            }

            GUI.color = Color.white;
        }

        private static void EnsureGuiAssets()
        {
            if (s_whiteTex == null)
            {
                s_whiteTex = Texture2D.whiteTexture;
            }

            if (s_labelStyle == null)
            {
                s_labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 15,
                    fontStyle = FontStyle.Bold
                };
                s_labelStyle.normal.textColor = new Color(0.93f, 0.89f, 0.80f, 0.98f);
            }

            if (s_gainStyle == null)
            {
                s_gainStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontSize = 15,
                    fontStyle = FontStyle.Bold
                };
                s_gainStyle.normal.textColor = Color.white;
            }

            if (s_bannerStyle == null)
            {
                s_bannerStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 18,
                    fontStyle = FontStyle.Bold
                };
                s_bannerStyle.normal.textColor = Color.white;
            }
        }

        internal static void SpawnBiomassBurstParticles(Vector3 worldPos, int count)
        {
            GameObject fxObj = new GameObject("BiomassBurstFX");
            fxObj.transform.position = worldPos;

            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.7f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.72f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 4.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.24f);
            main.gravityModifier = -0.35f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.14f, 0.96f, 0.82f, 0.95f),
                new Color(0.28f, 1.0f, 0.56f, 0.90f));

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.28f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.05f));

            ParticleSystemRenderer pr = ps.GetComponent<ParticleSystemRenderer>();
            if (pr != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                if (sh != null)
                {
                    Material mat = new Material(sh);
                    mat.color = new Color(0.22f, 1.0f, 0.76f, 0.95f);
                    pr.sharedMaterial = mat;
                }
            }

            ps.Emit(count);
            Destroy(fxObj, 1.3f);
        }
    }

    /// <summary>
    /// Recoverable glowing organic cocoon dropped at the player's death location.
    /// </summary>
    public class BiomassCocoonPickup : MonoBehaviour
    {
        private const float PickupRadius = 2.2f;

        private int _storedBiomass;
        private Vector3 _basePosition;
        private Vector3 _baseScale = new Vector3(0.52f, 0.72f, 0.52f);
        private Light _glowLight;
        private SimplePlayerController _cachedPlayer;
        private bool _collected;

        public static BiomassCocoonPickup Spawn(Vector3 worldPos, int biomassAmount)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.name = "Biomass_Death_Cocoon";
            root.transform.position = worldPos;
            root.transform.localScale = new Vector3(0.52f, 0.72f, 0.52f);

            Collider col = root.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            Renderer rend = root.GetComponent<Renderer>();
            if (rend != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (sh != null)
                {
                    Material mat = new Material(sh);
                    Color coreColor = new Color(0.10f, 0.78f, 0.64f, 1f);
                    Color emissionColor = new Color(0.16f, 1.65f, 1.25f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", coreColor);
                    else mat.color = coreColor;

                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", emissionColor);
                    }
                    rend.sharedMaterial = mat;
                }
            }

            Light light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.20f, 1.0f, 0.78f, 1f);
            light.range = 6.5f;
            light.intensity = 2.6f;

            // Gentle rising biomass spores
            ParticleSystem ps = root.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.75f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.gravityModifier = -0.18f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.16f, 0.96f, 0.80f, 0.85f),
                new Color(0.32f, 1.0f, 0.58f, 0.75f));

            var emission = ps.emission;
            emission.rateOverTime = 10f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.36f;

            ParticleSystemRenderer pr = ps.GetComponent<ParticleSystemRenderer>();
            if (pr != null)
            {
                Shader pSh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                if (pSh != null)
                {
                    Material pMat = new Material(pSh);
                    pMat.color = new Color(0.22f, 1.0f, 0.76f, 0.9f);
                    pr.sharedMaterial = pMat;
                }
            }
            ps.Play();

            BiomassCocoonPickup pickup = root.AddComponent<BiomassCocoonPickup>();
            pickup._storedBiomass = biomassAmount;
            pickup._basePosition = worldPos;
            pickup._glowLight = light;
            return pickup;
        }

        private void Update()
        {
            if (_collected) return;

            float t = Time.time;
            float pulse = 1f + Mathf.Sin(t * 4.2f) * 0.11f;
            transform.localScale = new Vector3(_baseScale.x * pulse, _baseScale.y * (1f / Mathf.Sqrt(pulse)), _baseScale.z * pulse);
            transform.position = _basePosition + Vector3.up * (Mathf.Sin(t * 2.4f) * 0.14f);
            transform.Rotate(0f, 38f * Time.deltaTime, 0f, Space.World);

            if (_glowLight != null)
            {
                _glowLight.intensity = 2.3f + Mathf.Sin(t * 4.2f) * 0.65f;
            }

            if (_cachedPlayer == null || !_cachedPlayer.gameObject.activeInHierarchy)
            {
                GameObject go = null;
                try { go = GameObject.FindGameObjectWithTag("Player"); } catch { }
                if (go != null) _cachedPlayer = go.GetComponent<SimplePlayerController>();
                if (_cachedPlayer == null) _cachedPlayer = FindAnyObjectByType<SimplePlayerController>();
            }

            if (_cachedPlayer != null && !_cachedPlayer.IsDead)
            {
                float dist = Vector3.Distance(_cachedPlayer.transform.position + Vector3.up * 0.55f, transform.position);
                if (dist <= PickupRadius)
                {
                    _collected = true;
                    BiomassProgressionSystem.RecoverCocoonBiomass(this, _storedBiomass, transform.position);
                    Destroy(gameObject);
                }
            }
        }
    }
}
