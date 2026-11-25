using System;
using MapaEstomago;
using StomachAbyss.Combat;
using StomachAbyss.Progression;
using StomachAbyss.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace StomachAbyss.Bonfire
{
    /// <summary>
    /// Interface PS1 (Obsidiana & Osso Envelhecido) do Menu/Mapa 3D das Portas Vivas.
    /// Aberta quando o jogador entra em uma porta ja visitada (apos a mastigacao).
    /// Permite girar o mapa 3D com o mouse, clicar em uma das 4 portas descobertas para viajar
    /// ou sair (sendo cuspido de volta pela porta atual, recarregando inimigos).
    /// </summary>
    public class BonfireMapUI : MonoBehaviour
    {
        private static BonfireMapUI s_instance;
        private static int s_lastCloseFrame = -10;

        public static bool IsOpen => s_instance != null && s_instance._isOpen;
        public static bool WasClosedThisFrame => s_lastCloseFrame == Time.frameCount;

        private Canvas _canvas;
        private Font _font;
        private GameObject _rootBackdrop;
        private RawImage _mapViewportImage;
        private RectTransform _mapViewportRect;
        private Text _statusText;
        private Text _toastText;
        private Button _upgradeButton;
        private Text _upgradeButtonLabel;
        private Button _travelButton;
        private Image _travelButtonImage;
        private Text _travelButtonLabel;
        private Button _exitButton;

        private BonfireMap3DViewer _viewer3D;

        private bool _isOpen;
        private bool _isUpgradeSubmenuOpen;
        private int _upgradeSubmenuClosedFrame = -10;
        private int _currentSlot;
        private int _selectedSlot = -1;
        private int _hoveredSlot = -1;
        private readonly bool[] _unlockedSlots = new bool[BonfireDoorNetwork.TotalMapSlots];

        private Action<int> _onTravelCallback;
        private Action _onExitCallback;

        private bool _isDragging;
        private Vector2 _lastPointerPos;
        private float _lastClickTime;
        private int _lastClickedSlot = -1;
        private float _toastTimer;

        private CursorLockMode _prevCursorLock;
        private bool _prevCursorVisible;
        private float _prevTimeScale = 1f;

        public static void Show(int currentSlotIndex, Action<int> onTravelToSlot, Action onExitSpitOut)
        {
            EnsureInstance();
            s_instance.OpenInternal(currentSlotIndex, onTravelToSlot, onExitSpitOut);
        }

        public static void ForceCloseImmediate()
        {
            if (VirgilMutationMenuUI.IsOpen)
            {
                VirgilMutationMenuUI.Close();
            }
            if (s_instance != null && s_instance._isOpen)
            {
                s_instance.CloseInternal(invokeExit: false);
            }
        }

        private static void EnsureInstance()
        {
            if (s_instance != null) return;
            s_instance = FindAnyObjectByType<BonfireMapUI>();
            if (s_instance == null)
            {
                GameObject go = new GameObject("BonfireMap_UI_System");
                s_instance = go.AddComponent<BonfireMapUI>();
            }
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_instance = this;
            BuildViewerAndUi();
        }

        private void OnDestroy()
        {
            if (_isOpen)
            {
                Time.timeScale = 1f;
                _isOpen = false;
            }
            if (_viewer3D != null)
            {
                Destroy(_viewer3D.gameObject);
                _viewer3D = null;
            }
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        private void OpenInternal(int currentSlotIndex, Action<int> onTravelToSlot, Action onExitSpitOut)
        {
            EnsureEventSystem();
            BonfireDoorNetwork.RefreshSceneDoors();

            _currentSlot = Mathf.Clamp(currentSlotIndex, 0, BonfireDoorNetwork.TotalMapSlots - 1);
            _selectedSlot = -1;
            _hoveredSlot = -1;
            _isDragging = false;
            _isUpgradeSubmenuOpen = false;
            _onTravelCallback = onTravelToSlot;
            _onExitCallback = onExitSpitOut;

            for (int i = 0; i < BonfireDoorNetwork.TotalMapSlots; i++)
            {
                _unlockedSlots[i] = BonfireDoorNetwork.IsSlotUnlocked(i) || i == _currentSlot;
            }

            _prevCursorLock = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            _prevTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;
            _isOpen = true;

            if (_rootBackdrop != null) _rootBackdrop.SetActive(true);
            if (_viewer3D != null)
            {
                _viewer3D.SetDoorStates(_currentSlot, _selectedSlot, _hoveredSlot, _unlockedSlots);
                _viewer3D.SetActiveRendering(true);
            }

            UpdateUiState();
            HideToastImmediate();
        }

        private void CloseInternal(bool invokeExit)
        {
            if (!_isOpen) return;

            _isOpen = false;
            _isUpgradeSubmenuOpen = false;
            _isDragging = false;
            s_lastCloseFrame = Time.frameCount;

            if (_rootBackdrop != null) _rootBackdrop.SetActive(false);
            if (_viewer3D != null) _viewer3D.SetActiveRendering(false);

            Time.timeScale = _prevTimeScale > 0.01f ? _prevTimeScale : 1f;
            Cursor.lockState = _prevCursorLock;
            Cursor.visible = _prevCursorVisible;

            Action exitCb = _onExitCallback;
            _onTravelCallback = null;
            _onExitCallback = null;

            if (invokeExit)
            {
                exitCb?.Invoke();
            }
        }

        private void Update()
        {
            if (!_isOpen) return;

            // Enquanto o submenu de Upgrades (VirgilMutationMenuUI) estiver aberto ou acabou de fechar neste frame,
            // nao processa ESC nem cliques no mapa 3D.
            if (_isUpgradeSubmenuOpen || VirgilMutationMenuUI.IsOpen || Time.frameCount <= _upgradeSubmenuClosedFrame)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) dt = 0.016f;

            HandleKeyboardShortcuts();
            HandleViewportPointerInput();

            if (_toastTimer > 0f)
            {
                _toastTimer -= dt;
                if (_toastTimer <= 0f && _toastText != null)
                {
                    _toastText.transform.parent.gameObject.SetActive(false);
                }
            }

            if (_viewer3D != null)
            {
                _viewer3D.TickUnscaled(dt);
            }
        }

        private void HandleKeyboardShortcuts()
        {
            bool escPressed = false;
            bool enterPressed = false;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                escPressed = Keyboard.current.escapeKey.wasPressedThisFrame;
                enterPressed = Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame;
            }
#else
            escPressed = Input.GetKeyDown(KeyCode.Escape);
            enterPressed = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif

            if (escPressed)
            {
                OnExitClicked();
                return;
            }

            if (enterPressed && CanTravelToSelected())
            {
                OnTravelClicked();
            }
        }

        private void HandleViewportPointerInput()
        {
            if (_mapViewportRect == null || _viewer3D == null) return;

            Vector2 mousePos = GetPointerPosition();
            bool down = WasLeftMousePressedThisFrame();
            bool held = IsLeftMouseHeld();
            bool up = WasLeftMouseReleasedThisFrame();

            bool insideViewport = TryGetViewportUv(mousePos, out Vector2 uv);

            if (up)
            {
                _isDragging = false;
            }

            if (_isDragging && held)
            {
                Vector2 delta = mousePos - _lastPointerPos;
                _lastPointerPos = mousePos;
                _viewer3D.OrbitBy(delta.x, delta.y);
                return;
            }

            int hitSlot = insideViewport ? _viewer3D.HitTestDoor(uv) : -1;
            if (hitSlot != _hoveredSlot)
            {
                _hoveredSlot = hitSlot;
                _viewer3D.SetDoorStates(_currentSlot, _selectedSlot, _hoveredSlot, _unlockedSlots);
            }

            if (down && insideViewport)
            {
                if (hitSlot >= 0)
                {
                    if (!_unlockedSlots[hitSlot])
                    {
                        ShowToast("PORTA AINDA SELADA");
                        return;
                    }

                    if (hitSlot == _currentSlot)
                    {
                        _selectedSlot = -1;
                        _viewer3D.SetDoorStates(_currentSlot, _selectedSlot, _hoveredSlot, _unlockedSlots);
                        UpdateUiState();
                        ShowToast("VOCE JA ESTA NESTA PORTA");
                        return;
                    }

                    // Duplo clique viaja imediatamente
                    float now = Time.unscaledTime;
                    if (_lastClickedSlot == hitSlot && (now - _lastClickTime) < 0.36f)
                    {
                        _selectedSlot = hitSlot;
                        OnTravelClicked();
                        return;
                    }

                    _lastClickedSlot = hitSlot;
                    _lastClickTime = now;
                    _selectedSlot = hitSlot;
                    _viewer3D.SetDoorStates(_currentSlot, _selectedSlot, _hoveredSlot, _unlockedSlots);
                    UpdateUiState();
                    CombatSfxBank.Play(CombatSfxId.WeaponSwap, 0.35f, 1.12f, 1.20f, 0.05f);
                }
                else
                {
                    _isDragging = true;
                    _lastPointerPos = mousePos;
                }
            }
        }

        private bool TryGetViewportUv(Vector2 screenPos, out Vector2 uv)
        {
            uv = Vector2.zero;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapViewportRect, screenPos, null, out Vector2 local))
            {
                return false;
            }

            Rect rect = _mapViewportRect.rect;
            if (!rect.Contains(local)) return false;

            uv = new Vector2(
                (local.x - rect.x) / Mathf.Max(1f, rect.width),
                (local.y - rect.y) / Mathf.Max(1f, rect.height));
            return true;
        }

        private bool CanTravelToSelected()
        {
            return _selectedSlot >= 0
                   && _selectedSlot < BonfireDoorNetwork.TotalMapSlots
                   && _selectedSlot != _currentSlot
                   && _unlockedSlots[_selectedSlot];
        }

        private void OnUpgradeClicked()
        {
            if (!_isOpen || _isUpgradeSubmenuOpen) return;

            _isUpgradeSubmenuOpen = true;
            _isDragging = false;

            if (_rootBackdrop != null) _rootBackdrop.SetActive(false);
            if (_viewer3D != null) _viewer3D.SetActiveRendering(false);

            CombatSfxBank.Play(CombatSfxId.WeaponSwap, 0.35f, 1.05f, 1.12f, 0.05f);
            SimplePlayerController player = FindAnyObjectByType<SimplePlayerController>();
            VirgilMutationMenuUI.Open(player, OnUpgradeSubmenuClosed);
        }

        private void OnUpgradeSubmenuClosed()
        {
            _isUpgradeSubmenuOpen = false;
            _upgradeSubmenuClosedFrame = Time.frameCount;

            if (!_isOpen) return;

            // Se o jogador evoluiu Vida Maxima ou Estus, ja atualiza a cura/cargas imediatamente
            SimplePlayerController player = FindAnyObjectByType<SimplePlayerController>();
            if (player != null)
            {
                player.Heal(player.maxHealth);
                player.RefillEstus();
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;

            if (_rootBackdrop != null) _rootBackdrop.SetActive(true);
            if (_viewer3D != null) _viewer3D.SetActiveRendering(true);

            UpdateUiState();
        }

        private void OnTravelClicked()
        {
            if (!CanTravelToSelected()) return;

            int targetSlot = _selectedSlot;
            Action<int> travelCb = _onTravelCallback;
            CloseInternal(invokeExit: false);
            travelCb?.Invoke(targetSlot);
        }

        private void OnExitClicked()
        {
            CloseInternal(invokeExit: true);
        }

        private void UpdateUiState()
        {
            bool canTravel = CanTravelToSelected();
            if (_upgradeButtonLabel != null)
            {
                BiomassProgressionSystem.EnsureExists();
                _upgradeButtonLabel.text = $"UPGRADES ({BiomassProgressionSystem.CurrentBiomass})";
            }
            if (_travelButton != null)
            {
                _travelButton.interactable = canTravel;
            }
            if (_travelButtonImage != null)
            {
                _travelButtonImage.color = canTravel ? Ps1UiTheme.CrimsonTagBg : Ps1UiTheme.ObsidianRecessed;
            }
            if (_travelButtonLabel != null)
            {
                _travelButtonLabel.color = canTravel ? Ps1UiTheme.BoneGold : Ps1UiTheme.BoneTrimMuted;
            }
            if (_statusText != null)
            {
                _statusText.text = canTravel
                    ? "DESTINO SELECIONADO  •  CLIQUE EM VIAJAR"
                    : "VIDA & ESTUS RESTAURADOS  •  SELECIONE UMA PORTA";
            }
        }

        private void ShowToast(string msg)
        {
            if (_toastText == null) return;
            _toastText.text = msg;
            _toastText.transform.parent.gameObject.SetActive(true);
            _toastTimer = 1.6f;
        }

        private void HideToastImmediate()
        {
            _toastTimer = 0f;
            if (_toastText != null)
            {
                _toastText.transform.parent.gameObject.SetActive(false);
            }
        }

        private void BuildViewerAndUi()
        {
            // IMPORTANTE: o rig 3D NAO pode ser filho do Canvas (ScreenSpaceOverlay),
            // pois o CanvasScaler move/escala seus filhos e tiraria as ilhas 3D do campo de visao da camera!
            GameObject viewerGo = new GameObject("BonfireMap3D_Rig");
            _viewer3D = viewerGo.AddComponent<BonfireMap3DViewer>();
            _viewer3D.Initialize(1152, 540);
            _viewer3D.SetActiveRendering(false);

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 20);

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 29000;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            // Backdrop escuro semi-opaco
            _rootBackdrop = new GameObject("Backdrop");
            _rootBackdrop.transform.SetParent(transform, false);
            Image bg = _rootBackdrop.AddComponent<Image>();
            bg.color = new Color(0.015f, 0.012f, 0.018f, 0.86f);
            StretchFull(_rootBackdrop.GetComponent<RectTransform>());

            // Moldura principal PS1
            GameObject panel = new GameObject("BonfireMap_Panel");
            panel.transform.SetParent(_rootBackdrop.transform, false);
            Image panelImg = panel.AddComponent<Image>();
            panelImg.sprite = Ps1UiTheme.PanelSprite;
            panelImg.type = Image.Type.Sliced;
            panelImg.color = Color.white;

            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(1260f, 760f);

            // Barra superior (legenda minima, SEM nomes de salas!)
            GameObject header = new GameObject("HeaderBar");
            header.transform.SetParent(panel.transform, false);
            Image headerImg = header.AddComponent<Image>();
            headerImg.color = Ps1UiTheme.ObsidianHeader;
            RectTransform headerRt = header.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.offsetMin = new Vector2(14f, -64f);
            headerRt.offsetMax = new Vector2(-14f, -12f);

            CreateLegendDot(header.transform, new Vector2(24f, 0f), Ps1UiTheme.BoneGold, "ATUAL");
            CreateLegendDot(header.transform, new Vector2(148f, 0f), new Color(1f, 0.29f, 0.31f, 1f), "DESCOBERTA");
            CreateLegendDot(header.transform, new Vector2(326f, 0f), new Color(0.45f, 0.42f, 0.37f, 1f), "SELADA");

            Text hintTxt = CreateText("Hint", header.transform, "ARRASTE PARA GIRAR  •  CLIQUE NAS PORTAS", 16, FontStyle.Normal, Ps1UiTheme.BoneMutedText, TextAnchor.MiddleRight);
            RectTransform hintRt = hintTxt.rectTransform;
            hintRt.anchorMin = new Vector2(0.45f, 0f);
            hintRt.anchorMax = new Vector2(1f, 1f);
            hintRt.offsetMin = Vector2.zero;
            hintRt.offsetMax = new Vector2(-22f, 0f);

            // Moldura do Viewport 3D
            GameObject viewportFrame = new GameObject("Viewport3D_Frame");
            viewportFrame.transform.SetParent(panel.transform, false);
            Image vfImg = viewportFrame.AddComponent<Image>();
            vfImg.sprite = Ps1UiTheme.ButtonSprite;
            vfImg.type = Image.Type.Sliced;
            vfImg.color = Ps1UiTheme.ObsidianRecessed;
            RectTransform vfRt = viewportFrame.GetComponent<RectTransform>();
            vfRt.anchorMin = new Vector2(0f, 0f);
            vfRt.anchorMax = new Vector2(1f, 1f);
            vfRt.offsetMin = new Vector2(18f, 94f);
            vfRt.offsetMax = new Vector2(-18f, -74f);

            GameObject rawGo = new GameObject("Map3D_RawImage");
            rawGo.transform.SetParent(viewportFrame.transform, false);
            _mapViewportImage = rawGo.AddComponent<RawImage>();
            _mapViewportImage.texture = _viewer3D.Texture;
            _mapViewportImage.color = Color.white;
            _mapViewportRect = rawGo.GetComponent<RectTransform>();
            _mapViewportRect.anchorMin = Vector2.zero;
            _mapViewportRect.anchorMax = Vector2.one;
            _mapViewportRect.offsetMin = new Vector2(5f, 5f);
            _mapViewportRect.offsetMax = new Vector2(-5f, -5f);

            // Toast discreto no canto superior direito do mapa
            GameObject toastBox = new GameObject("ToastBox");
            toastBox.transform.SetParent(viewportFrame.transform, false);
            Image toastBg = toastBox.AddComponent<Image>();
            toastBg.sprite = Ps1UiTheme.ButtonSprite;
            toastBg.type = Image.Type.Sliced;
            toastBg.color = Ps1UiTheme.ObsidianBg;
            RectTransform toastRt = toastBox.GetComponent<RectTransform>();
            toastRt.anchorMin = new Vector2(1f, 1f);
            toastRt.anchorMax = new Vector2(1f, 1f);
            toastRt.pivot = new Vector2(1f, 1f);
            toastRt.anchoredPosition = new Vector2(-18f, -18f);
            toastRt.sizeDelta = new Vector2(310f, 38f);

            _toastText = CreateText("ToastLabel", toastBox.transform, string.Empty, 16, FontStyle.Bold, Ps1UiTheme.BoneGold, TextAnchor.MiddleCenter);
            StretchFull(_toastText.rectTransform);
            toastBox.SetActive(false);

            // Barra inferior de status e acoes
            GameObject footer = new GameObject("FooterBar");
            footer.transform.SetParent(panel.transform, false);
            RectTransform footerRt = footer.AddComponent<RectTransform>();
            footerRt.anchorMin = new Vector2(0f, 0f);
            footerRt.anchorMax = new Vector2(1f, 0f);
            footerRt.pivot = new Vector2(0.5f, 0f);
            footerRt.offsetMin = new Vector2(18f, 16f);
            footerRt.offsetMax = new Vector2(-18f, 84f);

            _statusText = CreateText("StatusText", footer.transform, string.Empty, 15, FontStyle.Normal, Ps1UiTheme.BoneMutedText, TextAnchor.MiddleLeft);
            RectTransform statusRt = _statusText.rectTransform;
            statusRt.anchorMin = new Vector2(0f, 0f);
            statusRt.anchorMax = new Vector2(0.42f, 1f);
            statusRt.offsetMin = new Vector2(8f, 0f);
            statusRt.offsetMax = Vector2.zero;

            // Botao UPGRADES (abre Tecelagem de Biomassa)
            _upgradeButton = CreatePs1Button(
                "Btn_Upgrades",
                footer.transform,
                "UPGRADES",
                new Vector2(-478f, 0f),
                new Vector2(214f, 52f),
                new Color(0.08f, 0.16f, 0.10f, 0.96f),
                new Color(0.68f, 0.94f, 0.62f, 1f),
                out _,
                out _upgradeButtonLabel);
            _upgradeButton.onClick.AddListener(OnUpgradeClicked);

            // Botao VIAJAR
            _travelButton = CreatePs1Button(
                "Btn_Travel",
                footer.transform,
                "VIAJAR",
                new Vector2(-276f, 0f),
                new Vector2(188f, 52f),
                Ps1UiTheme.CrimsonTagBg,
                Ps1UiTheme.BoneGold,
                out _travelButtonImage,
                out _travelButtonLabel);
            _travelButton.onClick.AddListener(OnTravelClicked);

            // Botao SAIR (SER CUSPIDO)
            _exitButton = CreatePs1Button(
                "Btn_ExitSpit",
                footer.transform,
                "SAIR  (SER CUSPIDO)",
                new Vector2(-10f, 0f),
                new Vector2(252f, 52f),
                Ps1UiTheme.ObsidianHeader,
                Ps1UiTheme.BoneWhiteText,
                out _,
                out _);
            _exitButton.onClick.AddListener(OnExitClicked);

            _rootBackdrop.SetActive(false);
        }

        private void CreateLegendDot(Transform parent, Vector2 anchoredPos, Color dotColor, string label)
        {
            GameObject group = new GameObject("Legend_" + label);
            group.transform.SetParent(parent, false);
            RectTransform rt = group.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(150f, 28f);

            GameObject dot = new GameObject("Dot");
            dot.transform.SetParent(group.transform, false);
            Image dotImg = dot.AddComponent<Image>();
            dotImg.sprite = Ps1UiTheme.PixelDiamondSprite;
            dotImg.color = dotColor;
            RectTransform dotRt = dot.GetComponent<RectTransform>();
            dotRt.anchorMin = new Vector2(0f, 0.5f);
            dotRt.anchorMax = new Vector2(0f, 0.5f);
            dotRt.pivot = new Vector2(0f, 0.5f);
            dotRt.anchoredPosition = Vector2.zero;
            dotRt.sizeDelta = new Vector2(14f, 14f);

            Text txt = CreateText("Label", group.transform, label, 15, FontStyle.Bold, Ps1UiTheme.BoneTrim, TextAnchor.MiddleLeft);
            RectTransform txtRt = txt.rectTransform;
            txtRt.anchorMin = new Vector2(0f, 0f);
            txtRt.anchorMax = new Vector2(1f, 1f);
            txtRt.offsetMin = new Vector2(22f, 0f);
            txtRt.offsetMax = Vector2.zero;
        }

        private Button CreatePs1Button(
            string name,
            Transform parent,
            string text,
            Vector2 anchoredRightPos,
            Vector2 size,
            Color baseColor,
            Color textColor,
            out Image outImg,
            out Text outLabel)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            outImg = go.AddComponent<Image>();
            outImg.sprite = Ps1UiTheme.ButtonSprite;
            outImg.type = Image.Type.Sliced;
            outImg.color = baseColor;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = anchoredRightPos;
            rt.sizeDelta = size;

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.22f, 1.16f, 1.08f, 1f);
            cb.pressedColor = new Color(0.85f, 0.80f, 0.76f, 1f);
            cb.disabledColor = new Color(0.60f, 0.58f, 0.58f, 0.65f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;

            outLabel = CreateText("Label", go.transform, text, 18, FontStyle.Bold, textColor, TextAnchor.MiddleCenter);
            StretchFull(outLabel.rectTransform);
            return btn;
        }

        private Text CreateText(string name, Transform parent, string content, int fontSize, FontStyle style, Color color, TextAnchor align)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Text txt = go.AddComponent<Text>();
            txt.font = _font;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = align;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            return txt;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        private static Vector2 GetPointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) return Mouse.current.position.ReadValue();
            return Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        private static bool WasLeftMousePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static bool IsLeftMouseHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

        private static bool WasLeftMouseReleasedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(0);
#endif
        }
    }
}
