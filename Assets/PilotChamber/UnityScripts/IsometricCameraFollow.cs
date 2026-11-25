using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MapaEstomago
{
    /// <summary>
    /// Camera isometrica fixa estilo Hades / Path of Exile 2, com Trava Suave de Parede:
    /// detecta automaticamente em qual camara do estomago o jogador esta e impede o pivo
    /// da camera de ultrapassar as bordas seguras, evitando mostrar fora do mapa.
    /// </summary>
    public class IsometricCameraFollow : MonoBehaviour
    {
        [Serializable]
        public struct ChamberCameraBounds
        {
            public string name;
            public Vector2 centerXZ;
            [Tooltip("Raio seguro em X/Z para o pivo da camera (menor que a parede para nunca vazar fora do mapa).")]
            public Vector2 safeRadiusXZ;
        }

        [Header("Alvo (Opcional)")]
        public Transform target;

        [Header("Enquadramento Isometrico Fixo (Hades / PoE2)")]
        [Tooltip("Angulo de inclinacao vertical (42 a 48 graus funciona muito bem).")]
        [Range(25f, 65f)]
        public float pitchAngle = 44f;

        [Tooltip("Rotacao horizontal fixa.")]
        public float yawAngle = 35f;

        [Tooltip("Distancia da camera ate o pivo.")]
        public float distance = 22f;

        [Tooltip("Suavizacao do movimento da camera.")]
        public float followSmoothTime = 0.14f;

        [Tooltip("FOV baixo (32-40) da o aspecto isometrico sem distorcer a perspectiva.")]
        [Range(20f, 55f)]
        public float fieldOfView = 38f;

        [Header("Zoom Dinamico (Catedral / Boss)")]
        [Tooltip("Afasta suavemente a camera quando o jogador entra na catedral do boss.")]
        public bool enableCathedralAutoZoom = true;
        public bool autoSyncCathedralZoomArea = true;
        public float cathedralZoomOutDistance = 29.5f;
        public float cathedralZoomOutFov = 42.5f;
        public float cathedralZoomSmoothSpeed = 2.6f;
        public Bounds cathedralZoomArea = new Bounds(new Vector3(100.4f, 6.0f, 169.5f), new Vector3(32f, 24f, 44f));

        [Header("Trava de Camera nas Paredes (Evita Ver Fora do Mapa)")]
        [Tooltip("Mantem o pivo da camera dentro dos limites seguros da camara atual do estomago.")]
        public bool clampToActiveChamber = true;

        [Tooltip("Permite girar/dar zoom na camera livre quando nao ha player (desative para travar totalmente).")]
        public bool allowFreeRotationAndZoom = false;

        [Tooltip("Margem extra aplicada quando a distancia da camera aumenta/diminui.")]
        public float marginPadding = 0f;

        [Tooltip("Lista de camaras detectadas automaticamente (ou editaveis manualmente).")]
        public List<ChamberCameraBounds> chamberBounds = new List<ChamberCameraBounds>();

        [Header("Inspecao Livre Sem Player")]
        public float panSpeed = 16f;
        public float rotateSpeed = 65f;
        public float zoomSpeed = 3.5f;

        [Header("Limites Globais (Fallback se nao houver camaras)")]
        public bool clampMovement = false;
        public Vector2 xBounds = new Vector2(-60f, 60f);
        public Vector2 zBounds = new Vector2(-60f, 60f);

        private Camera _cam;
        private Vector3 _velocity;
        private Vector3 _freePivot = Vector3.zero;
        private float _runtimeDistance;
        private float _runtimeFov;
        private float _runtimePitch;
        private float _runtimeYaw;
        private Vector3 _runtimePivotOffset = Vector3.zero;
        private bool _bossZoomOverrideActive;
        private float _bossZoomOverrideDistance = -1f;
        private float _shakeIntensity;
        private float _shakeTimer;
        private float _shakeDuration;
        private BossTenia.TeniaArenaBounds _cachedArenaBounds;
        private readonly List<CameraZoneTrigger> _activeZones = new List<CameraZoneTrigger>();

        public static IsometricCameraFollow Instance { get; private set; }

        public Vector3 FreePivot => _freePivot;
        public bool lockInput { get; set; }

        public void TriggerShake(float intensity, float duration = 0.14f)
        {
            if (intensity <= 0f || duration <= 0f) return;
            if (intensity >= _shakeIntensity || _shakeTimer <= 0f)
            {
                _shakeIntensity = intensity;
                _shakeDuration = duration;
                _shakeTimer = duration;
            }
        }

        public void SetBossZoomActive(bool active, float customDistance = -1f)
        {
            _bossZoomOverrideActive = active;
            _bossZoomOverrideDistance = customDistance;
        }

        public void RegisterZone(CameraZoneTrigger zone)
        {
            if (zone != null && !_activeZones.Contains(zone))
            {
                _activeZones.Add(zone);
                _activeZones.Sort((a, b) => b.priority.CompareTo(a.priority));
            }
        }

        public void UnregisterZone(CameraZoneTrigger zone)
        {
            if (zone != null)
            {
                _activeZones.Remove(zone);
            }
        }

        private void Awake()
        {
            Instance = this;
            _cam = GetComponent<Camera>();
            _runtimePitch = pitchAngle;
            _runtimeYaw = yawAngle;
            _runtimeDistance = distance;
            _runtimeFov = fieldOfView;
            _runtimePivotOffset = Vector3.zero;
            if (_cam != null)
            {
                _cam.fieldOfView = _runtimeFov;
            }

            EnsureTarget();
            SyncCathedralZoomArea();

            if (chamberBounds == null || chamberBounds.Count == 0)
            {
                AutoDetectChambers();
            }

            SnapToTargetImmediate();
        }

        private void Start()
        {
            if (Instance == null) Instance = this;
            EnsureTarget();
            SyncCathedralZoomArea();
            SnapToTargetImmediate();
        }

        [ContextMenu("Sincronizar Zoom Area com Catedral")]
        public void SyncCathedralZoomArea()
        {
            if (_cachedArenaBounds == null)
            {
                _cachedArenaBounds = FindAnyObjectByType<BossTenia.TeniaArenaBounds>();
            }
            if (!autoSyncCathedralZoomArea || _cachedArenaBounds == null) return;

            _cachedArenaBounds.SyncWithCathedralAnchorIfNeeded();
            Vector3 c = _cachedArenaBounds.arenaCenter;
            cathedralZoomArea = new Bounds(new Vector3(c.x, c.y + 3.8f, c.z), new Vector3(38f, 24f, 46f));
        }

        public void EnsureTarget()
        {
            if (target != null) return;
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) p = GameObject.Find("Test_Player (1)");
            if (p == null) p = GameObject.Find("Test_Player");
            if (p != null)
            {
                target = p.transform;
            }
        }

        public void SnapToTargetImmediate()
        {
            _runtimePitch = pitchAngle;
            _runtimeYaw = yawAngle;
            _runtimeDistance = distance;
            _runtimeFov = fieldOfView;
            _runtimePivotOffset = Vector3.zero;

            Quaternion rot = Quaternion.Euler(pitchAngle, yawAngle, 0f);
            if (target != null)
            {
                _freePivot = target.position;
                Vector3 clamped = ClampPivotToChamber(_freePivot);
                transform.position = clamped - (rot * Vector3.forward) * distance;
                transform.rotation = rot;
                _velocity = Vector3.zero;
                return;
            }

            Vector3 inferredPivot = transform.position + (rot * Vector3.forward) * distance;
            inferredPivot.y = 0f;
            if (inferredPivot.sqrMagnitude > 0.01f)
            {
                _freePivot = inferredPivot;
            }
        }

        [ContextMenu("Detectar Camaras do Estomago Automaticamente")]
        public void AutoDetectChambers()
        {
            if (chamberBounds == null) chamberBounds = new List<ChamberCameraBounds>();
            chamberBounds.Clear();

            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (Transform t in allTransforms)
            {
                if (t == null || t.parent != null) continue;

                if (t.name.StartsWith("Stomach_Pilot_Chamber"))
                {
                    chamberBounds.Add(new ChamberCameraBounds
                    {
                        name = t.name,
                        centerXZ = new Vector2(t.position.x, t.position.z),
                        // Camara Piloto tem raio ~13.8x11.4m -> pivo seguro em 3.6x2.8m mantem a cupula fechando 100% das quinas da tela
                        safeRadiusXZ = new Vector2(3.6f * t.lossyScale.x, 2.8f * t.lossyScale.z)
                    });
                }
                else if (t.name.StartsWith("Stomach_Main_Dome"))
                {
                    chamberBounds.Add(new ChamberCameraBounds
                    {
                        name = t.name,
                        centerXZ = new Vector2(t.position.x, t.position.z),
                        // Cupula Principal tem raio ~28.6x23.2m -> pivo seguro em 18.5x14.0m
                        safeRadiusXZ = new Vector2(18.5f * t.lossyScale.x, 14.0f * t.lossyScale.z)
                    });
                }
            }
        }

        public void SetFreePivot(Vector3 worldPos, bool snapImmediate = false)
        {
            _freePivot = worldPos;
            if (snapImmediate)
            {
                _velocity = Vector3.zero;
                Vector3 clamped = ClampPivotToChamber(_freePivot);
                Quaternion rot = Quaternion.Euler(pitchAngle, yawAngle, 0f);
                transform.position = clamped - (rot * Vector3.forward) * distance;
                transform.rotation = rot;
            }
        }

        /// <summary>
        /// Forca snap imediato da posicao e rotacao da camera no alvo (usado em teleportes para evitar pan pelo mapa).
        /// </summary>
        public void SnapToTarget()
        {
            _velocity = Vector3.zero;
            Vector3 rawPivot = target != null ? target.position : _freePivot;
            Vector3 pivot = ClampPivotToChamber(rawPivot);
            float p = _runtimePitch > 0.1f ? _runtimePitch : pitchAngle;
            float y = _runtimeYaw;
            float d = _runtimeDistance > 0.1f ? _runtimeDistance : distance;
            Quaternion rot = Quaternion.Euler(p, y, 0f);
            transform.position = (pivot + _runtimePivotOffset) - (rot * Vector3.forward) * d;
            transform.rotation = rot;
        }

        private Vector3 ClampPivotToChamber(Vector3 rawPivot)
        {
            if (!clampToActiveChamber || chamberBounds == null || chamberBounds.Count == 0)
            {
                return rawPivot;
            }

            // Encontra a camara mais proxima do jogador/pivo atual
            int bestIdx = -1;
            float bestDistSq = float.MaxValue;
            Vector2 pXZ = new Vector2(rawPivot.x, rawPivot.z);

            for (int i = 0; i < chamberBounds.Count; i++)
            {
                float dSq = (pXZ - chamberBounds[i].centerXZ).sqrMagnitude;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) return rawPivot;

            ChamberCameraBounds cb = chamberBounds[bestIdx];
            float rx = Mathf.Max(0.5f, cb.safeRadiusXZ.x - marginPadding);
            float rz = Mathf.Max(0.5f, cb.safeRadiusXZ.y - marginPadding);

            Vector2 offset = pXZ - cb.centerXZ;
            float nx = offset.x / rx;
            float nz = offset.y / rz;
            float normLen = Mathf.Sqrt(nx * nx + nz * nz);

            if (normLen > 1.0f)
            {
                // Trava eliptica suave: impede o pivo de sair da area segura sem trancos
                float scale = 1.0f / normLen;
                Vector2 clampedXZ = cb.centerXZ + new Vector2(offset.x * scale, offset.y * scale);
                return new Vector3(clampedXZ.x, rawPivot.y, clampedXZ.y);
            }

            return rawPivot;
        }

        private void Update()
        {
            if (target != null || lockInput) return;

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                Vector2 move = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;

                if (move.sqrMagnitude > 0.001f)
                {
                    move.Normalize();
                    Quaternion yawRot = Quaternion.Euler(0f, yawAngle, 0f);
                    Vector3 right = yawRot * Vector3.right;
                    Vector3 fwd = yawRot * Vector3.forward;
                    _freePivot += (right * move.x + fwd * move.y) * (panSpeed * Time.deltaTime);
                    if (clampMovement)
                    {
                        _freePivot.x = Mathf.Clamp(_freePivot.x, xBounds.x, xBounds.y);
                        _freePivot.z = Mathf.Clamp(_freePivot.z, zBounds.x, zBounds.y);
                    }
                    _freePivot = ClampPivotToChamber(_freePivot);
                }

                if (allowFreeRotationAndZoom)
                {
                    if (kb.qKey.isPressed) yawAngle -= rotateSpeed * Time.deltaTime;
                    if (kb.eKey.isPressed) yawAngle += rotateSpeed * Time.deltaTime;
                }
            }

            if (allowFreeRotationAndZoom)
            {
                Mouse mouse = Mouse.current;
                if (mouse != null)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomSpeed, 6f, 65f);
                    }
                }
            }
#endif
        }

        private void LateUpdate()
        {
            EnsureTarget();

            Vector3 rawPivot = target != null ? target.position : _freePivot;
            Vector3 pivot = ClampPivotToChamber(rawPivot);

            // Limpa zonas nulas ou desativadas
            for (int i = _activeZones.Count - 1; i >= 0; i--)
            {
                if (_activeZones[i] == null || !_activeZones[i].isActiveAndEnabled)
                {
                    _activeZones.RemoveAt(i);
                }
            }

            CameraZoneTrigger activeZone = _activeZones.Count > 0 ? _activeZones[0] : null;

            float targetPitch = pitchAngle;
            float targetYaw = yawAngle;
            float targetDistance = distance;
            float targetFov = fieldOfView;
            Vector3 targetPivotOffset = Vector3.zero;
            float blendSpeed = cathedralZoomSmoothSpeed;

            if (activeZone != null)
            {
                if (activeZone.overridePitch) targetPitch = activeZone.targetPitch;
                if (activeZone.overrideYaw)
                {
                    if (activeZone.dynamicLookAtTarget != null)
                    {
                        Vector3 toTarget = activeZone.dynamicLookAtTarget.position - rawPivot;
                        toTarget.y = 0f;
                        if (toTarget.sqrMagnitude > 0.01f)
                        {
                            targetYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                        }
                    }
                    else
                    {
                        targetYaw = activeZone.targetYaw;
                    }
                }
                if (activeZone.overrideDistance) targetDistance = activeZone.targetDistance;
                if (activeZone.overrideFov) targetFov = activeZone.targetFov;
                targetPivotOffset = activeZone.pivotOffset;
                blendSpeed = activeZone.transitionSpeed;
            }
            else
            {
                bool insideCathedral = enableCathedralAutoZoom && (
                    cathedralZoomArea.Contains(rawPivot) ||
                    (_cachedArenaBounds != null && _cachedArenaBounds.IsPlayerInsideCathedral(rawPivot))
                );
                bool useZoomOut = _bossZoomOverrideActive || insideCathedral;
                targetDistance = useZoomOut
                    ? (_bossZoomOverrideActive && _bossZoomOverrideDistance > 0f ? _bossZoomOverrideDistance : Mathf.Max(distance, cathedralZoomOutDistance))
                    : distance;
                targetFov = useZoomOut ? Mathf.Max(fieldOfView, cathedralZoomOutFov) : fieldOfView;
            }

            if (Application.isPlaying)
            {
                HandleZoomHotkeys(Time.unscaledDeltaTime);
                targetDistance *= StomachAbyss.UI.GameSettingsManager.CameraDistanceScale;
                float dt = Time.unscaledDeltaTime;
                float blend = 1f - Mathf.Exp(-Mathf.Max(0.5f, blendSpeed) * dt);
                if (_runtimeDistance <= 0.1f) _runtimeDistance = targetDistance;
                if (_runtimeFov <= 1f) _runtimeFov = fieldOfView;
                if (_runtimePitch <= 0.1f) _runtimePitch = pitchAngle;

                _runtimePitch = Mathf.Lerp(_runtimePitch, targetPitch, blend);
                _runtimeYaw = Mathf.LerpAngle(_runtimeYaw, targetYaw, blend);
                _runtimeDistance = Mathf.Lerp(_runtimeDistance, targetDistance, blend);
                _runtimeFov = Mathf.Lerp(_runtimeFov, targetFov, blend);
                _runtimePivotOffset = Vector3.Lerp(_runtimePivotOffset, targetPivotOffset, blend);
            }
            else
            {
                _runtimePitch = targetPitch;
                _runtimeYaw = targetYaw;
                _runtimeDistance = targetDistance;
                _runtimeFov = targetFov;
                _runtimePivotOffset = targetPivotOffset;
            }

            if (_cam != null && Mathf.Abs(_cam.fieldOfView - _runtimeFov) > 0.05f)
            {
                _cam.fieldOfView = _runtimeFov;
            }

            Quaternion rot = Quaternion.Euler(_runtimePitch, _runtimeYaw, 0f);
            Vector3 desiredPos = (pivot + _runtimePivotOffset) - (rot * Vector3.forward) * _runtimeDistance;

            if (Application.isPlaying)
            {
                transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _velocity, followSmoothTime);
                if (_shakeTimer > 0f)
                {
                    _shakeTimer = Mathf.Max(0f, _shakeTimer - Time.unscaledDeltaTime);
                    float ratio = _shakeDuration > 0f ? (_shakeTimer / _shakeDuration) : 0f;
                    float amp = _shakeIntensity * ratio * ratio;
                    Vector3 shakeOffset = (transform.right * UnityEngine.Random.Range(-1f, 1f) +
                                           transform.up * UnityEngine.Random.Range(-1f, 1f)) * amp;
                    transform.position += shakeOffset;
                }
            }
            else
            {
                transform.position = desiredPos;
            }

            transform.rotation = rot;
        }

        private void HandleZoomHotkeys(float dt)
        {
            if (lockInput || Time.timeScale <= 0f) return;

            bool zoomIn = false;
            bool zoomOut = false;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                zoomIn = kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed;
                zoomOut = kb.minusKey.isPressed || kb.numpadMinusKey.isPressed;
            }
#else
            zoomIn = Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.Plus) || Input.GetKey(KeyCode.KeypadPlus);
            zoomOut = Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus);
#endif

            if (zoomIn == zoomOut) return;

            float delta = (zoomOut ? 1f : -1f) * 0.45f * dt;
            float next = Mathf.Clamp(StomachAbyss.UI.GameSettingsManager.CameraDistanceScale + delta, 0.70f, 1.35f);
            if (!Mathf.Approximately(next, StomachAbyss.UI.GameSettingsManager.CameraDistanceScale))
            {
                StomachAbyss.UI.GameSettingsManager.SetCameraDistanceScale(next);
            }
        }
    }
}
