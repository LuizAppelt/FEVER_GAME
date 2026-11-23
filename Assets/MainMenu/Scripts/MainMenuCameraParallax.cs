using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StomachAbyss.MainMenu
{
    /// <summary>
    /// Animates the Main Menu camera framing the Colossal Heart, Spinal Column, Vascular Canopy,
    /// and Foreground Red Crystals:
    /// - Smooth mouse-position parallax (<see cref="Mouse.current"/> with fallback to <see cref="Input.mousePosition"/>).
    /// - Slow organic respiratory camera drift.
    /// - Subtle camera micro-punch &amp; FOV shockwave synchronized with <see cref="ColossalHeartController.OnHeartbeatLub"/>
    ///   and <see cref="ColossalHeartController.OnHeartbeatDub"/>.
    /// - Cinematic dolly/zoom transition toward the glowing heart when clicking "Jogar" (<see cref="StartPlayZoomTransition(Transform, float)"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuCameraParallax : MonoBehaviour
    {
        [Header("Scene References")]
        public Camera targetCamera;
        public ColossalHeartController heartController;
        public Transform heartTarget;

        [Header("Anchor Framing")]
        public Vector3 anchorPosition = new Vector3(0f, 1.8f, -13.5f);
        public Vector3 anchorEulerAngles = new Vector3(-7.5f, 0f, 0f);
        [Range(25f, 85f)] public float baseFieldOfView = 50f;
        public bool captureInitialTransformOnAwake = true;

        [Header("Mouse Parallax")]
        public Vector2 parallaxPositionAmplitude = new Vector2(0.55f, 0.32f);
        public Vector2 parallaxRotationDegrees = new Vector2(1.65f, 2.45f);
        [Range(0.05f, 1.5f)] public float parallaxSmoothTime = 0.26f;

        [Header("Organic Breathing Drift")]
        public Vector3 breathingPositionAmplitude = new Vector3(0.07f, 0.13f, 0.05f);
        [Range(0f, 3f)] public float breathingPitchDegrees = 0.35f;
        [Range(0f, 3f)] public float breathingRollDegrees = 0.42f;
        [Range(0.05f, 1.5f)] public float breathingFrequency = 0.22f;

        [Header("Heartbeat Micro-Punch & FOV Pulse")]
        [Range(0f, 4f)] public float lubFovPulseDegrees = 0.65f;
        [Range(0f, 4f)] public float dubFovPulseDegrees = 1.25f;
        [Range(0f, 0.5f)] public float lubDollyPunch = 0.065f;
        [Range(0f, 0.5f)] public float dubDollyPunch = 0.135f;
        [Range(0f, 2f)] public float dubPitchKickDegrees = 0.32f;
        [Range(2f, 25f)] public float pulseRecoverySpeed = 9.5f;

        [Header("Play Zoom Transition")]
        [Range(0.2f, 0.9f)] public float zoomDistanceFraction = 0.58f;
        [Range(15f, 65f)] public float zoomTargetFov = 31f;
        [Range(0f, 12f)] public float zoomDutchRollDegrees = 3.2f;

        private Quaternion _anchorRotation;
        private bool _hasCapturedAnchor;
        private bool _isSubscribedToHeart;

        // Smoothed mouse parallax state
        private Vector2 _smoothedMouseNormalized;
        private Vector2 _mouseSmoothVelocity;

        // Heartbeat impulse accumulators
        private float _currentFovPulse;
        private float _currentDollyPulse;
        private float _currentPitchPulse;
        private float _currentRollPulse;
        private float _rollSign = 1f;

        // Play zoom transition state
        private bool _isZoomTransitioning;
        private float _zoomElapsed;
        private float _zoomDuration = 1.45f;
        private Vector3 _zoomStartPos;
        private Quaternion _zoomStartRot;
        private float _zoomStartFov;
        private Transform _activeZoomTarget;

        public bool IsZoomTransitioning => _isZoomTransitioning;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
                if (targetCamera == null)
                {
                    targetCamera = Camera.main;
                }
            }

            if (captureInitialTransformOnAwake)
            {
                CaptureCurrentPoseAsAnchor();
            }
            else
            {
                _anchorRotation = Quaternion.Euler(anchorEulerAngles);
                _hasCapturedAnchor = true;
            }
        }

        private void OnEnable()
        {
            TryBindHeartController();
        }

        private void Start()
        {
            if (!_hasCapturedAnchor)
            {
                CaptureCurrentPoseAsAnchor();
            }
            TryBindHeartController();
        }

        private void OnDisable()
        {
            UnbindHeartController();
        }

        /// <summary>
        /// Records the current Transform position/rotation and Camera FOV as the baseline framing anchor.
        /// </summary>
        public void CaptureCurrentPoseAsAnchor()
        {
            anchorPosition = transform.position;
            _anchorRotation = transform.rotation;
            anchorEulerAngles = _anchorRotation.eulerAngles;

            if (targetCamera != null)
            {
                baseFieldOfView = targetCamera.fieldOfView;
            }

            _hasCapturedAnchor = true;
        }

        /// <summary>
        /// Explicitly sets the baseline camera anchor pose and field of view.
        /// </summary>
        public void SetAnchorPose(Vector3 position, Quaternion rotation, float fov = 50f)
        {
            anchorPosition = position;
            _anchorRotation = rotation;
            anchorEulerAngles = rotation.eulerAngles;
            baseFieldOfView = fov;
            _hasCapturedAnchor = true;

            transform.SetPositionAndRotation(position, rotation);
            if (targetCamera != null)
            {
                targetCamera.fieldOfView = fov;
            }
        }

        /// <summary>
        /// Binds to <see cref="ColossalHeartController"/> events for heartbeat camera micro-punches.
        /// </summary>
        public void TryBindHeartController()
        {
            if (heartController == null)
            {
                heartController = FindFirstObjectByType<ColossalHeartController>();
            }

            if (heartController != null)
            {
                if (heartTarget == null)
                {
                    heartTarget = heartController.transform;
                }

                if (!_isSubscribedToHeart)
                {
                    heartController.OnHeartbeatLub -= HandleHeartbeatLub;
                    heartController.OnHeartbeatLub += HandleHeartbeatLub;
                    heartController.OnHeartbeatDub -= HandleHeartbeatDub;
                    heartController.OnHeartbeatDub += HandleHeartbeatDub;
                    _isSubscribedToHeart = true;
                }
            }
        }

        private void UnbindHeartController()
        {
            if (heartController != null && _isSubscribedToHeart)
            {
                heartController.OnHeartbeatLub -= HandleHeartbeatLub;
                heartController.OnHeartbeatDub -= HandleHeartbeatDub;
            }
            _isSubscribedToHeart = false;
        }

        private void HandleHeartbeatLub(float intensity)
        {
            float scale = Mathf.Clamp(intensity <= 0.001f ? 1f : intensity, 0.5f, 1.6f);
            _currentFovPulse -= lubFovPulseDegrees * scale;
            _currentDollyPulse += lubDollyPunch * scale;
            _currentPitchPulse -= dubPitchKickDegrees * 0.45f * scale;
        }

        private void HandleHeartbeatDub(float intensity)
        {
            float scale = Mathf.Clamp(intensity <= 0.001f ? 1f : intensity, 0.5f, 1.8f);
            _rollSign = -_rollSign;
            _currentFovPulse -= dubFovPulseDegrees * scale;
            _currentDollyPulse += dubDollyPunch * scale;
            _currentPitchPulse += dubPitchKickDegrees * scale;
            _currentRollPulse += 0.28f * _rollSign * scale;
        }

        /// <summary>
        /// Starts the smooth cinematic dolly/zoom transition toward the glowing Colossal Heart when clicking "Jogar".
        /// </summary>
        public void StartPlayZoomTransition(Transform heartTargetOverride, float duration)
        {
            _activeZoomTarget = heartTargetOverride != null
                ? heartTargetOverride
                : (heartTarget != null ? heartTarget : (heartController != null ? heartController.transform : null));

            _zoomDuration = Mathf.Max(0.25f, duration);
            _zoomElapsed = 0f;
            _zoomStartPos = transform.position;
            _zoomStartRot = transform.rotation;
            _zoomStartFov = targetCamera != null ? targetCamera.fieldOfView : baseFieldOfView;
            _isZoomTransitioning = true;
        }

        /// <summary>
        /// Overload for callers passing only duration.
        /// </summary>
        public void StartPlayZoomTransition(float duration)
        {
            StartPlayZoomTransition(heartTarget, duration);
        }

        /// <summary>
        /// Parameterless overload using default heart target and 1.45s duration.
        /// </summary>
        public void StartPlayZoomTransition()
        {
            StartPlayZoomTransition(heartTarget, 1.45f);
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f)
            {
                dt = 0.016f;
            }

            if (!_isSubscribedToHeart)
            {
                TryBindHeartController();
            }

            // Decay heartbeat micro-punch offsets smoothly
            float decay = Mathf.Exp(-pulseRecoverySpeed * dt);
            _currentFovPulse *= decay;
            _currentDollyPulse *= decay;
            _currentPitchPulse *= decay;
            _currentRollPulse *= decay;

            if (_isZoomTransitioning)
            {
                UpdatePlayZoomTransition(dt);
                return;
            }

            // 1. Read normalized mouse position in [-1, 1]
            Vector2 targetMouseNorm = ReadNormalizedMouseOffset();
            _smoothedMouseNormalized = Vector2.SmoothDamp(
                _smoothedMouseNormalized,
                targetMouseNorm,
                ref _mouseSmoothVelocity,
                Mathf.Max(0.04f, parallaxSmoothTime),
                Mathf.Infinity,
                dt);

            // 2. Slow organic multi-harmonic breathing drift
            float t = Time.unscaledTime;
            float w = 2f * Mathf.PI * breathingFrequency;
            Vector3 breathOffset = new Vector3(
                Mathf.Sin(t * w * 0.73f + 0.4f) * breathingPositionAmplitude.x,
                (Mathf.Sin(t * w) * 0.7f + Mathf.Sin(t * w * 2.0f + 1.1f) * 0.3f) * breathingPositionAmplitude.y,
                Mathf.Cos(t * w * 0.55f) * breathingPositionAmplitude.z);

            float breathPitch = Mathf.Sin(t * w + 0.6f) * breathingPitchDegrees;
            float breathRoll = Mathf.Sin(t * w * 0.62f + 1.7f) * breathingRollDegrees;

            // 3. Combine anchor pose + parallax + breathing + heartbeat dolly micro-punch
            Vector3 rightDir = _anchorRotation * Vector3.right;
            Vector3 upDir = _anchorRotation * Vector3.up;
            Vector3 forwardDir = _anchorRotation * Vector3.forward;

            Vector3 parallaxWorldOffset =
                rightDir * (_smoothedMouseNormalized.x * parallaxPositionAmplitude.x) +
                upDir * (_smoothedMouseNormalized.y * parallaxPositionAmplitude.y);

            Vector3 finalPosition =
                anchorPosition +
                parallaxWorldOffset +
                (_anchorRotation * breathOffset) +
                (forwardDir * _currentDollyPulse);

            float pitchDelta = (-_smoothedMouseNormalized.y * parallaxRotationDegrees.x) + breathPitch + _currentPitchPulse;
            float yawDelta = (_smoothedMouseNormalized.x * parallaxRotationDegrees.y);
            float rollDelta = (-_smoothedMouseNormalized.x * parallaxRotationDegrees.x * 0.35f) + breathRoll + _currentRollPulse;

            Quaternion finalRotation = _anchorRotation * Quaternion.Euler(pitchDelta, yawDelta, rollDelta);

            transform.SetPositionAndRotation(finalPosition, finalRotation);

            if (targetCamera != null)
            {
                targetCamera.fieldOfView = Mathf.Clamp(baseFieldOfView + _currentFovPulse, 15f, 100f);
            }
        }

        private void UpdatePlayZoomTransition(float dt)
        {
            _zoomElapsed += dt;
            float u = Mathf.Clamp01(_zoomElapsed / _zoomDuration);

            // Quintic ease-in-out with accelerating forward pull into the heart
            float ease = u < 0.5f
                ? 4f * u * u * u
                : 1f - Mathf.Pow(-2f * u + 2f, 3f) * 0.5f;

            Vector3 targetWorldPoint;
            if (_activeZoomTarget != null)
            {
                targetWorldPoint = _activeZoomTarget.position;
            }
            else
            {
                targetWorldPoint = _zoomStartPos + (_zoomStartRot * Vector3.forward) * 9.5f;
            }

            Vector3 toHeart = targetWorldPoint - _zoomStartPos;
            Vector3 dollyEndPos = _zoomStartPos + toHeart * zoomDistanceFraction;

            Vector3 pos = Vector3.Lerp(_zoomStartPos, dollyEndPos, ease)
                        + (transform.forward * (_currentDollyPulse * 1.35f));

            Quaternion lookAtHeartRot = toHeart.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(toHeart.normalized, Vector3.up)
                : _zoomStartRot;

            Quaternion blendedRot = Quaternion.Slerp(_zoomStartRot, lookAtHeartRot, ease * 0.85f);
            Quaternion dutchAndPulse = Quaternion.Euler(
                _currentPitchPulse * 1.4f,
                0f,
                Mathf.Lerp(0f, zoomDutchRollDegrees, ease) + _currentRollPulse * 1.5f);

            transform.SetPositionAndRotation(pos, blendedRot * dutchAndPulse);

            if (targetCamera != null)
            {
                float fov = Mathf.Lerp(_zoomStartFov, zoomTargetFov, ease) + _currentFovPulse * 1.25f;
                targetCamera.fieldOfView = Mathf.Clamp(fov, 15f, 100f);
            }
        }

        private static Vector2 ReadNormalizedMouseOffset()
        {
            float sw = Mathf.Max(1f, Screen.width);
            float sh = Mathf.Max(1f, Screen.height);
            Vector2 screenPos = new Vector2(sw * 0.5f, sh * 0.5f);
            bool hasMouse = false;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                screenPos = Mouse.current.position.ReadValue();
                hasMouse = true;
            }
#endif
            if (!hasMouse)
            {
                try
                {
                    screenPos = Input.mousePosition;
                    hasMouse = true;
                }
                catch
                {
                    // Input class may be disabled when Active Input Handling is set to Input System Package only
                }
            }

            if (!hasMouse)
            {
                return Vector2.zero;
            }

            float nx = Mathf.Clamp((screenPos.x / sw) * 2f - 1f, -1f, 1f);
            float ny = Mathf.Clamp((screenPos.y / sh) * 2f - 1f, -1f, 1f);
            return new Vector2(nx, ny);
        }
    }
}
