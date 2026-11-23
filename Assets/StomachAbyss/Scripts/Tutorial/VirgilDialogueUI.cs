using System;
using System.Collections;
using MapaEstomago;
using StomachAbyss.UI;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StomachAbyss.Tutorial
{
    /// <summary>
    /// Runtime PS1-gothic dialogue, interaction prompt, and area banner UI for O Vestígio
    /// and tutorial arrival notifications inside the Stomach Abyss.
    /// </summary>
    public class VirgilDialogueUI : MonoBehaviour
    {
        private static VirgilDialogueUI s_instance;

        private const float TypewriterCharsPerSecond = 54f;
        private const float DialogueFadeDuration = 0.18f;

        private Font _font;
        private Canvas _canvas;

        // Dialogue panel widgets
        private GameObject _dialogueRoot;
        private CanvasGroup _dialogueGroup;
        private UnityEngine.UI.Text _speakerNameText;
        private UnityEngine.UI.Text _speakerTitleText;
        private UnityEngine.UI.Text _pageCounterText;
        private UnityEngine.UI.Text _bodyText;
        private GameObject _hintBadgeRoot;
        private UnityEngine.UI.Text _hintBadgeText;
        private UnityEngine.UI.Text _continuePromptText;

        // Interaction prompt widgets
        private GameObject _promptRoot;
        private UnityEngine.UI.Text _promptText;

        // Area arrival banner widgets
        private GameObject _bannerRoot;
        private CanvasGroup _bannerGroup;
        private UnityEngine.UI.Text _bannerTitleText;
        private UnityEngine.UI.Text _bannerSubtitleText;

        private VirgilDialoguePage[] _pages;
        private int _pageIndex;
        private Action _onComplete;
        private int _openedFrame = -1;
        private int _closedFrame = -1;

        private string _currentFullText = string.Empty;
        private float _typewriterChars;
        private bool _isTyping;
        private float _dialogueAlphaTarget;

        private Coroutine _bannerCoroutine;
        private SimplePlayerController _cachedPlayer;

        public bool IsOpen { get; private set; }
        internal bool WasClosedThisFrame => Time.frameCount <= _closedFrame;

        public static VirgilDialogueUI GetOrCreate()
        {
            if (s_instance != null)
            {
                s_instance.EnsureBuilt();
                return s_instance;
            }

            s_instance = FindAnyObjectByType<VirgilDialogueUI>();
            if (s_instance != null)
            {
                s_instance.EnsureBuilt();
                return s_instance;
            }

            GameObject host = new GameObject("VirgilDialogueUI");
            s_instance = host.AddComponent<VirgilDialogueUI>();
            s_instance.EnsureBuilt();
            return s_instance;
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            EnsureBuilt();
        }

        private void OnDisable()
        {
            if (IsOpen)
            {
                IsOpen = false;
                LockPlayerInput(false);
            }
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        public void OpenDialogue(
            string speakerName,
            string speakerTitle,
            VirgilDialoguePage[] pages,
            Action onComplete = null)
        {
            if (pages == null || pages.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            EnsureBuilt();

            _pages = pages;
            _pageIndex = 0;
            _onComplete = onComplete;
            _openedFrame = Time.frameCount;
            IsOpen = true;
            _dialogueAlphaTarget = 1f;

            if (_promptRoot != null)
            {
                _promptRoot.SetActive(false);
            }

            if (_speakerNameText != null)
            {
                _speakerNameText.text = string.IsNullOrEmpty(speakerName) ? "VESTÍGIO" : speakerName;
            }

            if (_speakerTitleText != null)
            {
                bool hasTitle = !string.IsNullOrEmpty(speakerTitle);
                _speakerTitleText.gameObject.SetActive(hasTitle);
                _speakerTitleText.text = hasTitle ? speakerTitle : string.Empty;
            }

            if (_dialogueRoot != null)
            {
                _dialogueRoot.SetActive(true);
            }

            if (_dialogueGroup != null && _dialogueGroup.alpha < 0.05f)
            {
                _dialogueGroup.alpha = 0.05f;
            }

            LockPlayerInput(true);
            LoadPage(0);
        }

        public void ShowInteractPrompt(bool visible, string promptText = "[E] Falar com O Vestígio")
        {
            EnsureBuilt();
            if (_promptRoot == null) return;

            if (!visible || IsOpen)
            {
                _promptRoot.SetActive(false);
                return;
            }

            if (_promptText != null)
            {
                _promptText.text = string.IsNullOrEmpty(promptText)
                    ? "[E] Falar com O Vestígio"
                    : promptText;
            }

            _promptRoot.SetActive(true);
        }

        public void ShowBannerToast(string title, string subtitle, float duration = 3.5f)
        {
            EnsureBuilt();
            if (_bannerRoot == null || _bannerGroup == null) return;

            if (_bannerTitleText != null)
            {
                _bannerTitleText.text = title ?? string.Empty;
            }

            if (_bannerSubtitleText != null)
            {
                bool hasSub = !string.IsNullOrEmpty(subtitle);
                _bannerSubtitleText.gameObject.SetActive(hasSub);
                _bannerSubtitleText.text = hasSub ? subtitle : string.Empty;
            }

            if (_bannerCoroutine != null)
            {
                StopCoroutine(_bannerCoroutine);
            }

            _bannerRoot.SetActive(true);
            _bannerCoroutine = StartCoroutine(BannerToastRoutine(Mathf.Max(0.5f, duration)));
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (_dialogueGroup != null && _dialogueRoot != null && _dialogueRoot.activeSelf)
            {
                float step = dt / Mathf.Max(0.01f, DialogueFadeDuration);
                _dialogueGroup.alpha = Mathf.MoveTowards(_dialogueGroup.alpha, _dialogueAlphaTarget, step);
                if (!IsOpen && _dialogueGroup.alpha <= 0.001f)
                {
                    _dialogueRoot.SetActive(false);
                }
            }

            if (!IsOpen) return;

            if (_isTyping)
            {
                _typewriterChars += dt * TypewriterCharsPerSecond;
                int visibleCount = Mathf.Clamp(Mathf.FloorToInt(_typewriterChars), 0, _currentFullText.Length);
                if (_bodyText != null)
                {
                    _bodyText.text = _currentFullText.Substring(0, visibleCount);
                }

                if (visibleCount >= _currentFullText.Length)
                {
                    _isTyping = false;
                }
            }

            if (_continuePromptText != null)
            {
                float pulse = _isTyping ? 0.45f : (0.68f + 0.32f * Mathf.Sin(Time.unscaledTime * 3.8f));
                Color c = _continuePromptText.color;
                c.a = pulse;
                _continuePromptText.color = c;
            }

            // Ignore the frame in which OpenDialogue was triggered so pressing [E] to talk
            // never skips or finishes page 1 immediately.
            if (Time.frameCount <= _openedFrame) return;

            PollDialogueInput(out bool advancePressed, out bool closeEarlyPressed);

            if (closeEarlyPressed)
            {
                CloseDialogue();
                return;
            }

            if (advancePressed)
            {
                if (_isTyping)
                {
                    CompleteCurrentPageTyping();
                }
                else if (_pages != null && _pageIndex + 1 < _pages.Length)
                {
                    LoadPage(_pageIndex + 1);
                }
                else
                {
                    CloseDialogue();
                }
            }
        }

        private static void PollDialogueInput(out bool advancePressed, out bool closeEarlyPressed)
        {
            advancePressed = false;
            closeEarlyPressed = false;

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    closeEarlyPressed = true;
                }

                if (kb.eKey.wasPressedThisFrame ||
                    kb.spaceKey.wasPressedThisFrame ||
                    kb.enterKey.wasPressedThisFrame ||
                    kb.numpadEnterKey.wasPressedThisFrame)
                {
                    advancePressed = true;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                advancePressed = true;
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                closeEarlyPressed = true;
            }

            if (Input.GetKeyDown(KeyCode.E) ||
                Input.GetKeyDown(KeyCode.Space) ||
                Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.KeypadEnter) ||
                Input.GetMouseButtonDown(0))
            {
                advancePressed = true;
            }
#endif
        }

        private void LoadPage(int index)
        {
            if (_pages == null || index < 0 || index >= _pages.Length) return;

            _pageIndex = index;
            VirgilDialoguePage page = _pages[index];
            _currentFullText = page.text ?? string.Empty;
            _typewriterChars = 0f;
            _isTyping = _currentFullText.Length > 0;

            if (_bodyText != null)
            {
                _bodyText.text = string.Empty;
            }

            if (_pageCounterText != null)
            {
                _pageCounterText.text = $"{index + 1} / {_pages.Length}";
            }

            if (_hintBadgeRoot != null && _hintBadgeText != null)
            {
                bool hasHint = !string.IsNullOrWhiteSpace(page.hintBadge);
                _hintBadgeRoot.SetActive(hasHint);
                _hintBadgeText.text = hasHint ? page.hintBadge.Trim() : string.Empty;
            }

            if (_continuePromptText != null)
            {
                bool isLast = index >= _pages.Length - 1;
                _continuePromptText.text = isLast
                    ? "[E] / [Espaço] / Clique para encerrar"
                    : "[E] / [Espaço] / Clique para continuar";
            }
        }

        private void CompleteCurrentPageTyping()
        {
            _isTyping = false;
            _typewriterChars = _currentFullText.Length;
            if (_bodyText != null)
            {
                _bodyText.text = _currentFullText;
            }
        }

        private void CloseDialogue()
        {
            if (!IsOpen) return;

            IsOpen = false;
            _closedFrame = Time.frameCount;
            _dialogueAlphaTarget = 0f;

            if (_dialogueRoot != null)
            {
                _dialogueRoot.SetActive(false);
            }

            LockPlayerInput(false);

            Action callback = _onComplete;
            _onComplete = null;
            _pages = null;
            callback?.Invoke();
        }

        private void LockPlayerInput(bool locked)
        {
            if (_cachedPlayer == null || !_cachedPlayer.gameObject.activeInHierarchy)
            {
                GameObject playerGo = null;
                try { playerGo = GameObject.FindGameObjectWithTag("Player"); } catch { }
                if (playerGo != null)
                {
                    _cachedPlayer = playerGo.GetComponent<SimplePlayerController>();
                }
                if (_cachedPlayer == null)
                {
                    _cachedPlayer = FindAnyObjectByType<SimplePlayerController>();
                }
            }

            if (_cachedPlayer != null)
            {
                _cachedPlayer.SetInputLocked(locked);
            }
        }

        private IEnumerator BannerToastRoutine(float holdDuration)
        {
            const float fadeIn = 0.35f;
            const float fadeOut = 0.55f;

            float elapsed = 0f;
            while (elapsed < fadeIn)
            {
                elapsed += Time.unscaledDeltaTime;
                _bannerGroup.alpha = Mathf.Clamp01(elapsed / fadeIn);
                yield return null;
            }
            _bannerGroup.alpha = 1f;

            float held = 0f;
            while (held < holdDuration)
            {
                held += Time.unscaledDeltaTime;
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < fadeOut)
            {
                elapsed += Time.unscaledDeltaTime;
                _bannerGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeOut);
                yield return null;
            }

            _bannerGroup.alpha = 0f;
            _bannerRoot.SetActive(false);
            _bannerCoroutine = null;
        }

        private void EnsureBuilt()
        {
            if (_canvas != null) return;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont("Arial", 20);
            }

            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null)
            {
                _canvas = gameObject.AddComponent<Canvas>();
            }
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 220;

            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            BuildBannerToast();
            BuildInteractPrompt();
            BuildDialoguePanel();
        }

        private void BuildBannerToast()
        {
            _bannerRoot = CreateUiObject("AreaBannerToast", transform);
            RectTransform rt = _bannerRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -44f);
            rt.sizeDelta = new Vector2(720f, 96f);

            _bannerGroup = _bannerRoot.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
            _bannerGroup.blocksRaycasts = false;
            _bannerGroup.interactable = false;

            UnityEngine.UI.Image bg = _bannerRoot.AddComponent<UnityEngine.UI.Image>();
            Ps1UiTheme.ApplyPanelStyle(bg);
            bg.raycastTarget = false;

            // Top thin aged-bone accent line
            GameObject topLine = CreateUiObject("AccentLine", _bannerRoot.transform);
            RectTransform lineRt = topLine.GetComponent<RectTransform>();
            lineRt.anchorMin = new Vector2(0.08f, 1f);
            lineRt.anchorMax = new Vector2(0.92f, 1f);
            lineRt.pivot = new Vector2(0.5f, 1f);
            lineRt.anchoredPosition = new Vector2(0f, -10f);
            lineRt.sizeDelta = new Vector2(0f, 2f);
            UnityEngine.UI.Image lineImg = topLine.AddComponent<UnityEngine.UI.Image>();
            lineImg.color = Ps1UiTheme.BoneTrimMuted;
            lineImg.raycastTarget = false;

            _bannerTitleText = CreateText(
                "BannerTitle",
                _bannerRoot.transform,
                28,
                FontStyle.Bold,
                Ps1UiTheme.BoneGold,
                TextAnchor.MiddleCenter);
            RectTransform titleRt = _bannerTitleText.rectTransform;
            titleRt.anchorMin = new Vector2(0.04f, 0.42f);
            titleRt.anchorMax = new Vector2(0.96f, 0.88f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;

            _bannerSubtitleText = CreateText(
                "BannerSubtitle",
                _bannerRoot.transform,
                17,
                FontStyle.Normal,
                Ps1UiTheme.BoneMutedText,
                TextAnchor.MiddleCenter);
            RectTransform subRt = _bannerSubtitleText.rectTransform;
            subRt.anchorMin = new Vector2(0.06f, 0.12f);
            subRt.anchorMax = new Vector2(0.94f, 0.44f);
            subRt.offsetMin = Vector2.zero;
            subRt.offsetMax = Vector2.zero;

            _bannerRoot.SetActive(false);
        }

        private void BuildInteractPrompt()
        {
            _promptRoot = CreateUiObject("InteractPromptPill", transform);
            RectTransform rt = _promptRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 198f);
            rt.sizeDelta = new Vector2(380f, 50f);

            UnityEngine.UI.Image bg = _promptRoot.AddComponent<UnityEngine.UI.Image>();
            Ps1UiTheme.ApplyPanelStyle(bg);
            bg.raycastTarget = false;

            UnityEngine.UI.HorizontalLayoutGroup layout = _promptRoot.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 12, 12);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            UnityEngine.UI.ContentSizeFitter fitter = _promptRoot.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            _promptText = CreateText(
                "PromptLabel",
                _promptRoot.transform,
                21,
                FontStyle.Bold,
                Ps1UiTheme.BoneGold,
                TextAnchor.MiddleCenter);
            _promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _promptText.verticalOverflow = VerticalWrapMode.Overflow;

            _promptRoot.SetActive(false);
        }

        private void BuildDialoguePanel()
        {
            _dialogueRoot = CreateUiObject("VirgilDialoguePanel", transform);
            RectTransform rt = _dialogueRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 32f);
            rt.sizeDelta = new Vector2(1140f, 276f);

            _dialogueGroup = _dialogueRoot.AddComponent<CanvasGroup>();
            _dialogueGroup.alpha = 0f;
            _dialogueGroup.blocksRaycasts = false;
            _dialogueGroup.interactable = false;

            // Opcao 1: Caixa obsidiana fosca com borda dupla pixelada em osso envelhecido
            UnityEngine.UI.Image bg = _dialogueRoot.AddComponent<UnityEngine.UI.Image>();
            Ps1UiTheme.ApplyPanelStyle(bg);
            bg.raycastTarget = false;

            // Faixa superior discreta em carvao obsidiana
            GameObject headerBar = CreateUiObject("HeaderStrip", _dialogueRoot.transform);
            RectTransform headerRt = headerBar.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -8f);
            headerRt.sizeDelta = new Vector2(-16f, 54f);
            UnityEngine.UI.Image headerImg = headerBar.AddComponent<UnityEngine.UI.Image>();
            headerImg.color = Ps1UiTheme.ObsidianHeader;
            headerImg.raycastTarget = false;

            // Divisoria pixelada fina em osso envelhecido abaixo do cabecalho
            GameObject sep = CreateUiObject("HeaderDivider", _dialogueRoot.transform);
            RectTransform sepRt = sep.GetComponent<RectTransform>();
            sepRt.anchorMin = new Vector2(0f, 1f);
            sepRt.anchorMax = new Vector2(1f, 1f);
            sepRt.pivot = new Vector2(0.5f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -62f);
            sepRt.sizeDelta = new Vector2(-24f, 2f);
            UnityEngine.UI.Image sepImg = sep.AddComponent<UnityEngine.UI.Image>();
            sepImg.color = Ps1UiTheme.BoneTrimMuted;
            sepImg.raycastTarget = false;

            _speakerNameText = CreateText(
                "SpeakerName",
                headerBar.transform,
                28,
                FontStyle.Bold,
                Ps1UiTheme.BoneGold,
                TextAnchor.MiddleLeft);
            RectTransform nameRt = _speakerNameText.rectTransform;
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(0.32f, 1f);
            nameRt.offsetMin = new Vector2(24f, 2f);
            nameRt.offsetMax = new Vector2(0f, -2f);

            _speakerTitleText = CreateText(
                "SpeakerTitle",
                headerBar.transform,
                17,
                FontStyle.Italic,
                Ps1UiTheme.BoneMutedText,
                TextAnchor.MiddleLeft);
            RectTransform titleRt = _speakerTitleText.rectTransform;
            titleRt.anchorMin = new Vector2(0.24f, 0f);
            titleRt.anchorMax = new Vector2(0.86f, 1f);
            titleRt.offsetMin = new Vector2(10f, 2f);
            titleRt.offsetMax = new Vector2(-10f, -2f);

            _pageCounterText = CreateText(
                "PageCounter",
                headerBar.transform,
                17,
                FontStyle.Bold,
                Ps1UiTheme.BoneMutedText,
                TextAnchor.MiddleRight);
            RectTransform countRt = _pageCounterText.rectTransform;
            countRt.anchorMin = new Vector2(0.86f, 0f);
            countRt.anchorMax = new Vector2(1f, 1f);
            countRt.offsetMin = new Vector2(0f, 2f);
            countRt.offsetMax = new Vector2(-24f, -2f);

            // Texto principal do dialogo
            _bodyText = CreateText(
                "DialogueBody",
                _dialogueRoot.transform,
                23,
                FontStyle.Normal,
                Ps1UiTheme.BoneWhiteText,
                TextAnchor.UpperLeft);
            _bodyText.lineSpacing = 1.20f;
            RectTransform bodyRt = _bodyText.rectTransform;
            bodyRt.anchorMin = new Vector2(0f, 0f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.offsetMin = new Vector2(34f, 66f);
            bodyRt.offsetMax = new Vector2(-34f, -76f);

            // Etiqueta inferior esquerda em carmesim/osso discreto (sem azul ciano destoante)
            _hintBadgeRoot = CreateUiObject("TutorialHintPill", _dialogueRoot.transform);
            RectTransform hintRt = _hintBadgeRoot.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0f, 0f);
            hintRt.anchorMax = new Vector2(0f, 0f);
            hintRt.pivot = new Vector2(0f, 0f);
            hintRt.anchoredPosition = new Vector2(30f, 18f);
            hintRt.sizeDelta = new Vector2(680f, 38f);

            UnityEngine.UI.Image hintBg = _hintBadgeRoot.AddComponent<UnityEngine.UI.Image>();
            Ps1UiTheme.ApplyPanelStyle(hintBg, isBadge: true);
            hintBg.raycastTarget = false;

            UnityEngine.UI.HorizontalLayoutGroup hintLayout = _hintBadgeRoot.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            hintLayout.padding = new RectOffset(18, 18, 7, 7);
            hintLayout.childAlignment = TextAnchor.MiddleLeft;
            hintLayout.childControlWidth = true;
            hintLayout.childControlHeight = true;
            hintLayout.childForceExpandWidth = false;
            hintLayout.childForceExpandHeight = false;

            UnityEngine.UI.ContentSizeFitter hintFitter = _hintBadgeRoot.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            hintFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            hintFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            _hintBadgeText = CreateText(
                "HintBadgeLabel",
                _hintBadgeRoot.transform,
                16,
                FontStyle.Bold,
                Ps1UiTheme.CrimsonTagText,
                TextAnchor.MiddleLeft);
            _hintBadgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hintBadgeText.verticalOverflow = VerticalWrapMode.Overflow;
            _hintBadgeRoot.SetActive(false);

            // Indicador de continuar no canto inferior direito + losango pixelado PS1
            _continuePromptText = CreateText(
                "ContinuePrompt",
                _dialogueRoot.transform,
                16,
                FontStyle.Bold,
                Ps1UiTheme.BoneGold,
                TextAnchor.MiddleRight);
            RectTransform contRt = _continuePromptText.rectTransform;
            contRt.anchorMin = new Vector2(0.52f, 0f);
            contRt.anchorMax = new Vector2(1f, 0f);
            contRt.pivot = new Vector2(1f, 0f);
            contRt.anchoredPosition = new Vector2(-48f, 18f);
            contRt.sizeDelta = new Vector2(0f, 34f);

            GameObject diamondGo = CreateUiObject("ContinueDiamond", _dialogueRoot.transform);
            RectTransform dRt = diamondGo.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(1f, 0f);
            dRt.anchorMax = new Vector2(1f, 0f);
            dRt.pivot = new Vector2(0.5f, 0.5f);
            dRt.anchoredPosition = new Vector2(-30f, 35f);
            dRt.sizeDelta = new Vector2(12f, 12f);
            UnityEngine.UI.Image dImg = diamondGo.AddComponent<UnityEngine.UI.Image>();
            dImg.sprite = Ps1UiTheme.PixelDiamondSprite;
            dImg.color = Ps1UiTheme.BoneGold;
            dImg.raycastTarget = false;

            _dialogueRoot.SetActive(false);
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private UnityEngine.UI.Text CreateText(
            string name,
            Transform parent,
            int fontSize,
            FontStyle style,
            Color color,
            TextAnchor alignment)
        {
            GameObject go = CreateUiObject(name, parent);
            UnityEngine.UI.Text txt = go.AddComponent<UnityEngine.UI.Text>();
            txt.font = _font;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = alignment;
            txt.raycastTarget = false;
            txt.supportRichText = true;

            UnityEngine.UI.Shadow shadow = go.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.88f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);

            return txt;
        }

        private static void AddBorder(GameObject target, Color borderColor)
        {
            UnityEngine.UI.Outline outline = target.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = borderColor;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;
        }
    }
}
