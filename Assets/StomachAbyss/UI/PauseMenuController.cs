using MapaEstomago;
using StomachAbyss.Tutorial;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace StomachAbyss.UI
{
    /// <summary>
    /// Menu de Pause global no estilo PS1 Opcao 1 (Obsidiana & Osso Envelhecido).
    /// - Abre/fecha com ESC em qualquer cena de gameplay (ignora MainMenu e dialogos abertos).
    /// - Reutiliza o Ps1OptionsPanelView (3 abas: AUDIO, VIDEO, CONTROLES).
    /// - Congela Time.timeScale, trava input do jogador e garante restauracao limpa ao sair.
    /// </summary>
    public class PauseMenuController : MonoBehaviour
    {
        private static PauseMenuController s_instance;

        [SerializeField] private string mainMenuSceneName = "MainMenu";

        private Canvas _canvas;
        private Font _font;
        private GameObject _backdropRoot;
        private GameObject _mainPausePanel;
        private GameObject _optionsPanelRoot;
        private Ps1OptionsPanelView _optionsView;

        private bool _isPaused;
        private bool _isOptionsOpen;
        private CursorLockMode _prevCursorLock;
        private bool _prevCursorVisible;
        private SimplePlayerController _cachedPlayer;

        public static bool IsPaused => s_instance != null && s_instance._isPaused;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstallInGameplayScenes()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureForCurrentScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureForCurrentScene(scene);
        }

        private static void EnsureForCurrentScene(Scene scene)
        {
            if (string.Equals(scene.name, "MainMenu", System.StringComparison.OrdinalIgnoreCase))
            {
                if (s_instance != null)
                {
                    s_instance.ForceResumeImmediate();
                    Destroy(s_instance.gameObject);
                    s_instance = null;
                }
                return;
            }

            if (s_instance == null)
            {
                s_instance = FindAnyObjectByType<PauseMenuController>();
            }
            if (s_instance == null)
            {
                GameObject go = new GameObject("PauseMenu_Canvas");
                s_instance = go.AddComponent<PauseMenuController>();
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
            BuildUi();
        }

        private void OnDestroy()
        {
            if (_isPaused)
            {
                Time.timeScale = 1f;
            }
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        private void Update()
        {
            if (!WasEscapePressedThisFrame()) return;

            // Se o menu de mutacao do Virgilio ou o mapa 3D de Bonfire esta aberto (ou acabou de fechar neste frame), nao abre o pause
            if (StomachAbyss.Progression.VirgilMutationMenuUI.IsOpen
                || StomachAbyss.Progression.VirgilMutationMenuUI.WasClosedThisFrame
                || StomachAbyss.Bonfire.BonfireMapUI.IsOpen
                || StomachAbyss.Bonfire.BonfireMapUI.WasClosedThisFrame)
            {
                return;
            }

            // Se um dialogo esta aberto (ou acabou de fechar neste frame), nao abre o pause
            VirgilDialogueUI dlg = FindAnyObjectByType<VirgilDialogueUI>();
            if (dlg != null && dlg.IsOpen)
            {
                return;
            }

            if (!_isPaused)
            {
                PauseGame();
            }
            else if (_isOptionsOpen)
            {
                CloseOptions();
            }
            else
            {
                ResumeGame();
            }
        }

        public void PauseGame()
        {
            if (_isPaused) return;
            EnsureEventSystem();

            _isPaused = true;
            _isOptionsOpen = false;

            _prevCursorLock = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            LockPlayerInput(true);
            Time.timeScale = 0f;

            if (_backdropRoot != null) _backdropRoot.SetActive(true);
            if (_mainPausePanel != null) _mainPausePanel.SetActive(true);
            if (_optionsPanelRoot != null) _optionsPanelRoot.SetActive(false);
        }

        public void ResumeGame()
        {
            if (!_isPaused) return;
            ForceResumeImmediate();
        }

        private void ForceResumeImmediate()
        {
            _isPaused = false;
            _isOptionsOpen = false;
            Time.timeScale = 1f;

            LockPlayerInput(false);
            Cursor.lockState = _prevCursorLock;
            Cursor.visible = _prevCursorVisible;

            if (_backdropRoot != null) _backdropRoot.SetActive(false);
            if (_mainPausePanel != null) _mainPausePanel.SetActive(false);
            if (_optionsPanelRoot != null) _optionsPanelRoot.SetActive(false);
        }

        private void OpenOptions()
        {
            _isOptionsOpen = true;
            if (_mainPausePanel != null) _mainPausePanel.SetActive(false);
            if (_optionsPanelRoot != null)
            {
                _optionsPanelRoot.SetActive(true);
                _optionsView?.RefreshFromManager();
            }
        }

        private void CloseOptions()
        {
            _isOptionsOpen = false;
            if (_optionsPanelRoot != null) _optionsPanelRoot.SetActive(false);
            if (_mainPausePanel != null) _mainPausePanel.SetActive(true);
        }

        private void RestartScene()
        {
            ForceResumeImmediate();
            Scene active = SceneManager.GetActiveScene();
            SceneManager.LoadScene(active.buildIndex >= 0 ? active.name : "Stomach_Abyss");
        }

        private void ReturnToMainMenu()
        {
            ForceResumeImmediate();
            SceneManager.LoadScene(mainMenuSceneName);
        }

        private void QuitGame()
        {
            ForceResumeImmediate();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BuildUi()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 20);

            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;

            UnityEngine.UI.CanvasScaler scaler = gameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            // Fundo escuro translucido
            _backdropRoot = CreateUi("PauseBackdrop", transform);
            RectTransform bgRt = _backdropRoot.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            UnityEngine.UI.Image bgImg = _backdropRoot.AddComponent<UnityEngine.UI.Image>();
            bgImg.color = new Color(0.02f, 0.018f, 0.025f, 0.72f);
            bgImg.raycastTarget = true;

            // --- PAINEL PRINCIPAL DO PAUSE (Opcao 1 PS1) ---
            _mainPausePanel = CreateUi("MainPausePanel", _backdropRoot.transform);
            RectTransform pRt = _mainPausePanel.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(0.5f, 0.5f);
            pRt.anchorMax = new Vector2(0.5f, 0.5f);
            pRt.pivot = new Vector2(0.5f, 0.5f);
            pRt.sizeDelta = new Vector2(390f, 430f);

            UnityEngine.UI.Image panelImg = _mainPausePanel.AddComponent<UnityEngine.UI.Image>();
            Ps1UiTheme.ApplyPanelStyle(panelImg);

            // Cabecalho PAUSADO
            GameObject headerStrip = CreateUi("HeaderStrip", _mainPausePanel.transform);
            RectTransform hRt = headerStrip.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.anchoredPosition = new Vector2(0f, -8f);
            hRt.sizeDelta = new Vector2(-16f, 52f);
            UnityEngine.UI.Image hImg = headerStrip.AddComponent<UnityEngine.UI.Image>();
            hImg.color = Ps1UiTheme.ObsidianHeader;
            hImg.raycastTarget = false;

            UnityEngine.UI.Text title = CreateText("Title", headerStrip.transform, "PAUSADO", 24, FontStyle.Bold, Ps1UiTheme.BoneGold, TextAnchor.MiddleCenter);
            RectTransform tRt = title.rectTransform;
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;

            // Divisoria fina em osso
            GameObject sep = CreateUi("HeaderDivider", _mainPausePanel.transform);
            RectTransform sepRt = sep.GetComponent<RectTransform>();
            sepRt.anchorMin = new Vector2(0f, 1f);
            sepRt.anchorMax = new Vector2(1f, 1f);
            sepRt.pivot = new Vector2(0.5f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -62f);
            sepRt.sizeDelta = new Vector2(-28f, 2f);
            UnityEngine.UI.Image sepImg = sep.AddComponent<UnityEngine.UI.Image>();
            sepImg.color = Ps1UiTheme.BoneTrimMuted;
            sepImg.raycastTarget = false;

            // 5 Botoes empilhados
            float startY = -86f;
            float stepY = 64f;
            CreatePauseButton(_mainPausePanel.transform, "Btn_Continuar", "CONTINUAR", startY - stepY * 0, isPrimary: true, ResumeGame);
            CreatePauseButton(_mainPausePanel.transform, "Btn_Opcoes", "OPÇÕES", startY - stepY * 1, isPrimary: false, OpenOptions);
            CreatePauseButton(_mainPausePanel.transform, "Btn_Reiniciar", "REINICIAR", startY - stepY * 2, isPrimary: false, RestartScene);
            CreatePauseButton(_mainPausePanel.transform, "Btn_MenuInicial", "MENU INICIAL", startY - stepY * 3, isPrimary: false, ReturnToMainMenu);
            CreatePauseButton(_mainPausePanel.transform, "Btn_Sair", "SAIR DO JOGO", startY - stepY * 4, isPrimary: false, QuitGame);

            // --- PAINEL DE OPCOES REUTILIZADO (3 ABAS) ---
            _optionsPanelRoot = CreateUi("PauseOptionsPanel", _backdropRoot.transform);
            RectTransform optRt = _optionsPanelRoot.GetComponent<RectTransform>();
            optRt.anchorMin = new Vector2(0.5f, 0.5f);
            optRt.anchorMax = new Vector2(0.5f, 0.5f);
            optRt.pivot = new Vector2(0.5f, 0.5f);
            optRt.sizeDelta = new Vector2(560f, 430f);

            _optionsView = _optionsPanelRoot.AddComponent<Ps1OptionsPanelView>();
            _optionsView.Build(_font, CloseOptions);

            _backdropRoot.SetActive(false);
        }

        private void CreatePauseButton(Transform parent, string name, string label, float anchoredY, bool isPrimary, System.Action onClick)
        {
            GameObject btnGo = CreateUi(name, parent);
            RectTransform rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, anchoredY);
            rt.sizeDelta = new Vector2(316f, 52f);

            UnityEngine.UI.Image bg = btnGo.AddComponent<UnityEngine.UI.Image>();
            Ps1UiTheme.ApplyPanelStyle(bg);
            bg.color = isPrimary ? new Color(1.06f, 1.02f, 0.94f, 1f) : new Color(0.92f, 0.90f, 0.88f, 0.95f);

            UnityEngine.UI.Button btn = btnGo.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = bg;
            UnityEngine.UI.ColorBlock cb = btn.colors;
            cb.normalColor = bg.color;
            cb.highlightedColor = new Color(1.22f, 1.15f, 0.96f, 1f);
            cb.pressedColor = new Color(0.82f, 0.78f, 0.72f, 1f);
            cb.selectedColor = cb.highlightedColor;
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick?.Invoke());

            // Losango pixelado discreto a esquerda
            GameObject dGo = CreateUi("Diamond", btnGo.transform);
            RectTransform dRt = dGo.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0f, 0.5f);
            dRt.anchorMax = new Vector2(0f, 0.5f);
            dRt.pivot = new Vector2(0.5f, 0.5f);
            dRt.anchoredPosition = new Vector2(24f, 0f);
            dRt.sizeDelta = new Vector2(12f, 12f);
            UnityEngine.UI.Image dImg = dGo.AddComponent<UnityEngine.UI.Image>();
            dImg.sprite = Ps1UiTheme.PixelDiamondSprite;
            dImg.color = isPrimary ? Ps1UiTheme.BoneGold : Ps1UiTheme.BoneTrimMuted;
            dImg.raycastTarget = false;

            UnityEngine.UI.Text lbl = CreateText("Label", btnGo.transform, label, isPrimary ? 20 : 18, FontStyle.Bold, isPrimary ? Ps1UiTheme.BoneGold : Ps1UiTheme.BoneWhiteText, TextAnchor.MiddleCenter);
            RectTransform lRt = lbl.rectTransform;
            lRt.anchorMin = Vector2.zero;
            lRt.anchorMax = Vector2.one;
            lRt.offsetMin = new Vector2(20f, 0f);
            lRt.offsetMax = new Vector2(-20f, 0f);
        }

        private void LockPlayerInput(bool locked)
        {
            if (_cachedPlayer == null)
            {
                _cachedPlayer = FindAnyObjectByType<SimplePlayerController>();
            }
            if (_cachedPlayer != null)
            {
                _cachedPlayer.SetInputLocked(locked);
            }
        }

        private static bool WasEscapePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            GameObject esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            esGo.AddComponent<InputSystemUIInputModule>();
#else
            esGo.AddComponent<StandaloneInputModule>();
#endif
        }

        private UnityEngine.UI.Text CreateText(string name, Transform parent, string content, int size, FontStyle style, Color color, TextAnchor align)
        {
            GameObject go = CreateUi(name, parent);
            UnityEngine.UI.Text txt = go.AddComponent<UnityEngine.UI.Text>();
            txt.font = _font;
            txt.text = content;
            txt.fontSize = size;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = align;
            txt.raycastTarget = false;

            UnityEngine.UI.Shadow shadow = go.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.88f);
            shadow.effectDistance = new Vector2(1.4f, -1.4f);
            return txt;
        }

        private static GameObject CreateUi(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
