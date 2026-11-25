using System;
using System.Collections.Generic;
using StomachAbyss.Combat;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MapaEstomago
{
    /// <summary>
    /// Sistema modular de arsenal de armas do jogador:
    /// - Gerencia multiplos slots de armas (Espada Cruz, Chicote Mini Tenia, futuros drops de bosses)
    /// - Garante que APENAS 1 arma e seu modelo 3D estejam visiveis na mao (Weapon_Mount_Pivot) por vez
    /// - Troca rapida pelas teclas 1, 2, 3...
    /// - Exibe uma HUD temporaria limpa indicando a arma equipada e o status atual
    /// </summary>
    public class PlayerWeaponArsenal : MonoBehaviour
    {
        [Serializable]
        public class WeaponSlotEntry
        {
            public string weaponId;
            public string displayName;
            public MonoBehaviour weaponController;
            public GameObject visualRoot;
            public bool unlocked = true;
        }

        [Header("Slots de Armas")]
        public List<WeaponSlotEntry> slots = new List<WeaponSlotEntry>();
        public int activeSlotIndex = 0;

        [Header("HUD Temporario de Arma Equipada")]
        [Tooltip("Desative quando implementar sua UI definitiva separada.")]
        public bool showTempArsenalHud = false;

        private SimplePlayerController _player;
        private Transform _weaponMount;
        private Texture2D _whiteTex;
        private GUIStyle _titleStyle;
        private GUIStyle _slotStyle;
        private GUIStyle _slotActiveStyle;
        private GUIStyle _statusStyle;

        public int ActiveSlotIndex => activeSlotIndex;
        public WeaponSlotEntry ActiveSlot => (activeSlotIndex >= 0 && activeSlotIndex < slots.Count) ? slots[activeSlotIndex] : null;

        private void Awake()
        {
            _player = GetComponent<SimplePlayerController>();
            _weaponMount = transform.Find("Weapon_Mount_Pivot");
            AutoDiscoverMissingVisuals();
        }

        private void Start()
        {
            if (slots.Count == 0)
            {
                RebuildSlotsFromAttachedWeapons();
            }
            ApplySlotSelection(Mathf.Clamp(activeSlotIndex, 0, Mathf.Max(0, slots.Count - 1)), playSfx: false);
        }

        private void Update()
        {
            if (_player != null && (_player.IsDead || _player.inputLocked)) return;

            int requestedSlot = ReadNumberKeyPress();
            if (requestedSlot >= 0 && requestedSlot < slots.Count)
            {
                EquipSlot(requestedSlot);
            }
        }

        public bool EquipSlot(int index)
        {
            if (index < 0 || index >= slots.Count) return false;
            WeaponSlotEntry entry = slots[index];
            if (entry == null || !entry.unlocked || entry.weaponController == null) return false;
            if (index == activeSlotIndex && _player != null && ReferenceEquals(_player.ActiveWeapon, entry.weaponController))
            {
                EnsureExclusiveVisuals(index);
                return true;
            }

            if (_player != null && _player.IsDodging) return false;

            ApplySlotSelection(index, playSfx: true);
            return true;
        }

        public bool EquipWeaponById(string weaponId)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && string.Equals(slots[i].weaponId, weaponId, StringComparison.OrdinalIgnoreCase))
                {
                    return EquipSlot(i);
                }
            }
            return false;
        }

        public void RegisterOrUpdateSlot(
            string weaponId,
            string displayName,
            MonoBehaviour weaponController,
            GameObject visualRoot,
            bool unlocked = true,
            bool equipNow = false)
        {
            if (weaponController == null || !(weaponController is IPlayerWeapon)) return;

            int existingIdx = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                WeaponSlotEntry e = slots[i];
                if (e != null && (ReferenceEquals(e.weaponController, weaponController) ||
                    (!string.IsNullOrEmpty(weaponId) && string.Equals(e.weaponId, weaponId, StringComparison.OrdinalIgnoreCase))))
                {
                    existingIdx = i;
                    break;
                }
            }

            if (existingIdx >= 0)
            {
                slots[existingIdx].weaponId = weaponId;
                slots[existingIdx].displayName = displayName;
                slots[existingIdx].weaponController = weaponController;
                slots[existingIdx].visualRoot = visualRoot;
                slots[existingIdx].unlocked = unlocked;
            }
            else
            {
                slots.Add(new WeaponSlotEntry
                {
                    weaponId = weaponId,
                    displayName = displayName,
                    weaponController = weaponController,
                    visualRoot = visualRoot,
                    unlocked = unlocked
                });
                existingIdx = slots.Count - 1;
            }

            if (equipNow)
            {
                activeSlotIndex = existingIdx;
            }
        }

        public void ApplyCurrentSlotImmediate()
        {
            if (slots.Count == 0)
            {
                RebuildSlotsFromAttachedWeapons();
            }
            if (slots.Count > 0)
            {
                ApplySlotSelection(Mathf.Clamp(activeSlotIndex, 0, slots.Count - 1), playSfx: false);
            }
        }

        private void ApplySlotSelection(int index, bool playSfx)
        {
            if (slots.Count == 0 || index < 0 || index >= slots.Count) return;

            activeSlotIndex = index;
            if (_player == null) _player = GetComponent<SimplePlayerController>();
            if (_weaponMount == null) _weaponMount = transform.Find("Weapon_Mount_Pivot");

            // Primeiro desativa todas as outras armas e garante seus visuais ocultos
            for (int i = 0; i < slots.Count; i++)
            {
                if (i == index) continue;
                WeaponSlotEntry other = slots[i];
                if (other == null) continue;

                if (other.weaponController is IPlayerWeapon wOther)
                {
                    wOther.OnWeaponUnequipped();
                }
                if (other.weaponController != null)
                {
                    other.weaponController.enabled = false;
                }
                if (other.visualRoot != null && other.visualRoot.activeSelf)
                {
                    other.visualRoot.SetActive(false);
                }
            }

            // Reseta o pivo de montagem para que nenhuma rotacao residual contamine a nova arma
            if (_weaponMount != null)
            {
                _weaponMount.localRotation = Quaternion.identity;
            }

            WeaponSlotEntry selected = slots[index];
            if (selected != null && selected.weaponController != null)
            {
                if (selected.visualRoot != null && !selected.visualRoot.activeSelf)
                {
                    selected.visualRoot.SetActive(true);
                }
                selected.weaponController.enabled = true;

                if (_player != null && selected.weaponController is IPlayerWeapon weaponInterface)
                {
                    _player.SetActiveWeapon(weaponInterface);
                }
                else if (selected.weaponController is IPlayerWeapon standaloneWeapon)
                {
                    standaloneWeapon.OnWeaponEquipped(_player);
                }
            }

            EnsureExclusiveVisuals(index);

            if (playSfx && Application.isPlaying)
            {
                CombatSfxBank.Play(CombatSfxId.WeaponSwap, 0.78f, 0.95f, 1.06f, 0.05f);
            }
        }

        private void EnsureExclusiveVisuals(int activeIndex)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                WeaponSlotEntry entry = slots[i];
                if (entry == null || entry.visualRoot == null) continue;
                bool shouldBeActive = (i == activeIndex) && entry.unlocked;
                if (entry.visualRoot.activeSelf != shouldBeActive)
                {
                    entry.visualRoot.SetActive(shouldBeActive);
                }
            }
        }

        private void RebuildSlotsFromAttachedWeapons()
        {
            CrossRazorGreatswordController sword = GetComponent<CrossRazorGreatswordController>();
            if (sword != null)
            {
                sword.ResolveSwordHierarchy();
                GameObject swordVis = sword.swordRoot != null ? sword.swordRoot.gameObject : FindChildByName("Cross_Razor_Greatsword");
                RegisterOrUpdateSlot("cross_razor", "Espada Cruz", sword, swordVis, true, false);
            }

            TeniaWhipWeapon whip = GetComponent<TeniaWhipWeapon>();
            if (whip != null)
            {
                GameObject whipVis = whip.whipRoot != null ? whip.whipRoot.gameObject : FindChildByName("Tenia_Whip");
                RegisterOrUpdateSlot("tenia_whip", "Mini Tenia", whip, whipVis, true, false);
            }

            LaminaArdenteWeapon ardente = GetComponent<LaminaArdenteWeapon>();
            if (ardente != null)
            {
                ardente.ResolveSwordHierarchy();
                GameObject ardenteVis = ardente.swordRoot != null ? ardente.swordRoot.gameObject : FindChildByName("Lamina_Ardente");
                RegisterOrUpdateSlot("lamina_ardente", "Lamina Ardente", ardente, ardenteVis, true, false);
            }
        }

        private void AutoDiscoverMissingVisuals()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                WeaponSlotEntry entry = slots[i];
                if (entry == null || entry.visualRoot != null) continue;

                if (entry.weaponController is CrossRazorGreatswordController sword)
                {
                    sword.ResolveSwordHierarchy();
                    if (sword.swordRoot != null) entry.visualRoot = sword.swordRoot.gameObject;
                }
                else if (entry.weaponController is TeniaWhipWeapon whip)
                {
                    if (whip.whipRoot != null) entry.visualRoot = whip.whipRoot.gameObject;
                    else entry.visualRoot = FindChildByName("Tenia_Whip");
                }
                else if (entry.weaponController is LaminaArdenteWeapon ardente)
                {
                    ardente.ResolveSwordHierarchy();
                    if (ardente.swordRoot != null) entry.visualRoot = ardente.swordRoot.gameObject;
                    else entry.visualRoot = FindChildByName("Lamina_Ardente");
                }
            }
        }

        private GameObject FindChildByName(string childName)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i].name == childName)
                {
                    return children[i].gameObject;
                }
            }
            return null;
        }

        private static int ReadNumberKeyPress()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) return 0;
                if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) return 1;
                if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) return 2;
                if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) return 3;
                if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) return 4;
            }
            return -1;
#else
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) return 0;
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) return 1;
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) return 2;
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) return 3;
            if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) return 4;
            return -1;
#endif
        }

        private void EnsureGuiStyles()
        {
            if (_whiteTex == null)
            {
                _whiteTex = Texture2D.whiteTexture;
            }
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = new Color(0.95f, 0.90f, 0.85f) }
                };
            }
            if (_slotStyle == null)
            {
                _slotStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Normal,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.75f, 0.72f, 0.72f) }
                };
            }
            if (_slotActiveStyle == null)
            {
                _slotActiveStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.55f, 1.0f, 0.42f) }
                };
            }
            if (_statusStyle == null)
            {
                _statusStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = new Color(0.88f, 0.96f, 0.86f) }
                };
            }
        }

        private void OnGUI()
        {
            if (!showTempArsenalHud || slots == null || slots.Count == 0) return;
            EnsureGuiStyles();

            int unlockedCount = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && slots[i].unlocked) unlockedCount++;
            }
            if (unlockedCount == 0) return;

            float slotW = 148f;
            float slotGap = 8f;
            float panelW = Mathf.Max(316f, unlockedCount * (slotW + slotGap) + 16f);
            float panelH = 82f;
            float x = 18f;
            float y = Screen.height - panelH - 18f;

            // Fundo escuro translucido do HUD temporario
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.02f, 0.03f, 0.82f);
            GUI.DrawTexture(new Rect(x, y, panelW, panelH), _whiteTex);

            GUI.color = new Color(0.45f, 0.14f, 0.16f, 0.9f);
            GUI.DrawTexture(new Rect(x, y, panelW, 2f), _whiteTex);
            GUI.DrawTexture(new Rect(x, y + panelH - 2f, panelW, 2f), _whiteTex);

            float sx = x + 10f;
            float sy = y + 8f;
            for (int i = 0; i < slots.Count; i++)
            {
                WeaponSlotEntry entry = slots[i];
                if (entry == null || !entry.unlocked) continue;

                bool isActive = (i == activeSlotIndex);
                Rect cardRect = new Rect(sx, sy, slotW, 34f);

                GUI.color = isActive
                    ? new Color(0.18f, 0.34f, 0.14f, 0.95f)
                    : new Color(0.12f, 0.08f, 0.09f, 0.85f);
                GUI.DrawTexture(cardRect, _whiteTex);

                if (isActive)
                {
                    GUI.color = new Color(0.45f, 1.0f, 0.28f, 0.95f);
                    GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, 2f), _whiteTex);
                    GUI.DrawTexture(new Rect(cardRect.x, cardRect.yMax - 2f, cardRect.width, 2f), _whiteTex);
                }

                GUI.color = Color.white;
                string label = $"[{i + 1}] {entry.displayName}";
                GUI.Label(cardRect, label, isActive ? _slotActiveStyle : _slotStyle);
                sx += slotW + slotGap;
            }

            // Linha de status detalhado da arma equipada
            WeaponSlotEntry active = ActiveSlot;
            string statusLine = "Nenhuma arma";
            if (active != null && active.weaponController is IPlayerWeapon w)
            {
                statusLine = $"Equipada: {w.WeaponDisplayName}  |  {w.HudStatusText}";
            }

            GUI.color = Color.white;
            GUI.Label(new Rect(x + 12f, y + 48f, panelW - 24f, 24f), statusLine, _statusStyle);
            GUI.color = prev;
        }
    }
}
