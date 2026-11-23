using MapaEstomago;
using UnityEngine;

namespace StomachAbyss.UI
{
    /// <summary>
    /// Controlador principal da HUD inferior PS1 (estilo Minimalista Matte / PoE 2):
    /// - Orbe Esquerdo (Vermelho): Vida animada estilo liquido com rastro de dano e slosh ao sofrer hit
    /// - Orbe Direito (Verde): Estamina animada estilo liquido com rastro de gasto e slosh ao esquivar/correr
    /// - 5 Slots Centrais (1, 2, 3, R, RMB): Vazios por padrao para receberem os sprites separados no Inspector
    /// - Destaque automatico na arma equipada (slots 1, 2, 3) e contador de cargas de cura (slot R)
    /// </summary>
    public class PlayerHudController : MonoBehaviour
    {
        [Header("Referencias do Jogador (Auto-detectado se vazio)")]
        [SerializeField] private SimplePlayerController player;
        [SerializeField] private PlayerWeaponArsenal arsenal;

        [Header("Orbes Liquidos Animados")]
        [SerializeField] private UnityEngine.UI.Image hpOrbLiquidImage;
        [SerializeField] private UnityEngine.UI.Image staminaOrbLiquidImage;

        [Header("Slots de Icones Separados (Arraste seus Sprites aqui)")]
        [Tooltip("Slot 0 = Tecla 1 | Slot 1 = Tecla 2 | Slot 2 = Tecla 3 | Slot 3 = Tecla R (Cura) | Slot 4 = RMB (Especial)")]
        [SerializeField] private Sprite[] customSlotSprites = new Sprite[5];
        [SerializeField] private UnityEngine.UI.Image[] slotIconImages = new UnityEngine.UI.Image[5];

        [Header("Destaques de Arma Equipada (Slots 1, 2, 3)")]
        [SerializeField] private UnityEngine.UI.Image[] weaponSlotHighlights = new UnityEngine.UI.Image[3];

        [Header("Indicador de Cura Rapida (Slot R)")]
        [SerializeField] private UnityEngine.UI.Text healChargesText;

        [Header("Animacao do Liquido")]
        [SerializeField] private float fillSmoothSpeed = 9f;
        [SerializeField] private float ghostDrainDelay = 0.28f;
        [SerializeField] private float ghostDrainSpeed = 1.6f;
        [SerializeField] private float idleWaveAmplitude = 0.026f;
        [SerializeField] private float maxSloshWaveAmplitude = 0.068f;
        [SerializeField] private float sloshDecaySpeed = 2.4f;
        [SerializeField] private float lowHealthThreshold = 0.28f;

        private Material _hpMat;
        private Material _staminaMat;

        private float _hpVisual = 1f;
        private float _hpGhost = 1f;
        private float _hpGhostDelayTimer;
        private float _hpSlosh;
        private float _lastHpTarget = 1f;

        private float _staminaVisual = 1f;
        private float _staminaGhost = 1f;
        private float _staminaGhostDelayTimer;
        private float _staminaSlosh;
        private float _lastStaminaTarget = 1f;

        private int _lastActiveWeaponSlot = -1;
        private int _lastEstusCharges = -1;

        private static readonly int PropFillAmount = Shader.PropertyToID("_FillAmount");
        private static readonly int PropGhostFill = Shader.PropertyToID("_GhostFill");
        private static readonly int PropWaveAmp = Shader.PropertyToID("_WaveAmp");
        private static readonly int PropPulseIntensity = Shader.PropertyToID("_PulseIntensity");

        private void Awake()
        {
            InitializeMaterials();
            ApplyCustomSlotSprites();
        }

        private void Start()
        {
            ResolvePlayerReferences();
            SyncImmediate();
        }

        private void OnDestroy()
        {
            if (_hpMat != null) Destroy(_hpMat);
            if (_staminaMat != null) Destroy(_staminaMat);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                ApplyCustomSlotSprites();
            }
        }

        private void InitializeMaterials()
        {
            if (hpOrbLiquidImage != null && hpOrbLiquidImage.material != null)
            {
                _hpMat = new Material(hpOrbLiquidImage.material);
                hpOrbLiquidImage.material = _hpMat;
            }

            if (staminaOrbLiquidImage != null && staminaOrbLiquidImage.material != null)
            {
                _staminaMat = new Material(staminaOrbLiquidImage.material);
                staminaOrbLiquidImage.material = _staminaMat;
            }
        }

        private void ResolvePlayerReferences()
        {
            if (player == null)
            {
                player = FindFirstObjectByType<SimplePlayerController>();
            }
            if (arsenal == null && player != null)
            {
                arsenal = player.GetComponent<PlayerWeaponArsenal>();
            }
            if (arsenal == null)
            {
                arsenal = FindFirstObjectByType<PlayerWeaponArsenal>();
            }
        }

        /// <summary>
        /// Permite atribuir um icone separado para qualquer um dos 5 slots (0..4) em runtime ou pelo Inspector.
        /// </summary>
        public void SetSlotIcon(int slotIndex, Sprite sprite)
        {
            if (slotIndex < 0 || slotIndex >= 5) return;
            if (customSlotSprites != null && slotIndex < customSlotSprites.Length)
            {
                customSlotSprites[slotIndex] = sprite;
            }
            ApplySingleSlotSprite(slotIndex, sprite);
        }

        public void ApplyCustomSlotSprites()
        {
            if (slotIconImages == null) return;
            for (int i = 0; i < slotIconImages.Length; i++)
            {
                Sprite s = (customSlotSprites != null && i < customSlotSprites.Length) ? customSlotSprites[i] : null;
                ApplySingleSlotSprite(i, s);
            }
        }

        private void ApplySingleSlotSprite(int index, Sprite sprite)
        {
            if (slotIconImages == null || index < 0 || index >= slotIconImages.Length) return;
            UnityEngine.UI.Image img = slotIconImages[index];
            if (img == null) return;

            img.sprite = sprite;
            if (sprite != null)
            {
                img.enabled = true;
                img.color = Color.white;
                img.preserveAspect = true;
            }
            else
            {
                img.enabled = false;
                img.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        private void SyncImmediate()
        {
            float hpNorm = GetHealthNormalized();
            float stamNorm = GetStaminaNormalized();

            _hpVisual = _hpGhost = _lastHpTarget = hpNorm;
            _staminaVisual = _staminaGhost = _lastStaminaTarget = stamNorm;

            PushOrbMaterial(_hpMat, _hpVisual, _hpGhost, idleWaveAmplitude, hpNorm <= lowHealthThreshold ? 1f : 0f);
            PushOrbMaterial(_staminaMat, _staminaVisual, _staminaGhost, idleWaveAmplitude, 0f);

            UpdateWeaponHighlight(force: true);
            UpdateHealCharges(force: true);
        }

        private void Update()
        {
            if (player == null)
            {
                ResolvePlayerReferences();
            }

            float dt = Time.unscaledDeltaTime;

            // --- 1. Orbe Vermelho (HP Liquido) ---
            float hpTarget = GetHealthNormalized();
            if (hpTarget < _lastHpTarget - 0.004f)
            {
                float drop = _lastHpTarget - hpTarget;
                _hpSlosh = Mathf.Clamp01(_hpSlosh + drop * 3.2f);
                _hpGhostDelayTimer = ghostDrainDelay;
            }
            else if (hpTarget > _lastHpTarget + 0.004f)
            {
                float gain = hpTarget - _lastHpTarget;
                _hpSlosh = Mathf.Clamp01(_hpSlosh + gain * 2.4f);
                _hpGhost = Mathf.Max(_hpGhost, hpTarget);
            }
            _lastHpTarget = hpTarget;

            _hpVisual = Mathf.Lerp(_hpVisual, hpTarget, 1f - Mathf.Exp(-fillSmoothSpeed * dt));
            if (_hpGhost > _hpVisual)
            {
                if (_hpGhostDelayTimer > 0f)
                {
                    _hpGhostDelayTimer -= dt;
                }
                else
                {
                    _hpGhost = Mathf.MoveTowards(_hpGhost, _hpVisual, ghostDrainSpeed * dt);
                }
            }
            else
            {
                _hpGhost = _hpVisual;
            }

            _hpSlosh = Mathf.MoveTowards(_hpSlosh, 0f, sloshDecaySpeed * dt);
            float hpWave = Mathf.Lerp(idleWaveAmplitude, maxSloshWaveAmplitude, _hpSlosh);
            float lowHpPulse = (hpTarget > 0f && hpTarget <= lowHealthThreshold)
                ? Mathf.InverseLerp(lowHealthThreshold, 0.05f, hpTarget)
                : 0f;
            PushOrbMaterial(_hpMat, _hpVisual, _hpGhost, hpWave, lowHpPulse);

            // --- 2. Orbe Verde (Estamina Liquida) ---
            float stamTarget = GetStaminaNormalized();
            if (stamTarget < _lastStaminaTarget - 0.006f)
            {
                float drop = _lastStaminaTarget - stamTarget;
                _staminaSlosh = Mathf.Clamp01(_staminaSlosh + drop * 2.5f);
                _staminaGhostDelayTimer = ghostDrainDelay * 0.65f;
            }
            else if (stamTarget > _lastStaminaTarget + 0.02f)
            {
                _staminaGhost = Mathf.Max(_staminaGhost, stamTarget);
            }
            _lastStaminaTarget = stamTarget;

            _staminaVisual = Mathf.Lerp(_staminaVisual, stamTarget, 1f - Mathf.Exp(-(fillSmoothSpeed * 1.25f) * dt));
            if (_staminaGhost > _staminaVisual)
            {
                if (_staminaGhostDelayTimer > 0f)
                {
                    _staminaGhostDelayTimer -= dt;
                }
                else
                {
                    _staminaGhost = Mathf.MoveTowards(_staminaGhost, _staminaVisual, ghostDrainSpeed * 1.4f * dt);
                }
            }
            else
            {
                _staminaGhost = _staminaVisual;
            }

            _staminaSlosh = Mathf.MoveTowards(_staminaSlosh, 0f, sloshDecaySpeed * dt);
            float stamWave = Mathf.Lerp(idleWaveAmplitude, maxSloshWaveAmplitude, _staminaSlosh);
            PushOrbMaterial(_staminaMat, _staminaVisual, _staminaGhost, stamWave, 0f);

            // --- 3. Destaque de Arma Equipada (Slots 1, 2, 3) & Cargas de Cura (Slot R) ---
            UpdateWeaponHighlight(force: false);
            UpdateHealCharges(force: false);
        }

        private float GetHealthNormalized()
        {
            if (player == null || player.maxHealth <= 0f) return 1f;
            return Mathf.Clamp01(player.currentHealth / player.maxHealth);
        }

        private float GetStaminaNormalized()
        {
            if (player == null) return 1f;
            return player.StaminaNormalized;
        }

        private void PushOrbMaterial(Material mat, float fill, float ghost, float waveAmp, float pulse)
        {
            if (mat == null) return;
            mat.SetFloat(PropFillAmount, fill);
            mat.SetFloat(PropGhostFill, ghost);
            mat.SetFloat(PropWaveAmp, waveAmp);
            mat.SetFloat(PropPulseIntensity, pulse);
        }

        private void UpdateWeaponHighlight(bool force)
        {
            if (weaponSlotHighlights == null || weaponSlotHighlights.Length == 0) return;
            int activeIdx = arsenal != null ? arsenal.ActiveSlotIndex : 0;
            if (!force && activeIdx == _lastActiveWeaponSlot) return;
            _lastActiveWeaponSlot = activeIdx;

            for (int i = 0; i < weaponSlotHighlights.Length; i++)
            {
                if (weaponSlotHighlights[i] != null)
                {
                    weaponSlotHighlights[i].enabled = (i == activeIdx);
                }
            }
        }

        private void UpdateHealCharges(bool force)
        {
            if (healChargesText == null || player == null) return;
            int charges = player.CurrentEstusCharges;
            if (!force && charges == _lastEstusCharges) return;
            _lastEstusCharges = charges;

            healChargesText.text = charges.ToString();
            healChargesText.color = charges > 0
                ? new Color(0.95f, 0.90f, 0.78f, 1f)
                : new Color(0.55f, 0.30f, 0.30f, 0.85f);
        }
    }
}
