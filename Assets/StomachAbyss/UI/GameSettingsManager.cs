using System;
using UnityEngine;

namespace StomachAbyss.UI
{
    public enum SoundCategory
    {
        Master = 0,
        Music = 1,
        Ambient = 2,
        Monsters = 3,
        Actions = 4
    }

    /// <summary>
    /// Gerenciador central de configuracoes (Audio por categoria, Video e Jogabilidade)
    /// com persistencia automatica em PlayerPrefs e aplicacao global em tempo real.
    /// </summary>
    public static class GameSettingsManager
    {
        private const string KeyMaster     = "StomachAbyss_MasterVolume";
        private const string KeyMusic      = "StomachAbyss_MusicVolume";
        private const string KeyAmbient    = "StomachAbyss_AmbientVolume";
        private const string KeyMonsters   = "StomachAbyss_MonstersVolume";
        private const string KeyActions    = "StomachAbyss_ActionsVolume";
        private const string KeyFullscreen = "StomachAbyss_Fullscreen";
        private const string KeyResIndex   = "StomachAbyss_ResolutionIndex";
        private const string KeyVSync      = "StomachAbyss_VSync";
        private const string KeyBrightness = "StomachAbyss_Brightness";
        private const string KeyShake      = "StomachAbyss_ScreenShake";

        public static readonly Vector2Int[] SupportedResolutions =
        {
            new Vector2Int(1920, 1080),
            new Vector2Int(1600, 900),
            new Vector2Int(1280, 720)
        };

        private static bool s_initialized;

        public static float MasterVolume { get; private set; } = 0.85f;
        public static float MusicVolume { get; private set; } = 0.85f;
        public static float AmbientVolume { get; private set; } = 0.85f;
        public static float MonstersVolume { get; private set; } = 0.90f;
        public static float ActionsVolume { get; private set; } = 0.90f;

        public static bool Fullscreen { get; private set; } = true;
        public static int ResolutionIndex { get; private set; } = 0;
        public static bool VSyncEnabled { get; private set; } = true;
        public static float Brightness { get; private set; } = 1.0f;
        public static bool ScreenShakeEnabled { get; private set; } = true;

        public static event Action OnSettingsChanged;

        private static UnityEngine.UI.Image s_brightnessOverlay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (s_initialized) return;
            s_initialized = true;

            MasterVolume       = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMaster, 0.85f));
            MusicVolume        = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMusic, 0.85f));
            AmbientVolume      = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyAmbient, 0.85f));
            MonstersVolume     = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMonsters, 0.90f));
            ActionsVolume      = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyActions, 0.90f));

            Fullscreen         = PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
            ResolutionIndex    = Mathf.Clamp(PlayerPrefs.GetInt(KeyResIndex, 0), 0, SupportedResolutions.Length - 1);
            VSyncEnabled       = PlayerPrefs.GetInt(KeyVSync, 1) == 1;
            Brightness         = Mathf.Clamp(PlayerPrefs.GetFloat(KeyBrightness, 1.0f), 0.5f, 1.5f);
            ScreenShakeEnabled = PlayerPrefs.GetInt(KeyShake, 1) == 1;

            ApplyAudioGlobal();
            ApplyVSync();
            ApplyBrightnessOverlay();
        }

        public static float GetCategoryVolume(SoundCategory category)
        {
            if (!s_initialized) Initialize();
            switch (category)
            {
                case SoundCategory.Music:    return MusicVolume;
                case SoundCategory.Ambient:  return AmbientVolume;
                case SoundCategory.Monsters: return MonstersVolume;
                case SoundCategory.Actions:  return ActionsVolume;
                default:                     return MasterVolume;
            }
        }

        public static void SetMasterVolume(float value)
        {
            if (!s_initialized) Initialize();
            MasterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyMaster, MasterVolume);
            ApplyAudioGlobal();
            NotifyChanged();
        }

        public static void SetMusicVolume(float value)
        {
            if (!s_initialized) Initialize();
            MusicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyMusic, MusicVolume);
            NotifyChanged();
        }

        public static void SetAmbientVolume(float value)
        {
            if (!s_initialized) Initialize();
            AmbientVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyAmbient, AmbientVolume);
            NotifyChanged();
        }

        public static void SetMonstersVolume(float value)
        {
            if (!s_initialized) Initialize();
            MonstersVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyMonsters, MonstersVolume);
            NotifyChanged();
        }

        public static void SetActionsVolume(float value)
        {
            if (!s_initialized) Initialize();
            ActionsVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyActions, ActionsVolume);
            NotifyChanged();
        }

        public static void SetFullscreen(bool enabled)
        {
            if (!s_initialized) Initialize();
            Fullscreen = enabled;
            PlayerPrefs.SetInt(KeyFullscreen, Fullscreen ? 1 : 0);
            ApplyDisplayMode();
            NotifyChanged();
        }

        public static void SetResolutionIndex(int index)
        {
            if (!s_initialized) Initialize();
            int len = SupportedResolutions.Length;
            ResolutionIndex = ((index % len) + len) % len;
            PlayerPrefs.SetInt(KeyResIndex, ResolutionIndex);
            ApplyDisplayMode();
            NotifyChanged();
        }

        public static string GetCurrentResolutionLabel()
        {
            if (!s_initialized) Initialize();
            Vector2Int r = SupportedResolutions[Mathf.Clamp(ResolutionIndex, 0, SupportedResolutions.Length - 1)];
            return $"{r.x} x {r.y}";
        }

        public static void SetVSync(bool enabled)
        {
            if (!s_initialized) Initialize();
            VSyncEnabled = enabled;
            PlayerPrefs.SetInt(KeyVSync, VSyncEnabled ? 1 : 0);
            ApplyVSync();
            NotifyChanged();
        }

        public static void SetBrightness(float value)
        {
            if (!s_initialized) Initialize();
            Brightness = Mathf.Clamp(value, 0.5f, 1.5f);
            PlayerPrefs.SetFloat(KeyBrightness, Brightness);
            ApplyBrightnessOverlay();
            NotifyChanged();
        }

        public static void SetScreenShake(bool enabled)
        {
            if (!s_initialized) Initialize();
            ScreenShakeEnabled = enabled;
            PlayerPrefs.SetInt(KeyShake, ScreenShakeEnabled ? 1 : 0);
            NotifyChanged();
        }

        public static void SavePrefs()
        {
            PlayerPrefs.Save();
        }

        private static void ApplyAudioGlobal()
        {
            AudioListener.volume = MasterVolume;
        }

        private static void ApplyVSync()
        {
            QualitySettings.vSyncCount = VSyncEnabled ? 1 : 0;
        }

        private static void ApplyDisplayMode()
        {
            if (!Application.isPlaying) return;
            Vector2Int r = SupportedResolutions[Mathf.Clamp(ResolutionIndex, 0, SupportedResolutions.Length - 1)];
            Screen.SetResolution(r.x, r.y, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        private static void ApplyBrightnessOverlay()
        {
            if (!Application.isPlaying) return;

            if (Mathf.Abs(Brightness - 1.0f) < 0.01f)
            {
                if (s_brightnessOverlay != null)
                {
                    s_brightnessOverlay.gameObject.SetActive(false);
                }
                return;
            }

            if (s_brightnessOverlay == null)
            {
                GameObject canvasGo = new GameObject("GameSettings_BrightnessCanvas");
                UnityEngine.Object.DontDestroyOnLoad(canvasGo);

                Canvas canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000;

                GameObject imgGo = new GameObject("BrightnessVeil", typeof(RectTransform));
                imgGo.transform.SetParent(canvasGo.transform, false);
                RectTransform rt = imgGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                s_brightnessOverlay = imgGo.AddComponent<UnityEngine.UI.Image>();
                s_brightnessOverlay.raycastTarget = false;
            }

            s_brightnessOverlay.gameObject.SetActive(true);
            if (Brightness < 1.0f)
            {
                // Escurece suavemente (ate 0.60 alpha em 50%)
                float darkAlpha = Mathf.Clamp01((1.0f - Brightness) * 1.2f);
                s_brightnessOverlay.color = new Color(0f, 0f, 0f, darkAlpha);
            }
            else
            {
                // Clareia suavemente (ate 0.22 alpha em 150%)
                float brightAlpha = Mathf.Clamp01((Brightness - 1.0f) * 0.44f);
                s_brightnessOverlay.color = new Color(1f, 0.98f, 0.95f, brightAlpha);
            }
        }

        private static void NotifyChanged()
        {
            OnSettingsChanged?.Invoke();
        }
    }
}
