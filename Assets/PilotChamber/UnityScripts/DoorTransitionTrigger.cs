using System.Collections;
using System.Collections.Generic;
using StomachAbyss.Bonfire;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MapaEstomago
{
    public enum TransitionMode
    {
        TeleportTargetInScene,
        LoadSceneByName
    }

    /// <summary>
    /// Gatilho posicionado na boca da porta viva (Flesh_Eye_Door).
    /// Executa a sequencia cinematica completa:
    /// 1. Jogador entra na boca -> puxado para a garganta enquanto os dentes fecham com tudo;
    /// 2. Porta mastiga o jogador 3 vezes (sacudindo e espirrando sangue);
    /// 3. Tela escurece (Fade to Black);
    /// 4. Corta para a porta do proximo mapa (na mesma cena ou em outra cena);
    /// 5. Porta de destino incha, escancara a boca e COSPE o jogador em arco parabolico!
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DoorTransitionTrigger : MonoBehaviour
    {
        [Header("Modo de Transicao")]
        public TransitionMode mode = TransitionMode.TeleportTargetInScene;

        [Header("Destino na Mesma Cena (Teleporte entre Mapas)")]
        [Tooltip("Porta viva de destino que vai cuspir o jogador. Se vazio, busca automaticamente a porta do outro mapa!")]
        public FleshEyeDoorController destinationDoor;

        [Tooltip("Transform de destino alternativo caso nao use outra FleshEyeDoor.")]
        public Transform destinationAnchor;

        [Header("Destino por Cena Separada")]
        [Tooltip("Nome da cena Unity a ser carregada durante a tela preta (quando mode = LoadSceneByName).")]
        public string sceneToLoad = "Cupula_Gigante";

        [Header("Tempos da Sequencia (Segundos)")]
        public float biteCloseDuration = 0.28f;
        public float chewDuration = 1.10f;
        public float fadeOutDuration = 0.35f;
        public float blackScreenHold = 0.22f;
        public float fadeInDuration = 0.32f;
        public float preSpitSwellDuration = 0.22f;
        public float spitFlightDuration = 0.58f;

        [Header("Trajetoria da Cusparada")]
        [Tooltip("Distancia a frente da porta de destino onde o jogador aterrissa apos ser cuspido.")]
        public float spitLandingDistance = 4.2f;
        [Tooltip("Altura maxima do arco parabolico ao ser cuspido pela boca.")]
        public float spitArcHeight = 1.25f;

        [Header("Filtro de Ativacao")]
        public string triggerTag = "Player";
        [Tooltip("Permite testar a porta mesmo sem boneco Player (aproximando a camera livre da boca ou apertando F).")]
        public bool allowFreeCameraTrigger = true;
        public float freeCameraMouthRadius = 2.6f;
        public float freeCameraHotkeyRadius = 9.0f;

        [Header("Bonfire & Rede de Portas Vivas")]
        [Tooltip("Ativa o sistema de Bonfire + Mapa 3D ao entrar em portas ja visitadas.")]
        public bool enableBonfireSystem = true;
        [Tooltip("ID persistente desta porta (deixe vazio para detectar automaticamente pelo nome da porta).")]
        public string doorId = "";
        [Tooltip("Slot no mapa 3D (0=Ilha Esquerda, 1=Meio Sul, 2=Meio Norte, 3=Ilha Direita). -1 = automatico.")]
        public int mapSlotIndex = -1;

        private FleshEyeDoorController _myDoor;
        private Collider _col;
        private IsometricCameraFollow _isoCam;

        public string GetResolvedDoorId()
        {
            if (!string.IsNullOrEmpty(doorId)) return doorId;
            if (_myDoor == null) _myDoor = GetComponentInParent<FleshEyeDoorController>();
            BonfireDoorNetwork.InferDoorIdentity(_myDoor, out string inferredId, out _);
            return inferredId;
        }

        public int GetResolvedMapSlotIndex()
        {
            if (mapSlotIndex >= 0 && mapSlotIndex < BonfireDoorNetwork.TotalMapSlots) return mapSlotIndex;
            if (_myDoor == null) _myDoor = GetComponentInParent<FleshEyeDoorController>();
            BonfireDoorNetwork.InferDoorIdentity(_myDoor, out _, out int inferredSlot);
            return inferredSlot;
        }

        private static bool _isTransitionRunning;
        private static float _cooldownUntilTime;
        private static FleshEyeDoorController s_lastSpitDoor;
        private static bool s_mustLeaveLastSpitDoor;
        private static CanvasGroup _fadeCanvasGroup;
        private static Transform s_activeActor;
        private static List<Renderer> s_activeHiddenRenderers;
        private static Vector3 s_activeOriginalScale = Vector3.one;
        private static bool s_hasActiveOriginalScale;

        public static bool IsTransitionRunning => _isTransitionRunning;

        public static void ForceClearFade()
        {
            _isTransitionRunning = false;
            BonfireMapUI.ForceCloseImmediate();
            if (Time.timeScale < 0.01f)
            {
                Time.timeScale = 1f;
            }
            if (_fadeCanvasGroup != null)
            {
                _fadeCanvasGroup.alpha = 0f;
            }
            if (s_activeHiddenRenderers != null)
            {
                RestoreRenderersVisible(s_activeHiddenRenderers);
                s_activeHiddenRenderers = null;
            }
            if (s_activeActor != null)
            {
                if (s_hasActiveOriginalScale && s_activeOriginalScale.sqrMagnitude > 0.001f)
                {
                    s_activeActor.localScale = s_activeOriginalScale;
                }
                CharacterController cc = s_activeActor.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = true;
                SimplePlayerController spc = s_activeActor.GetComponent<SimplePlayerController>();
                if (spc != null) spc.inputLocked = false;
                s_activeActor = null;
                s_hasActiveOriginalScale = false;
            }
            if (IsometricCameraFollow.Instance != null)
            {
                IsometricCameraFollow.Instance.lockInput = false;
            }
        }

        private void OnEnable()
        {
            if (_myDoor == null) _myDoor = GetComponentInParent<FleshEyeDoorController>();
            BonfireDoorNetwork.RegisterDoor(this);
        }

        private void OnDisable()
        {
            // Nao desregistra nem aborta transicao aqui: o zone culling pode desativar Cardia
            // enquanto o jogador esta em outra ilha, mas a porta precisa continuar registrada para retorno!
        }

        private void OnDestroy()
        {
            BonfireDoorNetwork.UnregisterDoor(this);
        }

        private void Awake()
        {
            _col = GetComponent<Collider>();
            if (_col != null)
            {
                _col.isTrigger = true;
            }
            _myDoor = GetComponentInParent<FleshEyeDoorController>();
        }

        private void Update()
        {
            if (s_mustLeaveLastSpitDoor && s_lastSpitDoor != null && _myDoor == s_lastSpitDoor && !_isTransitionRunning)
            {
                GameObject p = GameObject.FindGameObjectWithTag(triggerTag);
                if (p != null)
                {
                    float dist = Vector3.Distance(p.transform.position, s_lastSpitDoor.transform.position);
                    if (dist >= 4.0f && Time.time >= _cooldownUntilTime)
                    {
                        s_mustLeaveLastSpitDoor = false;
                    }
                }
            }

            if (_isTransitionRunning || Time.time < _cooldownUntilTime) return;
            if (!allowFreeCameraTrigger) return;

            if (_isoCam == null)
            {
                _isoCam = FindFirstObjectByType<IsometricCameraFollow>();
            }

            // Se ainda nao houver Player vinculado a camera, permite testar com a Camera Livre!
            if (_isoCam != null && _isoCam.target == null)
            {
                Vector3 mouthPos = _myDoor != null ? _myDoor.GetMouthLipsWorldPos() : transform.position;
                Vector2 camXZ = new Vector2(_isoCam.FreePivot.x, _isoCam.FreePivot.z);
                Vector2 mouthXZ = new Vector2(mouthPos.x, mouthPos.z);
                float distXZ = Vector2.Distance(camXZ, mouthXZ);

                // 1. Entrou com o pivo da camera direto na boca da porta
                if (distXZ <= freeCameraMouthRadius)
                {
                    StartTransition(null);
                    return;
                }

                // 2. Apertou a tecla F perto da porta
#if ENABLE_INPUT_SYSTEM
                if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame && distXZ <= freeCameraHotkeyRadius)
                {
                    StartTransition(null);
                }
#endif
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (s_mustLeaveLastSpitDoor && _myDoor != null && _myDoor == s_lastSpitDoor)
            {
                if (other.CompareTag(triggerTag) || other.GetComponentInParent<SimplePlayerController>() != null)
                {
                    s_mustLeaveLastSpitDoor = false;
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_isTransitionRunning || Time.time < _cooldownUntilTime) return;
            if (s_mustLeaveLastSpitDoor && _myDoor != null && _myDoor == s_lastSpitDoor) return;

            SimplePlayerController spc = other.GetComponentInParent<SimplePlayerController>();
            if (spc != null || other.CompareTag(triggerTag) || other.name.ToLower().Contains("player"))
            {
                Transform rootActor = spc != null ? spc.transform : other.transform;
                StartTransition(rootActor);
            }
        }

        [ContextMenu("Testar Engolir + Mastigar + Cuspir Agora")]
        public void TestTransitionFromMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DoorTransitionTrigger] Entre no modo PLAY para testar a animacao de engolir e cuspir!");
                return;
            }
            GameObject p = GameObject.FindGameObjectWithTag(triggerTag);
            StartTransition(p != null ? p.transform : null);
        }

        public void StartTransition(Transform actor)
        {
            if (_isTransitionRunning || Time.time < _cooldownUntilTime) return;
            if (_myDoor == null) _myDoor = GetComponentInParent<FleshEyeDoorController>();

            DoorTransitionRunner.EnsureExists().StartCoroutine(SwallowChewAndSpitRoutine(actor));
        }

        private IEnumerator SwallowChewAndSpitRoutine(Transform actor)
        {
            _isTransitionRunning = true;

            if (_isoCam == null)
            {
                _isoCam = FindFirstObjectByType<IsometricCameraFollow>();
            }
            if (_isoCam != null)
            {
                _isoCam.lockInput = true;
            }

            // Desativa fisica e input do Player durante a animacao de ser engolido/cuspido
            CharacterController cc = actor != null ? actor.GetComponent<CharacterController>() : null;
            Rigidbody rb = actor != null ? actor.GetComponent<Rigidbody>() : null;
            SimplePlayerController actorPlayer = actor != null ? actor.GetComponent<SimplePlayerController>() : null;
            if (actorPlayer != null) actorPlayer.inputLocked = true;
            bool hadRbKinematic = rb != null && rb.isKinematic;
            if (cc != null) cc.enabled = false;
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            // Se estivermos testando sem boneco Player (Camera Livre), cria uma capsula/bolus temporario
            // para o usuario visualizar o corpo sendo engolido e cuspido pela boca!
            GameObject tempBolus = null;
            Transform movingSubject = actor;
            if (movingSubject == null)
            {
                tempBolus = CreateVisualVictimBolus();
                movingSubject = tempBolus.transform;
                Vector3 startCamPos = _isoCam != null ? _isoCam.FreePivot : transform.position + transform.forward * 2.2f;
                startCamPos.y = transform.position.y + 0.65f;
                movingSubject.position = startCamPos;
            }

            // ------------------------------------------------------------------
            // ETAPA 1: Puxa o jogador para dentro da boca + Fecha os dentes!
            // ------------------------------------------------------------------
            if (_myDoor != null)
            {
                _myDoor.PlayBiteAndChew(biteCloseDuration, chewDuration);
            }

            Vector3 swallowStartPos = movingSubject.position;
            Vector3 swallowEndPos = _myDoor != null
                ? _myDoor.GetMouthThroatWorldPos()
                : transform.position - transform.forward * 0.35f + Vector3.up * 0.85f;
            Vector3 originalScale = movingSubject.localScale;
            s_activeActor = actor;
            s_activeOriginalScale = originalScale;
            s_hasActiveOriginalScale = (actor != null);

            float elapsed = 0f;
            while (elapsed < biteCloseDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, biteCloseDuration));
                float ease = t * t * (3f - 2f * t);

                Vector3 curPos = Vector3.Lerp(swallowStartPos, swallowEndPos, ease);
                movingSubject.position = curPos;
                movingSubject.localScale = Vector3.Lerp(originalScale, originalScale * 0.45f, ease);

                if (_isoCam != null && actor == null)
                {
                    Vector3 camFocus = _myDoor != null ? _myDoor.GetMouthLipsWorldPos() : transform.position;
                    camFocus.y = 0f;
                    _isoCam.SetFreePivot(Vector3.Lerp(_isoCam.FreePivot, camFocus, Time.deltaTime * 10f));
                }
                yield return null;
            }

            // Esconde os renderers do jogador enquanto ele esta dentro da boca fechada sendo mastigado
            List<Renderer> hiddenRenderers = SetRenderersVisible(movingSubject, false);
            s_activeHiddenRenderers = hiddenRenderers;

            // ------------------------------------------------------------------
            // ETAPA 2: Mastigando o jogador
            // ------------------------------------------------------------------
            float chewBeforeDecision = Mathf.Max(0.15f, chewDuration - fadeOutDuration * 0.65f);
            elapsed = 0f;
            while (elapsed < chewBeforeDecision)
            {
                elapsed += Time.deltaTime;
                if (_isoCam != null && actor == null && _myDoor != null)
                {
                    Vector3 camFocus = _myDoor.GetMouthLipsWorldPos();
                    camFocus.y = 0f;
                    _isoCam.SetFreePivot(Vector3.Lerp(_isoCam.FreePivot, camFocus, Time.deltaTime * 12f));
                }
                yield return null;
            }

            // ------------------------------------------------------------------
            // ETAPA 2.5: Sistema Bonfire — 1a entrada avanca; porta ja visitada abre Mapa 3D!
            // ------------------------------------------------------------------
            string myDoorId = GetResolvedDoorId();
            int mySlot = GetResolvedMapSlotIndex();
            bool shouldOpenBonfireMenu = enableBonfireSystem
                                         && actor != null
                                         && mySlot >= 0
                                         && BonfireDoorNetwork.IsDoorUnlocked(myDoorId);

            FleshEyeDoorController selectedTravelDoor = null;
            bool exitSameDoorWithoutFade = false;

            if (shouldOpenBonfireMenu)
            {
                if (_myDoor != null)
                {
                    _myDoor.PrepareClosedForSpit();
                }

                // Cura vida e Estus imediatamente ao entrar no abrigo da porta
                if (actorPlayer != null)
                {
                    actorPlayer.ClearAllConditions();
                    actorPlayer.Heal(actorPlayer.maxHealth);
                    actorPlayer.RefillEstus();
                }

                bool menuCompleted = false;
                int chosenSlot = -1;
                bool choseExitSameDoor = false;

                BonfireMapUI.Show(
                    mySlot,
                    onTravelToSlot: slot =>
                    {
                        chosenSlot = slot;
                        menuCompleted = true;
                    },
                    onExitSpitOut: () =>
                    {
                        choseExitSameDoor = true;
                        menuCompleted = true;
                    });

                while (!menuCompleted)
                {
                    yield return null;
                }

                if (choseExitSameDoor || chosenSlot == mySlot)
                {
                    selectedTravelDoor = _myDoor;
                    exitSameDoorWithoutFade = true;
                }
                else
                {
                    selectedTravelDoor = BonfireDoorNetwork.GetDoorForSlot(chosenSlot);
                }
            }

            CanvasGroup fader = EnsureFadeCanvas();
            if (!exitSameDoorWithoutFade)
            {
                // Fade Out para Tela Preta!
                elapsed = 0f;
                while (elapsed < fadeOutDuration)
                {
                    elapsed += Time.deltaTime;
                    fader.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Mathf.Max(0.01f, fadeOutDuration)));
                    yield return null;
                }
                fader.alpha = 1f;

                // ------------------------------------------------------------------
                // ETAPA 3: Na Tela Preta -> Corta para a Porta de Destino
                // ------------------------------------------------------------------
                if (selectedTravelDoor == null && mode == TransitionMode.LoadSceneByName && !string.IsNullOrEmpty(sceneToLoad))
                {
                    AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneToLoad);
                    if (asyncLoad != null)
                    {
                        while (!asyncLoad.isDone)
                        {
                            yield return null;
                        }
                    }
                    _isoCam = FindFirstObjectByType<IsometricCameraFollow>();
                    if (_isoCam != null) _isoCam.lockInput = true;
                }
            }
            else
            {
                fader.alpha = 0f;
            }

            FleshEyeDoorController targetDoor = selectedTravelDoor != null ? selectedTravelDoor : ResolveDestinationDoor();
            Transform targetAnchor = targetDoor != null ? targetDoor.transform : destinationAnchor;

            // Garante que a zona da porta de destino (ex: Cardia) esteja ativa antes de animar a cusparada!
            if (targetDoor != null)
            {
                StomachAbyss.Performance.StomachPerformanceCuller.EnsureZoneActiveForPosition(targetDoor.transform.position);
                if (!targetDoor.gameObject.activeInHierarchy)
                {
                    Transform p = targetDoor.transform;
                    while (p != null)
                    {
                        if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
                        p = p.parent;
                    }
                }
            }
            else if (targetAnchor != null)
            {
                StomachAbyss.Performance.StomachPerformanceCuller.EnsureZoneActiveForPosition(targetAnchor.position);
            }

            // Calcula de onde o jogador vai sair (garganta da porta de destino) e onde vai cair (chao a frente)
            Vector3 spitOriginWorld;
            Vector3 spitForwardDir;
            Vector3 spitLandingWorld;

            if (targetDoor != null)
            {
                targetDoor.PrepareClosedForSpit();
                spitOriginWorld = targetDoor.GetMouthLipsWorldPos();
                spitForwardDir = targetDoor.transform.forward;
                spitForwardDir.y = 0f;
                if (spitForwardDir.sqrMagnitude < 0.001f) spitForwardDir = Vector3.forward;
                spitForwardDir.Normalize();

                float effectiveSpitDist = Mathf.Max(spitLandingDistance, 5.4f);
                spitLandingWorld = targetDoor.transform.position + spitForwardDir * effectiveSpitDist;
                spitLandingWorld.y = SampleFloorHeight(spitLandingWorld, targetDoor.transform.position.y + 0.15f);
            }
            else if (targetAnchor != null)
            {
                spitOriginWorld = targetAnchor.position + Vector3.up * 0.9f;
                spitForwardDir = targetAnchor.forward;
                spitForwardDir.y = 0f;
                if (spitForwardDir.sqrMagnitude < 0.001f) spitForwardDir = Vector3.forward;
                spitForwardDir.Normalize();

                spitLandingWorld = targetAnchor.position + spitForwardDir * spitLandingDistance;
                spitLandingWorld.y = SampleFloorHeight(spitLandingWorld, targetAnchor.position.y + 0.15f);
            }
            else
            {
                // Fallback caso nao haja segunda porta na cena: cospe de volta da mesma porta!
                targetDoor = _myDoor;
                if (targetDoor != null) targetDoor.PrepareClosedForSpit();
                spitOriginWorld = _myDoor != null ? _myDoor.GetMouthLipsWorldPos() : transform.position + Vector3.up;
                spitForwardDir = transform.forward;
                spitLandingWorld = transform.position + spitForwardDir * spitLandingDistance;
                spitLandingWorld.y = SampleFloorHeight(spitLandingWorld, transform.position.y + 0.15f);
            }

            // Posiciona o jogador (ainda oculto) dentro da boca da porta de destino e corta a camera na hora
            if (movingSubject != null)
            {
                movingSubject.position = spitOriginWorld - spitForwardDir * 0.35f;
                movingSubject.rotation = Quaternion.LookRotation(spitForwardDir, Vector3.up);
            }

            if (_isoCam != null)
            {
                Vector3 doorFocusPivot = spitOriginWorld + spitForwardDir * 1.4f;
                doorFocusPivot.y = 0f;
                _isoCam.SetFreePivot(doorFocusPivot, snapImmediate: true);
                if (_isoCam.target != null)
                {
                    _isoCam.SnapToTarget();
                }
            }

            if (!exitSameDoorWithoutFade)
            {
                yield return new WaitForSeconds(blackScreenHold);
            }

            // ------------------------------------------------------------------
            // ETAPA 4: Fade In da Tela Preta + Porta de Destino Incha e COSPE!
            // ------------------------------------------------------------------
            if (targetDoor != null)
            {
                targetDoor.PlaySpitOut(preSpitSwellDuration, spitFlightDuration + 0.25f);
            }

            // Clareia a tela enquanto a porta de destino incha prestes a cuspir
            elapsed = 0f;
            bool revealedSubject = false;

            while (elapsed < preSpitSwellDuration)
            {
                elapsed += Time.deltaTime;
                if (!exitSameDoorWithoutFade)
                {
                    fader.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Mathf.Max(0.01f, fadeInDuration)));
                }
                yield return null;
            }

            // A BOCA ABRE! Revela o jogador e lanca ele em um arco parabolico para fora da boca!
            RestoreRenderersVisible(hiddenRenderers);
            s_activeHiddenRenderers = null;
            revealedSubject = true;

            if (actorPlayer != null)
            {
                actorPlayer.respawnPosition = spitLandingWorld;
            }

            elapsed = 0f;
            while (elapsed < spitFlightDuration)
            {
                elapsed += Time.deltaTime;
                if (!exitSameDoorWithoutFade)
                {
                    float totalFadeElapsed = preSpitSwellDuration + elapsed;
                    fader.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(totalFadeElapsed / Mathf.Max(0.01f, fadeInDuration)));
                }

                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, spitFlightDuration));
                float horizT = 1f - Mathf.Pow(1f - t, 2.1f);

                Vector3 basePos = Vector3.Lerp(spitOriginWorld, spitLandingWorld, horizT);
                // Arco parabolico + leve quique ao tocar o chao no final
                float arcY = Mathf.Sin(t * Mathf.PI) * spitArcHeight;
                if (t > 0.82f)
                {
                    float bounceT = (t - 0.82f) / 0.18f;
                    arcY += Mathf.Sin(bounceT * Mathf.PI) * 0.18f;
                }

                if (movingSubject != null)
                {
                    movingSubject.position = new Vector3(basePos.x, basePos.y + arcY, basePos.z);
                    float scaleRecover = Mathf.SmoothStep(0.45f, 1.0f, Mathf.Clamp01(t * 2.2f));
                    movingSubject.localScale = originalScale * scaleRecover;
                }

                if (_isoCam != null && actor == null)
                {
                    Vector3 camFollow = new Vector3(basePos.x, 0f, basePos.z);
                    _isoCam.SetFreePivot(camFollow);
                }

                yield return null;
            }

            fader.alpha = 0f;

            if (!revealedSubject)
            {
                RestoreRenderersVisible(hiddenRenderers);
            }

            if (movingSubject != null)
            {
                movingSubject.position = spitLandingWorld;
                movingSubject.localScale = originalScale;
            }

            if (tempBolus != null)
            {
                Destroy(tempBolus, 0.25f);
            }

            if (cc != null) cc.enabled = true;
            if (rb != null)
            {
                rb.isKinematic = hadRbKinematic;
                if (!rb.isKinematic) rb.linearVelocity = Vector3.zero;
            }

            SimplePlayerController spc = actorPlayer != null ? actorPlayer : (actor != null ? actor.GetComponent<SimplePlayerController>() : null);
            if (spc != null)
            {
                spc.respawnPosition = spitLandingWorld;
                spc.inputLocked = false;
            }

            // Aplica Bonfire: salva portas descobertas em disco, cura, recarrega Estus e renasce inimigos comuns!
            if (enableBonfireSystem && actor != null)
            {
                BonfireDoorNetwork.UnlockDoor(myDoorId);
                if (targetDoor != null)
                {
                    BonfireDoorNetwork.UnlockDoor(targetDoor);
                }
                BonfireDoorNetwork.PerformBonfireRest(spc, spitLandingWorld, playFeedback: true);
            }

            if (_isoCam != null)
            {
                if (actor == null)
                {
                    _isoCam.SetFreePivot(new Vector3(spitLandingWorld.x, 0f, spitLandingWorld.z));
                }
                _isoCam.lockInput = false;
            }

            s_activeActor = null;
            s_activeHiddenRenderers = null;
            s_hasActiveOriginalScale = false;
            s_lastSpitDoor = targetDoor;
            s_mustLeaveLastSpitDoor = targetDoor != null;
            _cooldownUntilTime = Time.time + 3.2f;
            _isTransitionRunning = false;
        }

        /// <summary>
        /// Localiza a porta de destino configurada ou busca automaticamente a porta do outro mapa na cena!
        /// </summary>
        private FleshEyeDoorController ResolveDestinationDoor()
        {
            if (destinationDoor != null && destinationDoor != _myDoor)
            {
                return destinationDoor;
            }

            if (destinationAnchor != null)
            {
                FleshEyeDoorController anchoredDoor = destinationAnchor.GetComponentInChildren<FleshEyeDoorController>();
                if (anchoredDoor != null && anchoredDoor != _myDoor)
                {
                    return anchoredDoor;
                }
            }

            // Busca automatica inteligente: encontra a porta em outro mapa (distancia > 12m)
            FleshEyeDoorController[] allDoors = FindObjectsByType<FleshEyeDoorController>(FindObjectsSortMode.None);
            FleshEyeDoorController bestOtherMapDoor = null;
            float bestScore = float.MaxValue;

            foreach (FleshEyeDoorController d in allDoors)
            {
                if (d == null || d == _myDoor) continue;
                float dist = Vector3.Distance(transform.position, d.transform.position);
                if (dist < 4.0f) continue;

                // Prioriza portas que pertencam a outra raiz de mapa ou Entry_Flesh_Door_Anchor
                bool isEntry = d.transform.parent != null && d.transform.parent.name.Contains("Entry");
                float score = isEntry ? dist * 0.5f : dist;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestOtherMapDoor = d;
                }
            }

            return bestOtherMapDoor;
        }

        private float SampleFloorHeight(Vector3 worldPos, float fallbackY)
        {
            Ray ray = new Ray(new Vector3(worldPos.x, worldPos.y + 6.0f, worldPos.z), Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, 14.0f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return hit.point.y + 0.15f;
            }
            return fallbackY;
        }

        private List<Renderer> SetRenderersVisible(Transform root, bool visible)
        {
            List<Renderer> list = new List<Renderer>();
            if (root == null) return list;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (r != null && r.enabled != visible)
                {
                    r.enabled = visible;
                    list.Add(r);
                }
            }
            return list;
        }

        private static void RestoreRenderersVisible(List<Renderer> list)
        {
            if (list == null) return;
            foreach (Renderer r in list)
            {
                if (r != null) r.enabled = true;
            }
        }

        private GameObject CreateVisualVictimBolus()
        {
            GameObject bolus = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bolus.name = "Swallowed_Player_Preview";
            Collider col = bolus.GetComponent<Collider>();
            if (col != null) Destroy(col);
            bolus.transform.localScale = new Vector3(0.55f, 0.65f, 0.55f);

            Renderer r = bolus.GetComponent<Renderer>();
            if (r != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                if (sh != null)
                {
                    Material m = new Material(sh);
                    m.color = new Color(0.92f, 0.28f, 0.24f, 1f);
                    r.sharedMaterial = m;
                }
            }
            return bolus;
        }

        private static CanvasGroup EnsureFadeCanvas()
        {
            if (_fadeCanvasGroup != null) return _fadeCanvasGroup;

            GameObject canvasGo = new GameObject("DoorTransition_BlackFadeCanvas");
            DontDestroyOnLoad(canvasGo);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _fadeCanvasGroup = canvasGo.AddComponent<CanvasGroup>();
            _fadeCanvasGroup.alpha = 0f;
            _fadeCanvasGroup.interactable = false;
            _fadeCanvasGroup.blocksRaycasts = false;

            GameObject imgGo = new GameObject("BlackScreen");
            imgGo.transform.SetParent(canvasGo.transform, false);
            Image img = imgGo.AddComponent<Image>();
            img.color = new Color(0.02f, 0.002f, 0.005f, 1f);

            RectTransform rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            return _fadeCanvasGroup;
        }
    }
}
