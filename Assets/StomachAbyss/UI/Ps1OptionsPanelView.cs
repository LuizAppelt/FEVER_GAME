using System;
using UnityEngine;

namespace StomachAbyss.UI
{
    /// <summary>
    /// Painel de Opcoes reutilizavel no estilo PS1 Opcao 1 (Obsidiana & Osso Envelhecido)
    /// com 3 abas: AUDIO (Geral, Musica, Ambiente, Monstros, Acoes), VIDEO (Tela Cheia,
    /// Resolucao, V-Sync, Brilho, Tremor de Camera) e CONTROLES.
    /// Pode ser embutido tanto no Menu Inicial quanto no Menu de Pause.
    /// </summary>
    public class Ps1OptionsPanelView : MonoBehaviour
    {
        private enum TabId
        {
            Audio = 0,
            Video = 1,
            Controls = 2
        }

        private Font _font;
        private Action _onBackClicked;
        private Action _onUiClickSfx;

        private GameObject _audioPage;
        private GameObject _videoPage;
        private GameObject _controlsPage;

        private UnityEngine.UI.Image[] _tabBgs = new UnityEngine.UI.Image[3];
        private UnityEngine.UI.Text[] _tabLabels = new UnityEngine.UI.Text[3];

        // Audio controls
        private UnityEngine.UI.Slider _masterSlider;
        private UnityEngine.UI.Text _masterVal;
        private UnityEngine.UI.Slider _musicSlider;
        private UnityEngine.UI.Text _musicVal;
        private UnityEngine.UI.Slider _ambientSlider;
        private UnityEngine.UI.Text _ambientVal;
        private UnityEngine.UI.Slider _monstersSlider;
        private UnityEngine.UI.Text _monstersVal;
        private UnityEngine.UI.Slider _actionsSlider;
        private UnityEngine.UI.Text _actionsVal;

        // Video & Gameplay controls
        private UnityEngine.UI.Toggle _fullscreenToggle;
        private UnityEngine.UI.Text _resolutionValueText;
        private UnityEngine.UI.Text _fpsValueText;
        private UnityEngine.UI.Toggle _vsyncToggle;
        private UnityEngine.UI.Slider _brightnessSlider;
        private UnityEngine.UI.Text _brightnessVal;
        private UnityEngine.UI.Slider _camDistSlider;
        private UnityEngine.UI.Text _camDistVal;
        private UnityEngine.UI.Slider _vhsSlider;
        private UnityEngine.UI.Text _vhsVal;
        private UnityEngine.UI.Toggle _shakeToggle;

        private bool _built;

        /// <summary>
        /// Constroi o painel dentro do RectTransform deste GameObject (tamanho recomendado: 560x430).
        /// </summary>
        public void Build(Font font, Action onBackClicked, Action onUiClickSfx = null)
        {
            _font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _onBackClicked = onBackClicked;
            _onUiClickSfx = onUiClickSfx;

            if (_built)
            {
                RefreshFromManager();
                return;
            }
            _built = true;

            GameSettingsManager.Initialize();

            // Limpa filhos antigos caso esteja substituindo um painel legado
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            UnityEngine.UI.Image panelBg = GetOrAdd<UnityEngine.UI.Image>(gameObject);
            Ps1UiTheme.ApplyPanelStyle(panelBg);
            panelBg.raycastTarget = true;

            // --- CABECALHO ---
            GameObject headerStrip = CreateUi("HeaderStrip", transform);
            RectTransform hRt = headerStrip.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.anchoredPosition = new Vector2(0f, -8f);
            hRt.sizeDelta = new Vector2(-16f, 48f);
            UnityEngine.UI.Image hImg = headerStrip.AddComponent<UnityEngine.UI.Image>();
            hImg.color = Ps1UiTheme.ObsidianHeader;
            hImg.raycastTarget = false;

            UnityEngine.UI.Text title = CreateText("Title", headerStrip.transform, "OPÇÕES DO ABISMO", 21, FontStyle.Bold, Ps1UiTheme.BoneGold, TextAnchor.MiddleLeft);
            Stretch(title.rectTransform, 20f, 0f, -20f, 0f);

            // --- BARRA DE 3 ABAS: AUDIO | VIDEO | CONTROLES ---
            GameObject tabRow = CreateUi("TabRow", transform);
            RectTransform tabRowRt = tabRow.GetComponent<RectTransform>();
            tabRowRt.anchorMin = new Vector2(0f, 1f);
            tabRowRt.anchorMax = new Vector2(1f, 1f);
            tabRowRt.pivot = new Vector2(0.5f, 1f);
            tabRowRt.anchoredPosition = new Vector2(0f, -62f);
            tabRowRt.sizeDelta = new Vector2(-36f, 38f);

            string[] tabNames = { "ÁUDIO", "VÍDEO", "CONTROLES" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                GameObject tabGo = CreateUi($"Tab_{tabNames[i]}", tabRow.transform);
                RectTransform tRt = tabGo.GetComponent<RectTransform>();
                tRt.anchorMin = new Vector2(i / 3f, 0f);
                tRt.anchorMax = new Vector2((i + 1) / 3f, 1f);
                tRt.offsetMin = new Vector2(i == 0 ? 0f : 4f, 0f);
                tRt.offsetMax = new Vector2(i == 2 ? 0f : -4f, 0f);

                UnityEngine.UI.Image tBg = tabGo.AddComponent<UnityEngine.UI.Image>();
                Ps1UiTheme.ApplyPanelStyle(tBg);
                _tabBgs[i] = tBg;

                UnityEngine.UI.Button tBtn = tabGo.AddComponent<UnityEngine.UI.Button>();
                tBtn.targetGraphic = tBg;
                tBtn.transition = UnityEngine.UI.Selectable.Transition.None;
                tBtn.onClick.AddListener(() =>
                {
                    _onUiClickSfx?.Invoke();
                    SelectTab((TabId)idx);
                });

                UnityEngine.UI.Text tLabel = CreateText("Label", tabGo.transform, tabNames[i], 15, FontStyle.Bold, Ps1UiTheme.BoneWhiteText, TextAnchor.MiddleCenter);
                Stretch(tLabel.rectTransform, 0f, 0f, 0f, 0f);
                _tabLabels[i] = tLabel;
            }

            // Divisoria fina abaixo das abas
            GameObject sep = CreateUi("TabDivider", transform);
            RectTransform sepRt = sep.GetComponent<RectTransform>();
            sepRt.anchorMin = new Vector2(0f, 1f);
            sepRt.anchorMax = new Vector2(1f, 1f);
            sepRt.pivot = new Vector2(0.5f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -106f);
            sepRt.sizeDelta = new Vector2(-36f, 2f);
            UnityEngine.UI.Image sepImg = sep.AddComponent<UnityEngine.UI.Image>();
            sepImg.color = Ps1UiTheme.BoneTrimMuted;
            sepImg.raycastTarget = false;

            // --- AREA DE CONTEUDO DAS ABAS ---
            GameObject contentArea = CreateUi("ContentArea", transform);
            RectTransform contentRt = contentArea.GetComponent<RectTransform>();
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = new Vector2(24f, 74f);
            contentRt.offsetMax = new Vector2(-24f, -116f);

            BuildAudioPage(contentArea.transform);
            BuildVideoPage(contentArea.transform);
            BuildControlsPage(contentArea.transform);

            // --- BOTAO VOLTAR NO RODAPE ---
            GameObject backGo = CreateUi("Btn_Voltar", transform);
            RectTransform backRt = backGo.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0f, 0f);
            backRt.anchorMax = new Vector2(0f, 0f);
            backRt.pivot = new Vector2(0f, 0f);
            backRt.anchoredPosition = new Vector2(24f, 18f);
            backRt.sizeDelta = new Vector2(190f, 44f);

            UnityEngine.UI.Image backBg = backGo.AddComponent<UnityEngine.UI.Image>();
            Ps1UiTheme.ApplyPanelStyle(backBg);

            UnityEngine.UI.Button backBtn = backGo.AddComponent<UnityEngine.UI.Button>();
            backBtn.targetGraphic = backBg;
            backBtn.onClick.AddListener(() =>
            {
                _onUiClickSfx?.Invoke();
                GameSettingsManager.SavePrefs();
                _onBackClicked?.Invoke();
            });

            UnityEngine.UI.Text backLbl = CreateText("Label", backGo.transform, "VOLTAR", 18, FontStyle.Bold, Ps1UiTheme.BoneGold, TextAnchor.MiddleCenter);
            Stretch(backLbl.rectTransform, 0f, 0f, 0f, 0f);

            RefreshFromManager();
            SelectTab(TabId.Audio);
        }

        private void BuildAudioPage(Transform parent)
        {
            _audioPage = CreateUi("Page_Audio", parent);
            Stretch(_audioPage.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            float rowStep = 48f;
            float topY = -8f;

            CreateSliderRow(_audioPage.transform, "VOLUME GERAL", topY - rowStep * 0, 0f, 1f, out _masterSlider, out _masterVal, v =>
            {
                GameSettingsManager.SetMasterVolume(v);
                _masterVal.text = $"{Mathf.RoundToInt(v * 100f)}%";
            });

            CreateSliderRow(_audioPage.transform, "MÚSICA", topY - rowStep * 1, 0f, 1f, out _musicSlider, out _musicVal, v =>
            {
                GameSettingsManager.SetMusicVolume(v);
                _musicVal.text = $"{Mathf.RoundToInt(v * 100f)}%";
            });

            CreateSliderRow(_audioPage.transform, "AMBIENTE", topY - rowStep * 2, 0f, 1f, out _ambientSlider, out _ambientVal, v =>
            {
                GameSettingsManager.SetAmbientVolume(v);
                _ambientVal.text = $"{Mathf.RoundToInt(v * 100f)}%";
            });

            CreateSliderRow(_audioPage.transform, "MONSTROS", topY - rowStep * 3, 0f, 1f, out _monstersSlider, out _monstersVal, v =>
            {
                GameSettingsManager.SetMonstersVolume(v);
                _monstersVal.text = $"{Mathf.RoundToInt(v * 100f)}%";
            });

            CreateSliderRow(_audioPage.transform, "AÇÕES", topY - rowStep * 4, 0f, 1f, out _actionsSlider, out _actionsVal, v =>
            {
                GameSettingsManager.SetActionsVolume(v);
                _actionsVal.text = $"{Mathf.RoundToInt(v * 100f)}%";
            });
        }

        private void BuildVideoPage(Transform parent)
        {
            _videoPage = CreateUi("Page_Video", parent);
            Stretch(_videoPage.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            float rowStep = 31f;
            float topY = 0f;

            _fullscreenToggle = CreateToggleRow(_videoPage.transform, "TELA CHEIA", topY - rowStep * 0, v =>
            {
                _onUiClickSfx?.Invoke();
                GameSettingsManager.SetFullscreen(v);
            });

            CreateResolutionRow(_videoPage.transform, "RESOLUÇÃO", topY - rowStep * 1);

            CreateFpsLimitRow(_videoPage.transform, "LIMITE DE FPS", topY - rowStep * 2);

            _vsyncToggle = CreateToggleRow(_videoPage.transform, "V-SYNC", topY - rowStep * 3, v =>
            {
                _onUiClickSfx?.Invoke();
                GameSettingsManager.SetVSync(v);
            });

            CreateSliderRow(_videoPage.transform, "BRILHO", topY - rowStep * 4, 0.5f, 1.5f, out _brightnessSlider, out _brightnessVal, v =>
            {
                GameSettingsManager.SetBrightness(v);
                _brightnessVal.text = $"{Mathf.RoundToInt(v * 100f)}%";
            });

            CreateSliderRow(_videoPage.transform, "DISTÂNCIA DA CÂMERA", topY - rowStep * 5, 0.70f, 1.35f, out _camDistSlider, out _camDistVal, v =>
            {
                GameSettingsManager.SetCameraDistanceScale(v);
                _camDistVal.text = $"{Mathf.RoundToInt(v * 100f)}%";
            });

            CreateSliderRow(_videoPage.transform, "INTENSIDADE VHS", topY - rowStep * 6, 0f, 1f, out _vhsSlider, out _vhsVal, v =>
            {
                GameSettingsManager.SetVhsIntensity(v);
                _vhsVal.text = $"{Mathf.RoundToInt(v * 100f)}%";
            });

            _shakeToggle = CreateToggleRow(_videoPage.transform, "TREMOR DE CÂMERA", topY - rowStep * 7, v =>
            {
                _onUiClickSfx?.Invoke();
                GameSettingsManager.SetScreenShake(v);
            });
        }

        private void BuildControlsPage(Transform parent)
        {
            _controlsPage = CreateUi("Page_Controls", parent);
            Stretch(_controlsPage.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            (string key, string desc)[] bindings =
            {
                ("WASD", "Mover personagem"),
                ("SHIFT", "Correr (consome estamina)"),
                ("ESPAÇO", "Esquiva / Dash com i-frames"),
                ("1 / 2 / 3", "Alternar arma equipada"),
                ("BOTÃO ESQ.", "Ataque principal"),
                ("BOTÃO DIR.", "Mira / Ataque especial"),
                ("R", "Cura rápida (Frasco)"),
                ("E / ESC", "Interagir / Pausar jogo")
            };

            float y = -6f;
            float step = 30f;
            for (int i = 0; i < bindings.Length; i++)
            {
                GameObject row = CreateUi($"CtrlRow_{i}", _controlsPage.transform);
                RectTransform rRt = row.GetComponent<RectTransform>();
                rRt.anchorMin = new Vector2(0f, 1f);
                rRt.anchorMax = new Vector2(1f, 1f);
                rRt.pivot = new Vector2(0.5f, 1f);
                rRt.anchoredPosition = new Vector2(0f, y - i * step);
                rRt.sizeDelta = new Vector2(0f, 26f);

                UnityEngine.UI.Text kTxt = CreateText("Key", row.transform, bindings[i].key, 15, FontStyle.Bold, Ps1UiTheme.BoneGold, TextAnchor.MiddleLeft);
                RectTransform kRt = kTxt.rectTransform;
                kRt.anchorMin = new Vector2(0f, 0f);
                kRt.anchorMax = new Vector2(0.36f, 1f);
                kRt.offsetMin = Vector2.zero;
                kRt.offsetMax = Vector2.zero;

                UnityEngine.UI.Text dTxt = CreateText("Desc", row.transform, bindings[i].desc, 15, FontStyle.Normal, Ps1UiTheme.BoneWhiteText, TextAnchor.MiddleLeft);
                RectTransform dRt = dTxt.rectTransform;
                dRt.anchorMin = new Vector2(0.38f, 0f);
                dRt.anchorMax = new Vector2(1f, 1f);
                dRt.offsetMin = Vector2.zero;
                dRt.offsetMax = Vector2.zero;
            }
        }

        private void SelectTab(TabId tab)
        {
            if (_audioPage != null) _audioPage.SetActive(tab == TabId.Audio);
            if (_videoPage != null) _videoPage.SetActive(tab == TabId.Video);
            if (_controlsPage != null) _controlsPage.SetActive(tab == TabId.Controls);

            for (int i = 0; i < 3; i++)
            {
                bool active = i == (int)tab;
                if (_tabBgs[i] != null)
                {
                    _tabBgs[i].color = active ? new Color(1.22f, 1.15f, 0.96f, 1f) : new Color(0.75f, 0.75f, 0.75f, 0.85f);
                }
                if (_tabLabels[i] != null)
                {
                    _tabLabels[i].color = active ? Ps1UiTheme.BoneGold : Ps1UiTheme.BoneMutedText;
                }
            }
        }

        public void RefreshFromManager()
        {
            GameSettingsManager.Initialize();

            SetSliderSilent(_masterSlider, _masterVal, GameSettingsManager.MasterVolume);
            SetSliderSilent(_musicSlider, _musicVal, GameSettingsManager.MusicVolume);
            SetSliderSilent(_ambientSlider, _ambientVal, GameSettingsManager.AmbientVolume);
            SetSliderSilent(_monstersSlider, _monstersVal, GameSettingsManager.MonstersVolume);
            SetSliderSilent(_actionsSlider, _actionsVal, GameSettingsManager.ActionsVolume);

            if (_fullscreenToggle != null) _fullscreenToggle.SetIsOnWithoutNotify(GameSettingsManager.Fullscreen);
            if (_resolutionValueText != null) _resolutionValueText.text = GameSettingsManager.GetCurrentResolutionLabel();
            if (_fpsValueText != null) _fpsValueText.text = GameSettingsManager.GetCurrentFpsLabel();
            if (_vsyncToggle != null) _vsyncToggle.SetIsOnWithoutNotify(GameSettingsManager.VSyncEnabled);
            SetSliderSilent(_brightnessSlider, _brightnessVal, GameSettingsManager.Brightness);
            SetSliderSilent(_camDistSlider, _camDistVal, GameSettingsManager.CameraDistanceScale);
            SetSliderSilent(_vhsSlider, _vhsVal, GameSettingsManager.VhsIntensity);
            if (_shakeToggle != null) _shakeToggle.SetIsOnWithoutNotify(GameSettingsManager.ScreenShakeEnabled);
        }

        private static void SetSliderSilent(UnityEngine.UI.Slider slider, UnityEngine.UI.Text valText, float val)
        {
            if (slider != null) slider.SetValueWithoutNotify(val);
            if (valText != null) valText.text = $"{Mathf.RoundToInt(val * 100f)}%";
        }

        private void CreateSliderRow(
            Transform parent,
            string label,
            float anchoredY,
            float min,
            float max,
            out UnityEngine.UI.Slider slider,
            out UnityEngine.UI.Text valText,
            Action<float> onChanged)
        {
            GameObject row = CreateUi($"Row_{label}", parent);
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, anchoredY);
            rt.sizeDelta = new Vector2(0f, 40f);

            UnityEngine.UI.Text lbl = CreateText("Label", row.transform, label, 15, FontStyle.Bold, Ps1UiTheme.BoneWhiteText, TextAnchor.MiddleLeft);
            RectTransform lRt = lbl.rectTransform;
            lRt.anchorMin = new Vector2(0f, 0f);
            lRt.anchorMax = new Vector2(0.38f, 1f);
            lRt.offsetMin = Vector2.zero;
            lRt.offsetMax = Vector2.zero;

            GameObject sliderGo = CreateUi("Slider", row.transform);
            RectTransform sRt = sliderGo.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0.40f, 0.22f);
            sRt.anchorMax = new Vector2(0.84f, 0.78f);
            sRt.offsetMin = Vector2.zero;
            sRt.offsetMax = Vector2.zero;

            GameObject bgGo = CreateUi("Background", sliderGo.transform);
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.30f);
            bgRt.anchorMax = new Vector2(1f, 0.70f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            UnityEngine.UI.Image bgImg = bgGo.AddComponent<UnityEngine.UI.Image>();
            bgImg.sprite = Ps1UiTheme.WhiteSprite;
            bgImg.color = Ps1UiTheme.ObsidianRecessed;
            UnityEngine.UI.Outline trackOut = bgGo.AddComponent<UnityEngine.UI.Outline>();
            trackOut.effectColor = Ps1UiTheme.BoneTrimMuted;
            trackOut.effectDistance = new Vector2(1f, -1f);

            GameObject fillArea = CreateUi("Fill Area", sliderGo.transform);
            RectTransform faRt = fillArea.GetComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0f, 0.30f);
            faRt.anchorMax = new Vector2(1f, 0.70f);
            faRt.offsetMin = new Vector2(2f, 1f);
            faRt.offsetMax = new Vector2(-2f, -1f);

            GameObject fillGo = CreateUi("Fill", fillArea.transform);
            Stretch(fillGo.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            UnityEngine.UI.Image fillImg = fillGo.AddComponent<UnityEngine.UI.Image>();
            fillImg.sprite = Ps1UiTheme.WhiteSprite;
            fillImg.color = Ps1UiTheme.BoneTrim;

            GameObject handleArea = CreateUi("Handle Slide Area", sliderGo.transform);
            Stretch(handleArea.GetComponent<RectTransform>(), 8f, 0f, -8f, 0f);

            GameObject handleGo = CreateUi("Handle", handleArea.transform);
            RectTransform hRt = handleGo.GetComponent<RectTransform>();
            hRt.sizeDelta = new Vector2(16f, 20f);
            UnityEngine.UI.Image hImg = handleGo.AddComponent<UnityEngine.UI.Image>();
            hImg.sprite = Ps1UiTheme.PixelDiamondSprite;
            hImg.color = Ps1UiTheme.BoneGold;

            slider = sliderGo.AddComponent<UnityEngine.UI.Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.fillRect = fillGo.GetComponent<RectTransform>();
            slider.handleRect = hRt;
            slider.targetGraphic = hImg;
            slider.onValueChanged.AddListener(v => onChanged?.Invoke(v));

            valText = CreateText("Value", row.transform, "100%", 15, FontStyle.Bold, Ps1UiTheme.BoneGold, TextAnchor.MiddleRight);
            RectTransform vRt = valText.rectTransform;
            vRt.anchorMin = new Vector2(0.86f, 0f);
            vRt.anchorMax = new Vector2(1f, 1f);
            vRt.offsetMin = Vector2.zero;
            vRt.offsetMax = Vector2.zero;
        }

        private UnityEngine.UI.Toggle CreateToggleRow(Transform parent, string label, float anchoredY, Action<bool> onChanged)
        {
            GameObject row = CreateUi($"Row_{label}", parent);
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, anchoredY);
            rt.sizeDelta = new Vector2(0f, 40f);

            UnityEngine.UI.Text lbl = CreateText("Label", row.transform, label, 15, FontStyle.Bold, Ps1UiTheme.BoneWhiteText, TextAnchor.MiddleLeft);
            RectTransform lRt = lbl.rectTransform;
            lRt.anchorMin = new Vector2(0f, 0f);
            lRt.anchorMax = new Vector2(0.75f, 1f);
            lRt.offsetMin = Vector2.zero;
            lRt.offsetMax = Vector2.zero;

            GameObject togGo = CreateUi("Toggle", row.transform);
            RectTransform tRt = togGo.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(1f, 0.5f);
            tRt.anchorMax = new Vector2(1f, 0.5f);
            tRt.pivot = new Vector2(1f, 0.5f);
            tRt.anchoredPosition = Vector2.zero;
            tRt.sizeDelta = new Vector2(30f, 30f);

            GameObject bgGo = CreateUi("Background", togGo.transform);
            Stretch(bgGo.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            UnityEngine.UI.Image bgImg = bgGo.AddComponent<UnityEngine.UI.Image>();
            Ps1UiTheme.ApplyPanelStyle(bgImg);

            GameObject checkGo = CreateUi("Checkmark", bgGo.transform);
            RectTransform cRt = checkGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0.22f, 0.22f);
            cRt.anchorMax = new Vector2(0.78f, 0.78f);
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;
            UnityEngine.UI.Image cImg = checkGo.AddComponent<UnityEngine.UI.Image>();
            cImg.sprite = Ps1UiTheme.PixelDiamondSprite;
            cImg.color = Ps1UiTheme.BoneGold;
            cImg.raycastTarget = false;

            UnityEngine.UI.Toggle toggle = togGo.AddComponent<UnityEngine.UI.Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic = cImg;
            toggle.onValueChanged.AddListener(v => onChanged?.Invoke(v));
            return toggle;
        }

        private void CreateResolutionRow(Transform parent, string label, float anchoredY)
        {
            GameObject row = CreateUi($"Row_{label}", parent);
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, anchoredY);
            rt.sizeDelta = new Vector2(0f, 40f);

            UnityEngine.UI.Text lbl = CreateText("Label", row.transform, label, 15, FontStyle.Bold, Ps1UiTheme.BoneWhiteText, TextAnchor.MiddleLeft);
            RectTransform lRt = lbl.rectTransform;
            lRt.anchorMin = new Vector2(0f, 0f);
            lRt.anchorMax = new Vector2(0.42f, 1f);
            lRt.offsetMin = Vector2.zero;
            lRt.offsetMax = Vector2.zero;

            // Botao <
            CreateStepButton(row.transform, "BtnPrev", "<", new Vector2(-176f, 0f), () =>
            {
                _onUiClickSfx?.Invoke();
                GameSettingsManager.SetResolutionIndex(GameSettingsManager.ResolutionIndex - 1);
                _resolutionValueText.text = GameSettingsManager.GetCurrentResolutionLabel();
            });

            _resolutionValueText = CreateText("ResValue", row.transform, GameSettingsManager.GetCurrentResolutionLabel(), 15, FontStyle.Bold, Ps1UiTheme.BoneGold, TextAnchor.MiddleCenter);
            RectTransform vRt = _resolutionValueText.rectTransform;
            vRt.anchorMin = new Vector2(1f, 0.5f);
            vRt.anchorMax = new Vector2(1f, 0.5f);
            vRt.pivot = new Vector2(1f, 0.5f);
            vRt.anchoredPosition = new Vector2(-36f, 0f);
            vRt.sizeDelta = new Vector2(136f, 30f);

            // Botao >
            CreateStepButton(row.transform, "BtnNext", ">", new Vector2(0f, 0f), () =>
            {
                _onUiClickSfx?.Invoke();
                GameSettingsManager.SetResolutionIndex(GameSettingsManager.ResolutionIndex + 1);
                _resolutionValueText.text = GameSettingsManager.GetCurrentResolutionLabel();
            });
        }

        private void CreateFpsLimitRow(Transform parent, string label, float anchoredY)
        {
            GameObject row = CreateUi($"Row_{label}", parent);
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, anchoredY);
            rt.sizeDelta = new Vector2(0f, 40f);

            UnityEngine.UI.Text lbl = CreateText("Label", row.transform, label, 15, FontStyle.Bold, Ps1UiTheme.BoneWhiteText, TextAnchor.MiddleLeft);
            RectTransform lRt = lbl.rectTransform;
            lRt.anchorMin = new Vector2(0f, 0f);
            lRt.anchorMax = new Vector2(0.42f, 1f);
            lRt.offsetMin = Vector2.zero;
            lRt.offsetMax = Vector2.zero;

            CreateStepButton(row.transform, "BtnPrev", "<", new Vector2(-176f, 0f), () =>
            {
                _onUiClickSfx?.Invoke();
                GameSettingsManager.SetFpsLimitIndex(GameSettingsManager.FpsLimitIndex - 1);
                _fpsValueText.text = GameSettingsManager.GetCurrentFpsLabel();
            });

            _fpsValueText = CreateText("FpsValue", row.transform, GameSettingsManager.GetCurrentFpsLabel(), 15, FontStyle.Bold, Ps1UiTheme.BoneGold, TextAnchor.MiddleCenter);
            RectTransform vRt = _fpsValueText.rectTransform;
            vRt.anchorMin = new Vector2(1f, 0.5f);
            vRt.anchorMax = new Vector2(1f, 0.5f);
            vRt.pivot = new Vector2(1f, 0.5f);
            vRt.anchoredPosition = new Vector2(-36f, 0f);
            vRt.sizeDelta = new Vector2(136f, 30f);

            CreateStepButton(row.transform, "BtnNext", ">", new Vector2(0f, 0f), () =>
            {
                _onUiClickSfx?.Invoke();
                GameSettingsManager.SetFpsLimitIndex(GameSettingsManager.FpsLimitIndex + 1);
                _fpsValueText.text = GameSettingsManager.GetCurrentFpsLabel();
            });
        }

        private void CreateStepButton(Transform parent, string name, string text, Vector2 anchoredPos, Action onClick)
        {
            GameObject go = CreateUi(name, parent);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(30f, 30f);

            UnityEngine.UI.Image bg = go.AddComponent<UnityEngine.UI.Image>();
            Ps1UiTheme.ApplyPanelStyle(bg);

            UnityEngine.UI.Button btn = go.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick?.Invoke());

            UnityEngine.UI.Text lbl = CreateText("Lbl", go.transform, text, 16, FontStyle.Bold, Ps1UiTheme.BoneWhiteText, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform, 0f, 0f, 0f, 0f);
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

        private static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(right, top);
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
