using StomachAbyss.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StomachAbyss.MainMenu
{
    /// <summary>
    /// Procedural dark-gothic button hover/press visual controller:
    /// - Translucent obsidian/crimson backing plate transition
    /// - Glowing crimson vein border intensification on hover
    /// - Smooth horizontal slide (+14px) and subtle scale-up (1.045x) on pointer enter/exit
    /// - Pulsing crimson diamond/eye marker next to the hovered button
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuGothicButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private MainMenuUIController owner;
        [SerializeField] private RectTransform targetRect;
        [SerializeField] private Vector2 baseAnchoredPosition;
        [SerializeField] private Image backingPlate;
        [SerializeField] private Image topBorder;
        [SerializeField] private Image bottomBorder;
        [SerializeField] private Image leftAccent;
        [SerializeField] private Image rightAccent;
        [SerializeField] private Image diamondMarker;
        [SerializeField] private Image diamondCore;
        [SerializeField] private Text labelText;

        [SerializeField] private Color normalPlateColor = new Color(0.06f, 0.015f, 0.025f, 0.80f);
        [SerializeField] private Color hoverPlateColor = new Color(0.22f, 0.03f, 0.05f, 0.95f);
        [SerializeField] private Color normalTextColor = new Color(0.86f, 0.77f, 0.76f, 0.90f);
        [SerializeField] private Color hoverTextColor = new Color(1.0f, 0.95f, 0.92f, 1.0f);
        [SerializeField] private bool isPrimaryButton;

        private bool _isHovered;
        private bool _isPressed;
        private float _hoverBlend;
        private float _pulsePhase;

        private static readonly Color NormalVeinColor = new Color(0.72f, 0.68f, 0.57f, 0.0f);
        private static readonly Color HoverVeinColor = new Color(0.90f, 0.79f, 0.52f, 0.92f);
        private static readonly Color NormalLeftBarColor = new Color(0.72f, 0.68f, 0.57f, 0.0f);
        private static readonly Color HoverLeftBarColor = new Color(0.94f, 0.84f, 0.56f, 1.0f);

        public void Configure(
            MainMenuUIController owner,
            RectTransform targetRect,
            Vector2 baseAnchoredPos,
            Image backingPlate,
            Image topBorder,
            Image bottomBorder,
            Image leftAccent,
            Image rightAccent,
            Image diamondMarker,
            Image diamondCore,
            Text labelText,
            Color normalPlateColor,
            Color hoverPlateColor,
            Color normalTextColor,
            Color hoverTextColor,
            bool isPrimary)
        {
            this.owner = owner;
            this.targetRect = targetRect;
            this.baseAnchoredPosition = baseAnchoredPos;
            this.backingPlate = backingPlate;
            this.topBorder = topBorder;
            this.bottomBorder = bottomBorder;
            this.leftAccent = leftAccent;
            this.rightAccent = rightAccent;
            this.diamondMarker = diamondMarker;
            this.diamondCore = diamondCore;
            this.labelText = labelText;
            this.normalPlateColor = normalPlateColor;
            this.hoverPlateColor = hoverPlateColor;
            this.normalTextColor = normalTextColor;
            this.hoverTextColor = hoverTextColor;
            this.isPrimaryButton = isPrimary;

            ApplyVisualStateImmediate(0f);
        }

        private void OnEnable()
        {
            _isHovered = false;
            _isPressed = false;
            _hoverBlend = 0f;
            ApplyVisualStateImmediate(0f);
        }

        private void Update()
        {
            float target = _isHovered ? 1f : 0f;
            _hoverBlend = Mathf.MoveTowards(_hoverBlend, target, Time.unscaledDeltaTime * 7.5f);
            _pulsePhase += Time.unscaledDeltaTime * (_isHovered ? 5.2f : 2.1f);

            ApplyVisualStateImmediate(_hoverBlend);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_isHovered)
            {
                _isHovered = true;
                if (owner != null)
                {
                    owner.NotifyButtonHoverSfx();
                }
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            _isPressed = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _isPressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isPressed = false;
        }

        private void ApplyVisualStateImmediate(float blend)
        {
            float smooth = blend * blend * (3f - 2f * blend);
            float pulse = 0.5f + 0.5f * Mathf.Sin(_pulsePhase);

            if (targetRect != null)
            {
                float slideX = Mathf.Lerp(0f, 14f, smooth);
                targetRect.anchoredPosition = baseAnchoredPosition + new Vector2(slideX, 0f);

                float scale = Mathf.Lerp(1.0f, _isPressed ? 1.015f : 1.045f, smooth);
                targetRect.localScale = new Vector3(scale, scale, 1f);
            }

            if (backingPlate != null)
            {
                backingPlate.color = Color.Lerp(normalPlateColor, hoverPlateColor, smooth);
            }

            Color veinCol = Color.Lerp(NormalVeinColor, HoverVeinColor, smooth);
            if (smooth > 0.01f)
            {
                veinCol.a *= Mathf.Lerp(0.82f, 1.0f, pulse);
            }

            if (topBorder != null) topBorder.color = veinCol;
            if (bottomBorder != null) bottomBorder.color = veinCol;
            if (leftAccent != null) leftAccent.color = Color.Lerp(NormalLeftBarColor, HoverLeftBarColor, smooth);
            if (rightAccent != null) rightAccent.color = veinCol;

            if (labelText != null)
            {
                labelText.color = Color.Lerp(normalTextColor, hoverTextColor, smooth);
            }

            if (diamondMarker != null)
            {
                float baseAlpha = isPrimaryButton ? 0.50f : 0.25f;
                float hoverAlpha = Mathf.Lerp(0.82f, 1.0f, pulse);
                Color mc = new Color(Ps1UiTheme.BoneGold.r, Ps1UiTheme.BoneGold.g, Ps1UiTheme.BoneGold.b, Mathf.Lerp(baseAlpha, hoverAlpha, smooth));
                diamondMarker.color = mc;

                RectTransform mrt = diamondMarker.rectTransform;
                float markerScale = Mathf.Lerp(0.82f, 1.08f + 0.12f * pulse, smooth);
                mrt.localScale = new Vector3(markerScale, markerScale, 1f);
            }

            if (diamondCore != null)
            {
                float coreAlpha = Mathf.Lerp(0.18f, 0.95f, smooth);
                diamondCore.color = new Color(Ps1UiTheme.BoneWhiteText.r, Ps1UiTheme.BoneWhiteText.g, Ps1UiTheme.BoneWhiteText.b, coreAlpha);
            }
        }
    }
}
