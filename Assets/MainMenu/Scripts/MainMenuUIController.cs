using System;
using System.Collections;
using System.Reflection;
using StomachAbyss.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StomachAbyss.MainMenu
{
    /// <summary>
    /// Visceral Dark-Fantasy / Dark Souls Main Menu UI Controller for Stomach Abyss (Unity 6 URP).
    /// - Explicitly omits any visible game title/logo so the Colossal Pulsating Heart commands 100% visual focus.
    /// - Creates an inactive "Logo_Placeholder_Anchor" child inside the Canvas for future logo placement.
    /// - Positions the gothic menu stack cleanly in the bottom-left corner so the central heart, spinal column,
    ///   and bottom-right blood crystal monoliths remain completely unobstructed.
    /// - Provides JOGAR, OPÇÕES (Volume Geral 0-100%, Tela Cheia toggle, Voltar), and SAIR buttons,
    ///   plus a full-screen black FadeOverlay CanvasGroup for scene entry (1.2s) and play transition (1.45s).
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuUIController : MonoBehaviour
    {
        private const string PrefKeyMasterVolume = "StomachAbyss_MasterVolume";

        [Header("Scene Transition")]
        [Tooltip("Target gameplay scene loaded when JOGAR is clicked.")]
        public string gameplaySceneName = "Stomach_Abyss";

        [Tooltip("Duration in seconds for the initial fade-in from black (1 -> 0) when Main Menu starts.")]
        public float fadeInDuration = 1.2f;

        [Tooltip("Duration in seconds for the fade-to-black (0 -> 1) when JOGAR is clicked.")]
        public float fadeOutDuration = 1.45f;

        [Header("Scene Controller Links (Auto-detected if left empty)")]
        [Tooltip("Reference to the Colossal Heart controller (invokes TriggerPlayTransitionBoost on Play).")]
        public MonoBehaviour heartController;

        [Tooltip("Reference to MainMenuAudioController (invokes PlayStartGameImpactSfx, SetMasterVolume, hover/click SFX).")]
        public MonoBehaviour audioController;

        [Tooltip("Reference to MainMenuCameraParallax (invokes StartPlayZoomTransition on Play).")]
        public MonoBehaviour cameraParallax;

        [Header("Canvas & Anchors")]
        public Canvas mainCanvas;
        public RectTransform logoPlaceholderAnchor;
        public RectTransform menuBottomLeftRoot;
        public CanvasGroup menuButtonsCanvasGroup;

        [Header("Primary Menu Buttons")]
        public Button playButton;
        public Button optionsButton;
        public Button quitButton;

        [Header("Options Panel")]
        public GameObject optionsPanelRoot;
        public CanvasGroup optionsPanelCanvasGroup;
        public Slider masterVolumeSlider;
        public Text masterVolumeValueText;
        public Toggle fullscreenToggle;
        public Button optionsBackButton;

        [Header("Full-Screen Fade Overlay")]
        public CanvasGroup fadeOverlayGroup;
        public Image fadeOverlayImage;

        private Font _uiFont;
        private Sprite _whiteSprite;
        private Sprite _diamondSprite;
        private Sprite _buttonGradientSprite;
        private Sprite _cornerVignetteSprite;

        private bool _isOptionsOpen;
        private bool _isTransitioning;
        private Coroutine _fadeRoutine;
        private Coroutine _optionsAnimRoutine;

        public bool IsOptionsOpen => _isOptionsOpen;
        public bool IsTransitioning => _isTransitioning;

        private void Awake()
        {
            BuildOrBindUI();
            ResolveSceneControllers();
            BindRuntimeCallbacks();
            InitializeOptionsState();
        }

        private void Start()
        {
            ResolveSceneControllers();

            if (Application.isPlaying && fadeOverlayGroup != null)
            {
                fadeOverlayGroup.gameObject.SetActive(true);
                fadeOverlayGroup.alpha = 1f;
                fadeOverlayGroup.blocksRaycasts = true;
                fadeOverlayGroup.interactable = true;

                if (_fadeRoutine != null)
                {
                    StopCoroutine(_fadeRoutine);
                }
                _fadeRoutine = StartCoroutine(FadeOverlayRoutine(1f, 0f, Mathf.Max(0.1f, fadeInDuration), disableRaycastOnEnd: true));
            }
        }

        private void Update()
        {
            if (_isTransitioning) return;

            if (_isOptionsOpen && WasEscapePressedThisFrame())
            {
                OnCloseOptionsClicked();
            }
        }

        /// <summary>
        /// Explicitly binds references to the scene's heart, audio, and camera parallax controllers.
        /// </summary>
        public void BindControllers(MonoBehaviour heart, MonoBehaviour audio, MonoBehaviour parallax)
        {
            if (heart != null) heartController = heart;
            if (audio != null) audioController = audio;
            if (parallax != null) cameraParallax = parallax;
        }

        /// <summary>
        /// Idempotently builds or binds the complete uGUI hierarchy, EventSystem, bottom-left menu stack,
        /// inactive Logo_Placeholder_Anchor, translucent dark-crimson Options panel, and full-screen FadeOverlay.
        /// Safe to call both from Editor setup scripts and runtime Awake().
        /// </summary>
        public void BuildOrBindUI()
        {
            EnsureProceduralResources();
            EnsureEventSystem();

            // 1. Setup or locate root Canvas
            if (mainCanvas == null)
            {
                mainCanvas = GetComponent<Canvas>();
            }
            if (mainCanvas == null)
            {
                mainCanvas = GetComponentInChildren<Canvas>(true);
            }
            if (mainCanvas == null)
            {
                mainCanvas = gameObject.AddComponent<Canvas>();
            }

            mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            mainCanvas.sortingOrder = 100;
            mainCanvas.pixelPerfect = false;

            GameObject canvasGo = mainCanvas.gameObject;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvasGo.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (canvasGo.GetComponent<GraphicRaycaster>() == null)
            {
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            Transform canvasTransform = mainCanvas.transform;

            // 2. Inactive Logo Placeholder Anchor (no game title/logo displayed right now per user instruction)
            BuildOrBindLogoPlaceholder(canvasTransform);

            // 3. Subtle Bottom-Left Dark Vignette Backing (enhances button legibility without obstructing scene)
            BuildOrBindBottomLeftVignette(canvasTransform);

            // 4. Bottom-Left Main Menu Button Stack (JOGAR, OPÇÕES, SAIR)
            BuildOrBindBottomLeftMenuStack(canvasTransform);

            // 5. Sleek Translucent Dark-Crimson Options Panel (Volume Geral, Tela Cheia, Voltar)
            BuildOrBindOptionsPanel(canvasTransform);

            // 6. Topmost Full-Screen Black FadeOverlay CanvasGroup
            BuildOrBindFadeOverlay(canvasTransform);
        }

        private void BuildOrBindLogoPlaceholder(Transform canvasTransform)
        {
            bool isNew = false;
            if (logoPlaceholderAnchor == null)
            {
                Transform existing = canvasTransform.Find("Logo_Placeholder_Anchor");
                if (existing != null)
                {
                    logoPlaceholderAnchor = existing.GetComponent<RectTransform>();
                }
            }

            if (logoPlaceholderAnchor == null)
            {
                GameObject logoGo = CreateUiObject("Logo_Placeholder_Anchor", canvasTransform);
                logoPlaceholderAnchor = logoGo.GetComponent<RectTransform>();
                isNew = true;
            }

            // Apenas inicializa posicoes e dimensoes padrao se o objeto acabou de ser instanciado
            // ou se ainda nao possuir tamanho configurado (sizeDelta == Vector2.zero).
            // Desta forma, qualquer ajuste manual feito no RectTransform (Width, Height, Pos X, Pos Y, Scale)
            // pelo Inspector e totalmente preservado ao dar Play.
            if (isNew || logoPlaceholderAnchor.sizeDelta == Vector2.zero)
            {
                logoPlaceholderAnchor.anchorMin = new Vector2(0f, 1f);
                logoPlaceholderAnchor.anchorMax = new Vector2(0f, 1f);
                logoPlaceholderAnchor.pivot = new Vector2(0f, 1f);
                logoPlaceholderAnchor.anchoredPosition = new Vector2(58f, -38f);
                logoPlaceholderAnchor.sizeDelta = new Vector2(370f, 257f);
            }

            Image logoImg = GetOrAddComponent<Image>(logoPlaceholderAnchor.gameObject);
            logoImg.raycastTarget = false;
            if (logoImg.sprite == null)
            {
                logoImg.sprite = Resources.Load<Sprite>("UI/tex_fever_logo");
            }

            if (logoImg.sprite != null)
            {
                logoImg.preserveAspect = true;
                logoImg.color = Color.white;
                logoPlaceholderAnchor.gameObject.SetActive(true);
            }
            else
            {
                logoPlaceholderAnchor.gameObject.SetActive(false);
            }
        }

        private void BuildOrBindBottomLeftVignette(Transform canvasTransform)
        {
            Transform existing = canvasTransform.Find("BottomLeft_AbyssVignette");
            GameObject vignetteGo = existing != null ? existing.gameObject : CreateUiObject("BottomLeft_AbyssVignette", canvasTransform);

            RectTransform rt = vignetteGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(640f, 440f);

            Image img = GetOrAddComponent<Image>(vignetteGo);
            img.sprite = _cornerVignetteSprite;
            img.color = new Color(0.02f, 0.005f, 0.01f, 0.72f);
            img.raycastTarget = false;
        }

        private void BuildOrBindBottomLeftMenuStack(Transform canvasTransform)
        {
            if (menuBottomLeftRoot == null)
            {
                Transform existing = canvasTransform.Find("Menu_BottomLeft_Root");
                if (existing != null)
                {
                    menuBottomLeftRoot = existing.GetComponent<RectTransform>();
                }
            }

            if (menuBottomLeftRoot == null)
            {
                GameObject rootGo = CreateUiObject("Menu_BottomLeft_Root", canvasTransform);
                menuBottomLeftRoot = rootGo.GetComponent<RectTransform>();
            }

            menuBottomLeftRoot.anchorMin = new Vector2(0f, 0f);
            menuBottomLeftRoot.anchorMax = new Vector2(0f, 0f);
            menuBottomLeftRoot.pivot = new Vector2(0f, 0f);
            menuBottomLeftRoot.anchoredPosition = new Vector2(74f, 64f);
            menuBottomLeftRoot.sizeDelta = new Vector2(344f, 232f);

            menuButtonsCanvasGroup = GetOrAddComponent<CanvasGroup>(menuBottomLeftRoot.gameObject);
            menuButtonsCanvasGroup.alpha = 1f;
            menuButtonsCanvasGroup.interactable = true;
            menuButtonsCanvasGroup.blocksRaycasts = true;

            // Vertical dark-gothic arterial spine accent on the left edge of the menu buttons
            BuildOrBindGothicSpineDecoration(menuBottomLeftRoot);

            // Build the 3 visceral dark-fantasy menu buttons
            playButton = BuildOrBindGothicMenuButton(
                menuBottomLeftRoot,
                "Btn_Jogar",
                "JOGAR",
                new Vector2(22f, 156f),
                new Vector2(310f, 56f),
                isPrimaryAction: true);

            optionsButton = BuildOrBindGothicMenuButton(
                menuBottomLeftRoot,
                "Btn_Opcoes",
                "OPÇÕES",
                new Vector2(22f, 78f),
                new Vector2(310f, 56f),
                isPrimaryAction: false);

            quitButton = BuildOrBindGothicMenuButton(
                menuBottomLeftRoot,
                "Btn_Sair",
                "SAIR",
                new Vector2(22f, 0f),
                new Vector2(310f, 56f),
                isPrimaryAction: false);
        }

        private void BuildOrBindGothicSpineDecoration(RectTransform parent)
        {
            Transform existing = parent.Find("Gothic_Arterial_Spine");
            GameObject spineGo = existing != null ? existing.gameObject : CreateUiObject("Gothic_Arterial_Spine", parent);
            RectTransform spineRt = spineGo.GetComponent<RectTransform>();
            spineRt.anchorMin = new Vector2(0f, 0f);
            spineRt.anchorMax = new Vector2(0f, 1f);
            spineRt.pivot = new Vector2(0.5f, 0.5f);
            spineRt.anchoredPosition = new Vector2(4f, 0f);
            spineRt.sizeDelta = new Vector2(2f, -12f);

            Image spineImg = GetOrAddComponent<Image>(spineGo);
            spineImg.sprite = _whiteSprite;
            spineImg.color = Ps1UiTheme.BoneTrimMuted;
            spineImg.raycastTarget = false;

            // Top diamond finial
            Transform topNode = spineGo.transform.Find("TopDiamond");
            GameObject topGo = topNode != null ? topNode.gameObject : CreateUiObject("TopDiamond", spineGo.transform);
            RectTransform topRt = topGo.GetComponent<RectTransform>();
            topRt.anchorMin = new Vector2(0.5f, 1f);
            topRt.anchorMax = new Vector2(0.5f, 1f);
            topRt.pivot = new Vector2(0.5f, 0.5f);
            topRt.anchoredPosition = new Vector2(0f, 4f);
            topRt.sizeDelta = new Vector2(10f, 10f);
            Image topImg = GetOrAddComponent<Image>(topGo);
            topImg.sprite = Ps1UiTheme.PixelDiamondSprite;
            topImg.color = Ps1UiTheme.BoneTrim;
            topImg.raycastTarget = false;

            // Bottom diamond finial
            Transform botNode = spineGo.transform.Find("BottomDiamond");
            GameObject botGo = botNode != null ? botNode.gameObject : CreateUiObject("BottomDiamond", spineGo.transform);
            RectTransform botRt = botGo.GetComponent<RectTransform>();
            botRt.anchorMin = new Vector2(0.5f, 0f);
            botRt.anchorMax = new Vector2(0.5f, 0f);
            botRt.pivot = new Vector2(0.5f, 0.5f);
            botRt.anchoredPosition = new Vector2(0f, -4f);
            botRt.sizeDelta = new Vector2(10f, 10f);
            Image botImg = GetOrAddComponent<Image>(botGo);
            botImg.sprite = Ps1UiTheme.PixelDiamondSprite;
            botImg.color = Ps1UiTheme.BoneTrim;
            botImg.raycastTarget = false;
        }

        private Button BuildOrBindGothicMenuButton(
            RectTransform parent,
            string objectName,
            string labelText,
            Vector2 anchoredPos,
            Vector2 size,
            bool isPrimaryAction)
        {
            Transform existing = parent.Find(objectName);
            GameObject btnGo = existing != null ? existing.gameObject : CreateUiObject(objectName, parent);

            RectTransform rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;

            // Opcao 1 PS1 Minimalista Matte: Obsidiana fosca com borda dupla pixelada em osso
            Image bgImage = GetOrAddComponent<Image>(btnGo);
            bgImage.sprite = Ps1UiTheme.PanelSprite;
            bgImage.type = Image.Type.Sliced;
            bgImage.pixelsPerUnitMultiplier = 1f;
            Color normalBg = isPrimaryAction
                ? new Color(1.00f, 0.98f, 0.95f, 0.96f)
                : new Color(0.92f, 0.90f, 0.88f, 0.92f);
            Color hoverBg = new Color(1.18f, 1.12f, 0.98f, 1.00f);
            bgImage.color = normalBg;
            bgImage.raycastTarget = true;

            Button btn = GetOrAddComponent<Button>(btnGo);
            btn.targetGraphic = bgImage;
            btn.transition = Selectable.Transition.None;

            // Filetes internos sutis que acendem em osso-ouro no hover
            Image topVein = BuildOrBindEdgeLine(btnGo.transform, "VeinBorder_Top",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -2f), new Vector2(-4f, 2f));

            Image bottomVein = BuildOrBindEdgeLine(btnGo.transform, "VeinBorder_Bottom",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 2f), new Vector2(-4f, 2f));

            Image leftBar = BuildOrBindEdgeLine(btnGo.transform, "VeinBorder_Left",
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(2f, 0f), new Vector2(3f, -4f));

            Image rightBar = BuildOrBindEdgeLine(btnGo.transform, "VeinBorder_Right",
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-2f, 0f), new Vector2(2f, -4f));

            // Losango pixelado PS1 ao lado do botao selecionado
            Transform markerTr = btnGo.transform.Find("HoverDiamondMarker");
            GameObject markerGo = markerTr != null ? markerTr.gameObject : CreateUiObject("HoverDiamondMarker", btnGo.transform);
            RectTransform markerRt = markerGo.GetComponent<RectTransform>();
            markerRt.anchorMin = new Vector2(0f, 0.5f);
            markerRt.anchorMax = new Vector2(0f, 0.5f);
            markerRt.pivot = new Vector2(0.5f, 0.5f);
            markerRt.anchoredPosition = new Vector2(24f, 0f);
            markerRt.sizeDelta = new Vector2(14f, 14f);

            Image markerImg = GetOrAddComponent<Image>(markerGo);
            markerImg.sprite = Ps1UiTheme.PixelDiamondSprite;
            markerImg.color = new Color(Ps1UiTheme.BoneGold.r, Ps1UiTheme.BoneGold.g, Ps1UiTheme.BoneGold.b, 0.35f);
            markerImg.raycastTarget = false;

            Transform pupilTr = markerGo.transform.Find("InnerEmberCore");
            GameObject pupilGo = pupilTr != null ? pupilTr.gameObject : CreateUiObject("InnerEmberCore", markerGo.transform);
            RectTransform pupilRt = pupilGo.GetComponent<RectTransform>();
            pupilRt.anchorMin = new Vector2(0.5f, 0.5f);
            pupilRt.anchorMax = new Vector2(0.5f, 0.5f);
            pupilRt.pivot = new Vector2(0.5f, 0.5f);
            pupilRt.anchoredPosition = Vector2.zero;
            pupilRt.sizeDelta = new Vector2(6f, 6f);
            Image pupilImg = GetOrAddComponent<Image>(pupilGo);
            pupilImg.sprite = Ps1UiTheme.PixelDiamondSprite;
            pupilImg.color = Ps1UiTheme.BoneWhiteText;
            pupilImg.raycastTarget = false;

            // Button Label Text
            Transform labelTr = btnGo.transform.Find("Label");
            GameObject labelGo = labelTr != null ? labelTr.gameObject : CreateUiObject("Label", btnGo.transform);
            RectTransform labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(44f, 2f);
            labelRt.offsetMax = new Vector2(-16f, -2f);

            Text label = GetOrAddComponent<Text>(labelGo);
            label.font = _uiFont;
            label.text = labelText;
            label.fontSize = isPrimaryAction ? 24 : 21;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            Color normalTextCol = isPrimaryAction ? Ps1UiTheme.BoneGold : Ps1UiTheme.BoneWhiteText;
            Color hoverTextCol = new Color(1.0f, 0.96f, 0.84f, 1.0f);
            label.color = normalTextCol;

            Outline outline = GetOrAddComponent<Outline>(labelGo);
            outline.effectColor = Ps1UiTheme.PixelBlack;
            outline.effectDistance = new Vector2(1.4f, -1.4f);

            Shadow shadow = GetOrAddComponent<Shadow>(labelGo);
            shadow.effectColor = new Color(0f, 0f, 0f, 0.82f);
            shadow.effectDistance = new Vector2(2f, -2f);

            // Attach procedural hover/slide/vein-pulse animator
            MainMenuGothicButton fx = GetOrAddComponent<MainMenuGothicButton>(btnGo);
            fx.Configure(
                owner: this,
                targetRect: rt,
                baseAnchoredPos: anchoredPos,
                backingPlate: bgImage,
                topBorder: topVein,
                bottomBorder: bottomVein,
                leftAccent: leftBar,
                rightAccent: rightBar,
                diamondMarker: markerImg,
                diamondCore: pupilImg,
                labelText: label,
                normalPlateColor: normalBg,
                hoverPlateColor: hoverBg,
                normalTextColor: normalTextCol,
                hoverTextColor: hoverTextCol,
                isPrimary: isPrimaryAction);

            return btn;
        }

        private Image BuildOrBindEdgeLine(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPos,
            Vector2 sizeDelta)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : CreateUiObject(name, parent);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Image img = GetOrAddComponent<Image>(go);
            img.sprite = _whiteSprite;
            img.raycastTarget = false;
            return img;
        }

        private void BuildOrBindOptionsPanel(Transform canvasTransform)
        {
            if (optionsPanelRoot == null)
            {
                Transform existing = canvasTransform.Find("Options_Panel_Root");
                if (existing != null)
                {
                    optionsPanelRoot = existing.gameObject;
                }
            }

            if (optionsPanelRoot == null)
            {
                optionsPanelRoot = CreateUiObject("Options_Panel_Root", canvasTransform);
            }

            RectTransform panelRt = optionsPanelRoot.GetComponent<RectTransform>();
            // Positioned cleanly in bottom-left next to the main menu buttons so the central heart and right monoliths remain completely unobstructed
            panelRt.anchorMin = new Vector2(0f, 0f);
            panelRt.anchorMax = new Vector2(0f, 0f);
            panelRt.pivot = new Vector2(0f, 0f);
            panelRt.anchoredPosition = new Vector2(432f, 64f);
            panelRt.sizeDelta = new Vector2(540f, 426f);

            optionsPanelCanvasGroup = GetOrAddComponent<CanvasGroup>(optionsPanelRoot);

            Ps1OptionsPanelView optionsView = GetOrAddComponent<Ps1OptionsPanelView>(optionsPanelRoot);
            optionsView.Build(_uiFont, OnCloseOptionsClicked, NotifyButtonClickSfx);

            // Start closed by default
            _isOptionsOpen = false;
            optionsPanelCanvasGroup.alpha = 0f;
            optionsPanelCanvasGroup.interactable = false;
            optionsPanelCanvasGroup.blocksRaycasts = false;
            optionsPanelRoot.SetActive(false);
        }

        private void LegacyUnusedOptionsPanelPlaceholder(RectTransform panelRt)
        {
            Image panelBg = GetOrAddComponent<Image>(optionsPanelRoot);
            Ps1UiTheme.ApplyPanelStyle(panelBg);
            panelBg.raycastTarget = true;

            // Desativa bordas antigas de veia vermelha (a moldura 9-slice Opcao 1 ja desenha a borda dupla em osso)
            Image topBorder = BuildOrBindEdgeLine(optionsPanelRoot.transform, "PanelVein_Top",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 1f));
            topBorder.color = Color.clear;

            Image botBorder = BuildOrBindEdgeLine(optionsPanelRoot.transform, "PanelVein_Bottom",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 1f));
            botBorder.color = Color.clear;

            Image leftBorder = BuildOrBindEdgeLine(optionsPanelRoot.transform, "PanelVein_Left",
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1f, 0f));
            leftBorder.color = Color.clear;

            Image rightBorder = BuildOrBindEdgeLine(optionsPanelRoot.transform, "PanelVein_Right",
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(1f, 0f));
            rightBorder.color = Color.clear;

            // Header Title: "OPÇÕES DO ABISMO"
            Text headerText = BuildOrBindText(
                optionsPanelRoot.transform,
                "Header_Title",
                "OPÇÕES DO ABISMO",
                20,
                FontStyle.Bold,
                Ps1UiTheme.BoneGold,
                TextAnchor.MiddleLeft,
                new Vector2(24f, 224f),
                new Vector2(380f, 34f));

            Outline headerOutline = GetOrAddComponent<Outline>(headerText.gameObject);
            headerOutline.effectColor = Ps1UiTheme.PixelBlack;

            Image headerRule = BuildOrBindEdgeLine(optionsPanelRoot.transform, "Header_Divider",
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0.5f),
                new Vector2(24f, 218f), new Vector2(388f, 2f));
            headerRule.color = Ps1UiTheme.BoneTrimMuted;

            // 1. Volume Geral Row (0% to 100%)
            BuildOrBindText(
                optionsPanelRoot.transform,
                "Volume_Label",
                "VOLUME GERAL",
                16,
                FontStyle.Bold,
                Ps1UiTheme.BoneWhiteText,
                TextAnchor.MiddleLeft,
                new Vector2(24f, 174f),
                new Vector2(240f, 26f));

            masterVolumeValueText = BuildOrBindText(
                optionsPanelRoot.transform,
                "Volume_ValueText",
                "85%",
                16,
                FontStyle.Bold,
                Ps1UiTheme.BoneGold,
                TextAnchor.MiddleRight,
                new Vector2(312f, 174f),
                new Vector2(96f, 26f));

            masterVolumeSlider = BuildOrBindGothicSlider(
                optionsPanelRoot.transform,
                "Volume_Slider",
                new Vector2(24f, 142f),
                new Vector2(384f, 22f));

            // 2. Tela Cheia Toggle Row
            BuildOrBindText(
                optionsPanelRoot.transform,
                "Fullscreen_Label",
                "TELA CHEIA",
                16,
                FontStyle.Bold,
                Ps1UiTheme.BoneWhiteText,
                TextAnchor.MiddleLeft,
                new Vector2(24f, 92f),
                new Vector2(240f, 30f));

            fullscreenToggle = BuildOrBindGothicToggle(
                optionsPanelRoot.transform,
                "Fullscreen_Toggle",
                new Vector2(376f, 92f),
                new Vector2(30f, 30f));

            // 3. Voltar Button
            optionsBackButton = BuildOrBindGothicMenuButton(
                panelRt,
                "Btn_Voltar",
                "VOLTAR",
                new Vector2(24f, 20f),
                new Vector2(196f, 46f),
                isPrimaryAction: false);

            // Start closed by default
            _isOptionsOpen = false;
            optionsPanelCanvasGroup.alpha = 0f;
            optionsPanelCanvasGroup.interactable = false;
            optionsPanelCanvasGroup.blocksRaycasts = false;
            optionsPanelRoot.SetActive(false);
        }

        private Slider BuildOrBindGothicSlider(
            Transform parent,
            string name,
            Vector2 anchoredPos,
            Vector2 size)
        {
            Transform existing = parent.Find(name);
            GameObject sliderGo = existing != null ? existing.gameObject : CreateUiObject(name, parent);
            RectTransform rt = sliderGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            // Track background
            Transform bgTr = sliderGo.transform.Find("Background");
            GameObject bgGo = bgTr != null ? bgTr.gameObject : CreateUiObject("Background", sliderGo.transform);
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.30f);
            bgRt.anchorMax = new Vector2(1f, 0.70f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            Image bgImg = GetOrAddComponent<Image>(bgGo);
            bgImg.sprite = _whiteSprite;
            bgImg.color = Ps1UiTheme.ObsidianRecessed;
            Outline trackOutline = GetOrAddComponent<Outline>(bgGo);
            trackOutline.effectColor = Ps1UiTheme.BoneTrimMuted;
            trackOutline.effectDistance = new Vector2(1f, -1f);

            // Fill Area
            Transform fillAreaTr = sliderGo.transform.Find("Fill Area");
            GameObject fillAreaGo = fillAreaTr != null ? fillAreaTr.gameObject : CreateUiObject("Fill Area", sliderGo.transform);
            RectTransform fillAreaRt = fillAreaGo.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.30f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.70f);
            fillAreaRt.offsetMin = new Vector2(2f, 1f);
            fillAreaRt.offsetMax = new Vector2(-2f, -1f);

            Transform fillTr = fillAreaGo.transform.Find("Fill");
            GameObject fillGo = fillTr != null ? fillTr.gameObject : CreateUiObject("Fill", fillAreaGo.transform);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            Image fillImg = GetOrAddComponent<Image>(fillGo);
            fillImg.sprite = _whiteSprite;
            fillImg.color = Ps1UiTheme.BoneTrim;

            // Handle Slide Area
            Transform handleAreaTr = sliderGo.transform.Find("Handle Slide Area");
            GameObject handleAreaGo = handleAreaTr != null ? handleAreaTr.gameObject : CreateUiObject("Handle Slide Area", sliderGo.transform);
            RectTransform handleAreaRt = handleAreaGo.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(8f, 0f);
            handleAreaRt.offsetMax = new Vector2(-8f, 0f);

            Transform handleTr = handleAreaGo.transform.Find("Handle");
            GameObject handleGo = handleTr != null ? handleTr.gameObject : CreateUiObject("Handle", handleAreaGo.transform);
            RectTransform handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(18f, 22f);
            Image handleImg = GetOrAddComponent<Image>(handleGo);
            handleImg.sprite = Ps1UiTheme.PixelDiamondSprite;
            handleImg.color = Ps1UiTheme.BoneGold;

            Slider slider = GetOrAddComponent<Slider>(sliderGo);
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;

            return slider;
        }

        private Toggle BuildOrBindGothicToggle(
            Transform parent,
            string name,
            Vector2 anchoredPos,
            Vector2 size)
        {
            Transform existing = parent.Find(name);
            GameObject toggleGo = existing != null ? existing.gameObject : CreateUiObject(name, parent);
            RectTransform rt = toggleGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            Transform boxTr = toggleGo.transform.Find("Background");
            GameObject boxGo = boxTr != null ? boxTr.gameObject : CreateUiObject("Background", toggleGo.transform);
            RectTransform boxRt = boxGo.GetComponent<RectTransform>();
            boxRt.anchorMin = Vector2.zero;
            boxRt.anchorMax = Vector2.one;
            boxRt.offsetMin = Vector2.zero;
            boxRt.offsetMax = Vector2.zero;

            Image boxImg = GetOrAddComponent<Image>(boxGo);
            Ps1UiTheme.ApplyPanelStyle(boxImg);

            Transform checkTr = boxGo.transform.Find("Checkmark");
            GameObject checkGo = checkTr != null ? checkTr.gameObject : CreateUiObject("Checkmark", boxGo.transform);
            RectTransform checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(0.20f, 0.20f);
            checkRt.anchorMax = new Vector2(0.80f, 0.80f);
            checkRt.offsetMin = Vector2.zero;
            checkRt.offsetMax = Vector2.zero;

            Image checkImg = GetOrAddComponent<Image>(checkGo);
            checkImg.sprite = Ps1UiTheme.PixelDiamondSprite;
            checkImg.color = Ps1UiTheme.BoneGold;
            checkImg.raycastTarget = false;

            Toggle toggle = GetOrAddComponent<Toggle>(toggleGo);
            toggle.targetGraphic = boxImg;
            toggle.graphic = checkImg;

            return toggle;
        }

        private Text BuildOrBindText(
            Transform parent,
            string name,
            string content,
            int fontSize,
            FontStyle fontStyle,
            Color color,
            TextAnchor alignment,
            Vector2 anchoredPos,
            Vector2 size)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : CreateUiObject(name, parent);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            Text txt = GetOrAddComponent<Text>(go);
            txt.font = _uiFont;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.fontStyle = fontStyle;
            txt.color = color;
            txt.alignment = alignment;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;
            return txt;
        }

        private void BuildOrBindFadeOverlay(Transform canvasTransform)
        {
            if (fadeOverlayGroup == null)
            {
                Transform existing = canvasTransform.Find("FadeOverlay");
                if (existing != null)
                {
                    fadeOverlayGroup = existing.GetComponent<CanvasGroup>();
                    fadeOverlayImage = existing.GetComponent<Image>();
                }
            }

            if (fadeOverlayGroup == null)
            {
                GameObject fadeGo = CreateUiObject("FadeOverlay", canvasTransform);
                fadeOverlayImage = GetOrAddComponent<Image>(fadeGo);
                fadeOverlayGroup = GetOrAddComponent<CanvasGroup>(fadeGo);
            }

            RectTransform fadeRt = fadeOverlayGroup.GetComponent<RectTransform>();
            fadeRt.anchorMin = Vector2.zero;
            fadeRt.anchorMax = Vector2.one;
            fadeRt.offsetMin = Vector2.zero;
            fadeRt.offsetMax = Vector2.zero;
            fadeRt.SetAsLastSibling();

            if (fadeOverlayImage == null)
            {
                fadeOverlayImage = GetOrAddComponent<Image>(fadeOverlayGroup.gameObject);
            }
            fadeOverlayImage.sprite = _whiteSprite;
            fadeOverlayImage.color = Color.black;
            fadeOverlayImage.raycastTarget = true;

            // Keep transparent in Edit Mode so Scene/Game View previews are not obscured by black;
            // Start() sets alpha = 1 and smoothly fades 1 -> 0 over 1.2s when Play Mode begins.
            if (!Application.isPlaying)
            {
                fadeOverlayGroup.alpha = 0f;
                fadeOverlayGroup.blocksRaycasts = false;
                fadeOverlayGroup.interactable = false;
            }
        }

        /// <summary>
        /// Ensures an EventSystem exists in the active scene with InputSystemUIInputModule (if InputSystem is active)
        /// or StandaloneInputModule fallback.
        /// </summary>
        public static EventSystem EnsureEventSystem()
        {
            EventSystem es = FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                es = esGo.AddComponent<EventSystem>();
            }

            if (es.GetComponent<BaseInputModule>() == null)
            {
                Type inputSystemModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (inputSystemModuleType != null)
                {
                    es.gameObject.AddComponent(inputSystemModuleType);
                }
                else
                {
                    es.gameObject.AddComponent<StandaloneInputModule>();
                }
            }

            return es;
        }

        private void BindRuntimeCallbacks()
        {
            if (playButton != null)
            {
                playButton.onClick.RemoveListener(OnPlayClicked);
                playButton.onClick.AddListener(OnPlayClicked);
            }

            if (optionsButton != null)
            {
                optionsButton.onClick.RemoveListener(OnOptionsClicked);
                optionsButton.onClick.AddListener(OnOptionsClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(OnQuitClicked);
                quitButton.onClick.AddListener(OnQuitClicked);
            }

            if (optionsBackButton != null)
            {
                optionsBackButton.onClick.RemoveListener(OnCloseOptionsClicked);
                optionsBackButton.onClick.AddListener(OnCloseOptionsClicked);
            }

            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
                masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            }

            if (fullscreenToggle != null)
            {
                fullscreenToggle.onValueChanged.RemoveListener(OnFullscreenToggled);
                fullscreenToggle.onValueChanged.AddListener(OnFullscreenToggled);
            }
        }

        private void InitializeOptionsState()
        {
            float initialVol = PlayerPrefs.HasKey(PrefKeyMasterVolume)
                ? Mathf.Clamp01(PlayerPrefs.GetFloat(PrefKeyMasterVolume, 0.85f))
                : Mathf.Clamp01(AudioListener.volume > 0.001f ? AudioListener.volume : 0.85f);

            AudioListener.volume = initialVol;
            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.SetValueWithoutNotify(initialVol);
            }
            UpdateVolumeLabel(initialVol);
            ApplyVolumeToAudioController(initialVol);

            if (fullscreenToggle != null)
            {
                fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
            }
        }

        /// <summary>
        /// Auto-discovers heartController, audioController, and cameraParallax in the scene if not manually assigned.
        /// </summary>
        public void ResolveSceneControllers()
        {
            if (heartController != null && audioController != null && cameraParallax != null)
            {
                return;
            }

            MonoBehaviour[] allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < allBehaviours.Length; i++)
            {
                MonoBehaviour mb = allBehaviours[i];
                if (mb == null || mb == this) continue;

                Type t = mb.GetType();
                string typeName = t.Name;

                if (heartController == null &&
                    (typeName.IndexOf("Heart", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     HasMethod(t, "TriggerPlayTransitionBoost")))
                {
                    heartController = mb;
                }

                if (audioController == null &&
                    (typeName.IndexOf("MainMenuAudio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     HasMethod(t, "PlayStartGameImpactSfx") ||
                     HasMethod(t, "SetMasterVolume")))
                {
                    audioController = mb;
                }

                if (cameraParallax == null &&
                    (typeName.IndexOf("Parallax", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     HasMethod(t, "StartPlayZoomTransition")))
                {
                    cameraParallax = mb;
                }
            }
        }

        /// <summary>
        /// JOGAR click handler: locks UI input, triggers heart boost, impact SFX, and camera zoom transition,
        /// fades screen to black over 1.45s, and loads gameplaySceneName ("Stomach_Abyss").
        /// </summary>
        public void OnPlayClicked()
        {
            if (_isTransitioning) return;
            _isTransitioning = true;

            ResolveSceneControllers();

            // Lock UI interactions immediately
            if (menuButtonsCanvasGroup != null)
            {
                menuButtonsCanvasGroup.interactable = false;
                menuButtonsCanvasGroup.blocksRaycasts = false;
            }
            if (optionsPanelCanvasGroup != null)
            {
                optionsPanelCanvasGroup.interactable = false;
                optionsPanelCanvasGroup.blocksRaycasts = false;
            }

            // 1. Trigger Colossal Heart play transition boost
            InvokeControllerMethod(heartController, "TriggerPlayTransitionBoost", fadeOutDuration);

            // 2. Trigger dramatic start-game impact SFX on MainMenuAudioController
            if (!InvokeControllerMethod(audioController, "PlayStartGameImpactSfx"))
            {
                InvokeControllerMethod(audioController, "PlayClickSfx");
            }

            // 3. Trigger camera parallax zoom transition into the abyssal heart/throat
            InvokeControllerMethod(cameraParallax, "StartPlayZoomTransition", fadeOutDuration);

            // 4. Fade overlay to black over 1.45s and load gameplaySceneName
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }
            _fadeRoutine = StartCoroutine(PlayTransitionRoutine());
        }

        /// <summary>
        /// OPÇÕES click handler: toggles or opens the sleek translucent dark-crimson Options panel.
        /// </summary>
        public void OnOptionsClicked()
        {
            if (_isTransitioning) return;
            NotifyButtonClickSfx();
            SetOptionsPanelOpen(!_isOptionsOpen);
        }

        /// <summary>
        /// VOLTAR click handler inside the Options panel.
        /// </summary>
        public void OnCloseOptionsClicked()
        {
            if (_isTransitioning) return;

            NotifyButtonClickSfx();
            SetOptionsPanelOpen(false);
        }

        /// <summary>
        /// SAIR click handler: exits Play Mode in Editor or quits the application in standalone builds.
        /// </summary>
        public void OnQuitClicked()
        {
            if (_isTransitioning) return;

            NotifyButtonClickSfx();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        /// <summary>
        /// Volume Geral slider handler (0.0 to 1.0 -> 0% to 100%).
        /// Updates MainMenuAudioController.SetMasterVolume and AudioListener.volume.
        /// </summary>
        public void OnMasterVolumeChanged(float value01)
        {
            float clamped = Mathf.Clamp01(value01);
            AudioListener.volume = clamped;
            PlayerPrefs.SetFloat(PrefKeyMasterVolume, clamped);
            UpdateVolumeLabel(clamped);
            ApplyVolumeToAudioController(clamped);
        }

        /// <summary>
        /// Tela Cheia toggle handler.
        /// </summary>
        public void OnFullscreenToggled(bool isFullscreen)
        {
            Screen.fullScreen = isFullscreen;
            NotifyButtonClickSfx();
        }

        internal void NotifyButtonHoverSfx()
        {
            if (_isTransitioning) return;
            ResolveSceneControllers();
            if (!InvokeControllerMethod(audioController, "PlayHoverSfx"))
            {
                if (!InvokeControllerMethod(audioController, "PlayUiHoverSfx"))
                {
                    InvokeControllerMethod(audioController, "PlayButtonHoverSfx");
                }
            }
        }

        internal void NotifyButtonClickSfx()
        {
            if (_isTransitioning) return;
            ResolveSceneControllers();
            if (!InvokeControllerMethod(audioController, "PlayClickSfx"))
            {
                if (!InvokeControllerMethod(audioController, "PlayUiClickSfx"))
                {
                    InvokeControllerMethod(audioController, "PlayButtonClickSfx");
                }
            }
        }

        private void UpdateVolumeLabel(float value01)
        {
            if (masterVolumeValueText != null)
            {
                int pct = Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f);
                masterVolumeValueText.text = pct + "%";
            }
        }

        private void ApplyVolumeToAudioController(float value01)
        {
            ResolveSceneControllers();
            if (InvokeControllerMethod(audioController, "SetMasterVolume", value01))
            {
                return;
            }

            // Also support static MainMenuAudioController.SetMasterVolume(float) if invoked before instance resolution
            Type audioType = Type.GetType("StomachAbyss.MainMenu.MainMenuAudioController");
            if (audioType != null)
            {
                MethodInfo staticSet = audioType.GetMethod(
                    "SetMasterVolume",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(float) },
                    null);
                if (staticSet != null)
                {
                    try { staticSet.Invoke(null, new object[] { value01 }); } catch { }
                }
            }
        }

        private void SetOptionsPanelOpen(bool open)
        {
            _isOptionsOpen = open;
            if (optionsPanelRoot == null || optionsPanelCanvasGroup == null)
            {
                return;
            }

            if (_optionsAnimRoutine != null)
            {
                StopCoroutine(_optionsAnimRoutine);
            }
            _optionsAnimRoutine = StartCoroutine(AnimateOptionsPanelRoutine(open));
        }

        private IEnumerator AnimateOptionsPanelRoutine(bool open)
        {
            if (open)
            {
                optionsPanelRoot.SetActive(true);
            }

            optionsPanelCanvasGroup.interactable = open;
            optionsPanelCanvasGroup.blocksRaycasts = open;

            float startAlpha = optionsPanelCanvasGroup.alpha;
            float targetAlpha = open ? 1f : 0f;
            const float duration = 0.18f;
            float elapsed = 0f;

            RectTransform rt = optionsPanelRoot.GetComponent<RectTransform>();
            Vector2 basePos = new Vector2(432f, 64f);
            Vector2 startPos = open ? basePos + new Vector2(-18f, 0f) : rt.anchoredPosition;
            Vector2 endPos = open ? basePos : basePos + new Vector2(-14f, 0f);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smooth = 1f - Mathf.Pow(1f - t, 3f);
                optionsPanelCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, smooth);
                rt.anchoredPosition = Vector2.Lerp(startPos, endPos, smooth);
                yield return null;
            }

            optionsPanelCanvasGroup.alpha = targetAlpha;
            rt.anchoredPosition = basePos;

            if (!open)
            {
                optionsPanelRoot.SetActive(false);
            }

            _optionsAnimRoutine = null;
        }

        private IEnumerator FadeOverlayRoutine(float fromAlpha, float toAlpha, float duration, bool disableRaycastOnEnd)
        {
            if (fadeOverlayGroup == null) yield break;

            fadeOverlayGroup.gameObject.SetActive(true);
            fadeOverlayGroup.alpha = fromAlpha;
            fadeOverlayGroup.blocksRaycasts = true;
            fadeOverlayGroup.interactable = true;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smooth = t * t * (3f - 2f * t);
                fadeOverlayGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, smooth);
                yield return null;
            }

            fadeOverlayGroup.alpha = toAlpha;
            if (disableRaycastOnEnd)
            {
                fadeOverlayGroup.blocksRaycasts = false;
                fadeOverlayGroup.interactable = false;
            }

            _fadeRoutine = null;
        }

        private IEnumerator PlayTransitionRoutine()
        {
            float duration = Mathf.Max(0.2f, fadeOutDuration);
            yield return FadeOverlayRoutine(
                fadeOverlayGroup != null ? fadeOverlayGroup.alpha : 0f,
                1f,
                duration,
                disableRaycastOnEnd: false);

            LoadGameplaySceneWithFallback();
        }

        private void LoadGameplaySceneWithFallback()
        {
            string target = string.IsNullOrWhiteSpace(gameplaySceneName) ? "Stomach_Abyss" : gameplaySceneName.Trim();

            if (Application.CanStreamedLevelBeLoaded(target))
            {
                SceneManager.LoadScene(target);
                return;
            }

            if (Application.CanStreamedLevelBeLoaded("Stomach_Abyss"))
            {
                SceneManager.LoadScene("Stomach_Abyss");
                return;
            }

            if (Application.CanStreamedLevelBeLoaded("SampleScene"))
            {
                SceneManager.LoadScene("SampleScene");
                return;
            }

            if (SceneManager.sceneCountInBuildSettings > 1)
            {
                SceneManager.LoadScene(1);
                return;
            }

#if UNITY_EDITOR
            string[] candidatePaths =
            {
                "Assets/" + target + ".unity",
                "Assets/Scenes/" + target + ".unity",
                "Assets/Stomach_Abyss.unity",
                "Assets/Scenes/SampleScene.unity"
            };

            for (int i = 0; i < candidatePaths.Length; i++)
            {
                if (System.IO.File.Exists(candidatePaths[i]))
                {
                    UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                        candidatePaths[i],
                        new LoadSceneParameters(LoadSceneMode.Single));
                    return;
                }
            }
#endif

            // Final direct attempt
            SceneManager.LoadScene(target);
        }

        private static bool HasMethod(Type type, string methodName)
        {
            if (type == null) return false;
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                if (string.Equals(methods[i].Name, methodName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool InvokeControllerMethod(MonoBehaviour target, string methodName, params object[] preferredArgs)
        {
            if (target == null || string.IsNullOrEmpty(methodName)) return false;

            Type type = target.GetType();
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (!string.Equals(m.Name, methodName, StringComparison.Ordinal)) continue;

                ParameterInfo[] parameters = m.GetParameters();
                object instance = m.IsStatic ? null : target;

                try
                {
                    if (parameters.Length == 0)
                    {
                        m.Invoke(instance, null);
                        return true;
                    }

                    if (preferredArgs != null && parameters.Length == preferredArgs.Length)
                    {
                        m.Invoke(instance, preferredArgs);
                        return true;
                    }

                    if (parameters.Length == 1 && preferredArgs != null && preferredArgs.Length >= 1)
                    {
                        m.Invoke(instance, new[] { preferredArgs[0] });
                        return true;
                    }

                    // Support optional default parameters if present
                    object[] callArgs = new object[parameters.Length];
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        if (preferredArgs != null && p < preferredArgs.Length)
                        {
                            callArgs[p] = preferredArgs[p];
                        }
                        else if (parameters[p].HasDefaultValue)
                        {
                            callArgs[p] = parameters[p].DefaultValue;
                        }
                        else
                        {
                            callArgs[p] = parameters[p].ParameterType.IsValueType
                                ? Activator.CreateInstance(parameters[p].ParameterType)
                                : null;
                        }
                    }
                    m.Invoke(instance, callArgs);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[MainMenuUIController] Failed invoking " + type.Name + "." + methodName + ": " + e.Message);
                }
            }

            return false;
        }

        private static bool WasEscapePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                return true;
            }
#endif
            return false;
        }

        private void EnsureProceduralResources()
        {
            if (_uiFont == null)
            {
                _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_uiFont == null)
                {
                    _uiFont = Font.CreateDynamicFontFromOSFont("Arial", 22);
                }
            }

#if UNITY_EDITOR
            if (_whiteSprite == null)
            {
                _whiteSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MainMenu/Textures/tex_ui_white.png");
                _diamondSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MainMenu/Textures/tex_ui_diamond.png");
                _buttonGradientSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MainMenu/Textures/tex_ui_button_gradient.png");
                _cornerVignetteSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MainMenu/Textures/tex_ui_corner_vignette.png");
            }
#endif

            if (_whiteSprite == null)
            {
                Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
                {
                    name = "MainMenu_White_Proc",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                Color[] cols = new Color[16];
                for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
                tex.SetPixels(cols);
                tex.Apply();
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            }

            if (_diamondSprite == null)
            {
                const int size = 32;
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "MainMenu_Diamond_Proc",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                float half = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Abs(x - half) / half;
                        float dy = Mathf.Abs(y - half) / half;
                        float manhattan = dx + dy;
                        float alpha = Mathf.Clamp01((1.02f - manhattan) * 7f);
                        // Subtle inner arterial ring glow
                        float core = Mathf.Clamp01(1f - manhattan * 0.85f);
                        tex.SetPixel(x, y, new Color(core, core, core, alpha));
                    }
                }
                tex.Apply();
                _diamondSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            }

            if (_buttonGradientSprite == null)
            {
                const int w = 64;
                const int h = 16;
                Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    name = "MainMenu_ButtonGrad_Proc",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                for (int y = 0; y < h; y++)
                {
                    float v = y / (float)(h - 1);
                    float vertShade = Mathf.Lerp(0.82f, 1.0f, 1f - Mathf.Abs(v - 0.5f) * 1.4f);
                    for (int x = 0; x < w; x++)
                    {
                        float u = x / (float)(w - 1);
                        // Dense obsidian on the left fading smoothly toward the right edge
                        float alpha = Mathf.Lerp(1.0f, 0.22f, Mathf.Pow(u, 1.35f));
                        tex.SetPixel(x, y, new Color(vertShade, vertShade, vertShade, alpha));
                    }
                }
                tex.Apply();
                _buttonGradientSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            }

            if (_cornerVignetteSprite == null)
            {
                const int size = 64;
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "MainMenu_CornerVignette_Proc",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                for (int y = 0; y < size; y++)
                {
                    float ny = y / (float)(size - 1);
                    for (int x = 0; x < size; x++)
                    {
                        float nx = x / (float)(size - 1);
                        float dist = Mathf.Sqrt(nx * nx + ny * ny);
                        float alpha = Mathf.Pow(Mathf.Clamp01(1f - dist), 1.85f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
                tex.Apply();
                _cornerVignetteSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0f, 0f));
            }
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            T comp = go.GetComponent<T>();
            if (comp == null)
            {
                comp = go.AddComponent<T>();
            }
            return comp;
        }
    }
}
