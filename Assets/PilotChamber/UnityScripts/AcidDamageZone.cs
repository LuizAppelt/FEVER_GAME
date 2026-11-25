using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MapaEstomago
{
    /// <summary>
    /// Interface opcional que o seu script de Vida/Player pode implementar
    /// para receber o dano acido diretamente.
    /// </summary>
    public interface IAcidDamageable
    {
        void TakeAcidDamage(float amount);
    }

    /// <summary>
    /// Aplica dano continuo enquanto o jogador toca o acido E continua
    /// aplicando dano residual (queimadura acida) por alguns segundos apos sair.
    /// Anexe este script ao objeto "Acid_Lake" com um Collider marcado como IsTrigger.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class AcidDamageZone : MonoBehaviour
    {
        [Header("Configuracao de Alvo")]
        [Tooltip("Tag do objeto que sofre dano (ex: Player). Deixe vazio para aceitar qualquer IAcidDamageable.")]
        public string targetTag = "Player";

        [Header("Dano Dentro do Acido")]
        [Tooltip("Dano por segundo enquanto estiver pisando no acido.")]
        public float damagePerSecondInside = 20f;

        [Tooltip("Intervalo em segundos entre cada tick de dano.")]
        [Min(0.05f)]
        public float tickInterval = 0.25f;

        [Header("Dano Residual Apos Sair do Acido")]
        [Tooltip("Por quantos segundos o acido continua queimando depois que sair da poca.")]
        public float lingerDurationAfterExit = 3.0f;

        [Tooltip("Dano por segundo durante a queimadura residual apos sair.")]
        public float lingerDamagePerSecond = 10f;

        [Header("Eventos e Debug")]
        [Tooltip("Evento disparado a cada tick de dano (envia o valor de dano do tick).")]
        public UnityEvent<float> onAcidTickDamage;

        [Tooltip("Evento disparado ao entrar no acido / iniciar queimadura.")]
        public UnityEvent onAcidBurnStart;

        [Tooltip("Evento disparado quando o efeito residual termina por completo.")]
        public UnityEvent onAcidBurnEnd;

        [Header("Splash & Efeitos")]
        [Tooltip("Material de particula de gotas de acido ao cair na superficie (opcional).")]
        public Material splashDropletMaterial;

        public bool logDebugTicks = true;

        private class BurnState
        {
            public GameObject target;
            public IAcidDamageable damageable;
            public SimplePlayerController player;
            public bool wasInside;
            public int colliderContacts;
            public float lingerRemaining;
            public float tickTimer;
        }

        private static readonly HashSet<AcidDamageZone> s_activeZones = new HashSet<AcidDamageZone>();

        private readonly Dictionary<GameObject, BurnState> _tracked = new Dictionary<GameObject, BurnState>();
        private readonly List<GameObject> _toRemove = new List<GameObject>();

        private void OnEnable()
        {
            s_activeZones.Add(this);
        }

        private void OnDisable()
        {
            s_activeZones.Remove(this);
            _tracked.Clear();
        }

        /// <summary>
        /// Remove imediatamente qualquer estado de acido/queimadura residual do alvo em todas as zonas ativas
        /// (ex.: quando o jogador morre ou renasce).
        /// </summary>
        public static void ClearTargetFromAll(GameObject target)
        {
            if (target == null) return;

            foreach (AcidDamageZone zone in s_activeZones)
            {
                if (zone != null)
                {
                    zone.ClearTarget(target);
                }
            }
        }

        public void ClearTarget(GameObject target)
        {
            if (target == null) return;

            if (_tracked.TryGetValue(target, out BurnState state))
            {
                if (state.wasInside && state.player != null)
                {
                    state.player.SetInAcidPool(false);
                    state.wasInside = false;
                }
                _tracked.Remove(target);
                onAcidBurnEnd?.Invoke();
                if (logDebugTicks)
                {
                    Debug.Log($"[AcidDamageZone] Condicao de acido limpa para {target.name}.");
                }
            }
        }

        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            GameObject rootObj = ResolveTarget(other);
            if (rootObj == null) return;

            SimplePlayerController player = rootObj.GetComponentInParent<SimplePlayerController>() ?? rootObj.GetComponent<SimplePlayerController>();
            if (player != null && player.IsDead) return;

            if (!_tracked.TryGetValue(rootObj, out BurnState state))
            {
                state = new BurnState
                {
                    target = rootObj,
                    damageable = rootObj.GetComponentInParent<IAcidDamageable>() ?? rootObj.GetComponent<IAcidDamageable>(),
                    player = player,
                    wasInside = true,
                    colliderContacts = 1,
                    lingerRemaining = lingerDurationAfterExit,
                    tickTimer = 0f // aplica o primeiro tick imediatamente ao pisar
                };
                _tracked[rootObj] = state;
                if (player != null)
                {
                    player.SetInAcidPool(true);
                    SpawnAcidEntrySplash(rootObj.transform.position + Vector3.up * 0.25f);
                }
                onAcidBurnStart?.Invoke();

                if (logDebugTicks)
                {
                    Debug.Log($"[AcidDamageZone] {rootObj.name} entrou no acido gastrico!");
                }
            }
            else
            {
                bool reentered = state.colliderContacts == 0;
                state.colliderContacts++;
                state.lingerRemaining = lingerDurationAfterExit;
                if (reentered && state.player != null && !state.wasInside)
                {
                    state.wasInside = true;
                    state.player.SetInAcidPool(true);
                    SpawnAcidEntrySplash(rootObj.transform.position + Vector3.up * 0.25f);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            GameObject rootObj = ResolveTarget(other);
            if (rootObj == null) return;

            if (_tracked.TryGetValue(rootObj, out BurnState state))
            {
                state.colliderContacts = Mathf.Max(0, state.colliderContacts - 1);
                if (state.colliderContacts == 0)
                {
                    if (state.wasInside && state.player != null)
                    {
                        state.wasInside = false;
                        state.player.SetInAcidPool(false);
                    }
                    // Garante a duracao total do dano residual a partir do momento que saiu
                    state.lingerRemaining = lingerDurationAfterExit;
                    if (logDebugTicks)
                    {
                        Debug.Log($"[AcidDamageZone] {rootObj.name} saiu do acido — queimadura residual por {lingerDurationAfterExit:F1}s.");
                    }
                }
            }
        }

        private void Update()
        {
            if (_tracked.Count == 0) return;

            float dt = Time.deltaTime;
            _toRemove.Clear();

            foreach (var kvp in _tracked)
            {
                BurnState state = kvp.Value;
                if (state.target == null ||
                    (state.damageable is SimplePlayerController deadPlayer && deadPlayer.IsDead))
                {
                    _toRemove.Add(kvp.Key);
                    continue;
                }

                bool isInside = state.colliderContacts > 0;
                if (!isInside)
                {
                    state.lingerRemaining -= dt;
                    if (state.lingerRemaining <= 0f)
                    {
                        _toRemove.Add(kvp.Key);
                        continue;
                    }
                }
                else
                {
                    state.lingerRemaining = lingerDurationAfterExit;
                }

                state.tickTimer -= dt;
                if (state.tickTimer <= 0f)
                {
                    state.tickTimer += tickInterval;
                    float dps = isInside ? damagePerSecondInside : lingerDamagePerSecond;
                    float tickDamage = dps * tickInterval;
                    ApplyDamage(state, tickDamage, isInside);
                    if (state.damageable is SimplePlayerController)
                    {
                        StomachAbyss.Combat.CombatSfxBank.Play(StomachAbyss.Combat.CombatSfxId.PlayerAcidBurn, isInside ? 0.72f : 0.48f, 0.92f, 1.08f, 0.45f);
                        StomachAbyss.Combat.CombatCameraJuice.TriggerDamageVignette(isInside ? 0.42f : 0.24f, isAcid: true);
                    }
                }
            }

            for (int i = 0; i < _toRemove.Count; i++)
            {
                GameObject key = _toRemove[i];
                _tracked.Remove(key);
                onAcidBurnEnd?.Invoke();
                if (logDebugTicks && key != null)
                {
                    Debug.Log($"[AcidDamageZone] Queimadura acida encerrou em {key.name}.");
                }
            }
        }

        private void ApplyDamage(BurnState state, float amount, bool isInside)
        {
            if (state.player != null)
            {
                state.player.TakeEnvironmentalAcidDamage(amount);
            }
            else
            {
                state.damageable?.TakeAcidDamage(amount);
            }
            onAcidTickDamage?.Invoke(amount);

            if (logDebugTicks)
            {
                string phase = isInside ? "DENTRO" : $"RESIDUAL ({state.lingerRemaining:F1}s)";
                Debug.Log($"[AcidDamageZone] {state.target.name} sofreu {amount:F1} de dano [{phase}].");
            }
        }

        private void SpawnAcidEntrySplash(Vector3 worldPos)
        {
            GameObject fxObj = new GameObject("AcidEntrySplashFX");
            fxObj.transform.position = worldPos;

            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.8f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.72f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.8f, 5.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.30f);
            main.gravityModifier = 1.6f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.28f, 1.0f, 0.14f, 1f),
                new Color(0.84f, 1.0f, 0.42f, 1f)
            );
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 48f;
            shape.radius = 0.35f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var psRenderer = fxObj.GetComponent<ParticleSystemRenderer>();
            if (splashDropletMaterial != null)
            {
                psRenderer.sharedMaterial = splashDropletMaterial;
            }
            else
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                if (sh != null)
                {
                    Material mat = new Material(sh);
                    mat.color = new Color(0.45f, 1.0f, 0.2f, 1f);
                    psRenderer.sharedMaterial = mat;
                }
            }

            ps.Emit(Random.Range(16, 24));
            Destroy(fxObj, 1.5f);
        }

        private GameObject ResolveTarget(Collider other)
        {
            if (!string.IsNullOrEmpty(targetTag))
            {
                if (other.CompareTag(targetTag)) return other.gameObject;
                if (other.transform.root != null && other.transform.root.CompareTag(targetTag))
                {
                    return other.transform.root.gameObject;
                }
                return null;
            }

            IAcidDamageable dmg = other.GetComponentInParent<IAcidDamageable>();
            return dmg != null ? ((Component)dmg).gameObject : null;
        }
    }
}
