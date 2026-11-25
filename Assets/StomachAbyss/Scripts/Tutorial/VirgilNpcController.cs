using MapaEstomago;
using StomachAbyss.Progression;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StomachAbyss.Tutorial
{
    public enum VirgilAppearanceRole
    {
        CardiaTutorial,
        CathedralBridge,
        CathedralPostBoss
    }

    [System.Serializable]
    public struct VirgilDialoguePage
    {
        [TextArea(2, 4)]
        public string text;
        public string hintBadge;
    }

    /// <summary>
    /// Controller for "O Vestígio", the broken ancestral-strain NPC that guides Gon
    /// in the Cardia Tutorial chamber and before the Cathedral of the Tenia.
    /// </summary>
    public class VirgilNpcController : MonoBehaviour
    {
        public VirgilAppearanceRole role = VirgilAppearanceRole.CardiaTutorial;
        public string npcName = "VESTÍGIO";
        public string npcTitle = "O Primeiro Quebrado • Cepa Ancestral";
        public float interactRadius = 4.0f;
        public Transform visualRoot;
        public Light biolumLight;
        public bool autoFacePlayer = true;
        public VirgilDialoguePage[] firstPages;
        public VirgilDialoguePage[] repeatPages;

        private static VirgilNpcController s_promptOwner;

        private Transform _playerTransform;
        private SimplePlayerController _playerController;
        private Vector3 _visualBaseLocalPos;
        private Vector3 _visualBaseLocalScale = Vector3.one;
        private bool _hasVisualCached;
        private float _baseLightIntensity = 1.6f;
        private float _idlePhaseOffset;
        private float _lastDistToPlayer = float.MaxValue;
        private bool _hasSpoken;
        private bool _bridgeCheckpointUnlocked;

        private void Awake()
        {
            _idlePhaseOffset = Random.Range(0f, Mathf.PI * 2f);

            if (visualRoot != null)
            {
                _visualBaseLocalPos = visualRoot.localPosition;
                _visualBaseLocalScale = visualRoot.localScale;
                _hasVisualCached = true;
            }

            if (biolumLight != null)
            {
                _baseLightIntensity = biolumLight.intensity;
            }

            PopulateDefaultDialogueIfEmpty();
        }

        private void OnDisable()
        {
            ReleasePromptOwnership();
        }

        private void OnDestroy()
        {
            ReleasePromptOwnership();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            UpdateOrganicIdle(dt);

            ResolvePlayer();
            if (_playerTransform == null)
            {
                ReleasePromptOwnership();
                return;
            }

            Vector3 toPlayer = _playerTransform.position - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            _lastDistToPlayer = dist;

            // Ao chegar perto do Virgilio da Ponte (antes da Catedral), ativa automaticamente o Checkpoint da Ponte
            if (role == VirgilAppearanceRole.CathedralBridge && !_bridgeCheckpointUnlocked && _playerController != null && !_playerController.IsDead && dist <= 10.5f)
            {
                _bridgeCheckpointUnlocked = true;
                Vector3 safeBridgeRespawn = transform.position + transform.forward * 2.2f;
                safeBridgeRespawn.y = _playerTransform.position.y;
                _playerController.respawnPosition = safeBridgeRespawn;
                _playerController.RefillEstus();
                _playerController.Heal(_playerController.maxHealth);
                StomachAbyss.Combat.CombatSfxBank.Play(StomachAbyss.Combat.CombatSfxId.CheckpointLit, 0.85f, 0.97f, 1.03f, 0.5f);
                StomachAbyss.Combat.CombatCameraJuice.PunchFov(-2.0f, 0.35f);
                Debug.Log($"[Virgil Bridge Checkpoint] Checkpoint da Ponte ativado em: {safeBridgeRespawn}");
            }

            if (autoFacePlayer && dist <= interactRadius * 1.75f && toPlayer.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, dt * 3.8f);
            }

            VirgilDialogueUI ui = VirgilDialogueUI.GetOrCreate();
            bool playerAlive = _playerController == null || !_playerController.IsDead;
            bool inRange = playerAlive && dist <= interactRadius;

            if (!inRange || ui.IsOpen || VirgilMutationMenuUI.IsOpen)
            {
                ReleasePromptOwnership();
                return;
            }

            bool canMutate = role == VirgilAppearanceRole.CardiaTutorial || role == VirgilAppearanceRole.CathedralBridge;

            bool canClaimPrompt =
                s_promptOwner == null ||
                s_promptOwner == this ||
                !s_promptOwner.isActiveAndEnabled ||
                s_promptOwner._lastDistToPlayer > s_promptOwner.interactRadius ||
                dist < s_promptOwner._lastDistToPlayer - 0.08f;

            if (canClaimPrompt)
            {
                s_promptOwner = this;
                string prompt = canMutate
                    ? "[E] Falar com Virgílio   |   [F] Mutar Biomassa"
                    : $"[E] Falar com {npcName}";
                ui.ShowInteractPrompt(true, prompt);
            }

            if (s_promptOwner == this && !ui.WasClosedThisFrame && !VirgilMutationMenuUI.WasClosedThisFrame)
            {
                if (WasInteractPressedThisFrame())
                {
                    BeginConversation(ui);
                }
                else if (canMutate && WasMutatePressedThisFrame())
                {
                    ReleasePromptOwnership();
                    VirgilMutationMenuUI.Open(_playerController);
                }
            }
        }

        private void BeginConversation(VirgilDialogueUI ui)
        {
            StomachAbyss.Combat.CombatSfxBank.Play(StomachAbyss.Combat.CombatSfxId.VirgilWhisper, 0.72f, 0.94f, 1.06f, 0.4f);
            VirgilDialoguePage[] pages = (!_hasSpoken && firstPages != null && firstPages.Length > 0)
                ? firstPages
                : repeatPages;

            if (pages == null || pages.Length == 0)
            {
                pages = firstPages;
            }

            ReleasePromptOwnership();
            ui.OpenDialogue(npcName, npcTitle, pages, () =>
            {
                _hasSpoken = true;
            });
        }

        private void UpdateOrganicIdle(float dt)
        {
            float t = Time.time + _idlePhaseOffset;

            if (_hasVisualCached && visualRoot != null)
            {
                float breath = Mathf.Sin(t * 1.65f);
                float sway = Mathf.Sin(t * 0.85f);
                visualRoot.localPosition = _visualBaseLocalPos + new Vector3(sway * 0.015f, breath * 0.042f, 0f);
                float scaleBreath = 1f + breath * 0.022f;
                visualRoot.localScale = new Vector3(
                    _visualBaseLocalScale.x * (1f + (1f - scaleBreath) * 0.4f),
                    _visualBaseLocalScale.y * scaleBreath,
                    _visualBaseLocalScale.z * (1f + (1f - scaleBreath) * 0.4f));
            }

            if (biolumLight != null)
            {
                float flicker = 0.78f + 0.16f * Mathf.Sin(t * 2.1f) + 0.06f * Mathf.Sin(t * 5.7f);
                biolumLight.intensity = _baseLightIntensity * flicker;
            }
        }

        private void ReleasePromptOwnership()
        {
            if (s_promptOwner != this) return;

            s_promptOwner = null;
            VirgilDialogueUI ui = FindAnyObjectByType<VirgilDialogueUI>();
            if (ui != null)
            {
                ui.ShowInteractPrompt(false);
            }
        }

        private void ResolvePlayer()
        {
            if (_playerTransform != null && _playerTransform.gameObject.activeInHierarchy)
            {
                return;
            }

            GameObject playerGo = null;
            try { playerGo = GameObject.FindGameObjectWithTag("Player"); } catch { }
            if (playerGo != null)
            {
                _playerTransform = playerGo.transform;
                _playerController = playerGo.GetComponent<SimplePlayerController>();
                return;
            }

            _playerController = FindAnyObjectByType<SimplePlayerController>();
            if (_playerController != null)
            {
                _playerTransform = _playerController.transform;
            }
        }

        private static bool WasInteractPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && kb.eKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }

        private static bool WasMutatePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && kb.fKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F);
#endif
        }

        private void PopulateDefaultDialogueIfEmpty()
        {
            if (firstPages == null || firstPages.Length == 0)
            {
                if (role == VirgilAppearanceRole.CardiaTutorial)
                {
                    firstPages = new[]
                    {
                        new VirgilDialoguePage
                        {
                            text = "Você finalmente acordou!",
                            hintBadge = string.Empty
                        },
                        new VirgilDialoguePage
                        {
                            text = "A carne humana que te trouxe até este abismo já foi digerida. Mas este corpo não é um hospedeiro comum... há maldade antiga assumindo o controle, o trono desta mente já tem um dono.",
                            hintBadge = string.Empty
                        },
                        new VirgilDialoguePage
                        {
                            text = "Eu a muito tempo ja estive no seu lugar. Cego pela pressa, marchei direto para tentar tomar o poder... e fui feito em pedaços pelos lacaios antes de chegar ao controle totall.",
                            hintBadge = string.Empty
                        },
                        new VirgilDialoguePage
                        {
                            text = "mas em todo esse tempo, muita coisa mudou. O mal ficou mais forte.",
                            hintBadge = string.Empty
                        },
                        new VirgilDialoguePage
                        {
                            text = "pegue! estas armas eu recolhi na minha jornada. Elas vão lhe ser uteis. teste-as e descubra qual se adapta melhor a sua nova forma.",
                            hintBadge = string.Empty
                        },
                        new VirgilDialoguePage
                        {
                            text = "Se quiser arrancar o gânglio deste estômago daquela criatura, reaprenda sua forma: firme suas fibras para andar (WASD) e acelere o pulso (Shift) para correr antes que o ácido o alcance.",
                            hintBadge = "MOVIMENTO:  [WASD] Andar   |   [Shift] Correr"
                        },
                        new VirgilDialoguePage
                        {
                            text = "Atravesse a Escadaria do Refluxo saltando os obstáculos (Q) e deslizando através dos jatos ácidos (Espaço) para não ser dissolvido.",
                            hintBadge = "TRAVESSIA:  [Q] Pular   |   [Espaço] Esquiva Invulnerável"
                        },
                        new VirgilDialoguePage
                        {
                            text = "Cada célula que tomba sob tua lâmina libera Biomassa pura. Traga-a até mim — posso tecê-la em tua própria carne para endurecer tua carapaça, acelerar teu metabolismo e afiar tua virulência.",
                            hintBadge = "PRESSIONE [F] PERTO DE VIRGÍLIO PARA MUTAR BIOMASSA"
                        },
                        new VirgilDialoguePage
                        {
                            text = "Boa sorte! nos reencontraremos no coração deste estômago.",
                        }
                    };
                }
                else if (role == VirgilAppearanceRole.CathedralPostBoss)
                {
                    firstPages = new[]
                    {
                        new VirgilDialoguePage
                        {
                            text = "O silêncio... finalmente voltou a esta catedral. A Tênia tombou, e o pulso deste estômago agora responde apenas à sua vontade.",
                            hintBadge = string.Empty
                        },
                        new VirgilDialoguePage
                        {
                            text = "Você conseguiu ir além de onde eu fui quebrado. O primeiro gânglio foi arrancado das garras do demônio — o seu papel neste abismo está cumprido.",
                            hintBadge = string.Empty
                        },
                        new VirgilDialoguePage
                        {
                            text = "Mas o trono do hospedeiro ainda respira além destas paredes. Outros órgãos e novos servos ainda nos aguardam nas profundezas deste corpo... e é para a próxima parte que devemos partir.",
                            hintBadge = string.Empty
                        },
                        new VirgilDialoguePage
                        {
                            text = "Por ora, porém, o caminho adiante ainda está sendo tecido na carne. Aqui se encerra o nosso trecho jogável — o fim deste MVP.",
                            hintBadge = string.Empty
                        },
                        new VirgilDialoguePage
                        {
                            text = "Obrigado por caminhar por este inferno de carne conosco. Que nossas fibras voltem a se encontrar na próxima etapa desta peregrinação.",
                            hintBadge = "FIM DO MVP  •  OBRIGADO POR JOGAR"
                        }
                    };
                }
                else
                {
                    firstPages = new[]
                    {
                        new VirgilDialoguePage
                        {
                            text = "Sente o cheiro do incenso pútrido? A partir desta espinha dorsal, o ácido da lugar ao terror.",
                            hintBadge = string.Empty
                        },
                        new VirgilDialoguePage
                        {
                            text = "A Tênia não é apenas mais um parasita como nós... ela é apenas um dos servos do demônio que usurpou este corpo.",
                            hintBadge = string.Empty
                        },
                        new VirgilDialoguePage
                        {
                            text = "Cada célula que tomba sob tua lâmina libera Biomassa pura. Se ainda a carregas em tuas fibras, traga-a até mim antes do altar — posso tecê-la em tua carne para endurecer tua carapaça, acelerar teu metabolismo e afiar tua virulência.",
                            hintBadge = "PRESSIONE [F] PERTO DE VIRGÍLIO PARA MUTAR BIOMASSA"
                        },
                        new VirgilDialoguePage
                        {
                            text = "Não vacile diante do altar da Catedral. Derrote-a e arranque o domínio deste órgão para nós! Vá e tome o controle!",
                            hintBadge = "CATEDRAL À FRENTE: Prepare suas Lâminas"
                        }
                    };
                }
            }

            if ((repeatPages == null || repeatPages.Length == 0) && role == VirgilAppearanceRole.CathedralPostBoss)
            {
                repeatPages = new[]
                {
                    new VirgilDialoguePage
                    {
                        text = "Este órgão já é seu. Descanse por enquanto... em breve abriremos passagem para as próximas entranhas. Obrigado por jogar!",
                        hintBadge = "FIM DO MVP  •  OBRIGADO POR JOGAR"
                    }
                };
            }
        }
    }
}

