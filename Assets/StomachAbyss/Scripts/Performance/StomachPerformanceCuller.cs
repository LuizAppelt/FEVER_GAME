using System.Collections.Generic;
using UnityEngine;

namespace StomachAbyss.Performance
{
    /// <summary>
    /// Gerenciador leve de otimizacao em tempo real para Stomach_Abyss:
    /// 1) Faz distance culling nas 120+ Point Lights da cena (mantem ativas apenas luzes proximas a camera/jogador).
    /// 2) Desativa a Camara Tutorial (00_Cardia_Tutorial_Chamber) quando o jogador esta longe no mapa principal,
    ///    cortando centenas de renderers/triangulos fora de vista sem alterar o visual.
    /// </summary>
    public class StomachPerformanceCuller : MonoBehaviour
    {
        [Header("Culling de Luzes")]
        [Tooltip("Distancia extra alem do alcance da luz para mante-la ligada.")]
        public float lightCullDistance = 85f;
        [Tooltip("Intervalo em segundos entre verificacoes (evita custo todo frame).")]
        public float checkInterval = 0.35f;

        [Header("Culling de Zona (Camara Tutorial)")]
        public GameObject cardiaTutorialRoot;
        [Tooltip("Se player.x > este valor, desativa a Camara Tutorial (Cardia fica em X ~ -896, mapa principal em X ~ -612).")]
        public float cardiaDisableThresholdX = -765f;

        private struct ManagedLight
        {
            public Light light;
            public Transform transform;
            public float cullDistSq;
        }

        private static StomachPerformanceCuller s_instance;

        private readonly List<ManagedLight> _managedLights = new List<ManagedLight>();
        private Transform _focusTransform;
        private float _timer;

        private void Awake()
        {
            s_instance = this;
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        /// <summary>
        /// Garante que a zona de destino esteja ativa imediatamente antes de teleportar/cuspir o jogador.
        /// </summary>
        public static void EnsureZoneActiveForPosition(Vector3 worldPos)
        {
            if (s_instance == null || s_instance.cardiaTutorialRoot == null) return;
            if (worldPos.x <= s_instance.cardiaDisableThresholdX && !s_instance.cardiaTutorialRoot.activeSelf)
            {
                s_instance.cardiaTutorialRoot.SetActive(true);
            }
        }

        private void Start()
        {
            ResolveFocusTransform();
            if (cardiaTutorialRoot == null)
            {
                cardiaTutorialRoot = GameObject.Find("00_Cardia_Tutorial_Chamber");
            }

            // Garante que conexoes no mapa principal (Virgilio do Viaduto e Porta do Osseario)
            // nunca sejam desativadas pelo culling da Camara Tutorial!
            if (cardiaTutorialRoot != null)
            {
                Transform mainMapConnections = cardiaTutorialRoot.transform.Find("06_Ossuary_And_Viaduct_Connections");
                if (mainMapConnections != null)
                {
                    mainMapConnections.SetParent(null, true);
                }
            }

            CacheSceneLights();
            EvaluateCulling();
        }

        private void ResolveFocusTransform()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                _focusTransform = player.transform;
                return;
            }
            if (Camera.main != null)
            {
                _focusTransform = Camera.main.transform;
            }
        }

        private void CacheSceneLights()
        {
            _managedLights.Clear();
            Light[] allLights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allLights.Length; i++)
            {
                Light l = allLights[i];
                if (l == null || l.type == LightType.Directional) continue;
                // Nao interferir na luz do proprio jogador
                if (_focusTransform != null && l.transform.IsChildOf(_focusTransform)) continue;
                if (!l.enabled) continue;

                float effectiveDist = lightCullDistance + Mathf.Clamp(l.range, 5f, 45f);
                _managedLights.Add(new ManagedLight
                {
                    light = l,
                    transform = l.transform,
                    cullDistSq = effectiveDist * effectiveDist
                });
            }
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = Mathf.Max(0.15f, checkInterval);

            if (_focusTransform == null) ResolveFocusTransform();
            EvaluateCulling();
        }

        private void EvaluateCulling()
        {
            if (_focusTransform == null) return;
            Vector3 focusPos = _focusTransform.position;

            // 1. Zone culling da Camara Tutorial (nunca desativa no meio de uma transicao de porta!)
            if (cardiaTutorialRoot != null && !MapaEstomago.DoorTransitionTrigger.IsTransitionRunning)
            {
                bool shouldBeActive = focusPos.x <= cardiaDisableThresholdX;
                if (cardiaTutorialRoot.activeSelf != shouldBeActive)
                {
                    cardiaTutorialRoot.SetActive(shouldBeActive);
                }
            }

            // 2. Distance culling das Point/Spot Lights
            for (int i = 0; i < _managedLights.Count; i++)
            {
                ManagedLight ml = _managedLights[i];
                if (ml.light == null || ml.transform == null) continue;
                if (!ml.light.gameObject.activeInHierarchy) continue;

                float distSq = (ml.transform.position - focusPos).sqrMagnitude;
                bool shouldEnable = distSq <= ml.cullDistSq;
                if (ml.light.enabled != shouldEnable)
                {
                    ml.light.enabled = shouldEnable;
                }
            }
        }
    }
}
