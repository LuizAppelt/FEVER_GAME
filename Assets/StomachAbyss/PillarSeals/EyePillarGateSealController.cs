using System.Collections.Generic;
using MapaEstomago;
using StomachAbyss.Combat;
using UnityEngine;

namespace StomachAbyss.PillarSeals
{
    /// <summary>
    /// Controla o portão GateRig (1) na entrada norte da Arena Central.
    /// Começa trancado com 3 selos oculares. A cada Pilar Ocular derrotado nas ilhas,
    /// um selo se apaga; ao derrotar os 3, o portão de osso se abre dramaticamente.
    /// </summary>
    public class EyePillarGateSealController : MonoBehaviour
    {
        [Header("Pilares Seladores (3 Ilhas)")]
        public List<EyePillarController> pillars = new List<EyePillarController>();

        [Header("Partes do GateRig (1)")]
        public Transform leftWingPivot;
        public Transform rightWingPivot;
        public Transform upperPortcullis;
        public ParticleSystem gateDustParticles;
        public BoxCollider blockingCollider;
        public AudioSource audioSource;
        public AudioClip gateUnlockClip;

        [Header("Orbes de Selo no Portão")]
        public List<Transform> sealEyeOrbs = new List<Transform>();
        public List<Light> sealOrbLights = new List<Light>();

        [Header("Animação de Abertura")]
        public float openDuration = 1.15f;

        [Header("Aviso de Proximidade")]
        public float proximityPromptRadius = 7.0f;
        public string lockedPromptFormat = "Portão Selado: para prosseguir, destrua os {0} Pilares Oculares ({1}/{0})";

        private const float OpenWingX = 4.05f;
        private const float ClosedWingX = 3.82f;
        private const float OpenWingYaw = 88f;
        private const float PortcullisOpenY = 6.72f;
        private const float PortcullisClosedY = 4.65f;

        private int _destroyedCount;
        private bool _isUnlocked;
        private bool _isAnimatingOpen;
        private float _openTimer;
        private float _closeProgress = 1f;

        private Transform _playerTransform;
        private bool _playerNearLockedGate;

        private string _bannerText = string.Empty;
        private float _bannerTimer;
        private GUIStyle _bannerStyle;

        private void Awake()
        {
            AutoBindPartsIfMissing();
            ApplyGatePose(1f);

            if (blockingCollider != null)
            {
                blockingCollider.isTrigger = false;
                blockingCollider.enabled = true;
            }
        }

        private void OnEnable()
        {
            for (int i = 0; i < pillars.Count; i++)
            {
                if (pillars[i] != null)
                {
                    pillars[i].OnPillarDestroyed -= HandlePillarDestroyed;
                    pillars[i].OnPillarDestroyed += HandlePillarDestroyed;
                }
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < pillars.Count; i++)
            {
                if (pillars[i] != null)
                {
                    pillars[i].OnPillarDestroyed -= HandlePillarDestroyed;
                }
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_bannerTimer > 0f)
            {
                _bannerTimer = Mathf.Max(0f, _bannerTimer - dt);
            }

            UpdateProximityPrompt();

            if (_isAnimatingOpen)
            {
                _openTimer += dt;
                float t = Mathf.Clamp01(_openTimer / Mathf.Max(0.1f, openDuration));
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                _closeProgress = 1f - ease;
                ApplyGatePose(_closeProgress);

                if (t >= 1f)
                {
                    _isAnimatingOpen = false;
                    _closeProgress = 0f;
                    ApplyGatePose(0f);
                }
            }
        }

        private void HandlePillarDestroyed(EyePillarController pillar)
        {
            int idx = pillars.IndexOf(pillar);
            if (idx >= 0 && idx < sealEyeOrbs.Count && sealEyeOrbs[idx] != null)
            {
                sealEyeOrbs[idx].gameObject.SetActive(false);
            }
            if (idx >= 0 && idx < sealOrbLights.Count && sealOrbLights[idx] != null)
            {
                sealOrbLights[idx].enabled = false;
            }

            _destroyedCount = 0;
            for (int i = 0; i < pillars.Count; i++)
            {
                if (pillars[i] != null && pillars[i].IsDestroyed)
                {
                    _destroyedCount++;
                }
            }

            int total = Mathf.Max(1, pillars.Count);
            if (_destroyedCount >= total && !_isUnlocked)
            {
                UnlockGate();
            }
            else
            {
                ShowBanner($"Selo Ocular Quebrado ({_destroyedCount}/{total})", 3.2f);
            }
        }

        public void UnlockGate()
        {
            if (_isUnlocked) return;
            _isUnlocked = true;
            _isAnimatingOpen = true;
            _openTimer = 0f;

            if (blockingCollider != null)
            {
                blockingCollider.enabled = false;
            }

            if (gateDustParticles != null)
            {
                gateDustParticles.Play();
            }

            if (audioSource != null && gateUnlockClip != null)
            {
                audioSource.PlayOneShot(gateUnlockClip, 0.95f);
            }
            else
            {
                CombatSfxBank.Play(CombatSfxId.SwordSlamHeavy, 0.95f, 0.65f, 0.75f, 0f);
            }

            if (IsometricCameraFollow.Instance != null)
            {
                IsometricCameraFollow.Instance.TriggerShake(0.35f, 0.55f);
            }

            ShowBanner("Todos os Selos Quebrados — O Portão foi Liberado!", 4.5f);
        }

        private void ShowBanner(string text, float duration)
        {
            _bannerText = text;
            _bannerTimer = duration;
        }

        private void AutoBindPartsIfMissing()
        {
            if (leftWingPivot == null) leftWingPivot = transform.Find("LeftWingPivot");
            if (rightWingPivot == null) rightWingPivot = transform.Find("RightWingPivot");
            if (upperPortcullis == null) upperPortcullis = transform.Find("UpperPortcullis");
            if (gateDustParticles == null)
            {
                Transform fx = transform.Find("GateSlam_BloodDust_FX");
                if (fx != null) gateDustParticles = fx.GetComponent<ParticleSystem>();
            }
            if (blockingCollider == null) blockingCollider = GetComponent<BoxCollider>();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
        }

        private void ApplyGatePose(float closeAmount)
        {
            float c = Mathf.Clamp01(closeAmount);

            if (leftWingPivot != null)
            {
                float x = Mathf.Lerp(-OpenWingX, -ClosedWingX, c);
                float yaw = Mathf.Lerp(-OpenWingYaw, 0f, c);
                leftWingPivot.localPosition = new Vector3(x, 0f, 0f);
                leftWingPivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            if (rightWingPivot != null)
            {
                float x = Mathf.Lerp(OpenWingX, ClosedWingX, c);
                float yaw = Mathf.Lerp(OpenWingYaw, 0f, c);
                rightWingPivot.localPosition = new Vector3(x, 0f, 0f);
                rightWingPivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            if (upperPortcullis != null)
            {
                float y = Mathf.Lerp(PortcullisOpenY, PortcullisClosedY, c);
                upperPortcullis.localPosition = new Vector3(0f, y, 0f);
            }
        }

        private void UpdateProximityPrompt()
        {
            if (_isUnlocked)
            {
                _playerNearLockedGate = false;
                return;
            }

            if (_playerTransform == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _playerTransform = p.transform;
            }

            if (_playerTransform == null)
            {
                _playerNearLockedGate = false;
                return;
            }

            float dist = Vector3.Distance(_playerTransform.position, transform.position);
            _playerNearLockedGate = dist <= proximityPromptRadius;
        }

        private void OnGUI()
        {
            bool showBanner = _bannerTimer > 0f && !string.IsNullOrEmpty(_bannerText);
            bool showProximity = !showBanner && _playerNearLockedGate && !_isUnlocked;
            if (!showBanner && !showProximity) return;

            if (_bannerStyle == null)
            {
                _bannerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _bannerStyle.normal.textColor = StomachAbyss.UI.Ps1UiTheme.BoneGold;
            }

            string displayText;
            float alpha;
            if (showBanner)
            {
                displayText = _bannerText;
                alpha = Mathf.Clamp01(_bannerTimer < 0.6f ? _bannerTimer / 0.6f : 1f);
            }
            else
            {
                int total = Mathf.Max(1, pillars.Count);
                displayText = string.Format(lockedPromptFormat, total, _destroyedCount);
                alpha = 0.96f;
            }

            Color prev = GUI.color;
            float w = 740f;
            float h = 58f;
            Rect rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.16f, w, h);
            StomachAbyss.UI.Ps1UiTheme.DrawOnGuiPanel(rect, alpha, highlightGold: showBanner);

            GUI.color = new Color(0f, 0f, 0f, 0.85f * alpha);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), displayText, _bannerStyle);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(rect, displayText, _bannerStyle);

            GUI.color = prev;
        }
    }
}
