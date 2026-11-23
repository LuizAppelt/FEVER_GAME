using System;
using System.Collections.Generic;
using MapaEstomago;
using StomachAbyss.Combat;
using UnityEngine;

namespace StomachAbyss.PillarSeals
{
    /// <summary>
    /// Pilar Ocular selador do portão GateRig (1).
    /// Monólito suspenso preso por 3 correntes ao chão, com múltiplos olhos incrustados
    /// que acompanham o jogador em tempo real. Não ataca; ao ter o HP zerado,
    /// suas correntes se rompem e o pilar desaba, liberando um selo do portão.
    /// </summary>
    public class EyePillarController : MonoBehaviour, IAcidDamageable
    {
        [Header("Identificação & Vida")]
        public string pillarLabel = "Pilar Ocular";
        public float maxHealth = 200f;
        public float currentHealth = 200f;

        [Header("Referências Visuais")]
        public Transform monolithRoot;
        public List<Transform> eyeballs = new List<Transform>();
        public List<Transform> chains = new List<Transform>();
        public Light auraLight;
        public Collider hitCollider;

        [Header("Animação de Levitação & Olhos")]
        public float bobAmplitude = 0.22f;
        public float bobFrequency = 1.35f;
        public float swayAngle = 2.2f;
        public float eyeTrackSpeed = 8.5f;
        public float phaseOffset = 0f;

        [Header("Efeitos de Dano")]
        public float hitShakeDuration = 0.20f;
        public float hitShakeIntensity = 0.14f;
        public float hitFlashDuration = 0.14f;

        public bool IsDestroyed { get; private set; }
        public event Action<EyePillarController> OnPillarDestroyed;

        private Transform _playerTarget;
        private Vector3 _monolithBaseLocalPos;
        private Quaternion _monolithBaseLocalRot;
        private Vector3 _monolithBaseScale;
        private float _baseAuraIntensity;
        private float _hitShakeTimer;
        private float _hitFlashTimer;
        private float _collapseTimer = -1f;

        private Renderer[] _monolithRenderers;
        private Color[] _origColors;
        private MaterialPropertyBlock _mpb;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            currentHealth = maxHealth;
            IsDestroyed = false;
            _mpb = new MaterialPropertyBlock();

            if (monolithRoot != null)
            {
                _monolithBaseLocalPos = monolithRoot.localPosition;
                _monolithBaseLocalRot = monolithRoot.localRotation;
                _monolithBaseScale = monolithRoot.localScale;
                _monolithRenderers = monolithRoot.GetComponentsInChildren<Renderer>(true);
                if (_monolithRenderers != null && _monolithRenderers.Length > 0)
                {
                    _origColors = new Color[_monolithRenderers.Length];
                    for (int i = 0; i < _monolithRenderers.Length; i++)
                    {
                        Material mat = _monolithRenderers[i] != null ? _monolithRenderers[i].sharedMaterial : null;
                        if (mat != null)
                        {
                            if (mat.HasProperty(BaseColorId)) _origColors[i] = mat.GetColor(BaseColorId);
                            else if (mat.HasProperty(ColorId)) _origColors[i] = mat.GetColor(ColorId);
                            else _origColors[i] = Color.white;
                        }
                    }
                }
            }

            if (auraLight != null)
            {
                _baseAuraIntensity = auraLight.intensity;
            }

            if (hitCollider == null)
            {
                hitCollider = GetComponent<Collider>();
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (IsDestroyed)
            {
                TickCollapseAnimation(dt);
                return;
            }

            ResolvePlayerTarget();
            TickMonolithHoverAndShake(dt);
            TickEyeballTracking(dt);
            TickHitFlash(dt);
            TickAuraPulse();
        }

        public void TakeAcidDamage(float amount)
        {
            if (IsDestroyed || amount <= 0f) return;

            currentHealth = Mathf.Max(0f, currentHealth - amount);
            _hitShakeTimer = hitShakeDuration;
            _hitFlashTimer = hitFlashDuration;

            Vector3 hitPos = monolithRoot != null ? monolithRoot.position : transform.position + Vector3.up * 2.2f;
            Vector3 awayDir = _playerTarget != null
                ? (hitPos - _playerTarget.position).normalized
                : Vector3.up;

            BloodSplatterSystem.SpawnBloodBurst(hitPos, awayDir, 1.05f, null, false);
            CombatSfxBank.PlayImpact(false, hitPos, 0.90f);
            CombatHitstopManager.TriggerHitstop(0.036f, 0.04f, false);

            if (IsometricCameraFollow.Instance != null)
            {
                IsometricCameraFollow.Instance.TriggerShake(0.11f, 0.14f);
            }

            if (currentHealth <= 0f)
            {
                DestroyPillar();
            }
        }

        private void DestroyPillar()
        {
            if (IsDestroyed) return;
            IsDestroyed = true;
            _collapseTimer = 0f;

            if (hitCollider != null)
            {
                hitCollider.enabled = false;
            }

            // Solta e derruba as 3 correntes no chão
            for (int i = 0; i < chains.Count; i++)
            {
                Transform c = chains[i];
                if (c == null) continue;
                // Inclina a corrente para baixo como se tivesse arrebentado do pilar
                Vector3 euler = c.localEulerAngles;
                c.localEulerAngles = new Vector3(euler.x + 38f, euler.y, euler.z);
                c.localPosition += new Vector3(0f, -0.55f, 0f);
            }

            Vector3 burstPos = monolithRoot != null ? monolithRoot.position : transform.position + Vector3.up * 2.2f;
            BloodSplatterSystem.SpawnBloodBurst(burstPos, Vector3.up, 1.85f, null, true);
            CombatSfxBank.Play(CombatSfxId.HitWeakspotCrit, 0.95f, 0.72f, 0.85f, 0f);
            CombatHitstopManager.TriggerHitstop(0.065f, 0.08f, true);

            if (IsometricCameraFollow.Instance != null)
            {
                IsometricCameraFollow.Instance.TriggerShake(0.32f, 0.38f);
            }

            OnPillarDestroyed?.Invoke(this);
        }

        private void ResolvePlayerTarget()
        {
            if (_playerTarget != null) return;
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null)
            {
                _playerTarget = p.transform;
            }
        }

        private void TickMonolithHoverAndShake(float dt)
        {
            if (monolithRoot == null) return;

            float t = Time.time * bobFrequency + phaseOffset;
            float bobY = Mathf.Sin(t) * bobAmplitude;
            float swayX = Mathf.Sin(t * 0.7f) * swayAngle;
            float swayZ = Mathf.Cos(t * 0.55f) * swayAngle;

            Vector3 shakeOffset = Vector3.zero;
            if (_hitShakeTimer > 0f)
            {
                _hitShakeTimer = Mathf.Max(0f, _hitShakeTimer - dt);
                float ratio = _hitShakeTimer / Mathf.Max(0.01f, hitShakeDuration);
                shakeOffset = UnityEngine.Random.insideUnitSphere * (hitShakeIntensity * ratio);
            }

            monolithRoot.localPosition = _monolithBaseLocalPos + new Vector3(0f, bobY, 0f) + shakeOffset;
            monolithRoot.localRotation = _monolithBaseLocalRot * Quaternion.Euler(swayX, 0f, swayZ);
        }

        private void TickEyeballTracking(float dt)
        {
            if (eyeballs == null || eyeballs.Count == 0) return;

            Vector3 targetPos;
            if (_playerTarget != null)
            {
                targetPos = _playerTarget.position + Vector3.up * 1.1f;
            }
            else if (Camera.main != null)
            {
                targetPos = Camera.main.transform.position;
            }
            else
            {
                return;
            }

            float lerpT = 1f - Mathf.Exp(-eyeTrackSpeed * dt);
            for (int i = 0; i < eyeballs.Count; i++)
            {
                Transform eye = eyeballs[i];
                if (eye == null) continue;

                Vector3 dir = targetPos - eye.position;
                if (dir.sqrMagnitude < 0.001f) continue;

                Quaternion desiredWorldRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                eye.rotation = Quaternion.Slerp(eye.rotation, desiredWorldRot, lerpT);
            }
        }

        private void TickHitFlash(float dt)
        {
            if (_monolithRenderers == null || _monolithRenderers.Length == 0) return;

            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer = Mathf.Max(0f, _hitFlashTimer - dt);
                float ratio = _hitFlashTimer / Mathf.Max(0.01f, hitFlashDuration);
                Color flashCol = new Color(1.6f, 0.25f, 0.20f, 1f);

                for (int i = 0; i < _monolithRenderers.Length; i++)
                {
                    Renderer r = _monolithRenderers[i];
                    if (r == null) continue;
                    Color baseC = _origColors != null && i < _origColors.Length ? _origColors[i] : Color.white;
                    Color blended = Color.Lerp(baseC, flashCol, ratio);
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetColor(BaseColorId, blended);
                    _mpb.SetColor(ColorId, blended);
                    r.SetPropertyBlock(_mpb);
                }
            }
            else
            {
                for (int i = 0; i < _monolithRenderers.Length; i++)
                {
                    Renderer r = _monolithRenderers[i];
                    if (r == null) continue;
                    r.GetPropertyBlock(_mpb);
                    _mpb.Clear();
                    r.SetPropertyBlock(_mpb);
                }
            }
        }

        private void TickAuraPulse()
        {
            if (auraLight == null) return;
            float pulse = 0.85f + 0.25f * Mathf.Sin(Time.time * 2.2f + phaseOffset);
            auraLight.intensity = _baseAuraIntensity * pulse;
        }

        private void TickCollapseAnimation(float dt)
        {
            if (_collapseTimer < 0f) return;

            _collapseTimer += dt;
            float duration = 0.75f;
            float t = Mathf.Clamp01(_collapseTimer / duration);

            if (monolithRoot != null)
            {
                float dropY = Mathf.Lerp(0f, -2.4f, t * t);
                monolithRoot.localPosition = _monolithBaseLocalPos + new Vector3(0f, dropY, 0f);
                monolithRoot.localRotation = _monolithBaseLocalRot * Quaternion.Euler(t * 28f, t * 18f, t * 22f);
                monolithRoot.localScale = Vector3.Lerp(_monolithBaseScale, _monolithBaseScale * 0.15f, t * t);
                if (t >= 1f)
                {
                    monolithRoot.gameObject.SetActive(false);
                }
            }

            if (auraLight != null)
            {
                auraLight.intensity = Mathf.Lerp(_baseAuraIntensity, 0f, t);
                if (t >= 1f) auraLight.enabled = false;
            }

            if (t >= 1f)
            {
                _collapseTimer = -1f;
            }
        }
    }
}
