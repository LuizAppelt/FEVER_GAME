using System;
using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Volume de Gatilho para Zonas de Camera Dinamicas (ex: Viaduto da Catedral, Mirantes, Arenas).
    /// Quando o jogador entra neste volume, a IsometricCameraFollow transiciona suavemente
    /// para o enquadramento configurado aqui, e retorna ao normal ao sair.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CameraZoneTrigger : MonoBehaviour
    {
        [Header("Identificacao")]
        [Tooltip("Nome descritivo da zona para organizacao e depuracao.")]
        public string zoneName = "Viaduto_Cathedral_Vista";

        [Tooltip("Zonas com maior prioridade sobrepoe zonas menores se houver interseccao.")]
        public int priority = 10;

        [Header("Overrides de Enquadramento")]
        public bool overridePitch = true;
        [Range(5f, 65f)]
        [Tooltip("Inclinacao vertical da camera (valores baixos ~10-25 dao visão cinematografica monumental).")]
        public float targetPitch = 14f;

        public bool overrideYaw = true;
        [Tooltip("Rotacao horizontal fixa (ex: alinhada ao eixo da ponte).")]
        public float targetYaw = 353f;

        [Tooltip("Opcional: Se assinalado, o Yaw apontara sempre dinamicamente para este Transform (ex: Portal da Catedral).")]
        public Transform dynamicLookAtTarget;

        public bool overrideDistance = true;
        [Tooltip("Distancia da camera ate o pivo dentro desta zona.")]
        public float targetDistance = 20f;

        public bool overrideFov = true;
        [Range(20f, 65f)]
        [Tooltip("Campo de visao dentro desta zona.")]
        public float targetFov = 46f;

        [Tooltip("Deslocamento do ponto de foco (ex: elevar Y para mostrar a fachada alta da catedral).")]
        public Vector3 pivotOffset = new Vector3(0f, 3.5f, 0f);

        [Header("Transicao Suave")]
        [Tooltip("Velocidade da transicao ao entrar e sair da zona (valores entre 1.5 e 3.0 dao aspecto cinematografico).")]
        public float transitionSpeed = 2.2f;

        [Header("Deteccao de Jogador")]
        [Tooltip("Ativa checagem de Bounds por frame caso o jogador use CharacterController sem disparo de trigger fisico.")]
        public bool autoCheckBounds = true;

        private Collider _col;
        private bool _isPlayerInside;
        private Transform _cachedPlayerTransform;

        public bool IsPlayerInside => _isPlayerInside;

        private void Awake()
        {
            _col = GetComponent<Collider>();
            if (_col != null && !_col.isTrigger)
            {
                _col.isTrigger = true;
            }
        }

        private void Start()
        {
            FindPlayerIfNeeded();
        }

        private void Update()
        {
            if (!autoCheckBounds) return;

            FindPlayerIfNeeded();
            if (_cachedPlayerTransform == null || _col == null) return;

            bool contains = _col.bounds.Contains(_cachedPlayerTransform.position);
            if (contains && !_isPlayerInside)
            {
                EnterZone();
            }
            else if (!contains && _isPlayerInside)
            {
                ExitZone();
            }
        }

        private void FindPlayerIfNeeded()
        {
            if (_cachedPlayerTransform != null) return;
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) p = GameObject.Find("Test_Player (1)") ?? GameObject.Find("Test_Player");
            if (p != null) _cachedPlayerTransform = p.transform;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayerCollider(other)) return;
            EnterZone();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayerCollider(other)) return;
            ExitZone();
        }

        private bool IsPlayerCollider(Collider other)
        {
            if (other == null) return false;
            if (other.CompareTag("Player")) return true;
            if (other.name.StartsWith("Test_Player")) return true;
            return other.GetComponent<SimplePlayerController>() != null;
        }

        private void EnterZone()
        {
            if (_isPlayerInside) return;
            _isPlayerInside = true;

            var cam = IsometricCameraFollow.Instance;
            if (cam == null) cam = FindFirstObjectByType<IsometricCameraFollow>();
            if (cam != null)
            {
                cam.RegisterZone(this);
            }
        }

        private void ExitZone()
        {
            if (!_isPlayerInside) return;
            _isPlayerInside = false;

            var cam = IsometricCameraFollow.Instance;
            if (cam == null) cam = FindFirstObjectByType<IsometricCameraFollow>();
            if (cam != null)
            {
                cam.UnregisterZone(this);
            }
        }

        private void OnDisable()
        {
            if (_isPlayerInside)
            {
                ExitZone();
            }
        }

        private void OnDrawGizmos()
        {
            Collider c = GetComponent<Collider>();
            if (c == null) return;

            Gizmos.color = _isPlayerInside ? new Color(0.2f, 1f, 0.4f, 0.35f) : new Color(0.1f, 0.7f, 1f, 0.25f);
            Gizmos.DrawCube(c.bounds.center, c.bounds.size);

            Gizmos.color = _isPlayerInside ? Color.green : new Color(0.1f, 0.7f, 1f, 0.8f);
            Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
        }
    }
}
