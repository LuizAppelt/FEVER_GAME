using System;
using System.Collections.Generic;
using MapaEstomago;
using StomachAbyss.Combat;
using UnityEngine;

namespace BossTenia
{
    /// <summary>
    /// Root health, AoE cleave hit deduplication, MaterialPropertyBlock hit flashes,
    /// Phase 2 acid-organ emissive escalation, floating damage popups, and PS1/Souls OnGUI HUD
    /// for Malphas, a Tenia Liturgica.
    /// </summary>
    public class TeniaBossHealth : MonoBehaviour, IAcidDamageable
    {
        [Header("Vida do Boss")]
        public float maxHealth = 2400f;
        public float currentHealth = 2400f;

        [Range(0.05f, 0.95f)]
        public float phase2ThresholdNormalized = 0.5f;

        public string bossDisplayName = "MALPHAS, A TENIA LITURGICA";
        public string phase2Subtitle = "COROADA EM BILE LITURGICA";
        public bool showBossHud = false;
        public bool debugAlwaysShowHud = false;

        [Header("Feedback de Dano & Deduplicacao")]
        [Tooltip("Janela (s) para deduplicar colisoes multiplas do mesmo golpe AoE/boomerang.")]
        public float hitDedupWindow = 0.16f;

        [Tooltip("Duracao (s) do flash emissivo/base nos renderers ao sofrer dano.")]
        public float hitFlashDuration = 0.16f;

        [HideInInspector]
        public float damageTakenMultiplier = 1f;

        public bool IsDead { get; private set; }
        public bool IsPhase2 { get; private set; }
        public float NormalizedHealth => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

        public event Action<float, Vector3, bool> OnDamaged;
        public event Action OnPhase2Entered;
        public event Action OnDefeated;

        private struct RendererSlotCache
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public bool IsAcidOrgan;
            public bool HasBaseColor;
            public Color OriginalBaseColor;
            public bool HasColorProp;
            public Color OriginalColorProp;
            public bool HasEmissionColor;
            public Color OriginalEmissionColor;
        }

        private struct DamagePopup
        {
            public Vector3 WorldPos;
            public float Amount;
            public bool IsWeakSpot;
            public float Elapsed;
            public float Duration;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static readonly Color NormalFlashBaseTint = new Color(1.0f, 0.44f, 0.48f, 1.0f);
        private static readonly Color NormalFlashEmission = new Color(2.35f, 0.42f, 0.46f, 1.0f);

        private static readonly Color WeakSpotFlashBaseTint = new Color(1.0f, 0.95f, 0.58f, 1.0f);
        private static readonly Color WeakSpotFlashEmission = new Color(3.25f, 2.45f, 0.52f, 1.0f);

        private static readonly Color Phase2AcidEmission = new Color(1.55f, 2.95f, 0.34f, 1.0f);

        private const float PopupDuration = 0.85f;
        private const int MaxActivePopups = 16;

        private readonly List<RendererSlotCache> _rendererSlots = new List<RendererSlotCache>(64);
        private readonly List<DamagePopup> _popups = new List<DamagePopup>(MaxActivePopups);
        private MaterialPropertyBlock _mpb;

        private float _lastHitTimestamp = -999f;
        private float _lastWindowDamageApplied;
        private bool _lastWindowWasWeakSpot;
        private int _activeWindowPopupIndex = -1;

        private float _hitFlashTimer;
        private bool _hitFlashActive;
        private bool _hitFlashIsWeakSpot;

        private float _trailFill = 1f;
        private float _trailHoldTimer;

        private string _customSubtitle;
        private float _victoryBannerTimer;
        private float _victoryBannerDuration = 5.5f;

        private bool _guiStylesReady;
        private GUIStyle _titleStyle;
        private GUIStyle _hpValueStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _popupStyle;
        private GUIStyle _popupCritStyle;
        private GUIStyle _victoryTitleStyle;
        private GUIStyle _victorySubStyle;

        private void Awake()
        {
            maxHealth = Mathf.Max(1f, maxHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
            IsDead = currentHealth <= 0f;
            IsPhase2 = !IsDead && NormalizedHealth <= phase2ThresholdNormalized;
            _trailFill = NormalizedHealth;

            _mpb = new MaterialPropertyBlock();
            CacheChildRenderers();

            if (IsPhase2)
            {
                ApplyBaseVisualState();
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            UpdateHitFlash(dt);
            UpdateTrailBar(dt);
            UpdatePopups(dt);

            if (_victoryBannerTimer > 0f)
            {
                _victoryBannerTimer = Mathf.Max(0f, _victoryBannerTimer - dt);
            }
        }

        public void TakeAcidDamage(float amount)
        {
            ApplySegmentDamage(amount, transform.position, 1.0f, false);
        }

        public void ApplySegmentDamage(float rawAmount, Vector3 hitPos, float multiplier, bool isWeakSpot)
        {
            if (IsDead || rawAmount <= 0f)
            {
                return;
            }

            float safeMult = Mathf.Max(0f, multiplier) * Mathf.Max(0.1f, damageTakenMultiplier);
            float finalDmg = rawAmount * safeMult;
            if (finalDmg <= 0.0001f)
            {
                return;
            }

            BloodSplatterSystem.MarkEntityHandledInContext();

            float now = Time.time;
            float deltaDamage;
            bool isUpgradeInWindow = false;
            bool prevWindowWasWeakSpot = _lastWindowWasWeakSpot;

            if ((now - _lastHitTimestamp) > hitDedupWindow)
            {
                // Fresh attack window
                _lastHitTimestamp = now;
                _lastWindowDamageApplied = finalDmg;
                _lastWindowWasWeakSpot = isWeakSpot;
                _activeWindowPopupIndex = -1;
                deltaDamage = finalDmg;
            }
            else
            {
                // Inside AoE cleave dedup window: only apply positive difference (e.g. body 1.0x -> weakspot 1.75x)
                if (finalDmg <= _lastWindowDamageApplied + 0.001f)
                {
                    return;
                }

                deltaDamage = finalDmg - _lastWindowDamageApplied;
                _lastWindowDamageApplied = finalDmg;
                _lastWindowWasWeakSpot = _lastWindowWasWeakSpot || isWeakSpot;
                isUpgradeInWindow = true;
            }

            currentHealth = Mathf.Max(0f, currentHealth - deltaDamage);
            _trailHoldTimer = 0.20f;

            TriggerHitFlash(isWeakSpot || _lastWindowWasWeakSpot);
            SpawnOrUpgradePopup(hitPos, finalDmg, isWeakSpot || _lastWindowWasWeakSpot, isUpgradeInWindow);

            bool shouldEmitJuice = !isUpgradeInWindow || (isWeakSpot && !prevWindowWasWeakSpot);
            if (shouldEmitJuice)
            {
                bool effectiveWeakSpot = isWeakSpot || _lastWindowWasWeakSpot;
                bool isHeavyOrCrit = effectiveWeakSpot || CombatSfxBank.IsHeavyAttackContext || BloodSplatterSystem.ContextIsHeavy;

                CombatSfxBank.PlayImpact(
                    isHeavyOrCrit,
                    hitPos,
                    effectiveWeakSpot ? 0.96f : 0.86f,
                    forceUpgrade: isUpgradeInWindow && isWeakSpot);

                Vector3 bloodPos = BloodSplatterSystem.HasActiveAttackContext ? BloodSplatterSystem.ContextHitPoint : hitPos;
                Vector3 bloodDir = ResolveOutwardBloodDirection(bloodPos);
                float bloodIntensity = effectiveWeakSpot ? 1.55f : 1.15f;
                if (BloodSplatterSystem.HasActiveAttackContext)
                {
                    bloodIntensity *= Mathf.Clamp(BloodSplatterSystem.ContextIntensityMult, 0.75f, 1.35f);
                }
                BloodSplatterSystem.SpawnBloodBurst(bloodPos, bloodDir, bloodIntensity, null, effectiveWeakSpot);
            }

            OnDamaged?.Invoke(deltaDamage, hitPos, isWeakSpot);

            if (!IsPhase2 && currentHealth > 0f && NormalizedHealth <= phase2ThresholdNormalized)
            {
                EnterPhase2();
            }

            if (currentHealth <= 0f && !IsDead)
            {
                IsDead = true;
                OnDefeated?.Invoke();
            }
        }

        private Vector3 ResolveOutwardBloodDirection(Vector3 impactPos)
        {
            if (BloodSplatterSystem.HasActiveAttackContext)
            {
                return BloodSplatterSystem.ContextAttackDir;
            }

            GameObject playerGo = null;
            try { playerGo = GameObject.FindGameObjectWithTag("Player"); } catch { }
            if (playerGo != null)
            {
                Vector3 away = impactPos - playerGo.transform.position;
                away.y = 0.28f;
                if (away.sqrMagnitude > 0.001f) return away.normalized;
            }

            Vector3 radial = impactPos - transform.position;
            radial.y = 0.28f;
            return radial.sqrMagnitude > 0.001f ? radial.normalized : (Vector3.forward + Vector3.up * 0.25f).normalized;
        }

        public void ResetFullHealth()
        {
            maxHealth = Mathf.Max(1f, maxHealth);
            currentHealth = maxHealth;
            IsDead = false;
            IsPhase2 = false;
            damageTakenMultiplier = 1f;

            _lastHitTimestamp = -999f;
            _lastWindowDamageApplied = 0f;
            _lastWindowWasWeakSpot = false;
            _activeWindowPopupIndex = -1;

            _hitFlashTimer = 0f;
            _hitFlashActive = false;
            _hitFlashIsWeakSpot = false;

            _trailFill = 1f;
            _trailHoldTimer = 0f;
            _victoryBannerTimer = 0f;
            _popups.Clear();

            ApplyBaseVisualState();
        }

        public void SetHudVisible(bool visible, string customSubtitle = null)
        {
            showBossHud = visible;
            if (customSubtitle != null)
            {
                _customSubtitle = customSubtitle;
            }
        }

        public void ShowVictoryBanner(float duration = 5.5f)
        {
            _victoryBannerDuration = Mathf.Max(0.25f, duration);
            _victoryBannerTimer = _victoryBannerDuration;
        }

        public void ApplyDeathMeltTint(float melt01)
        {
            if (_rendererSlots.Count == 0)
            {
                CacheChildRenderers();
            }

            _hitFlashActive = false;
            _hitFlashTimer = 0f;

            if (_mpb == null)
            {
                _mpb = new MaterialPropertyBlock();
            }

            float t = Mathf.Clamp01(melt01);
            float pulse = 0.88f + 0.18f * Mathf.Sin(Time.time * 7.5f);
            Color acidBase = new Color(0.28f, 0.92f, 0.15f, 1f);
            Color acidEmission = new Color(0.45f, 1.95f, 0.16f, 1f) * (t * pulse);

            for (int i = 0; i < _rendererSlots.Count; i++)
            {
                RendererSlotCache slot = _rendererSlots[i];
                if (slot.Renderer == null) continue;

                slot.Renderer.GetPropertyBlock(_mpb, slot.MaterialIndex);

                if (slot.HasBaseColor)
                {
                    _mpb.SetColor(BaseColorId, Color.Lerp(slot.OriginalBaseColor, acidBase, t * 0.92f));
                }
                else if (slot.HasColorProp)
                {
                    _mpb.SetColor(ColorId, Color.Lerp(slot.OriginalColorProp, acidBase, t * 0.92f));
                }

                if (slot.HasEmissionColor)
                {
                    Color startEm = (IsPhase2 && slot.IsAcidOrgan) ? Phase2AcidEmission : slot.OriginalEmissionColor;
                    _mpb.SetColor(EmissionColorId, Color.Lerp(startEm, acidEmission, t));
                }

                slot.Renderer.SetPropertyBlock(_mpb, slot.MaterialIndex);
            }
        }

        private void EnterPhase2()
        {
            IsPhase2 = true;
            if (!_hitFlashActive)
            {
                ApplyBaseVisualState();
            }

            OnPhase2Entered?.Invoke();
        }

        private void CacheChildRenderers()
        {
            _rendererSlots.Clear();
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer rend = renderers[r];
                if (rend == null)
                {
                    continue;
                }

                bool isAcid = rend.gameObject.name.IndexOf("Acid", StringComparison.OrdinalIgnoreCase) >= 0;
                Material[] mats = rend.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    continue;
                }

                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null)
                    {
                        continue;
                    }

                    RendererSlotCache slot = new RendererSlotCache
                    {
                        Renderer = rend,
                        MaterialIndex = m,
                        IsAcidOrgan = isAcid,
                        HasBaseColor = mat.HasProperty(BaseColorId),
                        OriginalBaseColor = mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white,
                        HasColorProp = mat.HasProperty(ColorId),
                        OriginalColorProp = mat.HasProperty(ColorId) ? mat.GetColor(ColorId) : Color.white,
                        HasEmissionColor = mat.HasProperty(EmissionColorId),
                        OriginalEmissionColor = mat.HasProperty(EmissionColorId) ? mat.GetColor(EmissionColorId) : Color.black
                    };

                    _rendererSlots.Add(slot);
                }
            }
        }

        private void TriggerHitFlash(bool isWeakSpot)
        {
            if (_rendererSlots.Count == 0)
            {
                CacheChildRenderers();
            }

            _hitFlashDurationSafe();
            _hitFlashTimer = Mathf.Max(0.02f, hitFlashDuration);
            _hitFlashActive = true;
            _hitFlashIsWeakSpot = isWeakSpot;
            ApplyFlashVisualState(1f);
        }

        private void _hitFlashDurationSafe()
        {
            if (hitFlashDuration < 0.02f)
            {
                hitFlashDuration = 0.02f;
            }
        }

        private void UpdateHitFlash(float dt)
        {
            if (!_hitFlashActive)
            {
                return;
            }

            _hitFlashTimer -= dt;
            if (_hitFlashTimer <= 0f)
            {
                _hitFlashTimer = 0f;
                _hitFlashActive = false;
                ApplyBaseVisualState();
                return;
            }

            float normalized = Mathf.Clamp01(_hitFlashTimer / Mathf.Max(0.02f, hitFlashDuration));
            float curve = normalized * normalized;
            ApplyFlashVisualState(curve);
        }

        private void ApplyBaseVisualState()
        {
            ApplyFlashVisualState(0f);
        }

        private void ApplyFlashVisualState(float flashFactor)
        {
            if (_mpb == null)
            {
                _mpb = new MaterialPropertyBlock();
            }

            Color targetTint = _hitFlashIsWeakSpot ? WeakSpotFlashBaseTint : NormalFlashBaseTint;
            Color targetEmission = _hitFlashIsWeakSpot ? WeakSpotFlashEmission : NormalFlashEmission;

            for (int i = 0; i < _rendererSlots.Count; i++)
            {
                RendererSlotCache slot = _rendererSlots[i];
                if (slot.Renderer == null)
                {
                    continue;
                }

                slot.Renderer.GetPropertyBlock(_mpb, slot.MaterialIndex);

                if (slot.HasBaseColor)
                {
                    Color baseCol = flashFactor > 0f
                        ? Color.LerpUnclamped(slot.OriginalBaseColor, targetTint, flashFactor * 0.78f)
                        : slot.OriginalBaseColor;
                    _mpb.SetColor(BaseColorId, baseCol);
                }
                else if (slot.HasColorProp)
                {
                    Color col = flashFactor > 0f
                        ? Color.LerpUnclamped(slot.OriginalColorProp, targetTint, flashFactor * 0.78f)
                        : slot.OriginalColorProp;
                    _mpb.SetColor(ColorId, col);
                }

                if (slot.HasEmissionColor)
                {
                    Color baseEmission = (IsPhase2 && slot.IsAcidOrgan)
                        ? Phase2AcidEmission
                        : slot.OriginalEmissionColor;

                    Color finalEmission = flashFactor > 0f
                        ? Color.LerpUnclamped(baseEmission, targetEmission, flashFactor)
                        : baseEmission;
                    _mpb.SetColor(EmissionColorId, finalEmission);
                }

                slot.Renderer.SetPropertyBlock(_mpb, slot.MaterialIndex);
            }
        }

        private void UpdateTrailBar(float dt)
        {
            float target = NormalizedHealth;
            if (_trailFill < target)
            {
                _trailFill = target;
                _trailHoldTimer = 0f;
                return;
            }

            if (_trailHoldTimer > 0f)
            {
                _trailHoldTimer -= dt;
                return;
            }

            if (_trailFill > target)
            {
                _trailFill = Mathf.MoveTowards(_trailFill, target, dt * 0.36f);
            }
        }

        private void SpawnOrUpgradePopup(Vector3 hitPos, float totalDisplayDamage, bool isWeakSpot, bool isUpgradeInWindow)
        {
            Vector3 worldPos = hitPos + new Vector3(
                UnityEngine.Random.Range(-0.22f, 0.22f),
                0.55f,
                UnityEngine.Random.Range(-0.22f, 0.22f));

            if (isUpgradeInWindow && _activeWindowPopupIndex >= 0 && _activeWindowPopupIndex < _popups.Count)
            {
                DamagePopup existing = _popups[_activeWindowPopupIndex];
                existing.Amount = totalDisplayDamage;
                existing.IsWeakSpot = isWeakSpot;
                existing.WorldPos = worldPos;
                existing.Elapsed = 0f;
                _popups[_activeWindowPopupIndex] = existing;
                return;
            }

            if (_popups.Count >= MaxActivePopups)
            {
                _popups.RemoveAt(0);
                if (_activeWindowPopupIndex > 0)
                {
                    _activeWindowPopupIndex--;
                }
                else
                {
                    _activeWindowPopupIndex = -1;
                }
            }

            _popups.Add(new DamagePopup
            {
                WorldPos = worldPos,
                Amount = totalDisplayDamage,
                IsWeakSpot = isWeakSpot,
                Elapsed = 0f,
                Duration = PopupDuration
            });
            _activeWindowPopupIndex = _popups.Count - 1;
        }

        private void UpdatePopups(float dt)
        {
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                DamagePopup p = _popups[i];
                p.Elapsed += dt;
                if (p.Elapsed >= p.Duration)
                {
                    _popups.RemoveAt(i);
                    if (_activeWindowPopupIndex == i)
                    {
                        _activeWindowPopupIndex = -1;
                    }
                    else if (_activeWindowPopupIndex > i)
                    {
                        _activeWindowPopupIndex--;
                    }
                }
                else
                {
                    _popups[i] = p;
                }
            }
        }

        private void OnGUI()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            EnsureGuiStyles();

            if (_popups.Count > 0)
            {
                DrawDamagePopups();
            }

            if ((showBossHud || debugAlwaysShowHud) && !IsDead)
            {
                DrawBossHealthBar();
            }

            if (_victoryBannerTimer > 0f)
            {
                DrawVictoryBanner();
            }
        }

        private void DrawBossHealthBar()
        {
            float scale = Screen.width >= 600 ? 1f : Mathf.Clamp(Screen.width / 600f, 0.5f, 1f);
            float hudW = 540f * scale;
            float hudH = 52f * scale;
            float hudX = (Screen.width - hudW) * 0.5f;
            float hudY = 18f * scale;

            Rect panelRect = new Rect(hudX, hudY, hudW, hudH);

            // Opcao 1 PS1 Minimalista Matte (Obsidiana fosca + borda dupla osso envelhecido)
            StomachAbyss.UI.Ps1UiTheme.DrawOnGuiPanel(panelRect, 1f, highlightGold: IsPhase2);

            float padX = 12f * scale;
            float topRowY = hudY + 3f * scale;
            float rowW = hudW - padX * 2f;

            _titleStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(12f * scale));
            _hpValueStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(11f * scale));
            _subtitleStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(10f * scale));

            // Boss Title (left) & numeric HP (right)
            Rect titleRect = new Rect(hudX + padX, topRowY, rowW * 0.76f, 17f * scale);
            DrawShadowedLabel(titleRect, bossDisplayName, _titleStyle, new Color(0.92f, 0.87f, 0.76f, 0.98f));

            Rect hpTextRect = new Rect(hudX + padX, topRowY, rowW, 17f * scale);
            string hpStr = $"{Mathf.CeilToInt(currentHealth)} / {Mathf.CeilToInt(maxHealth)}";
            DrawShadowedLabel(hpTextRect, hpStr, _hpValueStyle, new Color(0.86f, 0.78f, 0.62f, 0.95f));

            // Health bar track
            float barX = hudX + padX;
            float barY = hudY + 21f * scale;
            float barW = rowW;
            float barH = 13f * scale;
            Rect barOuter = new Rect(barX, barY, barW, barH);

            DrawSolidRect(barOuter, new Color(0.08f, 0.015f, 0.025f, 0.96f));
            DrawBorder(barOuter, 1f, new Color(0.42f, 0.34f, 0.24f, 0.85f));

            float innerPad = 1f;
            float innerW = Mathf.Max(0f, barW - innerPad * 2f);
            float innerH = Mathf.Max(1f, barH - innerPad * 2f);
            float innerX = barX + innerPad;
            float innerY = barY + innerPad;

            // Delayed damage trail bar (warm white-orange)
            float trailRatio = Mathf.Clamp01(_trailFill);
            if (trailRatio > 0.001f)
            {
                DrawSolidRect(
                    new Rect(innerX, innerY, innerW * trailRatio, innerH),
                    new Color(0.94f, 0.56f, 0.24f, 0.90f));
            }

            // Main blood-crimson health fill
            float hpRatio = NormalizedHealth;
            if (hpRatio > 0.001f)
            {
                float fillW = innerW * hpRatio;
                Color bloodMain = IsPhase2
                    ? new Color(0.76f, 0.08f, 0.10f, 1f)
                    : new Color(0.64f, 0.05f, 0.09f, 1f);
                DrawSolidRect(new Rect(innerX, innerY, fillW, innerH), bloodMain);

                // PS1 top bevel highlight strip
                DrawSolidRect(
                    new Rect(innerX, innerY, fillW, Mathf.Max(1f, innerH * 0.24f)),
                    new Color(0.95f, 0.26f, 0.24f, 0.48f));
            }

            // Phase 2 divider notch at threshold (default 50%)
            float notchX = innerX + innerW * Mathf.Clamp01(phase2ThresholdNormalized);
            DrawSolidRect(
                new Rect(notchX - 1f, barY - 2f, 2f, barH + 4f),
                new Color(0.84f, 0.74f, 0.46f, 0.92f));

            // Phase / weak-spot subtitle
            string subtitle;
            Color subColor;
            if (IsPhase2)
            {
                subtitle = !string.IsNullOrEmpty(_customSubtitle) ? _customSubtitle : phase2Subtitle;
                subColor = new Color(0.76f, 0.96f, 0.34f, 0.96f);
            }
            else
            {
                subtitle = !string.IsNullOrEmpty(_customSubtitle)
                    ? _customSubtitle
                    : "Mire nas bolsas de acido (CRITICO)";
                subColor = new Color(0.78f, 0.72f, 0.62f, 0.88f);
            }

            Rect subRect = new Rect(barX, hudY + 36f * scale, barW, 14f * scale);
            DrawShadowedLabel(subRect, subtitle, _subtitleStyle, subColor);
        }

        private void DrawDamagePopups()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            for (int i = 0; i < _popups.Count; i++)
            {
                DamagePopup p = _popups[i];
                float t = Mathf.Clamp01(p.Elapsed / Mathf.Max(0.01f, p.Duration));
                Vector3 world = p.WorldPos + Vector3.up * (t * 1.45f);
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0.1f)
                {
                    continue;
                }

                float guiX = screen.x;
                float guiY = Screen.height - screen.y;
                float alpha = t < 0.65f ? 1f : Mathf.Clamp01(1f - (t - 0.65f) / 0.35f);

                int rounded = Mathf.Max(1, Mathf.RoundToInt(p.Amount));
                string text = p.IsWeakSpot ? $"CRITICO! -{rounded}" : $"-{rounded}";
                GUIStyle style = p.IsWeakSpot ? _popupCritStyle : _popupStyle;
                Color textColor = p.IsWeakSpot
                    ? new Color(1.0f, 0.88f, 0.24f, alpha)
                    : new Color(0.97f, 0.94f, 0.89f, alpha);

                Rect rect = new Rect(guiX - 90f, guiY - 14f, 180f, 28f);
                DrawShadowedLabel(rect, text, style, textColor);
            }
        }

        private void DrawVictoryBanner()
        {
            float elapsed = _victoryBannerDuration - _victoryBannerTimer;
            float fadeIn = Mathf.Clamp01(elapsed / 0.45f);
            float fadeOut = Mathf.Clamp01(_victoryBannerTimer / 0.90f);
            float alpha = Mathf.Min(fadeIn, fadeOut);
            if (alpha <= 0.01f)
            {
                return;
            }

            float bandH = 92f;
            float bandY = Screen.height * 0.32f - bandH * 0.5f;
            Rect bandRect = new Rect(0f, bandY, Screen.width, bandH);

            DrawSolidRect(bandRect, new Color(0.03f, 0.008f, 0.014f, 0.82f * alpha));
            DrawSolidRect(new Rect(0f, bandY, Screen.width, 2f), new Color(0.76f, 0.66f, 0.38f, 0.88f * alpha));
            DrawSolidRect(new Rect(0f, bandY + bandH - 2f, Screen.width, 2f), new Color(0.76f, 0.66f, 0.38f, 0.88f * alpha));

            _victoryTitleStyle.fontSize = Screen.width >= 600 ? 28 : 18;
            _victorySubStyle.fontSize = Screen.width >= 600 ? 13 : 10;

            Rect mainTextRect = new Rect(0f, bandY + 16f, Screen.width, 40f);
            DrawShadowedLabel(
                mainTextRect,
                "TENIA LITURGICA PURGADA",
                _victoryTitleStyle,
                new Color(0.93f, 0.83f, 0.52f, alpha));

            Rect subTextRect = new Rect(0f, bandY + 56f, Screen.width, 24f);
            DrawShadowedLabel(
                subTextRect,
                "O VESTIBULO GASTRICO JAZ EM SILENCIO",
                _victorySubStyle,
                new Color(0.78f, 0.72f, 0.62f, 0.92f * alpha));
        }

        private void EnsureGuiStyles()
        {
            if (_guiStylesReady)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                wordWrap = false,
                clipping = TextClipping.Clip
            };

            _hpValueStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                wordWrap = false
            };

            _subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                fontSize = 10,
                wordWrap = false
            };

            _popupStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                wordWrap = false
            };

            _popupCritStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 16,
                wordWrap = false
            };

            _victoryTitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 28,
                wordWrap = false
            };

            _victorySubStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Normal,
                fontSize = 13,
                wordWrap = false
            };

            _guiStylesReady = true;
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static void DrawBorder(Rect rect, float thickness, Color color)
        {
            DrawSolidRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawSolidRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static Rect InsetRect(Rect rect, float inset)
        {
            return new Rect(
                rect.x + inset,
                rect.y + inset,
                Mathf.Max(0f, rect.width - inset * 2f),
                Mathf.Max(0f, rect.height - inset * 2f));
        }

        private static void DrawShadowedLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            Color prev = GUI.color;
            Color shadow = new Color(0.04f, 0.008f, 0.015f, color.a * 0.92f);

            GUI.color = shadow;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);

            GUI.color = color;
            GUI.Label(rect, text, style);

            GUI.color = prev;
        }
    }
}