using UnityEngine;

namespace StomachAbyss.Combat
{
    /// <summary>
    /// Global non-overlapping freeze-frame (hitstop) and micro-FOV punch controller.
    /// Safely merges simultaneous hits, throttles rapid back-to-back multi-hits,
    /// and always restores Time.timeScale = 1f on disable/destroy/quit.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class CombatHitstopManager : MonoBehaviour
    {
        public static CombatHitstopManager Instance { get; private set; }

        [Header("Hitstop Tuning")]
        [Tooltip("Minimum unfrozen interval (s) between consecutive hitstops (keeps boomerang ticks rhythmic).")]
        public float backToBackCooldown = 0.08f;

        [Header("Micro-FOV Punch")]
        [Tooltip("FOV delta in degrees applied on heavy slams / critical strikes.")]
        public float fovPunchAngle = -1.6f;

        [Tooltip("Exponential recovery speed for the camera FOV punch.")]
        public float fovRecoverySpeed = 24f;

        private bool _isFrozen;
        private float _remainingFreeze;
        private float _targetTimeScale = 1f;
        private float _cooldownRemaining;
        private int _lastTriggerFrame = -1;

        private float _fovOffset;
        private Camera _cachedCam;
        private Camera _lastAppliedCamera;
        private float _lastWrittenFov;
        private float _lastAppliedOffset;

        public static CombatHitstopManager EnsureExists()
        {
            if (Instance != null) return Instance;

            Instance = FindAnyObjectByType<CombatHitstopManager>();
            if (Instance != null) return Instance;

            GameObject container = GameObject.Find("Combat_Juice_Systems");
            if (container == null)
            {
                container = new GameObject("Combat_Juice_Systems");
            }

            Instance = container.GetComponent<CombatHitstopManager>();
            if (Instance == null)
            {
                Instance = container.AddComponent<CombatHitstopManager>();
            }
            return Instance;
        }

        /// <summary>
        /// Triggers a global non-overlapping hitstop freeze frame and optional subtle camera FOV punch.
        /// </summary>
        public static void TriggerHitstop(float duration, float timeScale = 0.035f, bool fovPunch = false)
        {
            if (!Application.isPlaying || duration <= 0.002f) return;

            CombatHitstopManager mgr = EnsureExists();
            if (mgr == null || !mgr.isActiveAndEnabled) return;

            mgr.ApplyHitstopInternal(duration, timeScale, fovPunch);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDisable()
        {
            RestoreTimeAndFovSafely();
        }

        private void OnDestroy()
        {
            RestoreTimeAndFovSafely();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            RestoreTimeAndFovSafely();
        }

        private void ApplyHitstopInternal(float duration, float timeScale, bool fovPunch)
        {
            float clampedScale = Mathf.Clamp(timeScale, 0.005f, 1f);
            bool sameFrameOrActive = _isFrozen || Time.frameCount == _lastTriggerFrame;

            if (sameFrameOrActive)
            {
                _remainingFreeze = Mathf.Max(_remainingFreeze, duration);
                _targetTimeScale = Mathf.Min(_targetTimeScale, clampedScale);
                _isFrozen = true;
                _lastTriggerFrame = Time.frameCount;
                Time.timeScale = _targetTimeScale;

                if (fovPunch)
                {
                    _fovOffset = Mathf.Min(_fovOffset, fovPunchAngle);
                }
                return;
            }

            // Allow heavy slams (>= 0.065s) to punch through short cooldown, while gating rapid multi-hits
            if (_cooldownRemaining > 0f && duration < 0.065f)
            {
                if (fovPunch)
                {
                    _fovOffset = Mathf.Min(_fovOffset, fovPunchAngle);
                }
                return;
            }

            _isFrozen = true;
            _remainingFreeze = duration;
            _targetTimeScale = clampedScale;
            _lastTriggerFrame = Time.frameCount;
            Time.timeScale = _targetTimeScale;

            if (fovPunch)
            {
                _fovOffset = Mathf.Min(_fovOffset, fovPunchAngle);
            }
        }

        private void Update()
        {
            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (udt <= 0f) return;

            if (_isFrozen)
            {
                _remainingFreeze -= udt;
                if (_remainingFreeze <= 0f)
                {
                    _isFrozen = false;
                    _remainingFreeze = 0f;
                    _targetTimeScale = 1f;
                    Time.timeScale = 1f;
                    _cooldownRemaining = Mathf.Max(0f, backToBackCooldown);
                }
                else
                {
                    if (Mathf.Abs(Time.timeScale - _targetTimeScale) > 0.0005f)
                    {
                        Time.timeScale = _targetTimeScale;
                    }
                }
            }
            else if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - udt);
            }
        }

        private void LateUpdate()
        {
            if (Mathf.Abs(_fovOffset) < 0.001f && Mathf.Abs(_lastAppliedOffset) < 0.001f)
            {
                return;
            }

            Camera cam = ResolveCamera();
            if (cam == null)
            {
                _fovOffset = 0f;
                _lastAppliedOffset = 0f;
                _lastAppliedCamera = null;
                return;
            }

            float baseFov = (_lastAppliedCamera == cam && Mathf.Abs(cam.fieldOfView - _lastWrittenFov) < 0.002f)
                ? (_lastWrittenFov - _lastAppliedOffset)
                : cam.fieldOfView;

            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float blend = 1f - Mathf.Exp(-Mathf.Max(4f, fovRecoverySpeed) * udt);
            _fovOffset = Mathf.Lerp(_fovOffset, 0f, blend);

            if (Mathf.Abs(_fovOffset) < 0.005f)
            {
                _fovOffset = 0f;
                cam.fieldOfView = baseFov;
                _lastWrittenFov = baseFov;
                _lastAppliedOffset = 0f;
                _lastAppliedCamera = null;
                return;
            }

            float newFov = Mathf.Clamp(baseFov + _fovOffset, 15f, 110f);
            cam.fieldOfView = newFov;
            _lastWrittenFov = newFov;
            _lastAppliedOffset = newFov - baseFov;
            _lastAppliedCamera = cam;
        }

        private Camera ResolveCamera()
        {
            if (_cachedCam != null && _cachedCam.isActiveAndEnabled) return _cachedCam;
            _cachedCam = Camera.main;
            if (_cachedCam == null) _cachedCam = FindAnyObjectByType<Camera>();
            return _cachedCam;
        }

        private void RestoreTimeAndFovSafely()
        {
            _isFrozen = false;
            _remainingFreeze = 0f;
            _targetTimeScale = 1f;
            _cooldownRemaining = 0f;

            try
            {
                if (Mathf.Abs(Time.timeScale - 1f) > 0.0005f)
                {
                    Time.timeScale = 1f;
                }
            }
            catch
            {
                // Ignored during domain teardown
            }

            if (_lastAppliedCamera != null && Mathf.Abs(_lastAppliedOffset) > 0.001f)
            {
                if (Mathf.Abs(_lastAppliedCamera.fieldOfView - _lastWrittenFov) < 0.002f)
                {
                    _lastAppliedCamera.fieldOfView = _lastWrittenFov - _lastAppliedOffset;
                }
            }

            _fovOffset = 0f;
            _lastAppliedOffset = 0f;
            _lastAppliedCamera = null;
        }
    }
}
