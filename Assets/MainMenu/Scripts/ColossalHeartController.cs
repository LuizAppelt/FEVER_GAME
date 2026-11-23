using System;
using UnityEngine;

namespace StomachAbyss.MainMenu
{
    /// <summary>
    /// Controls the realistic double-beat cardiac pulse ("tum-tum" / lub-dub) of the colossal
    /// suspended heart in the Main Menu scene:
    /// - Phase 1 (Systole / "Lub"): sharp expansion of upper atria.
    /// - Phase 2 (Ventricular kick / "Dub", +0.12s): strong expansion & downward thrust of the
    ///   lower ventricular apex + bright crimson-orange emission surge on innerCoreRenderer,
    ///   corePointLight, and backlight.
    /// - Phase 3 (Diastole): smooth elastic relaxation before the next cardiac cycle.
    /// - Gently pulses and sways attached vascularCanopyTransforms in reaction to each shockwave.
    /// - Supports TriggerPlayTransitionBoost() when the player clicks "Jogar" (54 BPM -> 115 BPM).
    /// </summary>
    public class ColossalHeartController : MonoBehaviour
    {
        [Header("Cardiac Rhythm Settings")]
        [Tooltip("Resting ominous heartbeat rate in beats per minute.")]
        public float beatsPerMinute = 54f;

        [Tooltip("Target accelerated heartbeat rate when 'Jogar' play transition is triggered.")]
        public float boostedBeatsPerMinute = 115f;

        [Tooltip("Delay in seconds between Phase 1 (Atrial 'Lub') and Phase 2 (Ventricular 'Dub').")]
        public float lubToDubDelay = 0.12f;

        [Header("Heart Renderers & Transforms")]
        public Renderer innerCoreRenderer;
        public Renderer outerCageRenderer;
        public Renderer crownArteriesRenderer;

        public Transform heartRootTransform;
        public Transform innerCoreTransform;
        public Transform outerCageTransform;
        public Transform crownArteriesTransform;

        [Header("Vascular Canopy Shockwave Targets")]
        public Transform[] vascularCanopyTransforms;

        [Header("Heart Lighting")]
        public Light corePointLight;
        public Light backlight;

        [Header("Emission & Light Pulse Intensities")]
        public Color baseEmissionColor = new Color(0.95f, 0.12f, 0.04f, 1f);
        public Color peakEmissionColor = new Color(1.00f, 0.42f, 0.09f, 1f);
        public float baseEmissionIntensity = 2.4f;
        public float peakEmissionIntensity = 6.4f;

        public float baseCoreLightIntensity = 6.5f;
        public float peakCoreLightIntensity = 16.5f;

        public float baseBacklightIntensity = 9.0f;
        public float peakBacklightIntensity = 21.0f;

        [Header("Deformation Amplitudes")]
        [Tooltip("Upper atrial radial expansion amplitude during Phase 1 (Lub).")]
        public float atrialLubExpansion = 0.11f;

        [Tooltip("Lower ventricular radial expansion amplitude during Phase 2 (Dub).")]
        public float ventricularDubExpansion = 0.17f;

        [Tooltip("Downward apex thrust distance during Phase 2 (Dub).")]
        public float ventricularDownwardThrust = 0.24f;

        /// <summary>
        /// Fired at the onset of Phase 1 (Systole / "Lub"). Passes normalized pulse intensity.
        /// </summary>
        public event Action<float> OnHeartbeatLub;

        /// <summary>
        /// Fired 0.12s later at the onset of Phase 2 (Ventricular kick / "Dub"). Passes normalized pulse intensity.
        /// </summary>
        public event Action<float> OnHeartbeatDub;

        /// <summary>
        /// Current combined normalized cardiac pulse intensity [0..1+].
        /// </summary>
        public float CurrentPulseIntensity { get; private set; }

        /// <summary>
        /// True after TriggerPlayTransitionBoost() has been invoked.
        /// </summary>
        public bool IsTransitionBoostActive { get; private set; }

        // Internal cycle state
        private float _cycleTimer;
        private bool _lubFiredThisCycle;
        private bool _dubFiredThisCycle;
        private float _boostBlend; // 0 = normal 54 BPM, 1 = boosted 115 BPM

        // Cached initial transform state
        private bool _transformsCached;
        private Vector3 _rootBaseLocalPos;
        private Vector3 _rootBaseLocalScale;
        private Quaternion _rootBaseLocalRot;

        private Transform[] _cachedCanopyRefs;
        private Vector3[] _canopyBaseScales;
        private Quaternion[] _canopyBaseRotations;

        // Cached runtime deformable mesh buffers for true regional Atria vs. Ventricle deformation
        private Mesh _coreRuntimeMesh;
        private Vector3[] _coreBaseVertices;
        private Vector3[] _coreDeformedVertices;

        private Mesh _cageRuntimeMesh;
        private Vector3[] _cageBaseVertices;
        private Vector3[] _cageDeformedVertices;

        private MaterialPropertyBlock _corePropBlock;
        private MaterialPropertyBlock _cagePropBlock;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _corePropBlock = new MaterialPropertyBlock();
            _cagePropBlock = new MaterialPropertyBlock();
            AutoResolveReferencesIfNeeded();
        }

        private void Start()
        {
            AutoResolveReferencesIfNeeded();
            EnsureTransformsCached();
            EnsureDeformableMeshesInitialized();
        }

        /// <summary>
        /// Accelerates the heartbeat to 115 BPM and intensifies the crimson glow when the player clicks "Jogar".
        /// </summary>
        public void TriggerPlayTransitionBoost()
        {
            IsTransitionBoostActive = true;
        }

        private void Update()
        {
            AutoResolveReferencesIfNeeded();
            EnsureTransformsCached();
            EnsureDeformableMeshesInitialized();

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            // Smoothly ramp BPM and glow multiplier when play transition boost is triggered
            if (IsTransitionBoostActive)
            {
                _boostBlend = Mathf.MoveTowards(_boostBlend, 1f, dt * 1.85f);
                beatsPerMinute = Mathf.Lerp(beatsPerMinute, boostedBeatsPerMinute, dt * 4.5f);
            }

            float activeBpm = Mathf.Clamp(beatsPerMinute, 20f, 220f);
            float cyclePeriod = 60f / activeBpm;

            // Scale lub-to-dub delay slightly at very high BPM so Dub always fits cleanly before Diastole
            float effectiveDubDelay = Mathf.Min(lubToDubDelay, cyclePeriod * 0.28f);

            _cycleTimer += dt;
            if (_cycleTimer >= cyclePeriod)
            {
                _cycleTimer -= cyclePeriod;
                _lubFiredThisCycle = false;
                _dubFiredThisCycle = false;
            }

            float boostIntensityMult = 1.0f + 0.45f * _boostBlend;

            // 1. Trigger Phase 1 ("Lub") event at start of cardiac cycle
            if (!_lubFiredThisCycle && _cycleTimer >= 0f)
            {
                _lubFiredThisCycle = true;
                OnHeartbeatLub?.Invoke(0.85f * boostIntensityMult);
            }

            // 2. Trigger Phase 2 ("Dub") event 0.12s after "Lub"
            if (!_dubFiredThisCycle && _cycleTimer >= effectiveDubDelay)
            {
                _dubFiredThisCycle = true;
                OnHeartbeatDub?.Invoke(1.0f * boostIntensityMult);
            }

            // Evaluate analytic cardiac pressure waveforms
            // Phase 1 (Systole / "Lub"): sharp atrial contraction peak around t = 0.045s
            float lubWave = EvaluateAsymmetricPulse(_cycleTimer, peakTime: 0.042f, riseDuration: 0.042f, decayRate: 18f);

            // Phase 2 (Ventricular kick / "Dub"): strong ventricular expansion & downward apex thrust at t = effectiveDubDelay + 0.048s
            float dubTime = _cycleTimer - effectiveDubDelay;
            float dubWave = EvaluateAsymmetricPulse(dubTime, peakTime: 0.048f, riseDuration: 0.048f, decayRate: 11.5f);

            // Phase 3 (Diastole): gentle elastic rebound ripple during relaxation
            float diastoleRebound = 0f;
            if (dubTime > 0.18f)
            {
                float dT = dubTime - 0.18f;
                diastoleRebound = Mathf.Sin(dT * 9.5f) * Mathf.Exp(-dT * 4.5f) * 0.08f;
            }

            float atrialFactor = Mathf.Clamp01(lubWave) * boostIntensityMult;
            float ventricularFactor = Mathf.Max(0f, dubWave + diastoleRebound) * boostIntensityMult;

            CurrentPulseIntensity = Mathf.Clamp01(Mathf.Max(atrialFactor * 0.72f, ventricularFactor));

            // Apply anatomical vertex deformation + transform recoil
            ApplyHeartDeformation(atrialFactor, ventricularFactor);

            // Apply emission and light surge (strongest during Phase 2 ventricular kick)
            ApplyEmissionAndLighting(atrialFactor, ventricularFactor);

            // Apply travelling shockwave to attached vascular canopy transforms
            ApplyVascularCanopyShockwave( effectiveDubDelay, boostIntensityMult);
        }

        private static float EvaluateAsymmetricPulse(float localTime, float peakTime, float riseDuration, float decayRate)
        {
            if (localTime <= 0f)
                return 0f;

            if (localTime < riseDuration)
            {
                float u = localTime / Mathf.Max(0.001f, riseDuration);
                // Fast, snappy systolic rise
                return u * u * (3f - 2f * u);
            }

            float postPeak = localTime - peakTime;
            return Mathf.Exp(-postPeak * decayRate) * (1f + 0.25f * postPeak * decayRate);
        }

        private void ApplyHeartDeformation(float atrialLub, float ventricularDub)
        {
            Transform root = heartRootTransform != null ? heartRootTransform : transform;

            // Transform-level visceral kick:
            // Phase 1 swells upper width; Phase 2 thrusts downward along -Y and expands X/Z
            float scaleX = 1.0f + atrialLub * (atrialLubExpansion * 0.55f) + ventricularDub * (ventricularDubExpansion * 0.65f);
            float scaleY = 1.0f + atrialLub * (atrialLubExpansion * 0.35f) + ventricularDub * (ventricularDubExpansion * 0.45f);
            float scaleZ = 1.0f + atrialLub * (atrialLubExpansion * 0.55f) + ventricularDub * (ventricularDubExpansion * 0.68f);

            root.localScale = new Vector3(
                _rootBaseLocalScale.x * scaleX,
                _rootBaseLocalScale.y * scaleY,
                _rootBaseLocalScale.z * scaleZ);

            // Downward ventricular apex kick + subtle organic torsion twist during systole
            float downwardOffset = -ventricularDub * ventricularDownwardThrust + atrialLub * (ventricularDownwardThrust * 0.22f);
            root.localPosition = _rootBaseLocalPos + new Vector3(0f, downwardOffset, -ventricularDub * 0.08f);

            float pitchKick = ventricularDub * 2.8f - atrialLub * 1.4f;
            float rollKick = Mathf.Sin(Time.time * 1.3f) * 0.8f + ventricularDub * 1.6f;
            root.localRotation = _rootBaseLocalRot * Quaternion.Euler(pitchKick, ventricularDub * 1.9f, rollKick);

            // Regional vertex-level deformation:
            // Upper vertices (y > 0) expand sharply during Phase 1 (Atrial Lub),
            // Lower vertices (y <= 0) expand and thrust downward during Phase 2 (Ventricular Dub)
            if (_coreRuntimeMesh != null && _coreBaseVertices != null)
            {
                DeformHeartMeshBuffer(_coreBaseVertices, _coreDeformedVertices, atrialLub, ventricularDub, isInnerCore: true);
                _coreRuntimeMesh.vertices = _coreDeformedVertices;
            }

            if (_cageRuntimeMesh != null && _cageBaseVertices != null)
            {
                DeformHeartMeshBuffer(_cageBaseVertices, _cageDeformedVertices, atrialLub, ventricularDub, isInnerCore: false);
                _cageRuntimeMesh.vertices = _cageDeformedVertices;
            }

            if (crownArteriesTransform != null)
            {
                // Crown arteries swell slightly right after ventricular ejection
                float crownPulse = 1.0f + atrialLub * 0.05f + ventricularDub * 0.09f;
                crownArteriesTransform.localScale = new Vector3(crownPulse, 1.0f + ventricularDub * 0.04f, crownPulse);
            }
        }

        private void DeformHeartMeshBuffer(
            Vector3[] baseVerts,
            Vector3[] outVerts,
            float atrialLub,
            float ventricularDub,
            bool isInnerCore)
        {
            float lubAmp = atrialLub * atrialLubExpansion * (isInnerCore ? 1.15f : 0.95f);
            float dubAmp = ventricularDub * ventricularDubExpansion * (isInnerCore ? 1.22f : 1.0f);
            float apexDrop = ventricularDub * (ventricularDownwardThrust * 0.65f);

            int count = baseVerts.Length;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = baseVerts[i];

                // Normalized height masks:
                // upperMask (Atria): strongest for y in [0.1, 2.3]
                // lowerMask (Ventricle/Apex): strongest for y in [-2.8, 0.4]
                float upperMask = Mathf.Clamp01((v.y + 0.15f) / 2.15f);
                float lowerMask = Mathf.Clamp01((0.45f - v.y) / 2.85f);

                float radialScale = 1.0f + upperMask * lubAmp + lowerMask * dubAmp;
                float verticalShift = upperMask * (lubAmp * 0.25f) - (lowerMask * lowerMask) * apexDrop;

                outVerts[i] = new Vector3(
                    v.x * radialScale,
                    v.y + verticalShift,
                    v.z * radialScale);
            }
        }

        private void ApplyEmissionAndLighting(float atrialLub, float ventricularDub)
        {
            // Emission surges moderately on Lub and blazes intensely on Dub
            float surge = Mathf.Clamp01(atrialLub * 0.45f + ventricularDub * 0.95f + _boostBlend * 0.35f);

            float currentEmissionMultiplier = Mathf.Lerp(baseEmissionIntensity, peakEmissionIntensity, surge)
                                            * (1.0f + 0.45f * _boostBlend);
            Color rawColor = Color.Lerp(baseEmissionColor, peakEmissionColor, surge);
            Color hdrEmission = rawColor * currentEmissionMultiplier;

            if (innerCoreRenderer != null)
            {
                innerCoreRenderer.GetPropertyBlock(_corePropBlock);
                _corePropBlock.SetColor(EmissionColorId, hdrEmission);
                Color pulseTint = Color.Lerp(new Color(0.95f, 0.88f, 0.88f, 1f), new Color(1.0f, 1.0f, 0.96f, 1f), surge);
                _corePropBlock.SetColor(BaseColorId, pulseTint);
                _corePropBlock.SetColor(ColorId, pulseTint);
                innerCoreRenderer.SetPropertyBlock(_corePropBlock);
            }

            if (outerCageRenderer != null)
            {
                // Subtle warm subsurface bleed on the dark outer myocardial vessel cage during peak systole
                outerCageRenderer.GetPropertyBlock(_cagePropBlock);
                Color cageSubsurface = new Color(0.28f, 0.02f, 0.02f, 1f) * (0.25f + 0.85f * surge);
                _cagePropBlock.SetColor(EmissionColorId, cageSubsurface);
                outerCageRenderer.SetPropertyBlock(_cagePropBlock);
            }

            if (corePointLight != null)
            {
                corePointLight.intensity = Mathf.Lerp(baseCoreLightIntensity, peakCoreLightIntensity, surge)
                                         * (1.0f + 0.35f * _boostBlend);
                corePointLight.color = Color.Lerp(
                    new Color(1.0f, 0.14f, 0.08f, 1f),
                    new Color(1.0f, 0.34f, 0.14f, 1f),
                    surge);
            }

            if (backlight != null)
            {
                backlight.intensity = Mathf.Lerp(baseBacklightIntensity, peakBacklightIntensity, surge)
                                    * (1.0f + 0.35f * _boostBlend);
            }
        }

        private void ApplyVascularCanopyShockwave(float effectiveDubDelay, float boostIntensityMult)
        {
            if (vascularCanopyTransforms == null || vascularCanopyTransforms.Length == 0)
                return;

            // Refresh cache if array changed
            if (_cachedCanopyRefs != vascularCanopyTransforms || _canopyBaseScales == null || _canopyBaseScales.Length != vascularCanopyTransforms.Length)
            {
                CacheCanopyTransforms();
            }

            // Arterial shockwave travels outward into the canopy ~0.07s after the ventricular Dub ejection
            float shockTime = _cycleTimer - (effectiveDubDelay + 0.07f);
            float primaryShock = EvaluateAsymmetricPulse(shockTime, peakTime: 0.065f, riseDuration: 0.065f, decayRate: 7.5f) * boostIntensityMult;

            float time = Time.time;
            for (int i = 0; i < vascularCanopyTransforms.Length; i++)
            {
                Transform t = vascularCanopyTransforms[i];
                if (t == null)
                    continue;

                // Per-branch phase lag so the shockwave ripples organically across the ceiling veins
                float branchPhase = (i * 0.035f);
                float branchShock = EvaluateAsymmetricPulse(shockTime - branchPhase, peakTime: 0.07f, riseDuration: 0.07f, decayRate: 6.8f) * boostIntensityMult;
                if (branchShock < 0.001f)
                    branchShock = primaryShock * 0.5f;

                float radialPulse = 1.0f + branchShock * 0.065f;
                float axialPulse = 1.0f + branchShock * 0.025f;
                Vector3 baseScale = _canopyBaseScales[i];
                t.localScale = new Vector3(
                    baseScale.x * radialPulse,
                    baseScale.y * axialPulse,
                    baseScale.z * radialPulse);

                // Gentle organic sway + shockwave recoil rotation
                float swayAngleX = Mathf.Sin(time * 1.15f + i * 1.3f) * 1.25f + branchShock * 2.4f * ((i % 2 == 0) ? 1f : -1f);
                float swayAngleZ = Mathf.Cos(time * 0.92f + i * 1.9f) * 1.15f + branchShock * 1.9f;
                t.localRotation = _canopyBaseRotations[i] * Quaternion.Euler(swayAngleX, 0f, swayAngleZ);
            }
        }

        private void AutoResolveReferencesIfNeeded()
        {
            if (heartRootTransform == null)
                heartRootTransform = transform;

            if (innerCoreRenderer == null)
            {
                Transform coreChild = transform.Find("HeartInnerCore") ?? transform.Find("InnerCore");
                if (coreChild != null)
                {
                    innerCoreTransform = coreChild;
                    innerCoreRenderer = coreChild.GetComponent<Renderer>();
                }
            }
            else if (innerCoreTransform == null)
            {
                innerCoreTransform = innerCoreRenderer.transform;
            }

            if (outerCageRenderer == null)
            {
                Transform cageChild = transform.Find("HeartOuterVesselCage") ?? transform.Find("OuterVesselCage") ?? transform.Find("OuterCage");
                if (cageChild != null)
                {
                    outerCageTransform = cageChild;
                    outerCageRenderer = cageChild.GetComponent<Renderer>();
                }
            }
            else if (outerCageTransform == null)
            {
                outerCageTransform = outerCageRenderer.transform;
            }

            if (crownArteriesRenderer == null)
            {
                Transform crownChild = transform.Find("HeartCrownArteries") ?? transform.Find("CrownArteries");
                if (crownChild != null)
                {
                    crownArteriesTransform = crownChild;
                    crownArteriesRenderer = crownChild.GetComponent<Renderer>();
                }
            }
            else if (crownArteriesTransform == null)
            {
                crownArteriesTransform = crownArteriesRenderer.transform;
            }
        }

        private void EnsureTransformsCached()
        {
            Transform root = heartRootTransform != null ? heartRootTransform : transform;
            if (!_transformsCached)
            {
                _rootBaseLocalPos = root.localPosition;
                _rootBaseLocalScale = root.localScale;
                _rootBaseLocalRot = root.localRotation;
                _transformsCached = true;
            }

            if (vascularCanopyTransforms != null && (_cachedCanopyRefs != vascularCanopyTransforms))
            {
                CacheCanopyTransforms();
            }
        }

        private void CacheCanopyTransforms()
        {
            if (vascularCanopyTransforms == null)
                return;

            _cachedCanopyRefs = vascularCanopyTransforms;
            int n = vascularCanopyTransforms.Length;
            _canopyBaseScales = new Vector3[n];
            _canopyBaseRotations = new Quaternion[n];

            for (int i = 0; i < n; i++)
            {
                Transform t = vascularCanopyTransforms[i];
                if (t != null)
                {
                    _canopyBaseScales[i] = t.localScale;
                    _canopyBaseRotations[i] = t.localRotation;
                }
                else
                {
                    _canopyBaseScales[i] = Vector3.one;
                    _canopyBaseRotations[i] = Quaternion.identity;
                }
            }
        }

        private void EnsureDeformableMeshesInitialized()
        {
            if (_coreRuntimeMesh == null && innerCoreRenderer != null)
            {
                MeshFilter mf = innerCoreRenderer.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    _coreRuntimeMesh = Instantiate(mf.sharedMesh);
                    _coreRuntimeMesh.name = mf.sharedMesh.name + "_RuntimeDeform";
                    _coreRuntimeMesh.MarkDynamic();
                    mf.mesh = _coreRuntimeMesh;

                    _coreBaseVertices = _coreRuntimeMesh.vertices;
                    _coreDeformedVertices = new Vector3[_coreBaseVertices.Length];
                    Array.Copy(_coreBaseVertices, _coreDeformedVertices, _coreBaseVertices.Length);
                }
            }

            if (_cageRuntimeMesh == null && outerCageRenderer != null)
            {
                MeshFilter mf = outerCageRenderer.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    _cageRuntimeMesh = Instantiate(mf.sharedMesh);
                    _cageRuntimeMesh.name = mf.sharedMesh.name + "_RuntimeDeform";
                    _cageRuntimeMesh.MarkDynamic();
                    mf.mesh = _cageRuntimeMesh;

                    _cageBaseVertices = _cageRuntimeMesh.vertices;
                    _cageDeformedVertices = new Vector3[_cageBaseVertices.Length];
                    Array.Copy(_cageBaseVertices, _cageDeformedVertices, _cageBaseVertices.Length);
                }
            }
        }
    }
}
