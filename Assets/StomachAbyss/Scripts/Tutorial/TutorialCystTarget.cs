using System.Collections.Generic;
using MapaEstomago;
using StomachAbyss.Combat;
using UnityEngine;

namespace StomachAbyss.Tutorial
{
    public enum TutorialTargetKind
    {
        MeleeNode,
        RangedSeal
    }

    /// <summary>
    /// Interactive visceral tutorial cyst/gland target that reacts to melee or boomerang hits
    /// and notifies a linked TutorialGateBarrier upon destruction.
    /// </summary>
    public class TutorialCystTarget : MonoBehaviour, IAcidDamageable
    {
        public TutorialTargetKind kind = TutorialTargetKind.MeleeNode;
        public float maxHealth = 70f;
        public TutorialGateBarrier linkedBarrier;
        public Transform visualRoot;
        public Light pulseLight;
        public Color baseGlowColor = new Color(1.0f, 0.35f, 0.15f);

        public bool IsDestroyed { get; private set; }

        private static readonly int PropHitFlash = Shader.PropertyToID("_HitFlash");
        private static readonly int PropHitFlashColor = Shader.PropertyToID("_HitFlashColor");
        private static readonly int PropEmissionColor = Shader.PropertyToID("_EmissionColor");

        private static Texture2D s_whiteTex;
        private static GUIStyle s_hintStyle;

        private float _currentHealth;
        private float _hitFlashTimer;
        private float _hitPunchTimer;
        private float _phaseOffset;
        private float _baseLightIntensity = 2.0f;
        private Vector3 _baseVisualScale = Vector3.one;
        private Transform _animTarget;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Color> _baseEmissions = new List<Color>();
        private readonly List<bool> _hasHitFlashProp = new List<bool>();
        private readonly List<bool> _hasEmissionProp = new List<bool>();
        private MaterialPropertyBlock _mpb;

        private Transform _playerTransform;

        private void Awake()
        {
            _currentHealth = Mathf.Max(1f, maxHealth);
            _phaseOffset = Random.Range(0f, Mathf.PI * 2f);

            _animTarget = visualRoot != null ? visualRoot : transform;
            _baseVisualScale = _animTarget.localScale;

            if (pulseLight != null)
            {
                _baseLightIntensity = pulseLight.intensity;
                pulseLight.color = baseGlowColor;
            }

            CacheRenderers();
        }

        private void Update()
        {
            if (IsDestroyed) return;

            float dt = Time.deltaTime;
            float t = Time.time + _phaseOffset;

            float pulse = Mathf.Sin(t * 2.8f);
            float scaleWave = 1f + pulse * 0.055f;

            float punchBoost = 0f;
            if (_hitPunchTimer > 0f)
            {
                _hitPunchTimer = Mathf.Max(0f, _hitPunchTimer - dt);
                float p = _hitPunchTimer / 0.18f;
                punchBoost = Mathf.Sin(p * Mathf.PI) * 0.24f;
            }

            if (_animTarget != null)
            {
                float xz = scaleWave + punchBoost;
                float y = (1f - pulse * 0.035f) - punchBoost * 0.45f;
                _animTarget.localScale = new Vector3(
                    _baseVisualScale.x * xz,
                    _baseVisualScale.y * Mathf.Max(0.45f, y),
                    _baseVisualScale.z * xz);
            }

            if (pulseLight != null)
            {
                float flashBoost = _hitFlashTimer > 0f ? (_hitFlashTimer / 0.16f) * 1.4f : 0f;
                pulseLight.intensity = _baseLightIntensity * (0.82f + 0.22f * pulse) + flashBoost;
            }

            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer = Mathf.Max(0f, _hitFlashTimer - dt);
                ApplyHitFlash(_hitFlashTimer / 0.16f);
            }
        }

        public void TakeAcidDamage(float amount)
        {
            if (IsDestroyed || amount <= 0f) return;

            BloodSplatterSystem.MarkEntityHandledInContext();

            _currentHealth = Mathf.Max(0f, _currentHealth - amount);
            _hitFlashTimer = 0.16f;
            _hitPunchTimer = 0.18f;
            ApplyHitFlash(1f);

            bool isCrit = CombatSfxBank.IsHeavyAttackContext || BloodSplatterSystem.ContextIsHeavy;
            Vector3 hitPos = BloodSplatterSystem.HasActiveAttackContext
                ? BloodSplatterSystem.ContextHitPoint
                : (transform.position + Vector3.up * 0.65f);
            Vector3 attackDir = BloodSplatterSystem.HasActiveAttackContext
                ? BloodSplatterSystem.ContextAttackDir
                : ResolveFallbackAttackDir();

            CombatSfxBank.PlayImpact(isCrit, hitPos, isCrit ? 0.90f : 0.82f);

            float burstIntensity = (isCrit ? 1.25f : 0.95f) *
                                   (BloodSplatterSystem.HasActiveAttackContext ? BloodSplatterSystem.ContextIntensityMult : 1.0f);
            BloodSplatterSystem.SpawnBloodBurst(hitPos, attackDir, burstIntensity, baseGlowColor, isCrit);
            SpawnBurstParticles(hitPos, baseGlowColor, 9);

            if (_currentHealth <= 0f)
            {
                DestroyCyst(hitPos, attackDir);
            }
        }

        private void DestroyCyst(Vector3 hitPos, Vector3 attackDir)
        {
            if (IsDestroyed) return;
            IsDestroyed = true;

            Vector3 center = transform.position + Vector3.up * 0.6f;
            SpawnBurstParticles(center, baseGlowColor, 24);
            BloodSplatterSystem.SpawnBloodBurst(hitPos, attackDir, 1.48f, baseGlowColor, true);
            CombatSfxBank.PlayImpact(true, center, 0.95f, true);

            Collider[] cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) cols[i].enabled = false;
            }

            for (int i = 0; i < _renderers.Count; i++)
            {
                if (_renderers[i] != null) _renderers[i].enabled = false;
            }

            if (pulseLight != null)
            {
                pulseLight.enabled = false;
            }

            linkedBarrier?.NotifyTargetDestroyed(this);
        }

        private void OnGUI()
        {
            if (IsDestroyed) return;

            VirgilDialogueUI dialogueUi = FindAnyObjectByType<VirgilDialogueUI>();
            if (dialogueUi != null && dialogueUi.IsOpen) return;

            ResolvePlayer();
            if (_playerTransform == null) return;

            float maxHintDistance = kind == TutorialTargetKind.RangedSeal ? 18f : 9.5f;
            float dist = Vector3.Distance(_playerTransform.position, transform.position);
            if (dist > maxHintDistance) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 worldAnchor = transform.position + Vector3.up * 1.45f;
            Vector3 sp = cam.WorldToScreenPoint(worldAnchor);
            if (sp.z <= 0.1f) return;

            EnsureGuiAssets();

            float x = sp.x;
            float y = Screen.height - sp.y;

            // Mini HP bar
            float barW = 78f;
            float barH = 8f;
            float barX = x - barW * 0.5f;
            float barY = y - 10f;

            GUI.color = new Color(0.06f, 0.03f, 0.04f, 0.88f);
            GUI.DrawTexture(new Rect(barX - 2f, barY - 2f, barW + 4f, barH + 4f), s_whiteTex);

            float ratio = Mathf.Clamp01(_currentHealth / Mathf.Max(1f, maxHealth));
            GUI.color = new Color(
                Mathf.Clamp01(baseGlowColor.r * 1.05f),
                Mathf.Clamp01(baseGlowColor.g * 1.05f),
                Mathf.Clamp01(baseGlowColor.b * 1.05f),
                0.96f);
            GUI.DrawTexture(new Rect(barX, barY, barW * ratio, barH), s_whiteTex);

            // Contextual action hint
            string hint = kind == TutorialTargetKind.MeleeNode
                ? "[Botão Esq.] Cortar Nódulo"
                : "[Segurar Botão Dir. + Esq.] Arremessar Lâmina";

            Rect labelRect = new Rect(x - 165f, barY - 24f, 330f, 20f);
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(labelRect.x + 1f, labelRect.y + 1f, labelRect.width, labelRect.height), hint, s_hintStyle);

            GUI.color = kind == TutorialTargetKind.MeleeNode
                ? new Color(1.0f, 0.84f, 0.52f, 0.98f)
                : new Color(0.66f, 0.95f, 0.88f, 0.98f);
            GUI.Label(labelRect, hint, s_hintStyle);
            GUI.color = Color.white;
        }

        private void CacheRenderers()
        {
            _renderers.Clear();
            _baseEmissions.Clear();
            _hasHitFlashProp.Clear();
            _hasEmissionProp.Clear();

            GetComponentsInChildren(true, _renderers);
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                Material mat = r != null ? r.sharedMaterial : null;
                bool hasFlash = mat != null && mat.HasProperty(PropHitFlash);
                bool hasEm = mat != null && mat.HasProperty(PropEmissionColor);
                Color em = hasEm ? mat.GetColor(PropEmissionColor) : baseGlowColor * 0.45f;

                _hasHitFlashProp.Add(hasFlash);
                _hasEmissionProp.Add(hasEm);
                _baseEmissions.Add(em);
            }
        }

        private void ApplyHitFlash(float intensity)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            float clamped = Mathf.Clamp01(intensity);
            Color flashCol = new Color(1.45f, 0.88f, 0.42f, 1f);

            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(_mpb);
                if (_hasHitFlashProp[i])
                {
                    _mpb.SetFloat(PropHitFlash, clamped);
                    _mpb.SetColor(PropHitFlashColor, flashCol);
                }
                if (_hasEmissionProp[i])
                {
                    _mpb.SetColor(PropEmissionColor, Color.Lerp(_baseEmissions[i], flashCol, clamped));
                }
                r.SetPropertyBlock(_mpb);
            }
        }

        private Vector3 ResolveFallbackAttackDir()
        {
            ResolvePlayer();
            if (_playerTransform != null)
            {
                Vector3 away = transform.position - _playerTransform.position;
                away.y = 0.25f;
                if (away.sqrMagnitude > 0.001f) return away.normalized;
            }
            return (Vector3.up * 0.4f + transform.forward).normalized;
        }

        private void ResolvePlayer()
        {
            if (_playerTransform != null && _playerTransform.gameObject.activeInHierarchy) return;

            GameObject playerGo = null;
            try { playerGo = GameObject.FindGameObjectWithTag("Player"); } catch { }
            if (playerGo != null)
            {
                _playerTransform = playerGo.transform;
                return;
            }

            SimplePlayerController ctrl = FindAnyObjectByType<SimplePlayerController>();
            if (ctrl != null)
            {
                _playerTransform = ctrl.transform;
            }
        }

        private static void SpawnBurstParticles(Vector3 pos, Color color, int count)
        {
            GameObject go = new GameObject("TutorialCystBurstFX");
            go.transform.position = pos;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.65f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.58f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 6.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.36f);
            main.gravityModifier = 1.15f;
            main.startColor = color;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.32f;

            ParticleSystemRenderer pr = ps.GetComponent<ParticleSystemRenderer>();
            if (pr != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                if (sh != null)
                {
                    Material mat = new Material(sh);
                    mat.color = color;
                    pr.sharedMaterial = mat;
                }
            }

            ps.Emit(count);
            Destroy(go, 1.2f);
        }

        private static void EnsureGuiAssets()
        {
            if (s_whiteTex == null)
            {
                s_whiteTex = Texture2D.whiteTexture;
            }

            if (s_hintStyle == null)
            {
                s_hintStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                };
                s_hintStyle.normal.textColor = Color.white;
            }
        }
    }
}
