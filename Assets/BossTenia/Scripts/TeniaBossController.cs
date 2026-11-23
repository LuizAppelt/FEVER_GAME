using System.Collections.Generic;
using UnityEngine;

namespace BossTenia
{
    /// <summary>
    /// Procedural rigging and pose controller for the Tenia boss.
    /// Computes ground S-curve slithering poses, 3D helical pillar-coiled poses,
    /// world-gravity hanging vestments, and acid mouth aiming in both Edit Mode and Play Mode.
    /// </summary>
    [ExecuteAlways]
    public class TeniaBossController : MonoBehaviour
    {
        public enum BossPoseState
        {
            GroundSlither,
            PillarCoiled,
            ConstrictTarget
        }

        [Header("Pose State & Rig References")]
        public BossPoseState poseState = BossPoseState.GroundSlither;
        public Transform headSegment;
        public Transform acidMouthAnchor;
        public List<Transform> bodySegments = new List<Transform>();
        public Transform tailSegment;
        public List<Transform> hangingVestments = new List<Transform>();
        public Transform aimTarget;

        [Header("Segment Geometry & Ordering")]
        [Tooltip("Approximate cross-section radius of body segments used for floor clearance (y = segmentRadius * 0.8f).")]
        public float segmentRadius = 0.65f;

        [Tooltip("True when bodySegments[0] is closest to headSegment and bodySegments[last] is closest to tailSegment.")]
        public bool bodySegmentsOrderedHeadToTail = true;

        [Header("Ground Slither Pose")]
        public float segmentSpacing = 1.05f;
        public float groundWaveAmplitude = 1.85f;
        public float groundWaveFrequency = 0.55f;
        public float headElevation = 2.4f;
        public float slitherSpeed = 1.4f;

        [Header("Pillar-Coiled Pose")]
        public Vector3 pillarLocalOrigin = Vector3.zero;
        public bool pillarOriginIsWorld = false;
        public float pillarRadius = 1.18f;
        public float coilStartHeight = 1.5f;
        public float coilTopHeight = 10.0f;
        public float coilTurns = 2.25f;
        public float headHangOutward = 2.4f;
        public float headHangDrop = 1.6f;

        [Header("Runtime Transitions & Tracking")]
        [Tooltip("Duration in seconds for smooth Lerp/Slerp transitions between GroundSlither and PillarCoiled.")]
        public float transitionDuration = 0.85f;

        [Tooltip("Angular speed (deg/s) at which the upper coil slowly shifts if aimTarget circles behind a pillar.")]
        public float coilOrbitFollowSpeed = 40f;

        [Tooltip("Optional fallback target when aimTarget is null in PillarCoiled pose.")]
        public Transform naveCenterFallback;

        [Header("Contencao da Arena")]
        public TeniaArenaBounds arenaBounds;

        public bool IsTransitioningPillar => _pillarCrawlActive;
        public bool IsLeaping => _pillarCrawlActive && _pillarCrawlDirection == -2;
        public bool IsAirHoppingPillar => _pillarCrawlActive && _pillarCrawlDirection == 2;
        public bool IsFallingStagger => _pillarCrawlActive && _pillarCrawlDirection == -3;
        public float PillarCrawlProgress => _pillarCrawlProgress;

        private struct SegmentTarget
        {
            public Transform Transform;
            public Vector3 WorldPosition;
            public Quaternion WorldRotation;
        }

        private readonly List<Transform> _orderedBodyTailToHead = new List<Transform>(32);
        private readonly List<Transform> _orderedSpineAll = new List<Transform>(32);
        private readonly List<SegmentTarget> _targetBuffer = new List<SegmentTarget>(32);
        private readonly List<Vector3> _transitionStartPos = new List<Vector3>(32);
        private readonly List<Quaternion> _transitionStartRot = new List<Quaternion>(32);

        private readonly List<float> _curveArcLengths = new List<float>(128);
        private readonly List<Vector3> _floorPosBuffer = new List<Vector3>(32);
        private readonly List<Vector3> _floorTanBuffer = new List<Vector3>(32);

        // Runtime organic follow-the-leader world trail
        private readonly List<Vector3> _worldTrail = new List<Vector3>(360);
        private bool _worldTrailInitialized;
        private Vector3 _lastTrailRootPos;
        private float _coilTightness;
        private float _targetCoilTightness;

        // Runtime pillar spline-crawl (0 = grounded, 1 = fully coiled on pillar)
        private readonly List<Vector3> _unifiedCrawlPath = new List<Vector3>(160);
        private readonly List<float> _unifiedCrawlArcs = new List<float>(160);
        private bool _pillarCrawlActive;
        private int _pillarCrawlDirection = 1; // +1 climb, -1 descend, -2 parabolic leap slam
        private float _pillarCrawlProgress;
        private float _pillarCrawlDuration = 1.85f;
        private float _crawlStartArc;
        private float _crawlEndArc;
        private float _pillarLeapPrepOffset;

        // Runtime bite & tail sweep combat overlays
        private bool _biteAnimActive;
        private float _biteElapsed;
        private float _biteWindupDur = 0.45f;
        private float _biteStrikeDur = 0.28f;
        private float _biteRecoverDur = 0.35f;

        private bool _tailSweepActive;
        private float _tailSweepElapsed;
        private float _tailSweepWindupDur = 0.48f;
        private float _tailSweepSwingDur = 0.32f;
        private float _tailSweepRecoverDur = 0.42f;
        private float _tailSweepSideSign = 1f;

        // Runtime snake constriction pose
        private Vector3 _constrictCenterWorld;
        private float _constrictRadius = 1.65f;

        private Quaternion _targetMouthWorldRot = Quaternion.identity;
        private Quaternion _transitionStartMouthRot = Quaternion.identity;
        private bool _hasTargetMouthRot;

        private bool _isTransitioning;
        private float _transitionElapsed;

        private BossPoseState _lastObservedPoseState;
        private bool _initialized;
        private bool _editPreviewPending;

        private Vector3 _activePillarWorldPos;
        private float _activePillarRadius = 1.18f;
        private bool _hasRuntimePillarOverride;

        private Vector3 _lockedPillarOutwardXZ = Vector3.forward;
        private bool _hasLockedPillarOutward;

        private Vector3 _lastInspectorPillarOrigin;
        private bool _lastInspectorOriginIsWorld;
        private Transform _cachedNaveCenterAnchor;
        private bool _naveLookupAttempted;

        // Post-death freeze, floor collapse & acid melt state
        private bool _deadFrozen;
        private readonly List<Transform> _deathTransforms = new List<Transform>(48);
        private readonly List<Vector3> _deathStartPos = new List<Vector3>(48);
        private readonly List<Quaternion> _deathStartRot = new List<Quaternion>(48);
        private readonly List<Vector3> _deathStartScale = new List<Vector3>(48);
        private readonly List<Vector3> _deathRestPos = new List<Vector3>(48);
        private readonly List<Quaternion> _deathRestRot = new List<Quaternion>(48);
        private float _deathFloorY;
        private bool _deathVestmentsFrozen;

        private void Awake()
        {
            SyncInspectorWatchers();
        }

        private void OnEnable()
        {
            SyncInspectorWatchers();
            _initialized = true;
        }

        private void OnValidate()
        {
            if (!_initialized || Application.isPlaying || !gameObject.scene.IsValid())
            {
                return;
            }

            if (_lastInspectorPillarOrigin != pillarLocalOrigin || _lastInspectorOriginIsWorld != pillarOriginIsWorld)
            {
                _hasRuntimePillarOverride = false;
                _hasLockedPillarOutward = false;
                _lastInspectorPillarOrigin = pillarLocalOrigin;
                _lastInspectorOriginIsWorld = pillarOriginIsWorld;
            }

            _editPreviewPending = true;
        }

        private void Update()
        {
            if (!gameObject.scene.IsValid())
            {
                return;
            }

            if (!Application.isPlaying)
            {
                if (poseState != _lastObservedPoseState || _editPreviewPending)
                {
                    _editPreviewPending = false;
                    _lastObservedPoseState = poseState;

                    if (poseState == BossPoseState.GroundSlither)
                    {
                        ApplyGroundPose(0f);
                    }
                    else if (poseState == BossPoseState.PillarCoiled)
                    {
                        Vector3 pillarCenter = _hasRuntimePillarOverride ? _activePillarWorldPos : GetPillarWorldCenter();
                        float radius = _hasRuntimePillarOverride ? _activePillarRadius : pillarRadius;
                        ApplyPillarCoiledPose(pillarCenter, radius);
                    }
                }
                return;
            }

            if (_deadFrozen)
            {
                return;
            }

            float dt = Time.deltaTime;
            _coilTightness = Mathf.MoveTowards(_coilTightness, _targetCoilTightness, dt * 2.2f);
            if (_biteAnimActive)
            {
                _biteElapsed += dt;
                if (_biteElapsed >= _biteWindupDur + _biteStrikeDur + _biteRecoverDur)
                {
                    _biteAnimActive = false;
                }
            }
            if (_tailSweepActive)
            {
                _tailSweepElapsed += dt;
                if (_tailSweepElapsed >= _tailSweepWindupDur + _tailSweepSwingDur + _tailSweepRecoverDur)
                {
                    _tailSweepActive = false;
                }
            }

            if (poseState != _lastObservedPoseState && !_pillarCrawlActive)
            {
                if (poseState == BossPoseState.GroundSlither)
                {
                    SetStateGround();
                }
                else if (poseState == BossPoseState.PillarCoiled)
                {
                    SetStatePillar(GetPillarWorldCenter(), pillarRadius);
                }
            }

            if (_pillarCrawlActive)
            {
                UpdatePillarCrawl(dt);
                ApplyRuntimeTargets();
                return;
            }

            if (poseState == BossPoseState.GroundSlither)
            {
                EvaluateGroundTargetsRuntime(dt, Time.time);
            }
            else if (poseState == BossPoseState.ConstrictTarget)
            {
                EvaluateConstrictTargets(Time.time);
            }
            else
            {
                Vector3 pillarCenter = _hasRuntimePillarOverride ? _activePillarWorldPos : GetPillarWorldCenter();
                float radius = _hasRuntimePillarOverride ? _activePillarRadius : pillarRadius;

                Vector3 desiredOutward = ResolvePillarOutwardXZ(pillarCenter);
                if (!_hasLockedPillarOutward)
                {
                    _lockedPillarOutwardXZ = desiredOutward;
                    _hasLockedPillarOutward = true;
                }
                else if (aimTarget != null)
                {
                    float angle = Vector3.Angle(_lockedPillarOutwardXZ, desiredOutward);
                    if (angle > 35f && coilOrbitFollowSpeed > 0f)
                    {
                        float maxRad = Mathf.Deg2Rad * coilOrbitFollowSpeed * dt;
                        _lockedPillarOutwardXZ = Vector3.RotateTowards(_lockedPillarOutwardXZ, desiredOutward, maxRad, 0f);
                        _lockedPillarOutwardXZ.y = 0f;
                        if (_lockedPillarOutwardXZ.sqrMagnitude > 1e-6f)
                        {
                            _lockedPillarOutwardXZ.Normalize();
                        }
                    }
                }

                EvaluatePillarTargets(pillarCenter, radius, _lockedPillarOutwardXZ, Time.time);
            }

            ApplyRuntimeTargets();
        }

        private void LateUpdate()
        {
            if (!gameObject.scene.IsValid() || _deadFrozen)
            {
                return;
            }

            UpdateHangingVestments();
        }

        [ContextMenu("Preview Ground Pose")]
        public void PreviewGroundPose()
        {
            poseState = BossPoseState.GroundSlither;
            _lastObservedPoseState = BossPoseState.GroundSlither;
            _isTransitioning = false;
            _editPreviewPending = false;
            ApplyGroundPose(0f);
            MarkDirtyInEditor();
        }

        [ContextMenu("Preview Pillar Coiled Pose")]
        public void PreviewPillarCoiledPose()
        {
            poseState = BossPoseState.PillarCoiled;
            _lastObservedPoseState = BossPoseState.PillarCoiled;
            _isTransitioning = false;
            _editPreviewPending = false;
            _hasRuntimePillarOverride = false;
            _hasLockedPillarOutward = false;
            ApplyPillarCoiledPose(GetPillarWorldCenter(), pillarRadius);
            MarkDirtyInEditor();
        }

        /// <summary>
        /// Places tail and rear body segments slithering along the floor in an arc-length-uniform S-curve,
        /// while rearing the front 4-5 segments and head up to headElevation aiming forward or toward aimTarget.
        /// </summary>
        public void ApplyGroundPose(float timeOffset = 0f)
        {
            _editPreviewPending = false;
            _isTransitioning = false;
            EvaluateGroundTargets(timeOffset);
            ApplyTargetBufferImmediate();
            UpdateHangingVestments();
        }

        /// <summary>
        /// Wraps the tail and body segments along a smooth ascending 3D helix around pillarCenterWorld
        /// at orbital radius (radius + 0.72f) from coilStartHeight to coilTopHeight, then extends the front
        /// neck segments and head outward/downward aiming toward aimTarget or the cathedral nave center.
        /// </summary>
        public void ApplyPillarCoiledPose(Vector3 pillarCenterWorld, float radius)
        {
            _editPreviewPending = false;
            _isTransitioning = false;
            _activePillarWorldPos = pillarCenterWorld;
            _activePillarRadius = radius;
            _hasRuntimePillarOverride = true;

            Vector3 outwardXZ = ResolvePillarOutwardXZ(pillarCenterWorld);
            _lockedPillarOutwardXZ = outwardXZ;
            _hasLockedPillarOutward = true;

            EvaluatePillarTargets(pillarCenterWorld, radius, outwardXZ, 0f);
            ApplyTargetBufferImmediate();
            UpdateHangingVestments();
        }

        /// <summary>
        /// Rotates every transform in hangingVestments so its local -Y points straight toward Vector3.down
        /// in world space while keeping its forward vector facing radially outward from its parent body segment.
        /// </summary>
        public void UpdateHangingVestments()
        {
            if (hangingVestments == null || hangingVestments.Count == 0)
            {
                return;
            }

            for (int i = 0; i < hangingVestments.Count; i++)
            {
                Transform vestment = hangingVestments[i];
                if (vestment == null)
                {
                    continue;
                }

                Vector3 outwardXZ = ResolveVestmentOutwardXZ(vestment);
                Quaternion targetRot = Quaternion.LookRotation(outwardXZ, Vector3.up);

                if (Quaternion.Angle(vestment.rotation, targetRot) > 0.001f)
                {
                    vestment.rotation = targetRot;
                }
            }
        }

        /// <summary>
        /// Switches runtime state to GroundSlither cleanly (crawling down smoothly if coming from PillarCoiled).
        /// </summary>
        public void SetStateGround()
        {
            BossPoseState prevState = _lastObservedPoseState;
            poseState = BossPoseState.GroundSlither;
            _lastObservedPoseState = BossPoseState.GroundSlither;

            if (Application.isPlaying && prevState == BossPoseState.PillarCoiled && !_pillarCrawlActive)
            {
                Vector3 pillarCenter = _hasRuntimePillarOverride ? _activePillarWorldPos : GetPillarWorldCenter();
                float radius = _hasRuntimePillarOverride ? _activePillarRadius : pillarRadius;
                Vector3 outwardXZ = _hasLockedPillarOutward ? _lockedPillarOutwardXZ : ResolvePillarOutwardXZ(pillarCenter);
                BuildPillarDescendPathAndBegin(pillarCenter, radius, outwardXZ, 1.48f);
                return;
            }

            _pillarCrawlActive = false;
            _pillarLeapPrepOffset = 0f;
            _hasRuntimePillarOverride = false;
            _hasLockedPillarOutward = false;

            if (Application.isPlaying)
            {
                SeedWorldTrailFromCurrentPositions();
                _isTransitioning = false;
            }
            else
            {
                BeginTransition();
            }
        }

        public void SetPoseImmediateGround()
        {
            _pillarCrawlActive = false;
            _biteAnimActive = false;
            _tailSweepActive = false;
            _targetCoilTightness = 0f;
            _coilTightness = 0f;
            poseState = BossPoseState.GroundSlither;
            _lastObservedPoseState = BossPoseState.GroundSlither;
            _hasRuntimePillarOverride = false;
            _hasLockedPillarOutward = false;
            ApplyGroundPose(0f);
            SeedWorldTrailFromCurrentPositions();
        }

        public void SetAimTarget(Transform target)
        {
            aimTarget = _deadFrozen ? null : target;
        }

        public void SetDeadFrozen(bool frozen)
        {
            if (!frozen)
            {
                if (_deadFrozen)
                {
                    for (int i = 0; i < _deathTransforms.Count; i++)
                    {
                        if (_deathTransforms[i] != null)
                        {
                            _deathTransforms[i].localScale = _deathStartScale[i];
                        }
                    }
                }
                _deadFrozen = false;
                _deathVestmentsFrozen = false;
                _deathTransforms.Clear();
                _deathStartPos.Clear();
                _deathStartRot.Clear();
                _deathStartScale.Clear();
                _deathRestPos.Clear();
                _deathRestRot.Clear();
                SetVisualsVisible(true);
                return;
            }

            _deadFrozen = true;
            _deathVestmentsFrozen = false;
            aimTarget = null;
            slitherSpeed = 0f;
            _biteAnimActive = false;
            _tailSweepActive = false;
            _pillarCrawlActive = false;
            _isTransitioning = false;

            CaptureDeathCollapseSnapshot();
        }

        public void StepDeadCollapseAndMelt(float deadElapsed, float meltStartTime = 5.0f, float meltDuration = 2.4f)
        {
            if (!_deadFrozen || _deathTransforms.Count == 0)
            {
                return;
            }

            const float collapseDuration = 0.45f;
            if (deadElapsed <= collapseDuration)
            {
                float t = Mathf.Clamp01(deadElapsed / collapseDuration);
                float ease = 1f - (1f - t) * (1f - t);
                for (int i = 0; i < _deathTransforms.Count; i++)
                {
                    Transform seg = _deathTransforms[i];
                    if (seg == null) continue;
                    seg.position = Vector3.Lerp(_deathStartPos[i], _deathRestPos[i], ease);
                    seg.rotation = Quaternion.Slerp(_deathStartRot[i], _deathRestRot[i], ease);
                }
                UpdateHangingVestments();
                return;
            }

            if (!_deathVestmentsFrozen)
            {
                for (int i = 0; i < _deathTransforms.Count; i++)
                {
                    Transform seg = _deathTransforms[i];
                    if (seg == null) continue;
                    seg.position = _deathRestPos[i];
                    seg.rotation = _deathRestRot[i];
                }
                UpdateHangingVestments();
                _deathVestmentsFrozen = true;
            }

            if (deadElapsed < meltStartTime)
            {
                return;
            }

            float melt01 = Mathf.Clamp01((deadElapsed - meltStartTime) / Mathf.Max(0.1f, meltDuration));
            float meltEase = melt01 * melt01 * (3f - 2f * melt01);

            for (int i = 0; i < _deathTransforms.Count; i++)
            {
                Transform seg = _deathTransforms[i];
                if (seg == null) continue;

                Vector3 restPos = _deathRestPos[i];
                Vector3 meltedPos = new Vector3(restPos.x, _deathFloorY + 0.015f, restPos.z);
                seg.position = Vector3.Lerp(restPos, meltedPos, meltEase);

                Vector3 baseScale = _deathStartScale[i];
                seg.localScale = new Vector3(
                    baseScale.x * (1f + 0.34f * meltEase),
                    baseScale.y * Mathf.Lerp(1f, 0.04f, meltEase),
                    baseScale.z * (1f + 0.34f * meltEase));
            }
        }

        public void SetVisualsVisible(bool visible)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = visible;
                }
            }
        }

        public void PopulateDeathAcidPositions(List<Vector3> outPositions)
        {
            if (outPositions == null) return;
            outPositions.Clear();

            float floorY = arenaBounds != null ? arenaBounds.floorY : transform.position.y;
            if (headSegment != null)
            {
                Vector3 p = headSegment.position;
                outPositions.Add(new Vector3(p.x, floorY, p.z));
            }

            CollectOrderedBodySegmentsTailToHead();
            if (_orderedBodyTailToHead.Count > 0)
            {
                int midIdx = _orderedBodyTailToHead.Count / 2;
                Transform mid = _orderedBodyTailToHead[midIdx];
                if (mid != null)
                {
                    outPositions.Add(new Vector3(mid.position.x, floorY, mid.position.z));
                }

                Transform rear = _orderedBodyTailToHead[Mathf.Max(0, _orderedBodyTailToHead.Count / 5)];
                if (rear != null && rear != mid)
                {
                    outPositions.Add(new Vector3(rear.position.x, floorY, rear.position.z));
                }
            }

            if (outPositions.Count == 0)
            {
                outPositions.Add(new Vector3(transform.position.x, floorY, transform.position.z));
            }
        }

        private void CaptureDeathCollapseSnapshot()
        {
            CollectOrderedSpineTailToHead();
            _deathTransforms.Clear();
            _deathStartPos.Clear();
            _deathStartRot.Clear();
            _deathStartScale.Clear();
            _deathRestPos.Clear();
            _deathRestRot.Clear();

            _deathFloorY = arenaBounds != null ? arenaBounds.floorY : transform.position.y;
            float restSegY = _deathFloorY + Mathf.Max(0.18f, segmentRadius * 0.48f);

            for (int i = 0; i < _orderedSpineAll.Count; i++)
            {
                Transform seg = _orderedSpineAll[i];
                if (seg == null) continue;

                _deathTransforms.Add(seg);
                _deathStartPos.Add(seg.position);
                _deathStartRot.Add(seg.rotation);
                _deathStartScale.Add(seg.localScale);

                Vector3 restPos = new Vector3(seg.position.x, Mathf.Min(seg.position.y, restSegY), seg.position.z);
                _deathRestPos.Add(restPos);

                Vector3 fwdXZ = new Vector3(seg.forward.x, 0f, seg.forward.z);
                if (fwdXZ.sqrMagnitude < 1e-4f)
                {
                    fwdXZ = new Vector3(transform.forward.x, 0f, transform.forward.z);
                    if (fwdXZ.sqrMagnitude < 1e-4f) fwdXZ = Vector3.forward;
                }
                fwdXZ.Normalize();

                Quaternion flatRot = Quaternion.LookRotation(fwdXZ, Vector3.up);
                if (seg == headSegment)
                {
                    // Tilt dead head slightly nose-down onto the stone floor without tracking player
                    flatRot = flatRot * Quaternion.Euler(14f, 0f, 0f);
                }
                _deathRestRot.Add(flatRot);
            }
        }

        public void SetGroundCoilTightness(float tightness01)
        {
            _targetCoilTightness = Mathf.Clamp01(tightness01);
        }

        public void TriggerBiteAnimation(float windupDur = 0.44f, float strikeDur = 0.26f, float recoverDur = 0.34f)
        {
            _biteWindupDur = Mathf.Max(0.1f, windupDur);
            _biteStrikeDur = Mathf.Max(0.1f, strikeDur);
            _biteRecoverDur = Mathf.Max(0.1f, recoverDur);
            _biteElapsed = 0f;
            _biteAnimActive = true;
        }

        public void TriggerTailSweepAnimation(float swingSideSign = 1f, float windupDur = 0.48f, float swingDur = 0.30f, float recoverDur = 0.42f)
        {
            _tailSweepSideSign = swingSideSign >= 0f ? 1f : -1f;
            _tailSweepWindupDur = Mathf.Max(0.1f, windupDur);
            _tailSweepSwingDur = Mathf.Max(0.1f, swingDur);
            _tailSweepRecoverDur = Mathf.Max(0.1f, recoverDur);
            _tailSweepElapsed = 0f;
            _tailSweepActive = true;
        }

        public void SetStateConstrict(Vector3 centerWorldPos, float ringRadius = 1.75f)
        {
            _pillarCrawlActive = false;
            _biteAnimActive = false;
            _tailSweepActive = false;
            _constrictCenterWorld = centerWorldPos;
            _constrictRadius = Mathf.Max(1.05f, ringRadius);

            BeginTransition();
            transitionDuration = 0.42f;
            poseState = BossPoseState.ConstrictTarget;
            _lastObservedPoseState = BossPoseState.ConstrictTarget;
        }

        public void UpdateConstrictPose(Vector3 centerWorldPos, float ringRadius)
        {
            _constrictCenterWorld = centerWorldPos;
            _constrictRadius = Mathf.Max(1.05f, ringRadius);
        }

        public Vector3 GetTailWorldPosition()
        {
            if (tailSegment != null) return tailSegment.position;
            CollectOrderedBodySegmentsTailToHead();
            if (_orderedBodyTailToHead.Count > 0) return _orderedBodyTailToHead[0].position;
            return transform.position - transform.forward * 6f;
        }

        public void SetPillarLeapPrep(float prep01)
        {
            _pillarLeapPrepOffset = Mathf.Clamp01(prep01);
        }

        public void TriggerPillarLeap(Vector3 landingWorldPos, float leapDuration = 0.78f, float arcHeight = 5.4f)
        {
            _pillarLeapPrepOffset = 0f;
            poseState = BossPoseState.GroundSlither;
            _lastObservedPoseState = BossPoseState.GroundSlither;

            if (Application.isPlaying)
            {
                _isTransitioning = false;
                BuildPillarLeapPathAndBegin(landingWorldPos, leapDuration, arcHeight, -2);
            }
            else
            {
                SetPoseImmediateGround();
            }
        }

        public void TriggerPillarToPillarLeap(Vector3 targetPillarWorldPos, float radius = 0.98f, float flightDuration = 1.08f, float arcHeight = 6.2f)
        {
            _pillarLeapPrepOffset = 0f;
            poseState = BossPoseState.PillarCoiled;
            _lastObservedPoseState = BossPoseState.PillarCoiled;
            pillarRadius = radius;
            _activePillarWorldPos = targetPillarWorldPos;
            _activePillarRadius = radius;
            _hasRuntimePillarOverride = true;
            _biteAnimActive = false;
            _tailSweepActive = false;

            if (pillarOriginIsWorld) pillarLocalOrigin = targetPillarWorldPos;
            else pillarLocalOrigin = transform.InverseTransformPoint(targetPillarWorldPos);
            _lastInspectorPillarOrigin = pillarLocalOrigin;
            _lastInspectorOriginIsWorld = pillarOriginIsWorld;

            _lockedPillarOutwardXZ = ResolvePillarOutwardXZ(targetPillarWorldPos);
            _hasLockedPillarOutward = true;

            if (Application.isPlaying)
            {
                _isTransitioning = false;
                BuildPillarToPillarPathAndBegin(targetPillarWorldPos, radius, _lockedPillarOutwardXZ, flightDuration, arcHeight);
            }
            else
            {
                BeginTransition();
            }
        }

        public void TriggerStaggerFall(Vector3 fallGroundPos, float fallDuration = 0.56f)
        {
            _pillarLeapPrepOffset = 0f;
            _biteAnimActive = false;
            _tailSweepActive = false;
            poseState = BossPoseState.GroundSlither;
            _lastObservedPoseState = BossPoseState.GroundSlither;

            if (Application.isPlaying)
            {
                _isTransitioning = false;
                BuildPillarLeapPathAndBegin(fallGroundPos, fallDuration, 1.8f, -3);
            }
            else
            {
                SetPoseImmediateGround();
            }
        }

        /// <summary>
        /// Switches runtime state to PillarCoiled around pillarWorldPos (climbing continuously along helix in Play Mode).
        /// </summary>
        public void SetStatePillar(Vector3 pillarWorldPos, float radius = 1.18f)
        {
            poseState = BossPoseState.PillarCoiled;
            _lastObservedPoseState = BossPoseState.PillarCoiled;
            pillarRadius = radius;
            _activePillarWorldPos = pillarWorldPos;
            _activePillarRadius = radius;
            _hasRuntimePillarOverride = true;
            _biteAnimActive = false;
            _tailSweepActive = false;

            if (pillarOriginIsWorld)
            {
                pillarLocalOrigin = pillarWorldPos;
            }
            else
            {
                pillarLocalOrigin = transform.InverseTransformPoint(pillarWorldPos);
            }

            _lastInspectorPillarOrigin = pillarLocalOrigin;
            _lastInspectorOriginIsWorld = pillarOriginIsWorld;

            _lockedPillarOutwardXZ = ResolvePillarOutwardXZ(pillarWorldPos);
            _hasLockedPillarOutward = true;

            if (Application.isPlaying)
            {
                _isTransitioning = false;
                BuildPillarClimbPathAndBegin(pillarWorldPos, radius, _lockedPillarOutwardXZ, 1.85f);
            }
            else
            {
                BeginTransition();
            }
        }

        /// <summary>
        /// Returns the world-space projectile spawn position from acidMouthAnchor (or safe fallback).
        /// </summary>
        public Vector3 GetAcidProjectileSpawnPos()
        {
            if (acidMouthAnchor != null)
            {
                return acidMouthAnchor.position;
            }

            if (headSegment != null)
            {
                return headSegment.position + headSegment.forward * (Mathf.Max(0.2f, segmentRadius) * 0.8f);
            }

            return transform.position + transform.forward + Vector3.up * headElevation;
        }

        /// <summary>
        /// Returns the normalized world-space launch direction from the acid mouth anchor toward targetPos.
        /// </summary>
        public Vector3 GetAcidProjectileDirection(Vector3 targetPos)
        {
            Vector3 spawnPos = GetAcidProjectileSpawnPos();
            Vector3 delta = targetPos - spawnPos;
            if (delta.sqrMagnitude > 1e-6f)
            {
                return delta.normalized;
            }

            if (acidMouthAnchor != null && acidMouthAnchor.forward.sqrMagnitude > 1e-6f)
            {
                return acidMouthAnchor.forward.normalized;
            }

            if (headSegment != null && headSegment.forward.sqrMagnitude > 1e-6f)
            {
                return headSegment.forward.normalized;
            }

            return transform.forward.sqrMagnitude > 1e-6f ? transform.forward.normalized : Vector3.forward;
        }

        /// <summary>
        /// Resolves the configured pillar base center in world space.
        /// </summary>
        public Vector3 GetPillarWorldCenter()
        {
            return pillarOriginIsWorld ? pillarLocalOrigin : transform.TransformPoint(pillarLocalOrigin);
        }

        private void SyncInspectorWatchers()
        {
            _lastObservedPoseState = poseState;
            _lastInspectorPillarOrigin = pillarLocalOrigin;
            _lastInspectorOriginIsWorld = pillarOriginIsWorld;
        }

        private void CollectOrderedBodySegmentsTailToHead()
        {
            _orderedBodyTailToHead.Clear();
            if (bodySegments == null || bodySegments.Count == 0)
            {
                return;
            }

            if (bodySegmentsOrderedHeadToTail)
            {
                for (int i = bodySegments.Count - 1; i >= 0; i--)
                {
                    if (bodySegments[i] != null)
                    {
                        _orderedBodyTailToHead.Add(bodySegments[i]);
                    }
                }
            }
            else
            {
                for (int i = 0; i < bodySegments.Count; i++)
                {
                    if (bodySegments[i] != null)
                    {
                        _orderedBodyTailToHead.Add(bodySegments[i]);
                    }
                }
            }
        }

        private void CollectOrderedSpineTailToHead()
        {
            CollectOrderedBodySegmentsTailToHead();
            _orderedSpineAll.Clear();

            if (tailSegment != null)
            {
                _orderedSpineAll.Add(tailSegment);
            }

            for (int i = 0; i < _orderedBodyTailToHead.Count; i++)
            {
                _orderedSpineAll.Add(_orderedBodyTailToHead[i]);
            }

            if (headSegment != null)
            {
                _orderedSpineAll.Add(headSegment);
            }
        }

        private void BeginTransition()
        {
            CollectOrderedSpineTailToHead();
            _transitionStartPos.Clear();
            _transitionStartRot.Clear();

            for (int i = 0; i < _orderedSpineAll.Count; i++)
            {
                Transform seg = _orderedSpineAll[i];
                _transitionStartPos.Add(seg.position);
                _transitionStartRot.Add(seg.rotation);
            }

            _transitionStartMouthRot = acidMouthAnchor != null ? acidMouthAnchor.rotation : Quaternion.identity;
            _transitionElapsed = 0f;
            _isTransitioning = _orderedSpineAll.Count > 0 && transitionDuration > 0.001f;
        }

        private void EvaluateGroundTargets(float timeOffset)
        {
            CollectOrderedBodySegmentsTailToHead();
            _targetBuffer.Clear();
            _hasTargetMouthRot = false;

            float spacing = Mathf.Max(0.15f, segmentSpacing);
            float floorY = Mathf.Max(0.05f, segmentRadius * 0.8f);
            float phase = timeOffset * slitherSpeed;

            int bodyCount = _orderedBodyTailToHead.Count;
            int rearingBodyCount = bodyCount == 0
                ? 0
                : (bodyCount <= 4 ? Mathf.Max(0, bodyCount - 1) : 4);
            int floorBodyCount = bodyCount - rearingBodyCount;

            float rampDist = Mathf.Max(0.6f, spacing * 2.5f);
            float FloorWaveX(float u)
            {
                float env = Mathf.SmoothStep(0.18f, 1.0f, u / rampDist);
                return groundWaveAmplitude * env * Mathf.Sin(u * groundWaveFrequency - phase);
            }

            Vector3 SampleFloorLocal(float u) => new Vector3(FloorWaveX(u), floorY, -u);

            int floorSegTotal = (tailSegment != null ? 1 : 0) + floorBodyCount;

            _floorPosBuffer.Clear();
            _floorTanBuffer.Clear();

            if (floorSegTotal > 0)
            {
                for (int i = 0; i < floorSegTotal; i++)
                {
                    _floorPosBuffer.Add(Vector3.zero);
                    _floorTanBuffer.Add(Vector3.forward);
                }

                float firstArcOffset = rearingBodyCount > 0 ? spacing : spacing * 0.5f;
                float u = 0f;
                float arc = 0f;
                Vector3 prevPt = SampleFloorLocal(0f);
                const float du = 0.025f;

                // Walk from front-most floor segment back to tailSegment (reverse fill so index 0 = tail).
                for (int k = 0; k < floorSegTotal; k++)
                {
                    float targetArc = firstArcOffset + k * spacing;
                    while (arc < targetArc)
                    {
                        u += du;
                        Vector3 nextPt = SampleFloorLocal(u);
                        arc += Vector3.Distance(prevPt, nextPt);
                        prevPt = nextPt;
                    }

                    Vector3 ahead = SampleFloorLocal(Mathf.Max(0f, u - 0.08f));
                    Vector3 behind = SampleFloorLocal(u + 0.08f);
                    Vector3 tan = ahead - behind;
                    if (tan.sqrMagnitude < 1e-6f) tan = Vector3.forward;

                    int writeIndex = floorSegTotal - 1 - k;
                    _floorPosBuffer[writeIndex] = prevPt;
                    _floorTanBuffer[writeIndex] = tan.normalized;
                }

                int cursor = 0;
                if (tailSegment != null)
                {
                    AddLocalSegmentTarget(tailSegment, _floorPosBuffer[cursor], LookTangent(_floorTanBuffer[cursor], Vector3.up));
                    cursor++;
                }

                for (int i = 0; i < floorBodyCount; i++)
                {
                    AddLocalSegmentTarget(_orderedBodyTailToHead[i], _floorPosBuffer[cursor], LookTangent(_floorTanBuffer[cursor], Vector3.up));
                    cursor++;
                }
            }

            // Compute rearing neck + head Bezier in local space.
            Vector3 neckBaseLocal = SampleFloorLocal(0f);
            Vector3 neckBaseTan = (SampleFloorLocal(0f) - SampleFloorLocal(0.1f));
            if (neckBaseTan.sqrMagnitude < 1e-6f) neckBaseTan = Vector3.forward;
            neckBaseTan.Normalize();

            Vector3 neckSteerDirXZ = Vector3.forward;
            Vector3 localAimPos = Vector3.zero;
            bool hasAim = aimTarget != null;

            if (hasAim)
            {
                localAimPos = transform.InverseTransformPoint(aimTarget.position);
                Vector3 toAimXZ = new Vector3(localAimPos.x - neckBaseLocal.x, 0f, localAimPos.z - neckBaseLocal.z);
                if (toAimXZ.sqrMagnitude > 0.001f)
                {
                    float signedYaw = Vector3.SignedAngle(Vector3.forward, toAimXZ.normalized, Vector3.up);
                    float clampedNeckYaw = Mathf.Clamp(signedYaw, -75f, 75f);
                    neckSteerDirXZ = Quaternion.Euler(0f, clampedNeckYaw, 0f) * Vector3.forward;
                }
            }

            float targetHeadY = Mathf.Max(floorY + 0.25f, headElevation) + Mathf.Sin(phase * 0.85f) * 0.06f;

            int rearingSteps = rearingBodyCount + 1;
            float rearingBudget = rearingSteps * spacing;
            float forwardReach = Mathf.Max(spacing * 1.05f, rearingBudget * 0.54f);

            Vector3 p0 = neckBaseLocal;
            Vector3 p3 = new Vector3(p0.x, targetHeadY, p0.z) + neckSteerDirXZ * forwardReach;
            float deltaY = Mathf.Max(0.1f, targetHeadY - floorY);
            Vector3 p1 = p0 + neckBaseTan * (rearingBudget * 0.36f) + Vector3.up * (deltaY * 0.14f);
            Vector3 p2 = p3 - neckSteerDirXZ * (rearingBudget * 0.34f) - Vector3.up * (deltaY * 0.12f);

            BuildBezierArcTable(p0, p1, p2, p3, 64);

            Vector3 transportUp = Vector3.up;
            Vector3 transportRight = Vector3.Cross(transportUp, neckBaseTan).sqrMagnitude > 1e-5f
                ? Vector3.Cross(transportUp, neckBaseTan).normalized
                : Vector3.right;

            for (int i = 0; i < rearingBodyCount; i++)
            {
                float frac = (float)(i + 1) / rearingSteps;
                SampleBezierAtArcFraction(frac, p0, p1, p2, p3, out Vector3 posLocal, out Vector3 tanLocal);
                ParallelTransportFrame(tanLocal, ref transportUp, ref transportRight, out Quaternion rotLocal);

                Transform seg = _orderedBodyTailToHead[floorBodyCount + i];
                AddLocalSegmentTarget(seg, posLocal, rotLocal);
            }

            Vector3 headPosLocal = p3;
            Vector3 headForwardLocal = neckSteerDirXZ;
            if (hasAim)
            {
                Vector3 toAim = localAimPos - headPosLocal;
                if (toAim.sqrMagnitude > 1e-5f)
                {
                    headForwardLocal = toAim.normalized;
                }
            }

            Quaternion headLocalRot = LookTangent(headForwardLocal, Vector3.up);
            if (headSegment != null)
            {
                AddLocalSegmentTarget(headSegment, headPosLocal, headLocalRot);
            }

            if (acidMouthAnchor != null)
            {
                Vector3 headWorldPos = transform.TransformPoint(headPosLocal);
                Quaternion headWorldRot = transform.rotation * headLocalRot;

                if (hasAim)
                {
                    Vector3 mouthWorldPos = acidMouthAnchor.position;
                    if (headSegment != null && acidMouthAnchor.IsChildOf(headSegment))
                    {
                        mouthWorldPos = headWorldPos + headWorldRot * Quaternion.Inverse(headSegment.rotation) * (acidMouthAnchor.position - headSegment.position);
                    }
                    Vector3 mouthDir = aimTarget.position - mouthWorldPos;
                    _targetMouthWorldRot = mouthDir.sqrMagnitude > 1e-6f
                        ? LookTangent(mouthDir.normalized, headWorldRot * Vector3.up)
                        : headWorldRot;
                }
                else
                {
                    _targetMouthWorldRot = headWorldRot;
                }

                _hasTargetMouthRot = true;
            }
        }

        private void SeedWorldTrailFromCurrentPositions()
        {
            CollectOrderedSpineTailToHead();
            _worldTrail.Clear();

            Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();

            float floorY = transform.position.y + Mathf.Max(0.05f, segmentRadius * 0.8f);
            Vector3 frontPt = new Vector3(transform.position.x, floorY, transform.position.z);

            // Se os segmentos já estão próximos do chão, usa o trajeto real cabeça -> cauda
            if (_orderedSpineAll.Count >= 4 && headSegment != null && Mathf.Abs(_orderedSpineAll[0].position.y - floorY) < 1.8f)
            {
                _worldTrail.Add(frontPt);
                for (int i = _orderedSpineAll.Count - 2; i >= 0; i--)
                {
                    Transform seg = _orderedSpineAll[i];
                    if (seg == null) continue;
                    Vector3 p = seg.position;
                    p.y = floorY;
                    if (arenaBounds != null)
                    {
                        Vector3 cl = arenaBounds.ClampPointToNave(p, Mathf.Max(0.45f, segmentRadius * 0.85f));
                        p.x = cl.x;
                        p.z = cl.z;
                    }
                    AddResampledTrailPoint(p, 0.14f);
                }
            }
            else
            {
                // Inicializa uma curva S orgânica atrás do root
                _worldTrail.Add(frontPt);
                Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
                float totalLen = Mathf.Max(14f, (_orderedSpineAll.Count + 4) * Mathf.Max(0.25f, segmentSpacing));
                for (float d = 0.14f; d <= totalLen; d += 0.14f)
                {
                    float env = Mathf.SmoothStep(0.15f, 1f, d / 2.2f);
                    float side = env * groundWaveAmplitude * 0.72f * Mathf.Sin(d * groundWaveFrequency);
                    Vector3 pt = frontPt - fwd * d + right * side;
                    if (arenaBounds != null)
                    {
                        Vector3 cl = arenaBounds.ClampPointToNave(pt, Mathf.Max(0.45f, segmentRadius * 0.85f));
                        pt.x = cl.x;
                        pt.z = cl.z;
                    }
                    _worldTrail.Add(pt);
                }
            }

            // Garante extensão traseira suficiente para nunca faltar rastro na cauda
            Vector3 last = _worldTrail[_worldTrail.Count - 1];
            Vector3 tailExtendDir = _worldTrail.Count >= 2
                ? (_worldTrail[_worldTrail.Count - 1] - _worldTrail[_worldTrail.Count - 2])
                : -fwd;
            tailExtendDir.y = 0f;
            tailExtendDir = tailExtendDir.sqrMagnitude > 1e-4f ? tailExtendDir.normalized : -fwd;
            while (_worldTrail.Count < 210)
            {
                last += tailExtendDir * 0.14f;
                if (arenaBounds != null)
                {
                    Vector3 cl = arenaBounds.ClampPointToNave(last, Mathf.Max(0.45f, segmentRadius * 0.85f));
                    last.x = cl.x;
                    last.z = cl.z;
                }
                _worldTrail.Add(last);
            }

            _lastTrailRootPos = transform.position;
            _worldTrailInitialized = true;
        }

        private void AddResampledTrailPoint(Vector3 targetPt, float step)
        {
            if (_worldTrail.Count == 0)
            {
                _worldTrail.Add(targetPt);
                return;
            }
            Vector3 prev = _worldTrail[_worldTrail.Count - 1];
            float dist = Vector3.Distance(prev, targetPt);
            if (dist < step * 0.35f) return;

            int sub = Mathf.Max(1, Mathf.CeilToInt(dist / step));
            for (int s = 1; s <= sub; s++)
            {
                _worldTrail.Add(Vector3.Lerp(prev, targetPt, (float)s / sub));
            }
        }

        private void SampleWorldTrailAtArc(float targetArc, out Vector3 pos, out Vector3 tan)
        {
            if (_worldTrail.Count == 0)
            {
                pos = transform.position;
                tan = transform.forward;
                return;
            }
            if (_worldTrail.Count == 1 || targetArc <= 0.0001f)
            {
                pos = _worldTrail[0];
                tan = _worldTrail.Count >= 2 ? (_worldTrail[0] - _worldTrail[1]).normalized : transform.forward;
                if (tan.sqrMagnitude < 1e-5f) tan = transform.forward;
                return;
            }

            float acc = 0f;
            for (int i = 0; i < _worldTrail.Count - 1; i++)
            {
                Vector3 a = _worldTrail[i];
                Vector3 b = _worldTrail[i + 1];
                float segLen = Vector3.Distance(a, b);
                if (acc + segLen >= targetArc && segLen > 1e-5f)
                {
                    float frac = (targetArc - acc) / segLen;
                    pos = Vector3.Lerp(a, b, frac);
                    tan = (a - b) / segLen;
                    return;
                }
                acc += segLen;
            }

            int last = _worldTrail.Count - 1;
            pos = _worldTrail[last];
            tan = (_worldTrail[last - 1] - _worldTrail[last]);
            tan = tan.sqrMagnitude > 1e-5f ? tan.normalized : transform.forward;
        }

        private void EvaluateGroundTargetsRuntime(float dt, float timeOffset)
        {
            CollectOrderedBodySegmentsTailToHead();
            if (!_worldTrailInitialized || _worldTrail.Count < 16)
            {
                SeedWorldTrailFromCurrentPositions();
            }

            _targetBuffer.Clear();
            _hasTargetMouthRot = false;

            float baseSpacing = Mathf.Max(0.18f, segmentSpacing);
            float spacing = baseSpacing * Mathf.Lerp(1.0f, 0.78f, _coilTightness);
            float floorY = transform.position.y + Mathf.Max(0.05f, segmentRadius * 0.8f);
            float phase = timeOffset * Mathf.Max(0.6f, slitherSpeed);

            Vector3 fwdXZ = new Vector3(transform.forward.x, 0f, transform.forward.z);
            fwdXZ = fwdXZ.sqrMagnitude > 1e-4f ? fwdXZ.normalized : Vector3.forward;
            Vector3 rightXZ = new Vector3(fwdXZ.z, 0f, -fwdXZ.x);

            float moveDist = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(_lastTrailRootPos.x, 0f, _lastTrailRootPos.z));
            _lastTrailRootPos = transform.position;

            float weaveAmp = (0.34f + _coilTightness * 0.55f) * Mathf.Clamp01(moveDist * 18f + 0.15f);
            Vector3 neckBaseWorld = new Vector3(transform.position.x, floorY, transform.position.z)
                                  + rightXZ * (Mathf.Sin(phase * 1.1f) * weaveAmp);
            if (arenaBounds != null)
            {
                Vector3 cl = arenaBounds.ClampPointToNave(neckBaseWorld, Mathf.Max(0.45f, segmentRadius * 0.85f));
                neckBaseWorld.x = cl.x;
                neckBaseWorld.z = cl.z;
            }

            float headStepDist = Vector3.Distance(neckBaseWorld, _worldTrail[0]);
            if (headStepDist >= 0.14f)
            {
                int inserts = Mathf.Clamp(Mathf.CeilToInt(headStepDist / 0.14f), 1, 12);
                Vector3 prevHead = _worldTrail[0];
                for (int s = 1; s <= inserts; s++)
                {
                    _worldTrail.Insert(0, Vector3.Lerp(prevHead, neckBaseWorld, (float)s / inserts));
                }
                while (_worldTrail.Count > 340)
                {
                    _worldTrail.RemoveAt(_worldTrail.Count - 1);
                }
            }
            else if (headStepDist > 0.002f)
            {
                _worldTrail[0] = Vector3.Lerp(_worldTrail[0], neckBaseWorld, Mathf.Clamp01(dt * 16f));
            }

            int bodyCount = _orderedBodyTailToHead.Count;
            int rearingBodyCount = bodyCount == 0
                ? 0
                : (bodyCount <= 4 ? Mathf.Max(0, bodyCount - 1) : 4);
            int floorBodyCount = bodyCount - rearingBodyCount;
            int floorSegTotal = (tailSegment != null ? 1 : 0) + floorBodyCount;

            _floorPosBuffer.Clear();
            _floorTanBuffer.Clear();

            if (floorSegTotal > 0)
            {
                for (int i = 0; i < floorSegTotal; i++)
                {
                    _floorPosBuffer.Add(neckBaseWorld);
                    _floorTanBuffer.Add(fwdXZ);
                }

                float firstArcOffset = rearingBodyCount > 0 ? spacing : spacing * 0.5f;
                // Preenche do segmento dianteiro no chão (índice floorSegTotal - 1) até a cauda (índice 0)
                for (int k = 0; k < floorSegTotal; k++)
                {
                    int writeIdx = floorSegTotal - 1 - k;
                    float targetArc = firstArcOffset + k * spacing;
                    SampleWorldTrailAtArc(targetArc, out Vector3 wPos, out Vector3 wTan);

                    // Enrolamento lateral progressivo ao longo do corpo (acentua no modo defensivo / enrolado)
                    Vector3 lat = Vector3.Cross(Vector3.up, wTan).normalized;
                    float env = Mathf.SmoothStep(0f, 1f, k / 3.2f);
                    float extraWave = env * (_coilTightness * 0.92f + 0.18f) * Mathf.Sin(k * 0.58f - phase * 1.15f);
                    wPos += lat * extraWave;
                    wPos.y = floorY;

                    if (arenaBounds != null)
                    {
                        Vector3 cl = arenaBounds.ClampPointToNave(wPos, Mathf.Max(0.45f, segmentRadius * 0.85f));
                        wPos.x = cl.x;
                        wPos.z = cl.z;
                    }

                    _floorPosBuffer[writeIdx] = wPos;
                    _floorTanBuffer[writeIdx] = wTan;
                }

                // Overlay de Rabada (TailSweep): chicoteia os últimos segmentos formando um arco orgânico
                if (_tailSweepActive && floorSegTotal >= 4)
                {
                    int sweepCount = Mathf.Min(10, floorSegTotal - 1);
                    Vector3 pivot = _floorPosBuffer[sweepCount];

                    float swingAngleDeg = 0f;
                    if (_tailSweepElapsed < _tailSweepWindupDur)
                    {
                        float t = Mathf.SmoothStep(0f, 1f, _tailSweepElapsed / _tailSweepWindupDur);
                        swingAngleDeg = Mathf.Lerp(0f, -68f * _tailSweepSideSign, t);
                    }
                    else if (_tailSweepElapsed < _tailSweepWindupDur + _tailSweepSwingDur)
                    {
                        float t = (_tailSweepElapsed - _tailSweepWindupDur) / _tailSweepSwingDur;
                        float fast = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 2.4f);
                        swingAngleDeg = Mathf.Lerp(-68f * _tailSweepSideSign, 124f * _tailSweepSideSign, fast);
                    }
                    else
                    {
                        float t = Mathf.SmoothStep(0f, 1f,
                            (_tailSweepElapsed - _tailSweepWindupDur - _tailSweepSwingDur) / Mathf.Max(0.05f, _tailSweepRecoverDur));
                        swingAngleDeg = Mathf.Lerp(124f * _tailSweepSideSign, 0f, t);
                    }

                    for (int idx = 0; idx < sweepCount; idx++)
                    {
                        float whipWeight = 1f - (float)idx / sweepCount;
                        float segAngle = swingAngleDeg * (0.28f + 0.72f * whipWeight);
                        Quaternion rot = Quaternion.Euler(0f, segAngle, 0f);

                        Vector3 rel = _floorPosBuffer[idx] - pivot;
                        rel.y = 0f;
                        Vector3 swept = pivot + rot * rel;
                        swept.y = floorY + whipWeight * 0.18f;
                        if (arenaBounds != null)
                        {
                            Vector3 cl = arenaBounds.ClampPointToNave(swept, Mathf.Max(0.45f, segmentRadius * 0.85f));
                            swept.x = cl.x;
                            swept.z = cl.z;
                        }
                        _floorPosBuffer[idx] = swept;
                    }
                }

                // Recalcula tangentes suaves diretamente da posição de cada anel para o próximo à frente
                for (int i = 0; i < floorSegTotal; i++)
                {
                    Vector3 ahead = (i + 1 < floorSegTotal) ? _floorPosBuffer[i + 1] : neckBaseWorld;
                    Vector3 dir = ahead - _floorPosBuffer[i];
                    if (dir.sqrMagnitude > 1e-5f)
                    {
                        _floorTanBuffer[i] = dir.normalized;
                    }
                }

                int cursor = 0;
                if (tailSegment != null)
                {
                    AddWorldSegmentTarget(tailSegment, _floorPosBuffer[cursor], LookTangent(_floorTanBuffer[cursor], Vector3.up));
                    cursor++;
                }

                for (int i = 0; i < floorBodyCount; i++)
                {
                    AddWorldSegmentTarget(_orderedBodyTailToHead[i], _floorPosBuffer[cursor], LookTangent(_floorTanBuffer[cursor], Vector3.up));
                    cursor++;
                }
            }

            // Pescoço e Cabeça em world space + animação de Bote de Mordida
            float reachMult = 1f;
            float elevOffset = 0f;
            if (_biteAnimActive)
            {
                if (_biteElapsed < _biteWindupDur)
                {
                    float t = Mathf.SmoothStep(0f, 1f, _biteElapsed / _biteWindupDur);
                    reachMult = Mathf.Lerp(1f, 0.56f, t);
                    elevOffset = Mathf.Lerp(0f, 0.92f, t);
                }
                else if (_biteElapsed < _biteWindupDur + _biteStrikeDur)
                {
                    float t = (_biteElapsed - _biteWindupDur) / _biteStrikeDur;
                    float lunge = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                    reachMult = Mathf.Lerp(0.56f, 1.56f, lunge);
                    elevOffset = Mathf.Lerp(0.92f, -1.25f, lunge);
                }
                else
                {
                    float t = Mathf.SmoothStep(0f, 1f,
                        (_biteElapsed - _biteWindupDur - _biteStrikeDur) / Mathf.Max(0.05f, _biteRecoverDur));
                    reachMult = Mathf.Lerp(1.56f, 1f, t);
                    elevOffset = Mathf.Lerp(-1.25f, 0f, t);
                }
            }

            Vector3 neckTanXZ = _floorTanBuffer.Count > 0 ? _floorTanBuffer[_floorTanBuffer.Count - 1] : fwdXZ;
            neckTanXZ.y = 0f;
            neckTanXZ = neckTanXZ.sqrMagnitude > 1e-4f ? neckTanXZ.normalized : fwdXZ;

            Vector3 neckSteerDirXZ = fwdXZ;
            bool hasAim = aimTarget != null;
            if (hasAim)
            {
                Vector3 toAimXZ = aimTarget.position - neckBaseWorld;
                toAimXZ.y = 0f;
                if (toAimXZ.sqrMagnitude > 0.001f)
                {
                    float signedYaw = Vector3.SignedAngle(fwdXZ, toAimXZ.normalized, Vector3.up);
                    float clampedYaw = Mathf.Clamp(signedYaw, -70f, 70f);
                    neckSteerDirXZ = Quaternion.Euler(0f, clampedYaw, 0f) * fwdXZ;
                }
            }

            float targetHeadY = Mathf.Max(floorY + 0.35f, (transform.position.y + headElevation) + elevOffset)
                              + Mathf.Sin(phase * 0.85f) * 0.06f;

            int rearingSteps = rearingBodyCount + 1;
            float rearingBudget = rearingSteps * baseSpacing;
            float forwardReach = Mathf.Max(baseSpacing * 0.9f, rearingBudget * 0.52f * reachMult);

            Vector3 p0 = neckBaseWorld;
            Vector3 p3 = new Vector3(p0.x, targetHeadY, p0.z) + neckSteerDirXZ * forwardReach;
            if (arenaBounds != null)
            {
                Vector3 clHead = arenaBounds.ClampPointToNave(p3, Mathf.Max(0.55f, segmentRadius * 0.9f));
                p3.x = clHead.x;
                p3.z = clHead.z;
            }

            float deltaY = targetHeadY - floorY;
            Vector3 p1 = p0 + neckTanXZ * (rearingBudget * 0.34f) + Vector3.up * (Mathf.Max(0.1f, deltaY) * 0.16f);
            Vector3 p2 = p3 - neckSteerDirXZ * (rearingBudget * 0.34f) - Vector3.up * (deltaY * 0.10f);

            BuildBezierArcTable(p0, p1, p2, p3, 48);

            Vector3 transportUp = Vector3.up;
            Vector3 transportRight = Vector3.Cross(transportUp, neckTanXZ).sqrMagnitude > 1e-5f
                ? Vector3.Cross(transportUp, neckTanXZ).normalized
                : rightXZ;

            for (int i = 0; i < rearingBodyCount; i++)
            {
                float frac = (float)(i + 1) / rearingSteps;
                SampleBezierAtArcFraction(frac, p0, p1, p2, p3, out Vector3 posW, out Vector3 tanW);
                ParallelTransportFrame(tanW, ref transportUp, ref transportRight, out Quaternion rotW);

                Transform seg = _orderedBodyTailToHead[floorBodyCount + i];
                AddWorldSegmentTarget(seg, posW, rotW);
            }

            Vector3 headForwardW = neckSteerDirXZ;
            if (hasAim)
            {
                Vector3 toAim = aimTarget.position - p3;
                if (toAim.sqrMagnitude > 1e-5f) headForwardW = toAim.normalized;
            }

            Quaternion headWorldRot = LookTangent(headForwardW, Vector3.up);
            if (headSegment != null)
            {
                AddWorldSegmentTarget(headSegment, p3, headWorldRot);
            }

            if (acidMouthAnchor != null)
            {
                if (hasAim)
                {
                    Vector3 mouthDir = aimTarget.position - p3;
                    _targetMouthWorldRot = mouthDir.sqrMagnitude > 1e-6f
                        ? LookTangent(mouthDir.normalized, headWorldRot * Vector3.up)
                        : headWorldRot;
                }
                else
                {
                    _targetMouthWorldRot = headWorldRot;
                }
                _hasTargetMouthRot = true;
            }
        }

        private void EvaluateConstrictTargets(float timeOffset)
        {
            CollectOrderedBodySegmentsTailToHead();
            _targetBuffer.Clear();
            _hasTargetMouthRot = false;

            float baseFloorY = arenaBounds != null ? arenaBounds.floorY : _constrictCenterWorld.y;
            float ringR = Mathf.Max(1.05f, _constrictRadius);
            int bodyCount = _orderedBodyTailToHead.Count;
            int neckCount = Mathf.Clamp(Mathf.RoundToInt(bodyCount * 0.20f), 2, 4);
            int coilBodyCount = Mathf.Max(0, bodyCount - neckCount);
            int coilTotal = (tailSegment != null ? 1 : 0) + coilBodyCount;

            float turns = 2.05f;
            float totalRad = turns * Mathf.PI * 2f;
            float spinPhase = timeOffset * 1.15f;
            float startH = 0.36f;
            float topH = 2.30f;

            Vector3 CoilPt(float s)
            {
                float theta = spinPhase + s * totalRad;
                float y = baseFloorY + Mathf.Lerp(startH, topH, s);
                float r = ringR * Mathf.Lerp(1.12f, 0.96f, s);
                return new Vector3(
                    _constrictCenterWorld.x + r * Mathf.Cos(theta),
                    y,
                    _constrictCenterWorld.z + r * Mathf.Sin(theta));
            }

            Vector3 CoilTan(float s)
            {
                float theta = spinPhase + s * totalRad;
                Vector3 t = new Vector3(-Mathf.Sin(theta) * totalRad, topH - startH, Mathf.Cos(theta) * totalRad);
                return t.sqrMagnitude > 1e-5f ? t.normalized : Vector3.forward;
            }

            int cursor = 0;
            if (tailSegment != null)
            {
                Vector3 p = CoilPt(0f);
                Vector3 t = CoilTan(0f);
                AddWorldSegmentTarget(tailSegment, p, LookTangent(t, Vector3.up));
                cursor++;
            }

            for (int i = 0; i < coilBodyCount; i++)
            {
                float s = coilTotal <= 1 ? 1f : (float)cursor / (coilTotal - 1);
                Vector3 p = CoilPt(s);
                Vector3 t = CoilTan(s);
                AddWorldSegmentTarget(_orderedBodyTailToHead[i], p, LookTangent(t, Vector3.up));
                cursor++;
            }

            // Pescoço se eleva acima dos anéis e a cabeça paira ameaçadora diante do jogador
            Vector3 p0 = CoilPt(1f);
            Vector3 t0 = CoilTan(1f);
            Vector3 radialTop = new Vector3(p0.x - _constrictCenterWorld.x, 0f, p0.z - _constrictCenterWorld.z);
            radialTop = radialTop.sqrMagnitude > 1e-4f ? radialTop.normalized : Vector3.forward;

            Vector3 headPos = new Vector3(_constrictCenterWorld.x, baseFloorY + 2.85f, _constrictCenterWorld.z)
                            + radialTop * (ringR * 0.92f + 0.55f);
            Vector3 p1 = p0 + t0 * 1.1f + Vector3.up * 0.55f;
            Vector3 p2 = headPos + Vector3.up * 0.35f;

            BuildBezierArcTable(p0, p1, p2, headPos, 32);
            Vector3 tUp = Vector3.up;
            Vector3 tRight = Vector3.Cross(tUp, t0).normalized;
            if (tRight.sqrMagnitude < 1e-4f) tRight = Vector3.right;

            int neckSteps = neckCount + 1;
            for (int i = 0; i < neckCount; i++)
            {
                float frac = (float)(i + 1) / neckSteps;
                SampleBezierAtArcFraction(frac, p0, p1, p2, headPos, out Vector3 pw, out Vector3 tw);
                ParallelTransportFrame(tw, ref tUp, ref tRight, out Quaternion rw);
                AddWorldSegmentTarget(_orderedBodyTailToHead[coilBodyCount + i], pw, rw);
            }

            Vector3 biteTarget = _constrictCenterWorld + Vector3.up * 1.15f;
            Vector3 headFwd = (biteTarget - headPos).sqrMagnitude > 1e-4f ? (biteTarget - headPos).normalized : -radialTop;
            Quaternion headRot = LookTangent(headFwd, Vector3.up);
            if (headSegment != null)
            {
                AddWorldSegmentTarget(headSegment, headPos, headRot);
            }
            if (acidMouthAnchor != null)
            {
                _targetMouthWorldRot = headRot;
                _hasTargetMouthRot = true;
            }
        }

        private void BuildPillarClimbPathAndBegin(Vector3 pillarCenterWorld, float radius, Vector3 outwardXZ, float duration)
        {
            CollectOrderedSpineTailToHead();
            _unifiedCrawlPath.Clear();

            // 1. Pega as posições reais no chão (cauda -> cabeça) + ponto de entrada suave na base da coluna
            int segCount = _orderedSpineAll.Count;
            Vector3 baseSpiralStart = ComputePillarHelixPoint(pillarCenterWorld, radius, outwardXZ, 0f);
            Vector3 groundEntryPt = new Vector3(baseSpiralStart.x, pillarCenterWorld.y + Mathf.Max(0.25f, segmentRadius * 0.8f), baseSpiralStart.z);

            float groundArcLen = 0f;
            Vector3 prevAdded = Vector3.zero;
            bool hasPrev = false;

            for (int i = 0; i < segCount; i++)
            {
                if (_orderedSpineAll[i] == null) continue;
                Vector3 p = _orderedSpineAll[i].position;
                if (!hasPrev)
                {
                    _unifiedCrawlPath.Add(p);
                    prevAdded = p;
                    hasPrev = true;
                }
                else
                {
                    AppendInterpolatedPathPoints(prevAdded, p, 0.25f, ref groundArcLen);
                    prevAdded = p;
                }
            }

            // Conecta da cabeça atual até a base da espiral do pilar
            if (hasPrev)
            {
                AppendInterpolatedPathPoints(prevAdded, groundEntryPt, 0.25f, ref groundArcLen);
                AppendInterpolatedPathPoints(groundEntryPt, baseSpiralStart, 0.25f, ref groundArcLen);
                prevAdded = baseSpiralStart;
            }

            // 2. Anexa a espiral completa da coluna (s = 0..1) + pescoço/cabeça no topo
            float startClimbArc = groundArcLen;
            int helixSteps = 48;
            for (int i = 1; i <= helixSteps; i++)
            {
                float s = (float)i / helixSteps;
                Vector3 hp = ComputePillarHelixPoint(pillarCenterWorld, radius, outwardXZ, s);
                float dummy = 0f;
                AppendInterpolatedPathPoints(prevAdded, hp, 0.22f, ref dummy);
                prevAdded = hp;
            }

            Vector3 topHeadPos = ComputePillarHeadWorldPos(pillarCenterWorld, radius, outwardXZ);
            float neckDummy = 0f;
            AppendInterpolatedPathPoints(prevAdded, topHeadPos, 0.22f, ref neckDummy);

            RebuildUnifiedArcs();
            float totalArc = _unifiedCrawlArcs.Count > 0 ? _unifiedCrawlArcs[_unifiedCrawlArcs.Count - 1] : 0f;
            float bodySpan = Mathf.Min(groundArcLen, Mathf.Max(4f, (segCount - 1) * Mathf.Max(0.25f, segmentSpacing)));

            _crawlStartArc = bodySpan;
            _crawlEndArc = totalArc;
            _pillarCrawlDirection = 1;
            _pillarCrawlProgress = 0f;
            _pillarCrawlDuration = Mathf.Max(0.5f, duration);
            _pillarCrawlActive = _unifiedCrawlPath.Count >= 4 && totalArc > bodySpan + 0.5f;
        }

        private void BuildPillarDescendPathAndBegin(Vector3 pillarCenterWorld, float radius, Vector3 outwardXZ, float duration)
        {
            CollectOrderedSpineTailToHead();
            _unifiedCrawlPath.Clear();

            // 1. Espiral completa no pilar (base -> topo -> cabeça no topo)
            int helixSteps = 44;
            Vector3 firstPt = ComputePillarHelixPoint(pillarCenterWorld, radius, outwardXZ, 0f);
            _unifiedCrawlPath.Add(firstPt);
            Vector3 prev = firstPt;
            float dummy = 0f;

            for (int i = 1; i <= helixSteps; i++)
            {
                float s = (float)i / helixSteps;
                Vector3 hp = ComputePillarHelixPoint(pillarCenterWorld, radius, outwardXZ, s);
                AppendInterpolatedPathPoints(prev, hp, 0.24f, ref dummy);
                prev = hp;
            }

            Vector3 topHeadPos = ComputePillarHeadWorldPos(pillarCenterWorld, radius, outwardXZ);
            AppendInterpolatedPathPoints(prev, topHeadPos, 0.24f, ref dummy);
            RebuildUnifiedArcs();
            float pinnacleHeadArc = _unifiedCrawlArcs.Count > 0 ? _unifiedCrawlArcs[_unifiedCrawlArcs.Count - 1] : 0f;

            // 2. Rampa orgânica de descida do topo do pilar até o piso livre da catedral
            float floorY = (arenaBounds != null ? arenaBounds.floorY : pillarCenterWorld.y) + Mathf.Max(0.25f, segmentRadius * 0.8f);
            Vector3 safeOut = new Vector3(outwardXZ.x, 0f, outwardXZ.z);
            safeOut = safeOut.sqrMagnitude > 1e-4f ? safeOut.normalized : Vector3.right;
            Vector3 rightSide = new Vector3(safeOut.z, 0f, -safeOut.x);

            Vector3 touchDownPt = new Vector3(pillarCenterWorld.x, floorY, pillarCenterWorld.z) + safeOut * (radius + 3.4f);
            if (arenaBounds != null)
            {
                Vector3 cl = arenaBounds.ClampPointToNave(touchDownPt, 1.4f);
                touchDownPt.x = cl.x;
                touchDownPt.z = cl.z;
            }

            Vector3 midSlope = Vector3.Lerp(topHeadPos, touchDownPt, 0.5f) + safeOut * 1.1f;
            midSlope.y = Mathf.Lerp(topHeadPos.y, floorY, 0.55f);
            AppendInterpolatedPathPoints(topHeadPos, midSlope, 0.24f, ref dummy);
            AppendInterpolatedPathPoints(midSlope, touchDownPt, 0.24f, ref dummy);

            // Continua deslizando no chão em S-curve por mais ~16m dentro da nave
            Vector3 rollDir = arenaBounds != null
                ? (arenaBounds.arenaCenter - touchDownPt)
                : safeOut;
            rollDir.y = 0f;
            rollDir = rollDir.sqrMagnitude > 0.2f ? rollDir.normalized : safeOut;
            Vector3 rollLat = new Vector3(rollDir.z, 0f, -rollDir.x);

            Vector3 curFloor = touchDownPt;
            for (float d = 0.5f; d <= 16.0f; d += 0.5f)
            {
                Vector3 nextFloor = touchDownPt + rollDir * d + rollLat * (Mathf.Sin(d * 0.55f) * 1.25f);
                nextFloor.y = floorY;
                if (arenaBounds != null)
                {
                    Vector3 cl = arenaBounds.ClampPointToNave(nextFloor, 1.35f);
                    nextFloor.x = cl.x;
                    nextFloor.z = cl.z;
                }
                AppendInterpolatedPathPoints(curFloor, nextFloor, 0.24f, ref dummy);
                curFloor = nextFloor;
            }

            RebuildUnifiedArcs();
            float totalArc = _unifiedCrawlArcs.Count > 0 ? _unifiedCrawlArcs[_unifiedCrawlArcs.Count - 1] : pinnacleHeadArc;

            _crawlStartArc = pinnacleHeadArc;
            _crawlEndArc = totalArc;
            _pillarCrawlDirection = -1;
            _pillarCrawlProgress = 0f; // progrede 0 -> 1 ao longo da rampa de descida
            _pillarCrawlDuration = Mathf.Max(0.5f, duration);
            _pillarCrawlActive = _unifiedCrawlPath.Count >= 4 && totalArc > pinnacleHeadArc + 0.5f;
        }

        private void BuildPillarLeapPathAndBegin(Vector3 landingWorldPos, float duration, float arcHeight, int crawlCode = -2)
        {
            CollectOrderedSpineTailToHead();
            _unifiedCrawlPath.Clear();

            float floorY = arenaBounds != null ? arenaBounds.floorY : landingWorldPos.y;
            Vector3 clampedLanding = arenaBounds != null
                ? arenaBounds.ClampLandingPoint(landingWorldPos, 1.35f)
                : landingWorldPos;
            clampedLanding.y = floorY;

            // 1. Registra a pose atual exata (cauda -> cabeça) para o corpo desenrolar fluidamente do pilar pelo ar
            int segCount = _orderedSpineAll.Count;
            float preArcLen = 0f;
            Vector3 prevAdded = Vector3.zero;
            bool hasPrev = false;

            for (int i = 0; i < segCount; i++)
            {
                if (_orderedSpineAll[i] == null) continue;
                Vector3 p = _orderedSpineAll[i].position;
                if (!hasPrev)
                {
                    _unifiedCrawlPath.Add(p);
                    prevAdded = p;
                    hasPrev = true;
                }
                else
                {
                    AppendInterpolatedPathPoints(prevAdded, p, 0.22f, ref preArcLen);
                    prevAdded = p;
                }
            }

            Vector3 startHead = hasPrev ? prevAdded : transform.position + Vector3.up * 5f;
            Vector3 leapDeltaXZ = new Vector3(clampedLanding.x - startHead.x, 0f, clampedLanding.z - startHead.z);
            float leapFlatDist = leapDeltaXZ.magnitude;
            Vector3 leapDirXZ = leapFlatDist > 0.05f ? leapDeltaXZ / leapFlatDist : transform.forward;
            leapDirXZ.y = 0f;
            if (leapDirXZ.sqrMagnitude < 1e-4f) leapDirXZ = Vector3.forward;
            leapDirXZ.Normalize();

            // 2. Parabola de salto predador em Bezier cubica (do topo do pilar ate o chao)
            float peakHeight = Mathf.Max(startHead.y + 1.8f, floorY + Mathf.Max(4.2f, arcHeight));
            Vector3 p0 = startHead;
            Vector3 p1 = Vector3.Lerp(startHead, clampedLanding, 0.25f);
            p1.y = peakHeight;
            Vector3 p2 = Vector3.Lerp(startHead, clampedLanding, 0.72f);
            p2.y = Mathf.Lerp(peakHeight, floorY + 1.25f, 0.35f);
            Vector3 p3 = clampedLanding + Vector3.up * Mathf.Max(0.45f, segmentRadius * 0.85f);

            int airSteps = 34;
            Vector3 prevAir = p0;
            float leapDummy = 0f;
            for (int i = 1; i <= airSteps; i++)
            {
                float t = (float)i / airSteps;
                Vector3 airPt = EvalCubicBezier(p0, p1, p2, p3, t);
                if (arenaBounds != null)
                {
                    Vector3 cl = arenaBounds.ClampPointToNave(airPt, 0.9f);
                    airPt.x = cl.x;
                    airPt.z = cl.z;
                }
                AppendInterpolatedPathPoints(prevAir, airPt, 0.24f, ref leapDummy);
                prevAir = airPt;
            }

            // 3. Pequeno deslize pos-impacto no chao para o pescoço não dobrar para trás
            Vector3 slidePt = clampedLanding + leapDirXZ * 1.45f;
            slidePt.y = floorY + Mathf.Max(0.42f, segmentRadius * 0.8f);
            if (arenaBounds != null)
            {
                Vector3 cl = arenaBounds.ClampPointToNave(slidePt, 1.35f);
                slidePt.x = cl.x;
                slidePt.z = cl.z;
            }
            AppendInterpolatedPathPoints(prevAir, slidePt, 0.22f, ref leapDummy);

            RebuildUnifiedArcs();
            float totalArc = _unifiedCrawlArcs.Count > 0 ? _unifiedCrawlArcs[_unifiedCrawlArcs.Count - 1] : preArcLen;

            _crawlStartArc = preArcLen;
            _crawlEndArc = totalArc;
            _pillarCrawlDirection = crawlCode; // -2 = salto predatorio, -3 = queda por quebra/stagger
            _pillarCrawlProgress = 0f;
            _pillarCrawlDuration = Mathf.Max(0.35f, duration);
            _pillarCrawlActive = _unifiedCrawlPath.Count >= 4 && totalArc > preArcLen + 0.2f;
        }

        private void BuildPillarToPillarPathAndBegin(Vector3 nextPillarCenter, float radius, Vector3 outwardB, float duration, float arcHeight)
        {
            CollectOrderedSpineTailToHead();
            _unifiedCrawlPath.Clear();

            float floorY = arenaBounds != null ? arenaBounds.floorY : nextPillarCenter.y;
            int segCount = _orderedSpineAll.Count;
            float preArcLen = 0f;
            Vector3 prevAdded = Vector3.zero;
            bool hasPrev = false;

            for (int i = 0; i < segCount; i++)
            {
                if (_orderedSpineAll[i] == null) continue;
                Vector3 p = _orderedSpineAll[i].position;
                if (!hasPrev)
                {
                    _unifiedCrawlPath.Add(p);
                    prevAdded = p;
                    hasPrev = true;
                }
                else
                {
                    AppendInterpolatedPathPoints(prevAdded, p, 0.24f, ref preArcLen);
                    prevAdded = p;
                }
            }

            Vector3 startHead = hasPrev ? prevAdded : transform.position + Vector3.up * 6f;
            Vector3 nextBaseHelix = ComputePillarHelixPoint(nextPillarCenter, radius, outwardB, 0.10f);
            Vector3 nextHeadFinal = ComputePillarHeadWorldPos(nextPillarCenter, radius, outwardB);

            // Arco aereo cruzando a nave de um pilar ate o inicio da espiral superior do proximo pilar
            float peakY = Mathf.Max(startHead.y, nextBaseHelix.y) + Mathf.Max(2.2f, arcHeight);
            Vector3 midTarget = Vector3.Lerp(startHead, nextBaseHelix, 0.5f);
            midTarget.y = peakY;
            Vector3 p1 = Vector3.Lerp(startHead, midTarget, 0.6f);
            p1.y = peakY;
            Vector3 p2 = Vector3.Lerp(midTarget, nextBaseHelix, 0.45f);
            p2.y = peakY * 0.94f;

            int hopSteps = 26;
            Vector3 prevAir = startHead;
            float dummy = 0f;
            for (int i = 1; i <= hopSteps; i++)
            {
                float t = (float)i / hopSteps;
                Vector3 pt = EvalCubicBezier(startHead, p1, p2, nextBaseHelix, t);
                if (arenaBounds != null)
                {
                    Vector3 cl = arenaBounds.ClampPointToNave(pt, 0.85f);
                    pt.x = cl.x;
                    pt.z = cl.z;
                }
                pt.y = Mathf.Max(floorY + 2.2f, pt.y);
                AppendInterpolatedPathPoints(prevAir, pt, 0.26f, ref dummy);
                prevAir = pt;
            }

            // Sobe a espiral do segundo pilar ate a cabeca encaixar no topo
            int helixSteps = 32;
            for (int i = 1; i <= helixSteps; i++)
            {
                float s = Mathf.Lerp(0.10f, 1.0f, (float)i / helixSteps);
                Vector3 hp = ComputePillarHelixPoint(nextPillarCenter, radius, outwardB, s);
                AppendInterpolatedPathPoints(prevAir, hp, 0.24f, ref dummy);
                prevAir = hp;
            }

            AppendInterpolatedPathPoints(prevAir, nextHeadFinal, 0.24f, ref dummy);

            RebuildUnifiedArcs();
            float totalArc = _unifiedCrawlArcs.Count > 0 ? _unifiedCrawlArcs[_unifiedCrawlArcs.Count - 1] : preArcLen;

            _crawlStartArc = preArcLen;
            _crawlEndArc = totalArc;
            _pillarCrawlDirection = 2; // codigo de transicao aerea pilar -> pilar
            _pillarCrawlProgress = 0f;
            _pillarCrawlDuration = Mathf.Max(0.45f, duration);
            _pillarCrawlActive = _unifiedCrawlPath.Count >= 4 && totalArc > preArcLen + 0.5f;
        }

        private void UpdatePillarCrawl(float dt)
        {
            CollectOrderedSpineTailToHead();
            _targetBuffer.Clear();
            _hasTargetMouthRot = false;

            _pillarCrawlProgress = Mathf.Clamp01(_pillarCrawlProgress + dt / Mathf.Max(0.1f, _pillarCrawlDuration));
            float eased = (_pillarCrawlDirection == -2 || _pillarCrawlDirection == -3)
                ? Mathf.Pow(_pillarCrawlProgress, 1.18f) // queda / salto acelera com gravidade
                : Mathf.SmoothStep(0f, 1f, _pillarCrawlProgress);
            float headArc = Mathf.Lerp(_crawlStartArc, _crawlEndArc, eased);

            float spacing = Mathf.Max(0.20f, segmentSpacing);
            int n = _orderedSpineAll.Count;

            for (int i = n - 1; i >= 0; i--)
            {
                Transform seg = _orderedSpineAll[i];
                if (seg == null) continue;

                float distBack = (n - 1 - i) * spacing;
                float sampleArc = Mathf.Max(0f, headArc - distBack);
                SampleUnifiedCrawlAtArc(sampleArc, out Vector3 wPos, out Vector3 wTan);

                Vector3 rotUp = Vector3.up;
                if (i == n - 1 && aimTarget != null && _pillarCrawlDirection > 0 && _pillarCrawlProgress > 0.65f)
                {
                    Vector3 toAim = aimTarget.position - wPos;
                    if (toAim.sqrMagnitude > 1e-4f) wTan = Vector3.Slerp(wTan, toAim.normalized, (_pillarCrawlProgress - 0.65f) / 0.35f);
                }

                AddWorldSegmentTarget(seg, wPos, LookTangent(wTan, rotUp));
            }

            if (_pillarCrawlProgress >= 1f)
            {
                int finishedDir = _pillarCrawlDirection;
                _pillarCrawlActive = false;
                if (finishedDir < 0)
                {
                    // Ao completar a descida ou salto, sincroniza o root para onde o corpo pousou sem snap!
                    ApplyTargetBufferImmediate();
                    if (headSegment != null)
                    {
                        Vector3 neckAnchor = _orderedBodyTailToHead.Count > 3
                            ? _orderedBodyTailToHead[Mathf.Max(0, _orderedBodyTailToHead.Count - 4)].position
                            : headSegment.position;
                        float rootY = arenaBounds != null ? arenaBounds.floorY : transform.position.y;
                        transform.position = new Vector3(neckAnchor.x, rootY, neckAnchor.z);
                        Vector3 flatFwd = (headSegment.position - neckAnchor);
                        flatFwd.y = 0f;
                        if (flatFwd.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(flatFwd.normalized, Vector3.up);
                    }
                    _hasRuntimePillarOverride = false;
                    _hasLockedPillarOutward = false;
                    SeedWorldTrailFromCurrentPositions();
                }
            }
        }

        private void AppendInterpolatedPathPoints(Vector3 a, Vector3 b, float step, ref float arcCounter)
        {
            float dist = Vector3.Distance(a, b);
            if (dist < 1e-4f) return;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(0.05f, step)));
            for (int s = 1; s <= steps; s++)
            {
                Vector3 pt = Vector3.Lerp(a, b, (float)s / steps);
                _unifiedCrawlPath.Add(pt);
            }
            arcCounter += dist;
        }

        private void RebuildUnifiedArcs()
        {
            _unifiedCrawlArcs.Clear();
            float acc = 0f;
            if (_unifiedCrawlPath.Count == 0) return;
            _unifiedCrawlArcs.Add(0f);
            for (int i = 1; i < _unifiedCrawlPath.Count; i++)
            {
                acc += Vector3.Distance(_unifiedCrawlPath[i - 1], _unifiedCrawlPath[i]);
                _unifiedCrawlArcs.Add(acc);
            }
        }

        private void SampleUnifiedCrawlAtArc(float arc, out Vector3 pos, out Vector3 tan)
        {
            if (_unifiedCrawlPath.Count == 0)
            {
                pos = transform.position;
                tan = transform.forward;
                return;
            }
            if (_unifiedCrawlPath.Count == 1 || arc <= 0f)
            {
                pos = _unifiedCrawlPath[0];
                tan = _unifiedCrawlPath.Count > 1 ? (_unifiedCrawlPath[1] - _unifiedCrawlPath[0]).normalized : transform.forward;
                if (tan.sqrMagnitude < 1e-5f) tan = transform.forward;
                return;
            }

            for (int i = 0; i < _unifiedCrawlArcs.Count - 1; i++)
            {
                float a0 = _unifiedCrawlArcs[i];
                float a1 = _unifiedCrawlArcs[i + 1];
                if (a1 >= arc)
                {
                    float segLen = Mathf.Max(1e-5f, a1 - a0);
                    float t = Mathf.Clamp01((arc - a0) / segLen);
                    pos = Vector3.Lerp(_unifiedCrawlPath[i], _unifiedCrawlPath[i + 1], t);
                    tan = (_unifiedCrawlPath[i + 1] - _unifiedCrawlPath[i]).normalized;
                    if (tan.sqrMagnitude < 1e-5f) tan = Vector3.forward;
                    return;
                }
            }

            int last = _unifiedCrawlPath.Count - 1;
            pos = _unifiedCrawlPath[last];
            tan = (_unifiedCrawlPath[last] - _unifiedCrawlPath[last - 1]).normalized;
            if (tan.sqrMagnitude < 1e-5f) tan = transform.forward;
        }

        private Vector3 ComputePillarHelixPoint(Vector3 pillarCenterWorld, float radius, Vector3 outwardXZ, float s)
        {
            float coilOrbitRadius = Mathf.Max(0f, radius) + 0.72f;
            float totalAngleRad = coilTurns * Mathf.PI * 2f;
            float phi = Mathf.Atan2(outwardXZ.z, outwardXZ.x);
            float angleLead = (coilTurns >= 0f ? -0.35f : 0.35f) * Mathf.PI;
            float thetaStart = (phi + angleLead) - totalAngleRad;

            float theta = thetaStart + s * totalAngleRad;
            float y = pillarCenterWorld.y + Mathf.Lerp(coilStartHeight, coilTopHeight, s);
            return new Vector3(
                pillarCenterWorld.x + coilOrbitRadius * Mathf.Cos(theta),
                y,
                pillarCenterWorld.z + coilOrbitRadius * Mathf.Sin(theta));
        }

        private Vector3 ComputePillarHeadWorldPos(Vector3 pillarCenterWorld, float radius, Vector3 outwardXZ)
        {
            float coilOrbitRadius = Mathf.Max(0f, radius) + 0.72f;
            return new Vector3(
                pillarCenterWorld.x,
                pillarCenterWorld.y + coilTopHeight - headHangDrop,
                pillarCenterWorld.z) + outwardXZ * (coilOrbitRadius + headHangOutward);
        }

        private void EvaluatePillarTargets(Vector3 pillarCenterWorld, float radius, Vector3 outwardXZ, float timeOffset)
        {
            CollectOrderedBodySegmentsTailToHead();
            _targetBuffer.Clear();
            _hasTargetMouthRot = false;

            float coilOrbitRadius = Mathf.Max(0f, radius) + 0.72f;
            float totalAngleRad = coilTurns * Mathf.PI * 2f;
            float phi = Mathf.Atan2(outwardXZ.z, outwardXZ.x);
            float angleLead = (coilTurns >= 0f ? -0.35f : 0.35f) * Mathf.PI;
            float thetaTop = phi + angleLead;
            float thetaStart = thetaTop - totalAngleRad;

            int bodyCount = _orderedBodyTailToHead.Count;
            int neckCount = bodyCount == 0
                ? 0
                : (bodyCount <= 1 ? 0 : (bodyCount <= 4 ? 1 : Mathf.Clamp(Mathf.RoundToInt(bodyCount * 0.22f), 2, 4)));
            int coilBodyCount = bodyCount - neckCount;
            int coilSegTotal = (tailSegment != null ? 1 : 0) + coilBodyCount;

            Vector3 HelixPoint(float s)
            {
                float theta = thetaStart + s * totalAngleRad;
                float y = pillarCenterWorld.y + Mathf.Lerp(coilStartHeight, coilTopHeight, s);
                return new Vector3(
                    pillarCenterWorld.x + coilOrbitRadius * Mathf.Cos(theta),
                    y,
                    pillarCenterWorld.z + coilOrbitRadius * Mathf.Sin(theta));
            }

            Vector3 HelixTangent(float s)
            {
                float theta = thetaStart + s * totalAngleRad;
                float dx = -coilOrbitRadius * totalAngleRad * Mathf.Sin(theta);
                float dz = coilOrbitRadius * totalAngleRad * Mathf.Cos(theta);
                float dy = coilTopHeight - coilStartHeight;
                Vector3 t = new Vector3(dx, dy, dz);
                return t.sqrMagnitude > 1e-6f ? t.normalized : outwardXZ;
            }

            Vector3 HelixUp(float s, Vector3 tangent)
            {
                float theta = thetaStart + s * totalAngleRad;
                Vector3 radialOut = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
                Vector3 upHint = Vector3.Slerp(Vector3.up, radialOut, 0.35f).normalized;
                Vector3 proj = Vector3.ProjectOnPlane(upHint, tangent);
                return proj.sqrMagnitude > 1e-5f ? proj.normalized : Vector3.up;
            }

            if (coilSegTotal > 0)
            {
                int cursor = 0;
                if (tailSegment != null)
                {
                    float s = coilSegTotal == 1 ? 1f : 0f;
                    Vector3 pos = HelixPoint(s);
                    Vector3 tan = HelixTangent(s);
                    AddWorldSegmentTarget(tailSegment, pos, LookTangent(tan, HelixUp(s, tan)));
                    cursor++;
                }

                for (int i = 0; i < coilBodyCount; i++)
                {
                    float s = coilSegTotal == 1 ? 1f : (float)cursor / (coilSegTotal - 1);
                    Vector3 pos = HelixPoint(s);
                    Vector3 tan = HelixTangent(s);
                    AddWorldSegmentTarget(_orderedBodyTailToHead[i], pos, LookTangent(tan, HelixUp(s, tan)));
                    cursor++;
                }
            }

            Vector3 p0 = HelixPoint(1f);
            Vector3 t0 = HelixTangent(1f);
            Vector3 up0 = HelixUp(1f, t0);

            float swayOffset = Mathf.Sin(timeOffset * 1.25f) * 0.06f;
            Vector3 lateralXZ = new Vector3(-outwardXZ.z, 0f, outwardXZ.x);

            Vector3 headWorldPos = new Vector3(
                pillarCenterWorld.x,
                pillarCenterWorld.y + coilTopHeight - headHangDrop + swayOffset * 0.6f + _pillarLeapPrepOffset * 1.15f,
                pillarCenterWorld.z)
                + outwardXZ * (coilOrbitRadius + headHangOutward - _pillarLeapPrepOffset * 0.85f)
                + lateralXZ * swayOffset;

            Vector3 lookTarget = ResolvePillarAimWorldPosition(pillarCenterWorld, outwardXZ, headWorldPos);
            Vector3 headToTarget = lookTarget - headWorldPos;
            Vector3 headForward = headToTarget.sqrMagnitude > 1e-6f ? headToTarget.normalized : outwardXZ;

            float bezierSpan = Vector3.Distance(p0, headWorldPos);
            Vector3 p1 = p0 + t0 * (bezierSpan * 0.42f);

            Vector3 approachDir = headWorldPos - p0;
            approachDir.y = Mathf.Min(approachDir.y, -0.15f * bezierSpan);
            if (Vector3.Dot(new Vector3(approachDir.x, 0f, approachDir.z), outwardXZ) < 0.2f * bezierSpan)
            {
                approachDir = outwardXZ * 0.65f + Vector3.down * 0.45f;
            }
            approachDir.Normalize();
            Vector3 p2 = headWorldPos - approachDir * (bezierSpan * 0.34f);

            BuildBezierArcTable(p0, p1, p2, headWorldPos, 48);

            Vector3 transportUp = up0;
            Vector3 transportRight = Vector3.Cross(transportUp, t0).sqrMagnitude > 1e-5f
                ? Vector3.Cross(transportUp, t0).normalized
                : Vector3.Cross(Vector3.up, t0).normalized;

            int neckSteps = neckCount + 1;
            for (int i = 0; i < neckCount; i++)
            {
                float frac = (float)(i + 1) / neckSteps;
                SampleBezierAtArcFraction(frac, p0, p1, p2, headWorldPos, out Vector3 posWorld, out Vector3 tanWorld);
                ParallelTransportFrame(tanWorld, ref transportUp, ref transportRight, out Quaternion rotWorld);

                Transform seg = _orderedBodyTailToHead[coilBodyCount + i];
                AddWorldSegmentTarget(seg, posWorld, rotWorld);
            }

            Quaternion headWorldRot = LookTangent(headForward, Vector3.up);
            if (headSegment != null)
            {
                AddWorldSegmentTarget(headSegment, headWorldPos, headWorldRot);
            }

            if (acidMouthAnchor != null)
            {
                Vector3 mouthWorldPos = acidMouthAnchor.position;
                if (headSegment != null && acidMouthAnchor.IsChildOf(headSegment))
                {
                    mouthWorldPos = headWorldPos + headWorldRot * Quaternion.Inverse(headSegment.rotation) * (acidMouthAnchor.position - headSegment.position);
                }
                Vector3 mouthDir = lookTarget - mouthWorldPos;
                _targetMouthWorldRot = mouthDir.sqrMagnitude > 1e-6f
                    ? LookTangent(mouthDir.normalized, headWorldRot * Vector3.up)
                    : headWorldRot;
                _hasTargetMouthRot = true;
            }
        }

        private void ApplyTargetBufferImmediate()
        {
            for (int i = 0; i < _targetBuffer.Count; i++)
            {
                SegmentTarget st = _targetBuffer[i];
                if (st.Transform != null)
                {
                    st.Transform.position = st.WorldPosition;
                    st.Transform.rotation = st.WorldRotation;
                }
            }

            if (_hasTargetMouthRot && acidMouthAnchor != null)
            {
                acidMouthAnchor.rotation = _targetMouthWorldRot;
            }
        }

        private void ApplyRuntimeTargets()
        {
            if (_isTransitioning)
            {
                _transitionElapsed += Time.deltaTime;
                float normalized = Mathf.Clamp01(_transitionElapsed / Mathf.Max(0.01f, transitionDuration));
                float alpha = Mathf.SmoothStep(0f, 1f, normalized);

                int count = Mathf.Min(_targetBuffer.Count, _transitionStartPos.Count);
                for (int i = 0; i < count; i++)
                {
                    SegmentTarget st = _targetBuffer[i];
                    if (st.Transform != null)
                    {
                        st.Transform.position = Vector3.Lerp(_transitionStartPos[i], st.WorldPosition, alpha);
                        st.Transform.rotation = Quaternion.Slerp(_transitionStartRot[i], st.WorldRotation, alpha);
                    }
                }

                for (int i = count; i < _targetBuffer.Count; i++)
                {
                    SegmentTarget st = _targetBuffer[i];
                    if (st.Transform != null)
                    {
                        st.Transform.position = st.WorldPosition;
                        st.Transform.rotation = st.WorldRotation;
                    }
                }

                if (_hasTargetMouthRot && acidMouthAnchor != null)
                {
                    acidMouthAnchor.rotation = Quaternion.Slerp(_transitionStartMouthRot, _targetMouthWorldRot, alpha);
                }

                if (normalized >= 1f)
                {
                    _isTransitioning = false;
                }

                return;
            }

            float followBlend = 1f - Mathf.Exp(-(_pillarCrawlActive ? 26f : 22f) * Time.deltaTime);
            for (int i = 0; i < _targetBuffer.Count; i++)
            {
                SegmentTarget st = _targetBuffer[i];
                if (st.Transform == null)
                {
                    continue;
                }

                st.Transform.position = Vector3.Lerp(st.Transform.position, st.WorldPosition, followBlend);
                st.Transform.rotation = Quaternion.Slerp(st.Transform.rotation, st.WorldRotation, followBlend);
            }

            if (_hasTargetMouthRot && acidMouthAnchor != null)
            {
                acidMouthAnchor.rotation = Quaternion.Slerp(acidMouthAnchor.rotation, _targetMouthWorldRot, followBlend);
            }
        }

        private void AddLocalSegmentTarget(Transform seg, Vector3 localPos, Quaternion localRot)
        {
            Vector3 worldPos = transform.TransformPoint(localPos);
            if (Application.isPlaying && arenaBounds != null && poseState == BossPoseState.GroundSlither)
            {
                Vector3 clamped = arenaBounds.ClampPointToNave(worldPos, Mathf.Max(0.45f, segmentRadius * 0.85f));
                worldPos.x = clamped.x;
                worldPos.z = clamped.z;
            }

            _targetBuffer.Add(new SegmentTarget
            {
                Transform = seg,
                WorldPosition = worldPos,
                WorldRotation = transform.rotation * localRot
            });
        }

        private void AddWorldSegmentTarget(Transform seg, Vector3 worldPos, Quaternion worldRot)
        {
            _targetBuffer.Add(new SegmentTarget
            {
                Transform = seg,
                WorldPosition = worldPos,
                WorldRotation = worldRot
            });
        }

        private Vector3 ResolvePillarOutwardXZ(Vector3 pillarCenterWorld)
        {
            if (aimTarget != null)
            {
                Vector3 toAim = new Vector3(aimTarget.position.x - pillarCenterWorld.x, 0f, aimTarget.position.z - pillarCenterWorld.z);
                if (toAim.sqrMagnitude > 0.001f)
                {
                    return toAim.normalized;
                }
            }

            Vector3 naveCenter = GetNaveCenterWorldFallback(pillarCenterWorld);
            Vector3 toNave = new Vector3(naveCenter.x - pillarCenterWorld.x, 0f, naveCenter.z - pillarCenterWorld.z);
            if (toNave.sqrMagnitude > 0.001f)
            {
                return toNave.normalized;
            }

            Vector3 rootFwdXZ = new Vector3(transform.forward.x, 0f, transform.forward.z);
            return rootFwdXZ.sqrMagnitude > 1e-4f ? rootFwdXZ.normalized : Vector3.forward;
        }

        private Vector3 ResolvePillarAimWorldPosition(Vector3 pillarCenterWorld, Vector3 outwardXZ, Vector3 headWorldPos)
        {
            if (aimTarget != null)
            {
                return aimTarget.position;
            }

            Vector3 naveCenter = GetNaveCenterWorldFallback(pillarCenterWorld);
            Vector3 toNaveXZ = new Vector3(naveCenter.x - pillarCenterWorld.x, 0f, naveCenter.z - pillarCenterWorld.z);
            if (toNaveXZ.sqrMagnitude > 0.25f)
            {
                return naveCenter;
            }

            return new Vector3(pillarCenterWorld.x, pillarCenterWorld.y + 1.0f, pillarCenterWorld.z)
                + outwardXZ * (Mathf.Max(0f, pillarRadius) + 0.72f + headHangOutward + 4.5f);
        }

        private Vector3 GetNaveCenterWorldFallback(Vector3 pillarCenterWorld)
        {
            if (naveCenterFallback != null)
            {
                return naveCenterFallback.position;
            }

            Vector3 bossOffsetXZ = new Vector3(transform.position.x - pillarCenterWorld.x, 0f, transform.position.z - pillarCenterWorld.z);
            if (bossOffsetXZ.sqrMagnitude > 0.25f)
            {
                return new Vector3(transform.position.x, pillarCenterWorld.y + 1.0f, transform.position.z);
            }

            if (!_naveLookupAttempted)
            {
                _naveLookupAttempted = true;
                GameObject naveObj = GameObject.Find("Nave_Center_Anchor");
                if (naveObj != null)
                {
                    _cachedNaveCenterAnchor = naveObj.transform;
                }
            }

            if (_cachedNaveCenterAnchor != null)
            {
                return _cachedNaveCenterAnchor.position;
            }

            Vector3 fwdXZ = new Vector3(transform.forward.x, 0f, transform.forward.z);
            if (fwdXZ.sqrMagnitude < 1e-4f) fwdXZ = Vector3.forward;
            return pillarCenterWorld + fwdXZ.normalized * 6.0f + Vector3.up;
        }

        private static Vector3 ResolveVestmentOutwardXZ(Transform vestment)
        {
            Transform parent = vestment.parent;
            if (parent != null)
            {
                Vector3 offset = vestment.position - parent.position;
                Vector3 offsetXZ = new Vector3(offset.x, 0f, offset.z);
                if (offsetXZ.sqrMagnitude > 0.0001f)
                {
                    return offsetXZ.normalized;
                }

                Vector3 parentUpXZ = new Vector3(parent.up.x, 0f, parent.up.z);
                if (parentUpXZ.sqrMagnitude > 0.001f)
                {
                    return parentUpXZ.normalized;
                }

                Vector3 vestFwdXZ = new Vector3(vestment.forward.x, 0f, vestment.forward.z);
                if (vestFwdXZ.sqrMagnitude > 0.001f)
                {
                    return vestFwdXZ.normalized;
                }

                Vector3 parentFwdXZ = new Vector3(parent.forward.x, 0f, parent.forward.z);
                if (parentFwdXZ.sqrMagnitude > 0.001f)
                {
                    return parentFwdXZ.normalized;
                }
            }
            else
            {
                Vector3 vestFwdXZ = new Vector3(vestment.forward.x, 0f, vestment.forward.z);
                if (vestFwdXZ.sqrMagnitude > 0.001f)
                {
                    return vestFwdXZ.normalized;
                }
            }

            return Vector3.forward;
        }

        private void BuildBezierArcTable(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int steps)
        {
            _curveArcLengths.Clear();

            float totalLen = 0f;
            Vector3 prev = p0;
            _curveArcLengths.Add(0f);

            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 pt = EvalCubicBezier(p0, p1, p2, p3, t);
                totalLen += Vector3.Distance(prev, pt);
                _curveArcLengths.Add(totalLen);
                prev = pt;
            }
        }

        private void SampleBezierAtArcFraction(float fraction, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, out Vector3 position, out Vector3 tangent)
        {
            float clamped = Mathf.Clamp01(fraction);
            float totalLen = _curveArcLengths.Count > 0 ? _curveArcLengths[_curveArcLengths.Count - 1] : 0f;
            if (totalLen < 1e-5f)
            {
                position = Vector3.Lerp(p0, p3, clamped);
                Vector3 dir = p3 - p0;
                tangent = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
                return;
            }

            float targetArc = clamped * totalLen;
            int stepCount = _curveArcLengths.Count - 1;
            int segIndex = 0;
            for (int i = 0; i < stepCount; i++)
            {
                if (_curveArcLengths[i + 1] >= targetArc)
                {
                    segIndex = i;
                    break;
                }
                segIndex = i;
            }

            float arcA = _curveArcLengths[segIndex];
            float arcB = _curveArcLengths[segIndex + 1];
            float subT = arcB > arcA + 1e-6f ? (targetArc - arcA) / (arcB - arcA) : 0f;
            float tCurve = (segIndex + subT) / stepCount;

            position = EvalCubicBezier(p0, p1, p2, p3, tCurve);
            Vector3 deriv = EvalCubicBezierDerivative(p0, p1, p2, p3, tCurve);
            tangent = deriv.sqrMagnitude > 1e-6f ? deriv.normalized : (p3 - p0).sqrMagnitude > 1e-6f ? (p3 - p0).normalized : Vector3.forward;
        }

        private static Vector3 EvalCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            float uu = u * u;
            float tt = t * t;
            return (uu * u) * p0
                 + (3f * uu * t) * p1
                 + (3f * u * tt) * p2
                 + (tt * t) * p3;
        }

        private static Vector3 EvalCubicBezierDerivative(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return (3f * u * u) * (p1 - p0)
                 + (6f * u * t) * (p2 - p1)
                 + (3f * t * t) * (p3 - p2);
        }

        private static void ParallelTransportFrame(Vector3 tangent, ref Vector3 transportUp, ref Vector3 transportRight, out Quaternion rotation)
        {
            Vector3 fwd = tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.forward;
            Vector3 rightCandidate = Vector3.Cross(transportUp, fwd);
            if (rightCandidate.sqrMagnitude > 1e-5f)
            {
                transportRight = rightCandidate.normalized;
            }
            else if (Mathf.Abs(Vector3.Dot(transportRight, fwd)) > 0.99f)
            {
                Vector3 alt = Mathf.Abs(fwd.y) < 0.9f ? Vector3.up : Vector3.forward;
                transportRight = Vector3.Cross(alt, fwd).normalized;
            }

            transportUp = Vector3.Cross(fwd, transportRight).normalized;
            rotation = Quaternion.LookRotation(fwd, transportUp);
        }

        private static Quaternion LookTangent(Vector3 forward, Vector3 upHint)
        {
            Vector3 fwd = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            Vector3 up = upHint.sqrMagnitude > 1e-6f ? upHint.normalized : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(fwd, up)) > 0.995f)
            {
                up = Mathf.Abs(fwd.z) < 0.9f ? Vector3.forward : Vector3.right;
            }

            return Quaternion.LookRotation(fwd, up);
        }

        private void MarkDirtyInEditor()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
        }
    }
}
