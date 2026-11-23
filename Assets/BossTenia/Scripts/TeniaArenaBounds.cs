using System.Collections.Generic;
using UnityEngine;

namespace BossTenia
{
    public class TeniaArenaBounds : MonoBehaviour
    {
        [Header("Alinhamento Automatico na Catedral")]
        [Tooltip("Arraste o objeto Stomach_Boss_Cathedral aqui. Se vazio e autoAlignToCathedral estiver ativo, encontra automaticamente na cena.")]
        public Transform cathedralAnchor;
        public bool autoAlignToCathedral = true;

        [Header("Retangulo Verde: Nave Central (Boss Nao Pode Sair)")]
        public Vector3 arenaCenter = new Vector3(100.40f, 2.20f, 167.60f);
        public float floorY = 2.20f;
        public float naveMinX = 92.80f;
        public float naveMaxX = 108.00f;
        public float naveMinZ = 150.20f;
        public float naveMaxZ = 184.80f;

        [Header("Retangulo Laranja: Chao Total da Arena (Onde o Boss Joga Acido)")]
        public float acidMinX = 88.60f;
        public float acidMaxX = 112.20f;
        public float acidMinZ = 148.60f;
        public float acidMaxZ = 185.60f;

        [Header("Combat Entry Trigger")]
        public float portalTriggerZ = 153.20f;
        public Vector2 portalTriggerXRange = new Vector2(93.5f, 107.3f);

        [Header("Cathedral Pillars")]
        public float pillarRadius = 0.98f;
        public List<Vector3> pillarCentersWorld = new List<Vector3>
        {
            new Vector3(90.90f, 2.20f, 154.60f),
            new Vector3(90.90f, 2.20f, 162.60f),
            new Vector3(90.90f, 2.20f, 170.60f),
            new Vector3(90.90f, 2.20f, 178.60f),
            new Vector3(109.90f, 2.20f, 154.60f),
            new Vector3(109.90f, 2.20f, 162.60f),
            new Vector3(109.90f, 2.20f, 170.60f),
            new Vector3(109.90f, 2.20f, 178.60f)
        };

        [Header("Barreiras da Arena")]
        public GameObject portalBarrierObject;
        [Tooltip("Portao dramatico opcional (Stomach_Abyss). Se presente, substitui a barreira verde placeholder.")]
        public StomachAbyss.Cathedral.CathedralGateDramaticTrigger dramaticGate;
        public bool blockAltarStairs = true;
        public GameObject altarBarrierObject;

        // Retangulo verde (movimento do boss entre as fileiras de pilares)
        private const float LocalNaveMinX = -7.60f;
        private const float LocalNaveMaxX = 7.60f;
        private const float LocalNaveMinZ = 4.60f;
        private const float LocalNaveMaxZ = 39.20f;

        // Retangulo laranja (chao solido total + corredores laterais, parando antes dos lagos de acido em +-12.8 e antes da escada do altar em 41.5)
        private const float LocalAcidMinX = -11.80f;
        private const float LocalAcidMaxX = 11.80f;
        private const float LocalAcidMinZ = 3.00f;
        private const float LocalAcidMaxZ = 40.00f;

        private const float LocalPortalTriggerZ = 7.60f;
        private const float LocalPortalTriggerMinX = -6.90f;
        private const float LocalPortalTriggerMaxX = 6.90f;
        private static readonly Vector3 LocalArenaCenter = new Vector3(0f, 0f, 22.00f);
        private static readonly Vector3 LocalDormantSpawn = new Vector3(0f, 0f, 27.90f);
        private static readonly Vector3 LocalPortalBarrierPos = new Vector3(0f, 3.20f, 1.60f);
        private static readonly Vector3 LocalAltarBarrierPos = new Vector3(0f, 3.20f, 41.00f);

        private static readonly Vector3[] CanonicalLocalPillars =
        {
            new Vector3(-9.50f, 0f, 9.00f),
            new Vector3(-9.50f, 0f, 17.00f),
            new Vector3(-9.50f, 0f, 25.00f),
            new Vector3(-9.50f, 0f, 33.00f),
            new Vector3(9.50f, 0f, 9.00f),
            new Vector3(9.50f, 0f, 17.00f),
            new Vector3(9.50f, 0f, 25.00f),
            new Vector3(9.50f, 0f, 33.00f)
        };

        private Material _barrierMaterial;
        private bool _spawnedBarrierRuntime;
        private readonly RaycastHit[] _floorRayHits = new RaycastHit[16];

        private void Awake()
        {
            SyncWithCathedralAnchorIfNeeded();
            ResolveDramaticGate();
            if (portalBarrierObject != null)
            {
                portalBarrierObject.SetActive(false);
            }
            if (blockAltarStairs)
            {
                EnsureAltarStairBarrier();
            }
        }

        private void OnValidate()
        {
            SyncWithCathedralAnchorIfNeeded();
        }

        private void OnDestroy()
        {
            if (_spawnedBarrierRuntime && portalBarrierObject != null)
            {
                Destroy(portalBarrierObject);
                portalBarrierObject = null;
            }
        }

        private void Update()
        {
            if (portalBarrierObject != null && portalBarrierObject.activeSelf && _barrierMaterial != null)
            {
                float pulse = 0.34f + 0.14f * Mathf.Sin(Time.time * 4.5f);
                Color col = new Color(0.30f, 1.0f, 0.20f, pulse);
                if (_barrierMaterial.HasProperty("_BaseColor")) _barrierMaterial.SetColor("_BaseColor", col);
                if (_barrierMaterial.HasProperty("_Color")) _barrierMaterial.SetColor("_Color", col);
            }
        }

        public void SyncWithCathedralAnchorIfNeeded()
        {
            if (autoAlignToCathedral || cathedralAnchor != null)
            {
                SyncWithCathedralAnchor(autoAlignToCathedral);
            }
        }

        [ContextMenu("Alinhar Arena a Catedral")]
        public void ContextSyncWithCathedralAnchor()
        {
            SyncWithCathedralAnchor(true);
            if (blockAltarStairs)
            {
                EnsureAltarStairBarrier();
            }
        }

        public bool SyncWithCathedralAnchor(bool forceAutoFind = true)
        {
            if (cathedralAnchor == null && forceAutoFind)
            {
                cathedralAnchor = FindCathedralInScene();
            }

            if (cathedralAnchor == null)
            {
                return false;
            }

            Vector3 worldCenter = cathedralAnchor.TransformPoint(LocalArenaCenter);
            arenaCenter = worldCenter;
            floorY = worldCenter.y;

            ComputeTransformedRectBounds(LocalNaveMinX, LocalNaveMaxX, LocalNaveMinZ, LocalNaveMaxZ,
                out naveMinX, out naveMaxX, out naveMinZ, out naveMaxZ);

            ComputeTransformedRectBounds(LocalAcidMinX, LocalAcidMaxX, LocalAcidMinZ, LocalAcidMaxZ,
                out acidMinX, out acidMaxX, out acidMinZ, out acidMaxZ);

            Vector3 trigLeft = cathedralAnchor.TransformPoint(new Vector3(LocalPortalTriggerMinX, 0f, LocalPortalTriggerZ));
            Vector3 trigRight = cathedralAnchor.TransformPoint(new Vector3(LocalPortalTriggerMaxX, 0f, LocalPortalTriggerZ));
            portalTriggerZ = (trigLeft.z + trigRight.z) * 0.5f;
            portalTriggerXRange = new Vector2(Mathf.Min(trigLeft.x, trigRight.x), Mathf.Max(trigLeft.x, trigRight.x));

            if (pillarCentersWorld == null)
            {
                pillarCentersWorld = new List<Vector3>(CanonicalLocalPillars.Length);
            }
            pillarCentersWorld.Clear();
            for (int i = 0; i < CanonicalLocalPillars.Length; i++)
            {
                pillarCentersWorld.Add(cathedralAnchor.TransformPoint(CanonicalLocalPillars[i]));
            }

            if (portalBarrierObject != null)
            {
                portalBarrierObject.transform.position = cathedralAnchor.TransformPoint(LocalPortalBarrierPos);
                portalBarrierObject.transform.rotation = cathedralAnchor.rotation;
            }

            if (altarBarrierObject != null)
            {
                altarBarrierObject.transform.position = cathedralAnchor.TransformPoint(LocalAltarBarrierPos);
                altarBarrierObject.transform.rotation = cathedralAnchor.rotation;
            }

            return true;
        }

        private void ComputeTransformedRectBounds(
            float lMinX, float lMaxX, float lMinZ, float lMaxZ,
            out float wMinX, out float wMaxX, out float wMinZ, out float wMaxZ)
        {
            Vector3 c0 = cathedralAnchor.TransformPoint(new Vector3(lMinX, 0f, lMinZ));
            Vector3 c1 = cathedralAnchor.TransformPoint(new Vector3(lMaxX, 0f, lMinZ));
            Vector3 c2 = cathedralAnchor.TransformPoint(new Vector3(lMaxX, 0f, lMaxZ));
            Vector3 c3 = cathedralAnchor.TransformPoint(new Vector3(lMinX, 0f, lMaxZ));

            wMinX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
            wMaxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
            wMinZ = Mathf.Min(Mathf.Min(c0.z, c1.z), Mathf.Min(c2.z, c3.z));
            wMaxZ = Mathf.Max(Mathf.Max(c0.z, c1.z), Mathf.Max(c2.z, c3.z));
        }

        public void EnsureAltarStairBarrier()
        {
            if (!blockAltarStairs) return;
            SyncWithCathedralAnchorIfNeeded();

            if (altarBarrierObject == null && cathedralAnchor != null)
            {
                Transform existing = cathedralAnchor.Find("CathedralAltarStairBarrier");
                if (existing != null)
                {
                    altarBarrierObject = existing.gameObject;
                }
            }

            if (altarBarrierObject == null)
            {
                GameObject existingByName = GameObject.Find("CathedralAltarStairBarrier");
                if (existingByName != null)
                {
                    altarBarrierObject = existingByName;
                }
            }

            if (altarBarrierObject == null)
            {
                altarBarrierObject = new GameObject("CathedralAltarStairBarrier");
                var box = altarBarrierObject.AddComponent<BoxCollider>();
                box.size = new Vector3(33.0f, 7.2f, 0.85f);
                box.isTrigger = false;
            }
            else
            {
                var box = altarBarrierObject.GetComponent<BoxCollider>();
                if (box == null) box = altarBarrierObject.AddComponent<BoxCollider>();
                box.size = new Vector3(33.0f, 7.2f, 0.85f);
                box.isTrigger = false;
            }

            if (cathedralAnchor != null)
            {
                altarBarrierObject.transform.SetParent(cathedralAnchor, false);
                altarBarrierObject.transform.localPosition = LocalAltarBarrierPos;
                altarBarrierObject.transform.localRotation = Quaternion.identity;
                altarBarrierObject.transform.localScale = Vector3.one;
            }
            else
            {
                altarBarrierObject.transform.SetParent(null, false);
                altarBarrierObject.transform.position = new Vector3(arenaCenter.x, floorY + 3.20f, acidMaxZ + 1.00f);
                altarBarrierObject.transform.rotation = Quaternion.identity;
                altarBarrierObject.transform.localScale = Vector3.one;
            }

            altarBarrierObject.SetActive(true);
        }

        public Vector3 GetDormantSpawnWorldPos()
        {
            if (cathedralAnchor != null)
            {
                return cathedralAnchor.TransformPoint(LocalDormantSpawn);
            }
            return new Vector3(arenaCenter.x, floorY, arenaCenter.z + 5.90f);
        }

        public Vector3 GetCathedralForwardXZ()
        {
            if (cathedralAnchor != null)
            {
                Vector3 fwd = new Vector3(cathedralAnchor.forward.x, 0f, cathedralAnchor.forward.z);
                if (fwd.sqrMagnitude > 0.0001f) return fwd.normalized;
            }
            return Vector3.forward;
        }

        private Transform FindCathedralInScene()
        {
            Transform[] all = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            Transform best = null;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;

                bool isMatch = t.name.StartsWith("Stomach_Boss_Cathedral", System.StringComparison.OrdinalIgnoreCase);
                if (!isMatch && t.name == "Cathedral_Floor_Medallion" && t.parent != null)
                {
                    t = t.parent;
                    isMatch = true;
                }

                if (!isMatch) continue;

                float dSq = (t.position - transform.position).sqrMagnitude;
                if (best == null || dSq < bestDistSq)
                {
                    best = t;
                    bestDistSq = dSq;
                }
            }

            return best;
        }

        public StomachAbyss.Cathedral.CathedralGateDramaticTrigger ResolveDramaticGate()
        {
            if (dramaticGate != null)
            {
                return dramaticGate;
            }

            if (cathedralAnchor != null)
            {
                dramaticGate = cathedralAnchor.GetComponentInChildren<StomachAbyss.Cathedral.CathedralGateDramaticTrigger>(true);
            }

            if (dramaticGate == null)
            {
                dramaticGate = FindAnyObjectByType<StomachAbyss.Cathedral.CathedralGateDramaticTrigger>();
            }

            return dramaticGate;
        }

        public void SetPortalBarrierActive(bool active)
        {
            SyncWithCathedralAnchorIfNeeded();

            var gate = ResolveDramaticGate();
            if (gate != null)
            {
                if (portalBarrierObject != null)
                {
                    portalBarrierObject.SetActive(false);
                }

                if (active)
                {
                    gate.CloseDramatically();
                }
                else
                {
                    gate.OpenGate();
                }
                return;
            }

            if (portalBarrierObject == null)
            {
                if (!active)
                {
                    return;
                }

                portalBarrierObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                portalBarrierObject.name = "CathedralPortalAcidBarrier";
                portalBarrierObject.transform.SetParent(null, false);
                portalBarrierObject.transform.localScale = new Vector3(6.8f, 6.8f, 0.7f);
                _spawnedBarrierRuntime = true;

                var col = portalBarrierObject.GetComponent<BoxCollider>();
                if (col == null)
                {
                    col = portalBarrierObject.AddComponent<BoxCollider>();
                }
                col.isTrigger = false;

                var mr = portalBarrierObject.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader == null)
                    {
                        shader = Shader.Find("Universal Render Pipeline/Unlit");
                    }
                    if (shader != null)
                    {
                        _barrierMaterial = new Material(shader);
                        Color acidGlow = new Color(0.30f, 1.0f, 0.20f, 0.38f);
                        if (_barrierMaterial.HasProperty("_BaseColor")) _barrierMaterial.SetColor("_BaseColor", acidGlow);
                        if (_barrierMaterial.HasProperty("_Color")) _barrierMaterial.SetColor("_Color", acidGlow);
                        _barrierMaterial.renderQueue = 3050;
                        mr.sharedMaterial = _barrierMaterial;
                    }
                }
            }

            if (cathedralAnchor != null)
            {
                portalBarrierObject.transform.position = cathedralAnchor.TransformPoint(LocalPortalBarrierPos);
                portalBarrierObject.transform.rotation = cathedralAnchor.rotation;
            }
            else
            {
                portalBarrierObject.transform.position = new Vector3(arenaCenter.x, floorY + 3.20f, naveMinZ - 3.00f);
                portalBarrierObject.transform.rotation = Quaternion.identity;
            }

            portalBarrierObject.SetActive(active);
        }

        public bool IsPlayerInsideCathedral(Vector3 pos)
        {
            if (cathedralAnchor != null)
            {
                Vector3 local = cathedralAnchor.InverseTransformPoint(pos);
                return local.x >= (LocalAcidMinX - 3.8f) && local.x <= (LocalAcidMaxX + 3.8f)
                    && local.z >= LocalPortalTriggerZ && local.z <= (LocalAcidMaxZ + 1.5f)
                    && Mathf.Abs(local.y) <= 8f;
            }

            return pos.x >= (acidMinX - 3.8f) && pos.x <= (acidMaxX + 3.8f)
                && pos.z >= portalTriggerZ && pos.z <= (acidMaxZ + 1.5f);
        }

        public bool IsPlayerFarOutsideEntrance(Vector3 pos)
        {
            if (Vector3.Distance(pos, arenaCenter) <= 28f) return false;
            if (cathedralAnchor != null)
            {
                Vector3 local = cathedralAnchor.InverseTransformPoint(pos);
                return local.z < (LocalAcidMinZ - 5.5f);
            }
            return pos.z < (acidMinZ - 5.5f);
        }

        public bool IsInsideNave(Vector3 pos, float margin = 0f)
        {
            if (cathedralAnchor != null)
            {
                Vector3 local = cathedralAnchor.InverseTransformPoint(pos);
                GetRectBounds(LocalNaveMinX, LocalNaveMaxX, LocalNaveMinZ, LocalNaveMaxZ, margin,
                    out float lMinX, out float lMaxX, out float lMinZ, out float lMaxZ);
                return local.x >= lMinX && local.x <= lMaxX && local.z >= lMinZ && local.z <= lMaxZ;
            }

            GetRectBounds(naveMinX, naveMaxX, naveMinZ, naveMaxZ, margin,
                out float minX, out float maxX, out float minZ, out float maxZ);
            return pos.x >= minX && pos.x <= maxX && pos.z >= minZ && pos.z <= maxZ;
        }

        public Vector3 ClampPointToNave(Vector3 pos, float margin = 0.7f)
        {
            if (cathedralAnchor != null)
            {
                Vector3 local = cathedralAnchor.InverseTransformPoint(pos);
                GetRectBounds(LocalNaveMinX, LocalNaveMaxX, LocalNaveMinZ, LocalNaveMaxZ, margin,
                    out float lMinX, out float lMaxX, out float lMinZ, out float lMaxZ);
                local.x = Mathf.Clamp(local.x, lMinX, lMaxX);
                local.z = Mathf.Clamp(local.z, lMinZ, lMaxZ);
                Vector3 world = cathedralAnchor.TransformPoint(local);
                world.y = pos.y;
                return world;
            }

            GetRectBounds(naveMinX, naveMaxX, naveMinZ, naveMaxZ, margin,
                out float minX, out float maxX, out float minZ, out float maxZ);
            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
            return pos;
        }

        public Vector3 ClampLandingPoint(Vector3 pos, float margin = 1.15f)
        {
            Vector3 clamped = ClampPointToNave(pos, margin);
            clamped.y = floorY;
            return clamped;
        }

        /// <summary>
        /// Clampa pontos de impacto/poça de ácido no retangulo laranja (todo o chao solido dos corredores + nave),
        /// e valida via Raycast para impedir acido flutuando acima dos lagos/canais rebaixados nas quinas.
        /// </summary>
        public Vector3 ClampAcidPointToArena(Vector3 rawPos, float margin = 0.65f)
        {
            if (cathedralAnchor != null)
            {
                Vector3 local = cathedralAnchor.InverseTransformPoint(rawPos);
                GetRectBounds(LocalAcidMinX, LocalAcidMaxX, LocalAcidMinZ, LocalAcidMaxZ, margin,
                    out float aMinX, out float aMaxX, out float aMinZ, out float aMaxZ);
                local.x = Mathf.Clamp(local.x, aMinX, aMaxX);
                local.z = Mathf.Clamp(local.z, aMinZ, aMaxZ);
                local.y = 0f;

                // Garante que existe piso solido no nivel floorY (evita flutuar em cima dos canais de acido)
                for (int step = 0; step < 5; step++)
                {
                    Vector3 candidateWorld = cathedralAnchor.TransformPoint(local);
                    if (TrySampleWalkableFloorY(candidateWorld, out float sampledY))
                    {
                        candidateWorld.y = sampledY;
                        return candidateWorld;
                    }
                    local.x = Mathf.MoveTowards(local.x, 0f, 1.35f);
                }

                Vector3 fallback = cathedralAnchor.TransformPoint(local);
                fallback.y = floorY;
                return fallback;
            }

            GetRectBounds(acidMinX, acidMaxX, acidMinZ, acidMaxZ, margin,
                out float wMinX, out float wMaxX, out float wMinZ, out float wMaxZ);
            Vector3 world = new Vector3(
                Mathf.Clamp(rawPos.x, wMinX, wMaxX),
                floorY,
                Mathf.Clamp(rawPos.z, wMinZ, wMaxZ)
            );
            if (TrySampleWalkableFloorY(world, out float hitY))
            {
                world.y = hitY;
            }
            return world;
        }

        private bool TrySampleWalkableFloorY(Vector3 worldPos, out float hitFloorY)
        {
            hitFloorY = floorY;
            Vector3 origin = new Vector3(worldPos.x, floorY + 2.4f, worldPos.z);
            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, _floorRayHits, 6.0f, ~0, QueryTriggerInteraction.Ignore);
            float bestDiff = float.MaxValue;
            bool foundValidStone = false;

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _floorRayHits[i].collider;
                if (col == null) continue;
                if (col.transform.IsChildOf(transform)) continue; // ignora colliders da propria Tenia
                if (col.name.IndexOf("Acid_Channel", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (col.name.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                float y = _floorRayHits[i].point.y;
                float diff = Mathf.Abs(y - floorY);
                if (diff <= 0.35f && diff < bestDiff)
                {
                    bestDiff = diff;
                    hitFloorY = y;
                    foundValidStone = true;
                }
            }

            return foundValidStone;
        }

        public Vector3 SteerInsideNave(Vector3 rootPos, Vector3 desiredForwardXZ, float margin = 1.4f)
        {
            if (cathedralAnchor != null)
            {
                Vector3 localPos = cathedralAnchor.InverseTransformPoint(rootPos);
                Vector3 localDir = cathedralAnchor.InverseTransformDirection(new Vector3(desiredForwardXZ.x, 0f, desiredForwardXZ.z));
                localDir.y = 0f;

                Vector3 localSteered = SteerInsideRect(
                    localPos,
                    localDir,
                    LocalArenaCenter,
                    LocalNaveMinX,
                    LocalNaveMaxX,
                    LocalNaveMinZ,
                    LocalNaveMaxZ,
                    margin
                );

                Vector3 worldSteered = cathedralAnchor.TransformDirection(localSteered);
                worldSteered.y = 0f;
                return worldSteered.sqrMagnitude > 0.0001f ? worldSteered.normalized : GetCathedralForwardXZ();
            }

            return SteerInsideRect(rootPos, desiredForwardXZ, arenaCenter, naveMinX, naveMaxX, naveMinZ, naveMaxZ, margin);
        }

        private static Vector3 SteerInsideRect(
            Vector3 pos,
            Vector3 desiredDirXZ,
            Vector3 rectCenter,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float margin)
        {
            Vector3 dir = new Vector3(desiredDirXZ.x, 0f, desiredDirXZ.z);
            if (dir.sqrMagnitude < 0.0001f)
            {
                Vector3 toCenter = new Vector3(rectCenter.x - pos.x, 0f, rectCenter.z - pos.z);
                return toCenter.sqrMagnitude > 0.0001f ? toCenter.normalized : Vector3.forward;
            }
            dir.Normalize();

            float triggerDist = margin + 1.2f;
            float leftPen = (minX + triggerDist) - pos.x;
            float rightPen = pos.x - (maxX - triggerDist);
            float backPen = (minZ + triggerDist) - pos.z;
            float frontPen = pos.z - (maxZ - triggerDist);

            Vector3 inwardPush = Vector3.zero;
            float maxPressure = 0f;

            if (leftPen > 0f && dir.x < 0f)
            {
                float t = Mathf.Clamp01(leftPen / 1.2f);
                inwardPush.x += t;
                maxPressure = Mathf.Max(maxPressure, t);
            }
            if (rightPen > 0f && dir.x > 0f)
            {
                float t = Mathf.Clamp01(rightPen / 1.2f);
                inwardPush.x -= t;
                maxPressure = Mathf.Max(maxPressure, t);
            }
            if (backPen > 0f && dir.z < 0f)
            {
                float t = Mathf.Clamp01(backPen / 1.2f);
                inwardPush.z += t;
                maxPressure = Mathf.Max(maxPressure, t);
            }
            if (frontPen > 0f && dir.z > 0f)
            {
                float t = Mathf.Clamp01(frontPen / 1.2f);
                inwardPush.z -= t;
                maxPressure = Mathf.Max(maxPressure, t);
            }

            if (maxPressure <= 0f)
            {
                return dir;
            }

            Vector3 tangentSlide = dir;
            if (Mathf.Abs(inwardPush.x) > 0.01f && Mathf.Sign(dir.x) != Mathf.Sign(inwardPush.x))
            {
                tangentSlide.x = 0f;
            }
            if (Mathf.Abs(inwardPush.z) > 0.01f && Mathf.Sign(dir.z) != Mathf.Sign(inwardPush.z))
            {
                tangentSlide.z = 0f;
            }

            Vector3 toArenaCenter = new Vector3(rectCenter.x - pos.x, 0f, rectCenter.z - pos.z);
            Vector3 centerDir = toArenaCenter.sqrMagnitude > 0.0001f ? toArenaCenter.normalized : inwardPush.normalized;
            Vector3 safeTarget = tangentSlide.sqrMagnitude > 0.01f
                ? Vector3.Lerp(tangentSlide.normalized, centerDir, 0.45f).normalized
                : centerDir;

            Vector3 steered = Vector3.Lerp(dir, safeTarget, Mathf.Clamp01(maxPressure * 0.85f + 0.15f));
            return steered.sqrMagnitude > 0.0001f ? steered.normalized : centerDir;
        }

        private static void GetRectBounds(
            float srcMinX, float srcMaxX, float srcMinZ, float srcMaxZ, float margin,
            out float minX, out float maxX, out float minZ, out float maxZ)
        {
            float midX = (srcMinX + srcMaxX) * 0.5f;
            float midZ = (srcMinZ + srcMaxZ) * 0.5f;
            minX = Mathf.Min(srcMinX + margin, midX);
            maxX = Mathf.Max(srcMaxX - margin, midX);
            minZ = Mathf.Min(srcMinZ + margin, midZ);
            maxZ = Mathf.Max(srcMaxZ - margin, midZ);
        }

        public Vector3 SelectBestPillar(Vector3 playerPos, Vector3 currentBossPos, Vector3 lastPillarPos, float idealDistance = 14f)
        {
            if (pillarCentersWorld == null || pillarCentersWorld.Count == 0)
            {
                return arenaCenter;
            }

            bool playerInSideAisle = !IsInsideNave(playerPos, -0.35f);
            float playerSideSign = 0f;
            if (playerInSideAisle)
            {
                float localPlayerX = cathedralAnchor != null
                    ? cathedralAnchor.InverseTransformPoint(playerPos).x
                    : (playerPos.x - arenaCenter.x);
                playerSideSign = Mathf.Abs(localPlayerX) > 0.5f ? Mathf.Sign(localPlayerX) : 0f;
            }

            Vector3 bestPillar = pillarCentersWorld[0];
            float bestScore = float.MaxValue;
            bool hasAlternative = pillarCentersWorld.Count > 1;

            for (int i = 0; i < pillarCentersWorld.Count; i++)
            {
                Vector3 candidate = pillarCentersWorld[i];
                if (hasAlternative && (candidate - lastPillarPos).sqrMagnitude < 0.25f)
                {
                    continue;
                }

                Vector2 candXZ = new Vector2(candidate.x, candidate.z);
                Vector2 playerXZ = new Vector2(playerPos.x, playerPos.z);
                Vector2 bossXZ = new Vector2(currentBossPos.x, currentBossPos.z);

                float distToPlayer = Vector2.Distance(candXZ, playerXZ);
                float distToBoss = Vector2.Distance(candXZ, bossXZ);
                float distScore = Mathf.Abs(distToPlayer - (playerInSideAisle ? 6.5f : idealDistance));

                Vector2 toCenter = new Vector2(arenaCenter.x - candidate.x, arenaCenter.z - candidate.z).normalized;
                Vector2 toPlayer = (playerXZ - candXZ).sqrMagnitude > 0.001f ? (playerXZ - candXZ).normalized : toCenter;
                float naveVisibility = Vector2.Dot(toCenter, toPlayer);

                float visibilityPenalty = 0f;
                if (playerInSideAisle)
                {
                    float localCandX = cathedralAnchor != null
                        ? cathedralAnchor.InverseTransformPoint(candidate).x
                        : (candidate.x - arenaCenter.x);
                    if (playerSideSign != 0f && Mathf.Sign(localCandX) != playerSideSign)
                    {
                        visibilityPenalty = 14f; // Forca subir no pilar do MESMO lado em que o player se escondeu!
                    }
                }
                else
                {
                    visibilityPenalty = naveVisibility < 0.15f ? (0.15f - naveVisibility) * 8f : 0f;
                }

                float bossTravelCost = distToBoss * 0.12f;
                float totalScore = distScore + visibilityPenalty + bossTravelCost;

                if (totalScore < bestScore)
                {
                    bestScore = totalScore;
                    bestPillar = candidate;
                }
            }

            return bestPillar;
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
            {
                SyncWithCathedralAnchorIfNeeded();
            }

            // 1. Retangulo VERDE: Nave central onde o boss se movimenta (nao sai dele)
            Gizmos.color = Color.green;
            DrawRectGizmo(LocalNaveMinX, LocalNaveMaxX, LocalNaveMinZ, LocalNaveMaxZ, naveMinX, naveMaxX, naveMinZ, naveMaxZ, 0f);

            // 2. Retangulo LARANJA: Chao solido completo onde o boss pode cuspir acido (inclui corredores atras dos pilares)
            Gizmos.color = new Color(1.0f, 0.52f, 0.08f);
            DrawRectGizmo(LocalAcidMinX, LocalAcidMaxX, LocalAcidMinZ, LocalAcidMaxZ, acidMinX, acidMaxX, acidMinZ, acidMaxZ, 0.04f);

            // 3. Linha CIANA: Gatilho de entrada do combate
            Gizmos.color = Color.cyan;
            if (cathedralAnchor != null)
            {
                Gizmos.DrawLine(
                    cathedralAnchor.TransformPoint(new Vector3(LocalPortalTriggerMinX, 0.05f, LocalPortalTriggerZ)),
                    cathedralAnchor.TransformPoint(new Vector3(LocalPortalTriggerMaxX, 0.05f, LocalPortalTriggerZ))
                );
            }
            else
            {
                Gizmos.DrawLine(
                    new Vector3(portalTriggerXRange.x, floorY + 0.05f, portalTriggerZ),
                    new Vector3(portalTriggerXRange.y, floorY + 0.05f, portalTriggerZ)
                );
            }

            // 4. Linha MAGENTA: Barreira na escada do altar
            if (blockAltarStairs)
            {
                Gizmos.color = Color.magenta;
                if (cathedralAnchor != null)
                {
                    Gizmos.DrawLine(
                        cathedralAnchor.TransformPoint(new Vector3(-15.5f, 0.15f, LocalAltarBarrierPos.z)),
                        cathedralAnchor.TransformPoint(new Vector3(15.5f, 0.15f, LocalAltarBarrierPos.z))
                    );
                }
                else
                {
                    Gizmos.DrawLine(
                        new Vector3(acidMinX - 3.5f, floorY + 0.15f, acidMaxZ + 1.0f),
                        new Vector3(acidMaxX + 3.5f, floorY + 0.15f, acidMaxZ + 1.0f)
                    );
                }
            }

            if (pillarCentersWorld != null)
            {
                Gizmos.color = Color.yellow;
                const int segments = 24;
                for (int i = 0; i < pillarCentersWorld.Count; i++)
                {
                    Vector3 center = pillarCentersWorld[i];
                    Vector3 prev = center + new Vector3(pillarRadius, 0.05f, 0f);
                    for (int s = 1; s <= segments; s++)
                    {
                        float angle = (s / (float)segments) * Mathf.PI * 2f;
                        Vector3 next = center + new Vector3(Mathf.Cos(angle) * pillarRadius, 0.05f, Mathf.Sin(angle) * pillarRadius);
                        Gizmos.DrawLine(prev, next);
                        prev = next;
                    }
                }
            }
        }

        private void DrawRectGizmo(float lMinX, float lMaxX, float lMinZ, float lMaxZ, float wMinX, float wMaxX, float wMinZ, float wMaxZ, float yOffset)
        {
            Vector3 p1, p2, p3, p4;
            if (cathedralAnchor != null)
            {
                p1 = cathedralAnchor.TransformPoint(new Vector3(lMinX, yOffset, lMinZ));
                p2 = cathedralAnchor.TransformPoint(new Vector3(lMaxX, yOffset, lMinZ));
                p3 = cathedralAnchor.TransformPoint(new Vector3(lMaxX, yOffset, lMaxZ));
                p4 = cathedralAnchor.TransformPoint(new Vector3(lMinX, yOffset, lMaxZ));
            }
            else
            {
                p1 = new Vector3(wMinX, floorY + yOffset, wMinZ);
                p2 = new Vector3(wMaxX, floorY + yOffset, wMinZ);
                p3 = new Vector3(wMaxX, floorY + yOffset, wMaxZ);
                p4 = new Vector3(wMinX, floorY + yOffset, wMaxZ);
            }
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p4);
            Gizmos.DrawLine(p4, p1);
        }
    }
}
