using MapaEstomago;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StomachAbyss.UI.Editor
{
    public static class PlayerHudBuilder
    {
        private const string SpritesFolder = "Assets/StomachAbyss/UI/Sprites";
        private const string MaterialsFolder = "Assets/StomachAbyss/UI/Materials";
        private const string ShaderPath = "Assets/StomachAbyss/UI/Shaders/UI_LiquidOrb_PS1.shader";

        private const string OrbFramePath = SpritesFolder + "/HUD_OrbFrame_PS1.png";
        private const string SlotBarPath = SpritesFolder + "/HUD_SlotBar_Empty_PS1.png";
        private const string HighlightPath = SpritesFolder + "/HUD_SlotHighlight_PS1.png";

        private const string HpMatPath = MaterialsFolder + "/Mat_HUD_Orb_HP.mat";
        private const string StaminaMatPath = MaterialsFolder + "/Mat_HUD_Orb_Stamina.mat";

        [MenuItem("StomachAbyss/UI/Build PS1 Bottom HUD")]
        public static void BuildHudInActiveScene()
        {
            ConfigureSpriteImporter(OrbFramePath);
            ConfigureSpriteImporter(SlotBarPath);
            ConfigureSpriteImporter(HighlightPath);

            AssetDatabase.Refresh();

            Sprite orbFrameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(OrbFramePath);
            Sprite slotBarSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlotBarPath);
            Sprite highlightSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HighlightPath);

            if (orbFrameSprite == null || slotBarSprite == null || highlightSprite == null)
            {
                Debug.LogError("[PlayerHudBuilder] Falha ao carregar sprites da HUD em " + SpritesFolder);
                return;
            }

            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder("Assets/StomachAbyss/UI", "Materials");
            }

            Shader orbShader = Shader.Find("StomachAbyss/UI_LiquidOrb_PS1");
            if (orbShader == null)
            {
                orbShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            }
            if (orbShader == null)
            {
                Debug.LogError("[PlayerHudBuilder] Shader StomachAbyss/UI_LiquidOrb_PS1 nao encontrado!");
                return;
            }

            Material hpMat = CreateOrUpdateOrbMaterial(
                HpMatPath,
                orbShader,
                dark: new Color(0.26f, 0.02f, 0.05f, 1f),
                mid: new Color(0.76f, 0.06f, 0.11f, 1f),
                bright: new Color(0.98f, 0.22f, 0.22f, 1f),
                foam: new Color(1.0f, 0.56f, 0.52f, 1f),
                ghost: new Color(0.96f, 0.78f, 0.20f, 1f),
                waveSpeed: 2.3f,
                bubbleStrength: 0.42f);

            Material staminaMat = CreateOrUpdateOrbMaterial(
                StaminaMatPath,
                orbShader,
                dark: new Color(0.05f, 0.24f, 0.07f, 1f),
                mid: new Color(0.24f, 0.68f, 0.16f, 1f),
                bright: new Color(0.62f, 0.94f, 0.26f, 1f),
                foam: new Color(0.84f, 1.0f, 0.62f, 1f),
                ghost: new Color(0.88f, 0.96f, 0.42f, 1f),
                waveSpeed: 3.1f,
                bubbleStrength: 0.68f);

            // Reaproveita ou cria PlayerHUD_Canvas sem duplicar
            GameObject existing = GameObject.Find("PlayerHUD_Canvas");
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            GameObject canvasGo = new GameObject("PlayerHUD_Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            UnityEngine.UI.CanvasScaler scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Container principal ancorado e grudado na parte inferior central da tela
            GameObject rootGo = CreateUiObject("HUD_BottomBar_Root", canvasGo.transform);
            RectTransform rootRt = rootGo.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0f);
            rootRt.anchorMax = new Vector2(0.5f, 0f);
            rootRt.pivot = new Vector2(0.5f, 0f);
            rootRt.sizeDelta = new Vector2(960f, 174f);
            rootRt.anchoredPosition = new Vector2(0f, 6f);

            // --- ORBE ESQUERDO: HP (Vermelho Liquido) ---
            UnityEngine.UI.Image hpLiquidImg = CreateOrbCluster(
                "HP_Orb",
                rootGo.transform,
                anchoredPos: new Vector2(-392f, 88f),
                frameSize: 164f,
                liquidSize: 126f,
                frameSprite: orbFrameSprite,
                liquidMaterial: hpMat);

            // --- ORBE DIREITO: ESTAMINA (Verde Liquido) ---
            UnityEngine.UI.Image staminaLiquidImg = CreateOrbCluster(
                "Stamina_Orb",
                rootGo.transform,
                anchoredPos: new Vector2(392f, 88f),
                frameSize: 164f,
                liquidSize: 126f,
                frameSprite: orbFrameSprite,
                liquidMaterial: staminaMat);

            // --- BARRA CENTRAL COM 5 SLOTS VAZIOS (1, 2, 3, R, RMB) ---
            GameObject barGo = CreateUiObject("SlotBar_Center", rootGo.transform);
            RectTransform barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0.5f, 0f);
            barRt.anchorMax = new Vector2(0.5f, 0f);
            barRt.pivot = new Vector2(0.5f, 0f);
            barRt.sizeDelta = new Vector2(616f, 162f);
            barRt.anchoredPosition = new Vector2(0f, 2f);

            UnityEngine.UI.Image barBg = barGo.AddComponent<UnityEngine.UI.Image>();
            barBg.sprite = slotBarSprite;
            barBg.preserveAspect = false;
            barBg.raycastTarget = false;

            // Posicoes normalizadas exatas dos 5 slots dentro de HUD_SlotBar_Empty_PS1.png (768x202)
            float[] slotCenterNormX = { 0.1107f, 0.3060f, 0.4993f, 0.6940f, 0.8880f };
            float slotWidthNorm = 0.145f;
            float slotMinYNorm = 0.3465f;
            float slotMaxYNorm = 0.8762f;

            UnityEngine.UI.Image[] iconImgs = new UnityEngine.UI.Image[5];
            UnityEngine.UI.Image[] highlightImgs = new UnityEngine.UI.Image[3];
            UnityEngine.UI.Text healChargesText = null;

            string[] slotNames = { "Slot_1_Weapon1", "Slot_2_Weapon2", "Slot_3_Weapon3", "Slot_4_Heal_R", "Slot_5_Special_RMB" };

            for (int i = 0; i < 5; i++)
            {
                GameObject slotGo = CreateUiObject(slotNames[i], barGo.transform);
                RectTransform slotRt = slotGo.GetComponent<RectTransform>();
                float halfW = slotWidthNorm * 0.5f;
                slotRt.anchorMin = new Vector2(slotCenterNormX[i] - halfW, slotMinYNorm);
                slotRt.anchorMax = new Vector2(slotCenterNormX[i] + halfW, slotMaxYNorm);
                slotRt.offsetMin = Vector2.zero;
                slotRt.offsetMax = Vector2.zero;

                // Icone separado (vazio e desativado por padrao)
                GameObject iconGo = CreateUiObject("Icon", slotGo.transform);
                RectTransform iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.anchorMin = new Vector2(0.08f, 0.08f);
                iconRt.anchorMax = new Vector2(0.92f, 0.92f);
                iconRt.offsetMin = Vector2.zero;
                iconRt.offsetMax = Vector2.zero;

                UnityEngine.UI.Image iconImg = iconGo.AddComponent<UnityEngine.UI.Image>();
                iconImg.color = new Color(1f, 1f, 1f, 0f);
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                iconImg.enabled = false;
                iconImgs[i] = iconImg;

                // Destaque dourado-osso nos 3 slots de arma (1, 2, 3)
                if (i < 3)
                {
                    GameObject hlGo = CreateUiObject("ActiveHighlight", slotGo.transform);
                    RectTransform hlRt = hlGo.GetComponent<RectTransform>();
                    hlRt.anchorMin = Vector2.zero;
                    hlRt.anchorMax = Vector2.one;
                    hlRt.offsetMin = new Vector2(-3f, -3f);
                    hlRt.offsetMax = new Vector2(3f, 3f);

                    UnityEngine.UI.Image hlImg = hlGo.AddComponent<UnityEngine.UI.Image>();
                    hlImg.sprite = highlightSprite;
                    hlImg.raycastTarget = false;
                    hlImg.enabled = (i == 0);
                    highlightImgs[i] = hlImg;
                }

                // Contador numerico discreto de cargas de cura no slot R (indice 3)
                if (i == 3)
                {
                    GameObject badgeGo = CreateUiObject("HealChargesText", slotGo.transform);
                    RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
                    badgeRt.anchorMin = new Vector2(0.55f, 0.02f);
                    badgeRt.anchorMax = new Vector2(0.96f, 0.40f);
                    badgeRt.offsetMin = Vector2.zero;
                    badgeRt.offsetMax = Vector2.zero;

                    healChargesText = badgeGo.AddComponent<UnityEngine.UI.Text>();
                    healChargesText.text = "2";
                    healChargesText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    healChargesText.fontSize = 22;
                    healChargesText.fontStyle = FontStyle.Bold;
                    healChargesText.alignment = TextAnchor.LowerRight;
                    healChargesText.color = new Color(0.95f, 0.90f, 0.78f, 1f);
                    healChargesText.raycastTarget = false;

                    UnityEngine.UI.Outline outline = badgeGo.AddComponent<UnityEngine.UI.Outline>();
                    outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                }
            }

            // Adiciona e conecta o PlayerHudController
            PlayerHudController controller = canvasGo.AddComponent<PlayerHudController>();
            SerializedObject so = new SerializedObject(controller);

            SimplePlayerController player = Object.FindFirstObjectByType<SimplePlayerController>();
            PlayerWeaponArsenal arsenal = player != null ? player.GetComponent<PlayerWeaponArsenal>() : Object.FindFirstObjectByType<PlayerWeaponArsenal>();

            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("arsenal").objectReferenceValue = arsenal;
            so.FindProperty("hpOrbLiquidImage").objectReferenceValue = hpLiquidImg;
            so.FindProperty("staminaOrbLiquidImage").objectReferenceValue = staminaLiquidImg;
            so.FindProperty("healChargesText").objectReferenceValue = healChargesText;

            SerializedProperty iconArray = so.FindProperty("slotIconImages");
            iconArray.arraySize = 5;
            for (int i = 0; i < 5; i++)
            {
                iconArray.GetArrayElementAtIndex(i).objectReferenceValue = iconImgs[i];
            }

            SerializedProperty hlArray = so.FindProperty("weaponSlotHighlights");
            hlArray.arraySize = 3;
            for (int i = 0; i < 3; i++)
            {
                hlArray.GetArrayElementAtIndex(i).objectReferenceValue = highlightImgs[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[PlayerHudBuilder] PlayerHUD_Canvas criado e conectado com sucesso!");
        }

        private static UnityEngine.UI.Image CreateOrbCluster(
            string name,
            Transform parent,
            Vector2 anchoredPos,
            float frameSize,
            float liquidSize,
            Sprite frameSprite,
            Material liquidMaterial)
        {
            GameObject cluster = CreateUiObject(name, parent);
            RectTransform rt = cluster.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(frameSize, frameSize);
            rt.anchoredPosition = anchoredPos;

            // Camada de liquido animado por shader (atras da moldura)
            GameObject liquidGo = CreateUiObject("LiquidFill", cluster.transform);
            RectTransform lRt = liquidGo.GetComponent<RectTransform>();
            lRt.anchorMin = new Vector2(0.5f, 0.5f);
            lRt.anchorMax = new Vector2(0.5f, 0.5f);
            lRt.pivot = new Vector2(0.5f, 0.5f);
            lRt.sizeDelta = new Vector2(liquidSize, liquidSize);
            lRt.anchoredPosition = Vector2.zero;

            UnityEngine.UI.Image liquidImg = liquidGo.AddComponent<UnityEngine.UI.Image>();
            liquidImg.material = liquidMaterial;
            liquidImg.color = Color.white;
            liquidImg.raycastTarget = false;

            // Moldura circular pixelada por cima
            GameObject frameGo = CreateUiObject("OrbFrame", cluster.transform);
            RectTransform fRt = frameGo.GetComponent<RectTransform>();
            fRt.anchorMin = Vector2.zero;
            fRt.anchorMax = Vector2.one;
            fRt.offsetMin = Vector2.zero;
            fRt.offsetMax = Vector2.zero;

            UnityEngine.UI.Image frameImg = frameGo.AddComponent<UnityEngine.UI.Image>();
            frameImg.sprite = frameSprite;
            frameImg.raycastTarget = false;

            return liquidImg;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void ConfigureSpriteImporter(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }
            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                dirty = true;
            }
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }
            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }

        private static Material CreateOrUpdateOrbMaterial(
            string matPath,
            Shader shader,
            Color dark,
            Color mid,
            Color bright,
            Color foam,
            Color ghost,
            float waveSpeed,
            float bubbleStrength)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetFloat("_FillAmount", 0.85f);
            mat.SetFloat("_GhostFill", 0.85f);
            mat.SetColor("_LiquidDark", dark);
            mat.SetColor("_LiquidMid", mid);
            mat.SetColor("_LiquidBright", bright);
            mat.SetColor("_FoamColor", foam);
            mat.SetColor("_GhostColor", ghost);
            mat.SetColor("_EmptyGlassColor", new Color(0.05f, 0.045f, 0.055f, 0.94f));
            mat.SetFloat("_WaveAmp", 0.026f);
            mat.SetFloat("_WaveFreq", 6.5f);
            mat.SetFloat("_WaveSpeed", waveSpeed);
            mat.SetFloat("_BubbleStrength", bubbleStrength);
            mat.SetFloat("_PixelRes", 48f);
            mat.SetFloat("_DitherStrength", 0.09f);

            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
