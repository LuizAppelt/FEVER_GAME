using UnityEngine;

namespace StomachAbyss.Combat
{
    /// <summary>
    /// Global camera & screen game-feel controller for HELLFLESH:
    /// - Damped Perlin-noise rotational/positional camera shake (zero drift)
    /// - Clean base-tracking FOV punch (zero permanent FOV drift)
    /// - Non-blocking procedural radial damage vignette (physical crimson / acid green)
    /// - Lightweight URP-compatible ground slam shockwave burst
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class CombatCameraJuice : MonoBehaviour
    {
        public static CombatCameraJuice Instance { get; private set; }

        private Camera _cachedCam;

        // Shake state
        private float _shakeIntensity;
        private float _shakeDuration;
        private float _shakeTimer;
        private float _noiseSeed;
        private float _noiseTime;

        private Camera _lastShakeCam;
        private Vector3 _lastAppliedPosOffset;
        private Vector3 _lastWrittenPos;
        private Quaternion _lastAppliedRotOffset = Quaternion.identity;
        private Quaternion _lastWrittenRot = Quaternion.identity;
        private bool _hasAppliedShake;

        // FOV punch state
        private float _fovPunchDelta;
        private float _fovPunchDuration;
        private float _fovPunchTimer;

        private Camera _lastFovCam;
        private float _lastAppliedFovOffset;
        private float _lastWrittenFov;

        // Damage vignette state
        private const float VignetteFadeDuration = 0.45f;
        private float _vignettePeakAlpha;
        private float _vignetteTimer;
        private Color _vignetteColor = new Color(0.75f, 0.04f, 0.06f, 1f);
        private Texture2D _vignetteTex;

        // Shared URP-compatible particle material for slam shockwaves
        private Material _shockwaveMaterial;

        public static CombatCameraJuice EnsureExists()
        {
            if (Instance != null) return Instance;

            Instance = FindAnyObjectByType<CombatCameraJuice>();
            if (Instance != null) return Instance;

            GameObject container = GameObject.Find("Combat_Juice_Systems");
            if (container == null)
            {
                container = new GameObject("Combat_Juice_Systems");
            }

            Instance = container.GetComponent<CombatCameraJuice>();
            if (Instance == null)
            {
                Instance = container.AddComponent<CombatCameraJuice>();
            }
            return Instance;
        }

        public static void Shake(float intensity, float duration)
        {
            if (!Application.isPlaying || intensity <= 0.001f || duration <= 0.001f) return;
            if (!StomachAbyss.UI.GameSettingsManager.ScreenShakeEnabled) return;

            CombatCameraJuice inst = EnsureExists();
            if (inst == null || !inst.isActiveAndEnabled) return;

            inst.ApplyShakeInternal(intensity, duration);
        }

        public static void PunchFov(float deltaFov, float duration)
        {
            if (!Application.isPlaying || Mathf.Abs(deltaFov) <= 0.01f || duration <= 0.001f) return;

            CombatCameraJuice inst = EnsureExists();
            if (inst == null || !inst.isActiveAndEnabled) return;

            inst.ApplyFovPunchInternal(deltaFov, duration);
        }

        public static void TriggerDamageVignette(float intensity = 0.65f, bool isAcid = false)
        {
            if (!Application.isPlaying || intensity <= 0.001f) return;

            CombatCameraJuice inst = EnsureExists();
            if (inst == null || !inst.isActiveAndEnabled) return;

            inst.TriggerVignetteInternal(intensity, isAcid);
        }

        public static void SpawnSlamShockwave(Vector3 worldPos, float radius = 2.6f)
        {
            if (!Application.isPlaying) return;

            CombatCameraJuice inst = EnsureExists();
            if (inst == null || !inst.isActiveAndEnabled) return;

            inst.SpawnSlamShockwaveInternal(worldPos, radius);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _noiseSeed = Random.Range(1f, 999f);
        }

        private void OnDisable()
        {
            RestoreCameraOffsetsSafely();
        }

        private void OnDestroy()
        {
            RestoreCameraOffsetsSafely();

            if (_vignetteTex != null)
            {
                Destroy(_vignetteTex);
                _vignetteTex = null;
            }

            if (_shockwaveMaterial != null)
            {
                Destroy(_shockwaveMaterial);
                _shockwaveMaterial = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            RestoreCameraOffsetsSafely();
        }

        private void ApplyShakeInternal(float intensity, float duration)
        {
            float clampedIntensity = Mathf.Clamp(intensity, 0.01f, 2.5f);
            float clampedDuration = Mathf.Clamp(duration, 0.02f, 2.0f);

            if (_shakeTimer <= 0f || clampedIntensity >= _shakeIntensity * 0.85f)
            {
                _shakeIntensity = Mathf.Max(_shakeIntensity * (_shakeDuration > 0f ? (_shakeTimer / _shakeDuration) : 0f), clampedIntensity);
                _shakeDuration = Mathf.Max(_shakeTimer, clampedDuration);
                _shakeTimer = _shakeDuration;
                _noiseSeed = Random.Range(1f, 999f);
            }
            else
            {
                _shakeTimer = Mathf.Max(_shakeTimer, clampedDuration * 0.6f);
                _shakeDuration = Mathf.Max(_shakeDuration, _shakeTimer);
            }
        }

        private void ApplyFovPunchInternal(float deltaFov, float duration)
        {
            float clampedDelta = Mathf.Clamp(deltaFov, -12f, 12f);
            float clampedDuration = Mathf.Clamp(duration, 0.04f, 1.5f);

            if (_fovPunchTimer <= 0f || Mathf.Abs(clampedDelta) >= Mathf.Abs(_fovPunchDelta))
            {
                _fovPunchDelta = clampedDelta;
                _fovPunchDuration = clampedDuration;
                _fovPunchTimer = clampedDuration;
            }
            else
            {
                _fovPunchTimer = Mathf.Max(_fovPunchTimer, clampedDuration * 0.5f);
                _fovPunchDuration = Mathf.Max(_fovPunchDuration, _fovPunchTimer);
            }
        }

        private void TriggerVignetteInternal(float intensity, bool isAcid)
        {
            float clamped = Mathf.Clamp01(intensity);
            float currentAlpha = _vignetteTimer > 0f
                ? _vignettePeakAlpha * Mathf.Clamp01(_vignetteTimer / VignetteFadeDuration)
                : 0f;

            _vignettePeakAlpha = Mathf.Max(currentAlpha, clamped);
            _vignetteTimer = VignetteFadeDuration;
            _vignetteColor = isAcid
                ? new Color(0.18f, 0.78f, 0.14f, 1f)
                : new Color(0.75f, 0.04f, 0.06f, 1f);
        }

        private void Update()
        {
            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (_vignetteTimer > 0f)
            {
                _vignetteTimer = Mathf.Max(0f, _vignetteTimer - udt);
                if (_vignetteTimer <= 0f)
                {
                    _vignettePeakAlpha = 0f;
                }
            }

            // Undo previous frame's transform shake before follow cameras sample transform in LateUpdate
            RestoreShakeTransformIfUnchanged();
        }

        private void LateUpdate()
        {
            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            Camera cam = ResolveCamera();
            if (cam == null)
            {
                _hasAppliedShake = false;
                _lastShakeCam = null;
                _lastFovCam = null;
                _lastAppliedFovOffset = 0f;
                return;
            }

            UpdateShake(cam, udt);
            UpdateFovPunch(cam, udt);
        }

        private void UpdateShake(Camera cam, float udt)
        {
            // If Update() did not already undo last frame's offset (e.g., same-frame camera), strip it cleanly
            RestoreShakeTransformIfUnchanged();

            if (_shakeTimer <= 0f)
            {
                _shakeIntensity = 0f;
                return;
            }

            _shakeTimer = Mathf.Max(0f, _shakeTimer - udt);
            _noiseTime += udt * 28f;

            float ratio = _shakeDuration > 0.001f ? Mathf.Clamp01(_shakeTimer / _shakeDuration) : 0f;
            float damp = ratio * ratio;
            float amp = _shakeIntensity * damp;

            if (amp <= 0.0005f)
            {
                _shakeTimer = 0f;
                _shakeIntensity = 0f;
                return;
            }

            float nx = Mathf.PerlinNoise(_noiseSeed, _noiseTime) * 2f - 1f;
            float ny = Mathf.PerlinNoise(_noiseSeed + 31.7f, _noiseTime) * 2f - 1f;
            float nz = Mathf.PerlinNoise(_noiseSeed + 67.3f, _noiseTime) * 2f - 1f;

            Transform camT = cam.transform;
            Vector3 posOffset = (camT.right * nx + camT.up * ny) * (amp * 0.42f);
            Quaternion rotOffset = Quaternion.Euler(ny * amp * 2.1f, nx * amp * 1.7f, nz * amp * 2.5f);

            camT.position += posOffset;
            camT.rotation = camT.rotation * rotOffset;

            _lastShakeCam = cam;
            _lastAppliedPosOffset = posOffset;
            _lastWrittenPos = camT.position;
            _lastAppliedRotOffset = rotOffset;
            _lastWrittenRot = camT.rotation;
            _hasAppliedShake = true;
        }

        private void UpdateFovPunch(Camera cam, float udt)
        {
            if (_fovPunchTimer <= 0f && Mathf.Abs(_lastAppliedFovOffset) < 0.001f)
            {
                return;
            }

            float baseFov = (_lastFovCam == cam && Mathf.Abs(cam.fieldOfView - _lastWrittenFov) < 0.002f)
                ? (_lastWrittenFov - _lastAppliedFovOffset)
                : cam.fieldOfView;

            if (_fovPunchTimer <= 0f)
            {
                cam.fieldOfView = baseFov;
                _lastWrittenFov = baseFov;
                _lastAppliedFovOffset = 0f;
                _lastFovCam = null;
                return;
            }

            _fovPunchTimer = Mathf.Max(0f, _fovPunchTimer - udt);
            float ratio = _fovPunchDuration > 0.001f ? Mathf.Clamp01(_fovPunchTimer / _fovPunchDuration) : 0f;
            float currentOffset = _fovPunchDelta * (ratio * ratio);

            if (Mathf.Abs(currentOffset) < 0.005f)
            {
                _fovPunchTimer = 0f;
                _fovPunchDelta = 0f;
                cam.fieldOfView = baseFov;
                _lastWrittenFov = baseFov;
                _lastAppliedFovOffset = 0f;
                _lastFovCam = null;
                return;
            }

            float newFov = Mathf.Clamp(baseFov + currentOffset, 15f, 110f);
            cam.fieldOfView = newFov;
            _lastWrittenFov = newFov;
            _lastAppliedFovOffset = newFov - baseFov;
            _lastFovCam = cam;
        }

        private void RestoreShakeTransformIfUnchanged()
        {
            if (!_hasAppliedShake) return;

            if (_lastShakeCam != null)
            {
                Transform camT = _lastShakeCam.transform;
                if ((camT.position - _lastWrittenPos).sqrMagnitude < 0.0001f)
                {
                    camT.position -= _lastAppliedPosOffset;
                }
                if (Quaternion.Angle(camT.rotation, _lastWrittenRot) < 0.05f)
                {
                    camT.rotation = camT.rotation * Quaternion.Inverse(_lastAppliedRotOffset);
                }
            }

            _hasAppliedShake = false;
            _lastAppliedPosOffset = Vector3.zero;
            _lastAppliedRotOffset = Quaternion.identity;
        }

        private void RestoreCameraOffsetsSafely()
        {
            RestoreShakeTransformIfUnchanged();

            if (_lastFovCam != null && Mathf.Abs(_lastAppliedFovOffset) > 0.001f)
            {
                if (Mathf.Abs(_lastFovCam.fieldOfView - _lastWrittenFov) < 0.002f)
                {
                    _lastFovCam.fieldOfView = _lastWrittenFov - _lastAppliedFovOffset;
                }
            }

            _shakeTimer = 0f;
            _shakeIntensity = 0f;
            _fovPunchTimer = 0f;
            _fovPunchDelta = 0f;
            _lastAppliedFovOffset = 0f;
            _lastFovCam = null;
            _lastShakeCam = null;
        }

        private Camera ResolveCamera()
        {
            if (_cachedCam != null && _cachedCam.isActiveAndEnabled) return _cachedCam;
            _cachedCam = Camera.main;
            if (_cachedCam == null) _cachedCam = FindAnyObjectByType<Camera>();
            return _cachedCam;
        }

        private void OnGUI()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (_vignetteTimer <= 0f || _vignettePeakAlpha <= 0.001f) return;

            EnsureVignetteTexture();
            if (_vignetteTex == null) return;

            float ratio = Mathf.Clamp01(_vignetteTimer / VignetteFadeDuration);
            float alpha = _vignettePeakAlpha * (ratio * ratio);
            if (alpha <= 0.002f) return;

            Color prevColor = GUI.color;
            GUI.color = new Color(_vignetteColor.r, _vignetteColor.g, _vignetteColor.b, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _vignetteTex, ScaleMode.StretchToFill);
            GUI.color = prevColor;
        }

        private void EnsureVignetteTexture()
        {
            if (_vignetteTex != null) return;

            const int size = 64;
            _vignetteTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "CombatDamageVignette64",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            float invHalf = 2f / (size - 1);

            for (int y = 0; y < size; y++)
            {
                float ny = y * invHalf - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = x * invHalf - 1f;
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);
                    float edge = Mathf.SmoothStep(0.42f, 1.18f, dist);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, edge * edge);
                }
            }

            _vignetteTex.SetPixels(pixels);
            _vignetteTex.Apply(false, true);
        }

        private void SpawnSlamShockwaveInternal(Vector3 worldPos, float radius)
        {
            float clampedRadius = Mathf.Clamp(radius, 0.5f, 12f);

            GameObject go = new GameObject("SlamShockwave_FX");
            go.transform.position = worldPos + Vector3.up * 0.08f;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.58f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(clampedRadius * 1.4f, clampedRadius * 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
            main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.62f, 0.14f, 0.16f, 0.85f),
                new Color(0.84f, 0.78f, 0.68f, 0.78f)
            );

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Clamp(clampedRadius * 0.28f, 0.25f, 1.6f);
            shape.radiusThickness = 0.15f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            var limitVel = ps.limitVelocityOverLifetime;
            limitVel.enabled = true;
            limitVel.limit = 0.55f;
            limitVel.dampen = 0.22f;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.75f),
                new Keyframe(0.25f, 1.15f),
                new Keyframe(1f, 0.08f)
            );
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.72f, 0.62f, 0.58f), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.55f, 0.5f), new GradientAlphaKey(0f, 1f) }
            );
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
            if (psr != null)
            {
                psr.sharedMaterial = GetOrCreateShockwaveMaterial();
                psr.renderMode = ParticleSystemRenderMode.Billboard;
            }

            ps.Emit(24);
            Destroy(go, 1.0f);
        }

        private Material GetOrCreateShockwaveMaterial()
        {
            if (_shockwaveMaterial != null) return _shockwaveMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            _shockwaveMaterial = new Material(shader)
            {
                name = "CombatSlamShockwave_Mat"
            };
            if (_shockwaveMaterial.HasProperty("_BaseColor"))
            {
                _shockwaveMaterial.SetColor("_BaseColor", Color.white);
            }
            if (_shockwaveMaterial.HasProperty("_Surface"))
            {
                _shockwaveMaterial.SetFloat("_Surface", 1f); // Transparent
            }
            return _shockwaveMaterial;
        }
    }
}
