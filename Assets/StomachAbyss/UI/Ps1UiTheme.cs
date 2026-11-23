using UnityEngine;

namespace StomachAbyss.UI
{
    /// <summary>
    /// Tema visual unificado PS1 Minimalista Matte (Opcao 1 - Obsidiana & Osso Envelhecido).
    /// Compartilhado entre Dialogos (VirgilDialogueUI), Menu Inicial (MainMenuUIController),
    /// Banners de Area, Prompts de Interacao, Barra de Boss, Selos de Pilar e futuro Menu de Pause.
    /// </summary>
    public static class Ps1UiTheme
    {
        // Paleta oficial Opcao 1 (Obsidiana Fosca + Osso Envelhecido)
        public static readonly Color PixelBlack       = new Color(0.025f, 0.022f, 0.028f, 1.00f);
        public static readonly Color ObsidianBg       = new Color(0.075f, 0.072f, 0.080f, 0.96f);
        public static readonly Color ObsidianHeader   = new Color(0.112f, 0.106f, 0.118f, 0.98f);
        public static readonly Color ObsidianHover    = new Color(0.175f, 0.162f, 0.148f, 0.98f);
        public static readonly Color ObsidianRecessed = new Color(0.050f, 0.048f, 0.055f, 0.96f);

        public static readonly Color BoneTrim         = new Color(0.722f, 0.678f, 0.573f, 1.00f); // #B8AD92
        public static readonly Color BoneTrimMuted    = new Color(0.455f, 0.425f, 0.365f, 0.95f);
        public static readonly Color BoneGold         = new Color(0.902f, 0.788f, 0.518f, 1.00f);
        public static readonly Color BoneWhiteText    = new Color(0.930f, 0.910f, 0.865f, 1.00f);
        public static readonly Color BoneMutedText    = new Color(0.680f, 0.650f, 0.590f, 0.94f);

        public static readonly Color CrimsonAccent    = new Color(0.740f, 0.145f, 0.165f, 1.00f);
        public static readonly Color CrimsonTagBg     = new Color(0.165f, 0.055f, 0.065f, 0.96f);
        public static readonly Color CrimsonTagBorder = new Color(0.620f, 0.260f, 0.240f, 0.96f);
        public static readonly Color CrimsonTagText   = new Color(0.940f, 0.780f, 0.680f, 1.00f);

        private static Sprite s_panelSprite;
        private static Sprite s_buttonSprite;
        private static Sprite s_badgeSprite;
        private static Sprite s_diamondSprite;
        private static Sprite s_whiteSprite;
        private static Texture2D s_whiteTex;

        public static Texture2D WhiteTexture
        {
            get
            {
                if (s_whiteTex == null)
                {
                    s_whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Point,
                        name = "PS1_UI_WhiteTex"
                    };
                    s_whiteTex.SetPixel(0, 0, Color.white);
                    s_whiteTex.Apply();
                }
                return s_whiteTex;
            }
        }

        public static Sprite WhiteSprite
        {
            get
            {
                if (s_whiteSprite == null)
                {
                    s_whiteSprite = Sprite.Create(WhiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
                }
                return s_whiteSprite;
            }
        }

        /// <summary>
        /// Moldura 9-slice dupla pixelada (Opcao 1):
        /// 2px preto externo -> 2px osso envelhecido -> 2px obsidiana -> 1px filete osso interno -> fundo obsidiana fosco.
        /// </summary>
        public static Sprite PanelSprite
        {
            get
            {
                if (s_panelSprite == null)
                {
                    s_panelSprite = BuildDoubleBevelSprite(
                        "PS1_Panel_9Slice",
                        size: 32,
                        outerBlack: 2,
                        outerTrim: 2,
                        gapDark: 2,
                        innerTrim: 1,
                        fillColor: ObsidianBg,
                        trimOuterColor: BoneTrim,
                        trimInnerColor: BoneTrimMuted);
                }
                return s_panelSprite;
            }
        }

        /// <summary>
        /// Moldura 9-slice mais fina para botoes de menu, pills e campos interativos.
        /// </summary>
        public static Sprite ButtonSprite
        {
            get
            {
                if (s_buttonSprite == null)
                {
                    s_buttonSprite = BuildDoubleBevelSprite(
                        "PS1_Button_9Slice",
                        size: 24,
                        outerBlack: 2,
                        outerTrim: 2,
                        gapDark: 1,
                        innerTrim: 0,
                        fillColor: Color.white, // tintado via Image.color para transicao de hover
                        trimOuterColor: BoneTrim,
                        trimInnerColor: BoneTrimMuted,
                        tintFillOnly: false);
                }
                return s_buttonSprite;
            }
        }

        /// <summary>
        /// Moldura 9-slice para badges/etiquetas (ex: FIM DO MVP) em tom carmesim/osso discreto.
        /// </summary>
        public static Sprite BadgeSprite
        {
            get
            {
                if (s_badgeSprite == null)
                {
                    s_badgeSprite = BuildDoubleBevelSprite(
                        "PS1_Badge_9Slice",
                        size: 24,
                        outerBlack: 1,
                        outerTrim: 2,
                        gapDark: 1,
                        innerTrim: 0,
                        fillColor: CrimsonTagBg,
                        trimOuterColor: CrimsonTagBorder,
                        trimInnerColor: BoneTrimMuted);
                }
                return s_badgeSprite;
            }
        }

        /// <summary>
        /// Losango pixelado PS1 para indicadores de selecao e prompt de continuar.
        /// </summary>
        public static Sprite PixelDiamondSprite
        {
            get
            {
                if (s_diamondSprite == null)
                {
                    const int s = 15;
                    Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        name = "PS1_PixelDiamond"
                    };
                    int c = s / 2;
                    for (int y = 0; y < s; y++)
                    {
                        for (int x = 0; x < s; x++)
                        {
                            int manhattan = Mathf.Abs(x - c) + Mathf.Abs(y - c);
                            if (manhattan <= c)
                            {
                                tex.SetPixel(x, y, manhattan == c ? PixelBlack : Color.white);
                            }
                            else
                            {
                                tex.SetPixel(x, y, Color.clear);
                            }
                        }
                    }
                    tex.Apply();
                    s_diamondSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
                }
                return s_diamondSprite;
            }
        }

        /// <summary>
        /// Aplica a moldura 9-slice Opcao 1 a uma UnityEngine.UI.Image.
        /// </summary>
        public static void ApplyPanelStyle(UnityEngine.UI.Image img, bool isBadge = false)
        {
            if (img == null) return;
            img.sprite = isBadge ? BadgeSprite : PanelSprite;
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = Color.white;
        }

        /// <summary>
        /// Desenha uma caixa no estilo PS1 Opcao 1 (Obsidiana + borda dupla osso) para OnGUI legado.
        /// </summary>
        public static void DrawOnGuiPanel(Rect rect, float alpha = 1f, bool highlightGold = false)
        {
            Color prev = GUI.color;
            Texture2D tex = WhiteTexture;

            // Fundo obsidiana fosco
            GUI.color = new Color(ObsidianBg.r, ObsidianBg.g, ObsidianBg.b, ObsidianBg.a * alpha);
            GUI.DrawTexture(rect, tex);

            // Borda externa preta (2px)
            DrawOnGuiBorder(rect, 2f, new Color(PixelBlack.r, PixelBlack.g, PixelBlack.b, alpha));

            // Borda principal osso envelhecido (2px)
            Color trim = highlightGold ? BoneGold : BoneTrim;
            DrawOnGuiBorder(Inset(rect, 2f), 2f, new Color(trim.r, trim.g, trim.b, alpha));

            // Borda interna fina sutil (1px)
            DrawOnGuiBorder(Inset(rect, 5f), 1f, new Color(BoneTrimMuted.r, BoneTrimMuted.g, BoneTrimMuted.b, 0.72f * alpha));

            GUI.color = prev;
        }

        private static void DrawOnGuiBorder(Rect r, float t, Color c)
        {
            GUI.color = c;
            Texture2D tex = WhiteTexture;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), tex);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), tex);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), tex);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), tex);
        }

        private static Rect Inset(Rect r, float amount)
        {
            return new Rect(r.x + amount, r.y + amount, Mathf.Max(0f, r.width - amount * 2f), Mathf.Max(0f, r.height - amount * 2f));
        }

        private static Sprite BuildDoubleBevelSprite(
            string spriteName,
            int size,
            int outerBlack,
            int outerTrim,
            int gapDark,
            int innerTrim,
            Color fillColor,
            Color trimOuterColor,
            Color trimInnerColor,
            bool tintFillOnly = false)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = spriteName
            };

            int b1 = outerBlack;
            int b2 = b1 + outerTrim;
            int b3 = b2 + gapDark;
            int b4 = b3 + innerTrim;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int d = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    bool isCornerCut = (x == 0 || x == size - 1) && (y == 0 || y == size - 1);

                    Color px;
                    if (isCornerCut)
                    {
                        px = Color.clear;
                    }
                    else if (d < b1)
                    {
                        px = PixelBlack;
                    }
                    else if (d < b2)
                    {
                        // Leve chanfro pixelado PS1 (topo/esquerda ligeiramente mais claro)
                        bool topOrLeft = (y >= size - b2) || (x < b2);
                        px = topOrLeft ? trimOuterColor : Color.Lerp(trimOuterColor, PixelBlack, 0.22f);
                    }
                    else if (d < b3)
                    {
                        px = ObsidianRecessed;
                    }
                    else if (d < b4)
                    {
                        px = trimInnerColor;
                    }
                    else
                    {
                        px = fillColor;
                    }

                    tex.SetPixel(x, y, px);
                }
            }

            tex.Apply();
            int border = b4 + 1;
            return Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }
    }
}
