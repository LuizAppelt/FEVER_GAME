using System;
using MapaEstomago;
using StomachAbyss.Combat;
using StomachAbyss.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace StomachAbyss.Progression
{
    /// <summary>
    /// Menu de Tecelagem de Biomassa de Virgilio no estilo PS1 Opcao 1 (Obsidiana & Osso Envelhecido).
    /// Permite evoluir 4 mutacoes permanentes usando a Biomassa coletada das celulas abatidas.
    /// </summary>
    public class VirgilMutationMenuUI : MonoBehaviour
    {
        private struct MutationRowSpec
        {
            public MutationType Type;
            public string Title;
            public string Description;
        }

        private class MutationRowWidgets
        {
            public MutationType Type;
            public Text LevelText;
            public Image[] Pips;
            public Button UpgradeButton;
            public Image ButtonBg;
            public Text ButtonLabel;
        }

        private static readonly MutationRowSpec[] RowSpecs =
        {
            new MutationRowSpec
            {
                Type = MutationType.Carapace,
                Title = "CARAPAÇA",
                Description = "+15 Vida Máxima"
            },
            new MutationRowSpec
            {
                Type = MutationType.Metabolism,
                Title = "METABOLISMO",
                Description = "+15 Stamina & +5 Regen/s"
            },
            new MutationRowSpec
            {
                Type = MutationType.Virulence,
                Title = "VIRULÊNCIA",
                Description = "+12% Dano das Armas"
            },
            new MutationRowSpec
            {
                Type = MutationType.Gland,
                Title = "GLÂNDULA",
                Description = "+1 Carga de Estus & +10 Cura"
            }
        };

        private static readonly Color BiomassEmeraldGold = new Color(0.58f, 0.94f, 0.56f, 1.00f);
        private static readonly Color PipFilledColor = new Color(0.86f, 0.76f, 0.44f, 1.00f);
        private static readonly Color PipMaxColor = new Color(0.55f, 0.92f, 0.54f, 1.00f);
        private static readonly Color PipEmptyColor = new Color(0.18f, 0.17f, 0.19f, 0.95f);

        private static VirgilMutationMenuUI s_instance;

        private Canvas _canvas;
        private Font _font;
        private GameObject _backdropRoot;
        private Text _biomassValueText;
        private MutationRowWidgets[] _rows;

        private SimplePlayerController _player;
        private bool _isOpen;
        private int _openedFrame = -1;
        private int _closedFrame = -1;
        private CursorLockMode _prevCursorLock;
        private bool _prevCursorVisible;
        private bool _prevCameraLock;
        private bool _prevPlayerInputLocked;
        private Action _onClosedCallback;

        public static bool IsOpen => s_instance != null && s_instance._isOpen;
        public static bool WasClosedThisFrame => s_instance != null && Time.frameCount <= s_instance._closedFrame;

        public static void Open(SimplePlayerController player, Action onClosed = null)
        {
            BiomassProgressionSystem.EnsureExists();
            VirgilMutationMenuUI ui = GetOrCreate();
            ui.OpenInternal(player, onClosed);
        }

        public static void Close()
        {
            if (s_instance != null)
            {
                s_instance.CloseInternal();
            }
        }

        private static VirgilMutationMenuUI GetOrCreate()
        {
            if (s_instance != null)
            {
                s_instance.EnsureBuilt();
                return s_instance;
            }

            s_instance = FindAnyObjectByType<VirgilMutationMenuUI>();
            if (s_instance != null)
            {
                s_instance.EnsureBuilt();
                return s_instance;
            }

            GameObject host = new GameObject("VirgilMutationMenu_Canvas");
            s_instance = host.AddComponent<VirgilMutationMenuUI>();
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
            if (_isOpen)
            {
                CloseInternal();
            }
        }

        private void OnDestroy()
        {
            if (_isOpen)
            {
                CloseInternal();
            }

            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        private void Update()
        {
            if (!_isOpen) return;

            if (_player != null && _player.IsDead)
            {
                CloseInternal();
                return;
            }

            // Ignora o frame em que o menu foi aberto para que apertar [F] nao feche imediatamente
            if (Time.frameCount <= _openedFrame) return;

            if (WasCloseKeyPressedThisFrame())
            {
                CloseInternal();
            }
        }

        private void OpenInternal(SimplePlayerController player, Action onClosed = null)
        {
            EnsureBuilt();
            EnsureEventSystem();

            if (_isOpen)
            {
                if (onClosed != null) _onClosedCallback = onClosed;
                RefreshUi();
                return;
            }

            _player = player != null ? player : FindAnyObjectByType<SimplePlayerController>();
            _onClosedCallback = onClosed;
            _isOpen = true;
            _openedFrame = Time.frameCount;

            _prevCursorLock = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_player != null)
            {
                _prevPlayerInputLocked = _player.inputLocked;
                _player.inputLocked = true;
            }

            if (IsometricCameraFollow.Instance != null)
            {
                _prevCameraLock = IsometricCameraFollow.Instance.lockInput;
                IsometricCameraFollow.Instance.lockInput = true;
            }

            if (_backdropRoot != null)
            {
                _backdropRoot.SetActive(true);
            }

            RefreshUi();
        }

        private void CloseInternal()
        {
            if (!_isOpen) return;

            _isOpen = false;
            _closedFrame = Time.frameCount;

            if (_backdropRoot != null)
            {
                _backdropRoot.SetActive(false);
            }

            if (_player != null)
            {
                _player.inputLocked = _prevPlayerInputLocked;
            }

            if (IsometricCameraFollow.Instance != null)
            {
                IsometricCameraFollow.Instance.lockInput = _prevCameraLock;
            }

            Cursor.lockState = _prevCursorLock;
            Cursor.visible = _prevCursorVisible;

            Action cb = _onClosedCallback;
            _onClosedCallback = null;
            cb?.Invoke();
        }

        private void OnUpgradeClicked(MutationType type)
        {
            if (!_isOpen) return;

            bool purchased = BiomassProgressionSystem.TryPurchaseUpgrade(type, _player);
            if (purchased)
            {
                CombatSfxBank.Play(CombatSfxId.VirgilWhisper, 0.65f, 1.05f, 1.18f, 0.12f);
            }

            RefreshUi();
        }

        private void RefreshUi()
        {
            int biomass = BiomassProgressionSystem.CurrentBiomass;
            if (_biomassValueText != null)
            {
                _biomassValueText.text = $"BIOMASSA DISPONÍVEL: {biomass}";
            }

            if (_rows == null) return;

            for (int i = 0; i < _rows.Length; i++)
            {
                MutationRowWidgets row = _rows[i];
                if (row == null) continue;

                int level = BiomassProgressionSystem.GetMutationLevel(row.Type);
                int maxLevel = BiomassProgressionSystem.MaxMutationLevel;
                bool isMax = level >= maxLevel;
                int cost = isMax ? 0 : BiomassProgressionSystem.GetUpgradeCost(row.Type);
                bool canAfford = !isMax && biomass >= cost;

                if (row.LevelText != null)
                {
                    row.LevelText.text = $"Nv. {level}/{maxLevel}";
                    row.LevelText.color = isMax
                        ? PipMaxColor
                        : (level > 0 ? Ps1UiTheme.BoneGold : Ps1UiTheme.BoneMutedText);
                }

                if (row.Pips != null)
                {
                    for (int p = 0; p < row.Pips.Length; p++)
                    {
                        if (row.Pips[p] == null) continue;
                        if (p < level)
                        {
                            row.Pips[p].color = isMax ? PipMaxColor : PipFilledColor;
                        }
                        else
                        {
                            row.Pips[p].color = PipEmptyColor;
                        }
                    }
                }

                if (row.UpgradeButton != null)
                {
                    row.UpgradeButton.interactable = canAfford;
                }

                if (row.ButtonLabel != null)
                {
                    row.ButtonLabel.text = isMax ? "MÁXIMO" : $"EVOLUIR ({cost})";
                    row.ButtonLabel.color = isMax
                        ? PipMaxColor
                        : (canAfford ? Ps1UiTheme.BoneGold : Ps1UiTheme.BoneMutedText);
                }

                if (row.ButtonBg != null)
                {
                    row.ButtonBg.color = isMax
                        ? new Color(0.76f, 0.86f, 0.76f, 0.88f)
                        : (canAfford ? new Color(1.06f, 1.02f, 0.94f, 1f) : new Color(0.68f, 0.66f, 0.64f, 0.82f));
                }
            }
        }

        private void EnsureBuilt()
        {
            if (_backdropRoot != null) return;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 20);

            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 29500;

            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // Fundo escuro translucido
            _backdropRoot = CreateUi("MutationBackdrop", transform);
            RectTransform bgRt = _backdropRoot.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            Image bgImg = _backdropRoot.AddComponent<Image>();
            bgImg.color = new Color(0.02f, 0.018f, 0.025f, 0.74f);
            bgImg.raycastTarget = true;

            // Painel central PS1 (~580x440)
            GameObject panel = CreateUi("MutationPanel", _backdropRoot.transform);
            RectTransform pRt = panel.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(0.5f, 0.5f);
            pRt.anchorMax = new Vector2(0.5f, 0.5f);
            pRt.pivot = new Vector2(0.5f, 0.5f);
            pRt.sizeDelta = new Vector2(596f, 456f);

            Image panelImg = panel.AddComponent<Image>();
            Ps1UiTheme.ApplyPanelStyle(panelImg);

            // Cabecalho
            GameObject headerStrip = CreateUi("HeaderStrip", panel.transform);
            RectTransform hRt = headerStrip.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.anchoredPosition = new Vector2(0f, -8f);
            hRt.sizeDelta = new Vector2(-16f, 46f);

            Image hImg = headerStrip.AddComponent<Image>();
            hImg.color = Ps1UiTheme.ObsidianHeader;
            hImg.raycastTarget = false;

            Text title = CreateText(
                "Title",
                headerStrip.transform,
                "TECELAGEM DE BIOMASSA — VIRGÍLIO",
                19,
                FontStyle.Bold,
                Ps1UiTheme.BoneGold,
                TextAnchor.MiddleCenter);
            StretchFull(title.rectTransform);

            // Subtitulo com Biomassa disponivel
            GameObject subStrip = CreateUi("BiomassBar", panel.transform);
            RectTransform subRt = subStrip.GetComponent<RectTransform>();
            subRt.anchorMin = new Vector2(0f, 1f);
            subRt.anchorMax = new Vector2(1f, 1f);
            subRt.pivot = new Vector2(0.5f, 1f);
            subRt.anchoredPosition = new Vector2(0f, -58f);
            subRt.sizeDelta = new Vector2(-28f, 30f);

            Image subBg = subStrip.AddComponent<Image>();
            subBg.color = Ps1UiTheme.ObsidianRecessed;
            subBg.raycastTarget = false;

            _biomassValueText = CreateText(
                "BiomassValue",
                subStrip.transform,
                "BIOMASSA DISPONÍVEL: 0",
                15,
                FontStyle.Bold,
                BiomassEmeraldGold,
                TextAnchor.MiddleCenter);
            StretchFull(_biomassValueText.rectTransform);

            // Divisoria fina osso
            GameObject sep = CreateUi("HeaderDivider", panel.transform);
            RectTransform sepRt = sep.GetComponent<RectTransform>();
            sepRt.anchorMin = new Vector2(0f, 1f);
            sepRt.anchorMax = new Vector2(1f, 1f);
            sepRt.pivot = new Vector2(0.5f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -94f);
            sepRt.sizeDelta = new Vector2(-28f, 2f);

            Image sepImg = sep.AddComponent<Image>();
            sepImg.color = Ps1UiTheme.BoneTrimMuted;
            sepImg.raycastTarget = false;

            // 4 Linhas de Mutacao
            _rows = new MutationRowWidgets[RowSpecs.Length];
            float rowStartY = -108f;
            float rowStepY = 68f;
            for (int i = 0; i < RowSpecs.Length; i++)
            {
                _rows[i] = BuildMutationRow(panel.transform, RowSpecs[i], rowStartY - rowStepY * i);
            }

            // Botao de Rodape: [ESC] FECHAR
            BuildFooterCloseButton(panel.transform);

            _backdropRoot.SetActive(false);
        }

        private MutationRowWidgets BuildMutationRow(Transform parent, MutationRowSpec spec, float anchoredY)
        {
            GameObject rowGo = CreateUi($"Row_{spec.Type}", parent);
            RectTransform rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.anchoredPosition = new Vector2(0f, anchoredY);
            rowRt.sizeDelta = new Vector2(-28f, 58f);

            Image rowBg = rowGo.AddComponent<Image>();
            rowBg.color = Ps1UiTheme.ObsidianRecessed;
            rowBg.raycastTarget = false;

            // Filete lateral esquerdo osso
            GameObject accent = CreateUi("LeftAccent", rowGo.transform);
            RectTransform accRt = accent.GetComponent<RectTransform>();
            accRt.anchorMin = new Vector2(0f, 0f);
            accRt.anchorMax = new Vector2(0f, 1f);
            accRt.pivot = new Vector2(0f, 0.5f);
            accRt.anchoredPosition = Vector2.zero;
            accRt.sizeDelta = new Vector2(3f, 0f);
            Image accImg = accent.AddComponent<Image>();
            accImg.color = Ps1UiTheme.BoneTrimMuted;
            accImg.raycastTarget = false;

            // Nome da Mutacao
            Text nameText = CreateText(
                "Name",
                rowGo.transform,
                spec.Title,
                16,
                FontStyle.Bold,
                Ps1UiTheme.BoneWhiteText,
                TextAnchor.MiddleLeft);
            RectTransform nameRt = nameText.rectTransform;
            nameRt.anchorMin = new Vector2(0f, 0.5f);
            nameRt.anchorMax = new Vector2(0f, 1f);
            nameRt.pivot = new Vector2(0f, 1f);
            nameRt.offsetMin = new Vector2(14f, 0f);
            nameRt.offsetMax = new Vector2(276f, -4f);

            // Descricao do bonus
            Text descText = CreateText(
                "Desc",
                rowGo.transform,
                spec.Description,
                13,
                FontStyle.Normal,
                Ps1UiTheme.BoneMutedText,
                TextAnchor.MiddleLeft);
            RectTransform descRt = descText.rectTransform;
            descRt.anchorMin = new Vector2(0f, 0f);
            descRt.anchorMax = new Vector2(0f, 0.5f);
            descRt.pivot = new Vector2(0f, 0f);
            descRt.offsetMin = new Vector2(14f, 4f);
            descRt.offsetMax = new Vector2(276f, 0f);

            // Nivel atual (Nv. X/5)
            Text levelText = CreateText(
                "Level",
                rowGo.transform,
                "Nv. 0/5",
                14,
                FontStyle.Bold,
                Ps1UiTheme.BoneMutedText,
                TextAnchor.MiddleCenter);
            RectTransform lvlRt = levelText.rectTransform;
            lvlRt.anchorMin = new Vector2(1f, 0.5f);
            lvlRt.anchorMax = new Vector2(1f, 1f);
            lvlRt.pivot = new Vector2(1f, 1f);
            lvlRt.anchoredPosition = new Vector2(-176f, -6f);
            lvlRt.sizeDelta = new Vector2(92f, 22f);

            // 5 quadradinhos de progresso PS1 abaixo do nivel
            int maxLvl = BiomassProgressionSystem.MaxMutationLevel;
            Image[] pips = new Image[maxLvl];
            float pipWidth = 12f;
            float pipGap = 4f;
            float totalPipW = maxLvl * pipWidth + (maxLvl - 1) * pipGap;
            float pipStartX = -176f - (92f - totalPipW) * 0.5f - totalPipW;
            for (int p = 0; p < maxLvl; p++)
            {
                GameObject pipGo = CreateUi($"Pip_{p}", rowGo.transform);
                RectTransform pipRt = pipGo.GetComponent<RectTransform>();
                pipRt.anchorMin = new Vector2(1f, 0f);
                pipRt.anchorMax = new Vector2(1f, 0f);
                pipRt.pivot = new Vector2(0f, 0f);
                pipRt.anchoredPosition = new Vector2(pipStartX + p * (pipWidth + pipGap), 11f);
                pipRt.sizeDelta = new Vector2(pipWidth, 8f);
                Image pipImg = pipGo.AddComponent<Image>();
                pipImg.color = PipEmptyColor;
                pipImg.raycastTarget = false;
                pips[p] = pipImg;
            }

            // Botao EVOLUIR (<cost>)
            GameObject btnGo = CreateUi($"Btn_Upgrade_{spec.Type}", rowGo.transform);
            RectTransform btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-10f, 0f);
            btnRt.sizeDelta = new Vector2(156f, 40f);

            Image btnBg = btnGo.AddComponent<Image>();
            Ps1UiTheme.ApplyPanelStyle(btnBg);

            Button btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = btnBg;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.22f, 1.15f, 0.96f, 1f);
            cb.pressedColor = new Color(0.82f, 0.78f, 0.72f, 1f);
            cb.selectedColor = cb.highlightedColor;
            cb.disabledColor = new Color(0.70f, 0.68f, 0.66f, 0.85f);
            btn.colors = cb;

            MutationType capturedType = spec.Type;
            btn.onClick.AddListener(() => OnUpgradeClicked(capturedType));

            Text btnLabel = CreateText(
                "Label",
                btnGo.transform,
                "EVOLUIR (100)",
                14,
                FontStyle.Bold,
                Ps1UiTheme.BoneGold,
                TextAnchor.MiddleCenter);
            StretchFull(btnLabel.rectTransform);

            return new MutationRowWidgets
            {
                Type = spec.Type,
                LevelText = levelText,
                Pips = pips,
                UpgradeButton = btn,
                ButtonBg = btnBg,
                ButtonLabel = btnLabel
            };
        }

        private void BuildFooterCloseButton(Transform parent)
        {
            GameObject btnGo = CreateUi("Btn_Close", parent);
            RectTransform rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 18f);
            rt.sizeDelta = new Vector2(220f, 42f);

            Image bg = btnGo.AddComponent<Image>();
            Ps1UiTheme.ApplyPanelStyle(bg);
            bg.color = new Color(0.95f, 0.92f, 0.88f, 0.96f);

            Button btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = bg;
            ColorBlock cb = btn.colors;
            cb.normalColor = bg.color;
            cb.highlightedColor = new Color(1.22f, 1.15f, 0.96f, 1f);
            cb.pressedColor = new Color(0.82f, 0.78f, 0.72f, 1f);
            cb.selectedColor = cb.highlightedColor;
            btn.colors = cb;
            btn.onClick.AddListener(CloseInternal);

            Text lbl = CreateText(
                "Label",
                btnGo.transform,
                "[ESC] FECHAR",
                16,
                FontStyle.Bold,
                Ps1UiTheme.BoneWhiteText,
                TextAnchor.MiddleCenter);
            StretchFull(lbl.rectTransform);
        }

        private static bool WasCloseKeyPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && (kb.escapeKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F);
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

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private Text CreateText(string name, Transform parent, string content, int size, FontStyle style, Color color, TextAnchor align)
        {
            GameObject go = CreateUi(name, parent);
            Text txt = go.AddComponent<Text>();
            txt.font = _font;
            txt.text = content;
            txt.fontSize = size;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = align;
            txt.raycastTarget = false;

            Shadow shadow = go.AddComponent<Shadow>();
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
