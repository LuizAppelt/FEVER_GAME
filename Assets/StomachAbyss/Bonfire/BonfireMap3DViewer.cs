using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StomachAbyss.Bonfire
{
    /// <summary>
    /// Miniatura 3D procedural isolada do mapa de Portas Vivas (renderizada em RenderTexture):
    /// - 3 ilhas arredondadas em sequencia (vermelho visceral / carne, sem paredes e sem nomes)
    /// - 2 pontes organicas conectando as 3 ilhas
    /// - Lago de acido verde luminoso com bolhas animadas
    /// - 4 portas selecionaveis (1 na ilha esquerda, 2 na ilha do meio, 1 na ilha direita)
    /// - Camera orbital interativa atualizada com unscaledDeltaTime (funciona mesmo pausado)
    /// </summary>
    public class BonfireMap3DViewer : MonoBehaviour
    {
        private static readonly Vector3 RigOrigin = new Vector3(0f, -450f, 0f);

        private struct DoorBeacon
        {
            public int SlotIndex;
            public Vector3 LocalPos;
            public Transform Root;
            public Transform EyeGroup;
            public Transform Reticle;
            public Renderer AuraRenderer;
            public Renderer StemRenderer;
            public Renderer OuterEyeRenderer;
            public Renderer InnerEyeRenderer;
            public Renderer IrisRenderer;
            public Renderer PupilRenderer;
            public Renderer ClosedSlitRenderer;
            public Renderer ReticleRenderer;
        }

        private struct BubbleItem
        {
            public Transform Tr;
            public Vector3 BaseLocalPos;
            public float Phase;
            public float BaseScale;
        }

        private Camera _miniCamera;
        private RenderTexture _renderTexture;
        private Transform _worldRoot;
        private Transform _acidSurface;
        private readonly List<DoorBeacon> _beacons = new List<DoorBeacon>(4);
        private readonly List<BubbleItem> _bubbles = new List<BubbleItem>(24);
        private readonly List<Material> _ownedMaterials = new List<Material>();
        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();

        private float _yaw = -0.35f;
        private float _pitch = 0.66f;
        private float _cameraDistance = 16.5f;
        private bool _autoSpin = true;
        private float _animTime;

        private int _currentSlot = 0;
        private int _selectedSlot = -1;
        private int _hoveredSlot = -1;
        private readonly bool[] _slotUnlocked = new bool[BonfireDoorNetwork.TotalMapSlots];
        private bool _savedFogState;
        private bool _fogHookRegistered;

        // Paleta aprovada: ilhas vermelhas + acido verde + osso envelhecido + ouro osso
        private static readonly Color BgColor = new Color(0.035f, 0.055f, 0.040f, 1f);
        private static readonly Color AcidMainColor = new Color(0.28f, 0.56f, 0.14f, 1f);
        private static readonly Color AcidRimColor = new Color(0.66f, 0.93f, 0.32f, 1f);
        private static readonly Color AcidShimmerColor = new Color(0.38f, 0.70f, 0.19f, 1f);
        private static readonly Color BubbleColor = new Color(0.80f, 1.00f, 0.45f, 1f);

        private static readonly Color IslandTopColor = new Color(0.49f, 0.12f, 0.15f, 1f);
        private static readonly Color IslandInnerColor = new Color(0.66f, 0.17f, 0.20f, 1f);
        private static readonly Color IslandSideColor = new Color(0.27f, 0.06f, 0.09f, 1f);
        private static readonly Color BridgeTopColor = new Color(0.42f, 0.10f, 0.13f, 1f);
        private static readonly Color BridgeSideColor = new Color(0.23f, 0.05f, 0.07f, 1f);
        private static readonly Color BoneRimColor = new Color(0.87f, 0.78f, 0.68f, 1f);
        private static readonly Color LinkDotColor = new Color(0.90f, 0.79f, 0.52f, 1f);

        private static readonly Color GoldColor = new Color(0.90f, 0.79f, 0.52f, 1f);
        private static readonly Color CrimsonIrisColor = new Color(1.00f, 0.29f, 0.31f, 1f);
        private static readonly Color BoneWhiteColor = new Color(0.93f, 0.91f, 0.86f, 1f);
        private static readonly Color LockedOutlineColor = new Color(0.37f, 0.34f, 0.30f, 1f);
        private static readonly Color LockedFillColor = new Color(0.10f, 0.09f, 0.10f, 1f);
        private static readonly Color ActiveFillColor = new Color(0.15f, 0.11f, 0.05f, 1f);
        private static readonly Color UnlockedFillColor = new Color(0.10f, 0.03f, 0.04f, 1f);
        private static readonly Color PupilColor = new Color(0.03f, 0.025f, 0.03f, 1f);

        // Posicoes das 4 portas nas 3 ilhas (1 esquerda, 2 na ilha do meio, 1 direita)
        private static readonly Vector3[] DoorLocalPositions =
        {
            new Vector3(-5.60f, 0.88f,  0.22f), // Slot 0: Ilha Esquerda (CardiaExit)
            new Vector3(-1.35f, 0.80f,  0.38f), // Slot 1: Ilha do Meio - Sul (OssuaryArrival)
            new Vector3( 1.35f, 0.80f, -0.32f), // Slot 2: Ilha do Meio - Norte (CentralArena)
            new Vector3( 5.75f, 0.98f, -0.22f), // Slot 3: Ilha Direita (CathedralBridge)
        };

        public RenderTexture Texture => _renderTexture;

        public void Initialize(int width = 1024, int height = 480)
        {
            if (_renderTexture != null) return;

            transform.position = RigOrigin;

            _renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.Default)
            {
                name = "BonfireMap3D_RT",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear
            };
            _renderTexture.Create();

            // Limpa a VRAM imediatamente para nunca exibir ruido antes do primeiro frame do URP
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = _renderTexture;
            GL.Clear(true, true, BgColor);
            RenderTexture.active = prevActive;

            _worldRoot = new GameObject("Map3D_GeometryRoot").transform;
            _worldRoot.SetParent(transform, false);

            GameObject camGo = new GameObject("Map3D_Camera");
            camGo.transform.SetParent(transform, false);
            _miniCamera = camGo.AddComponent<Camera>();
            _miniCamera.clearFlags = CameraClearFlags.SolidColor;
            _miniCamera.backgroundColor = BgColor;
            _miniCamera.fieldOfView = 32f;
            _miniCamera.nearClipPlane = 0.2f;
            _miniCamera.farClipPlane = 60f;
            _miniCamera.targetTexture = _renderTexture;
            _miniCamera.allowHDR = false;
            _miniCamera.allowMSAA = false;
            _miniCamera.depth = 5f;
            _miniCamera.enabled = false;

            UniversalAdditionalCameraData urpData = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (urpData == null) urpData = camGo.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderType = CameraRenderType.Base;
            urpData.renderPostProcessing = false;
            urpData.renderShadows = false;
            urpData.antialiasing = AntialiasingMode.None;
            urpData.requiresColorOption = CameraOverrideOption.Off;
            urpData.requiresDepthOption = CameraOverrideOption.Off;

            if (!_fogHookRegistered)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
                _fogHookRegistered = true;
            }

            BuildAcidPool();
            BuildIslandsAndBridges();
            BuildLinkDots();
            BuildDoorBeacons();
            UpdateCameraTransform();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (cam == _miniCamera)
            {
                _savedFogState = RenderSettings.fog;
                RenderSettings.fog = false;
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (cam == _miniCamera)
            {
                RenderSettings.fog = _savedFogState;
            }
        }

        public void SetActiveRendering(bool active)
        {
            if (_worldRoot != null) _worldRoot.gameObject.SetActive(active);
            if (_miniCamera != null) _miniCamera.enabled = active;
            if (active)
            {
                _yaw = -0.35f;
                _pitch = 0.66f;
                _autoSpin = true;
                UpdateCameraTransform();
            }
        }

        public void SetDoorStates(int currentSlot, int selectedSlot, int hoveredSlot, bool[] unlockedSlots)
        {
            _currentSlot = currentSlot;
            _selectedSlot = selectedSlot;
            _hoveredSlot = hoveredSlot;

            if (unlockedSlots != null)
            {
                for (int i = 0; i < _slotUnlocked.Length && i < unlockedSlots.Length; i++)
                {
                    _slotUnlocked[i] = unlockedSlots[i];
                }
            }
            ApplyDoorVisualStates();
        }

        public void OrbitBy(float deltaX, float deltaY)
        {
            _autoSpin = false;
            _yaw += deltaX * 0.0085f;
            _pitch = Mathf.Clamp(_pitch + deltaY * 0.0065f, 0.28f, 1.28f);
            UpdateCameraTransform();
        }

        public void TickUnscaled(float unscaledDt)
        {
            if (_miniCamera == null || !_miniCamera.enabled) return;

            _animTime += unscaledDt;
            if (_autoSpin)
            {
                _yaw += unscaledDt * 0.14f;
            }

            UpdateCameraTransform();

            // Onda suave no acido verde
            if (_acidSurface != null)
            {
                _acidSurface.localPosition = new Vector3(0f, -0.62f + Mathf.Sin(_animTime * 1.6f) * 0.04f, 0f);
            }

            // Bolhas de acido pulsando
            for (int i = 0; i < _bubbles.Count; i++)
            {
                BubbleItem b = _bubbles[i];
                float p = 0.5f + 0.5f * Mathf.Sin(_animTime * 2.2f + b.Phase);
                b.Tr.localPosition = b.BaseLocalPos + new Vector3(0f, p * 0.07f, 0f);
                float s = b.BaseScale * (0.65f + 0.55f * p);
                b.Tr.localScale = new Vector3(s, s * 0.35f, s);
            }

            // Portas flutuando e olhando para a camera (billboard)
            Quaternion camRot = _miniCamera.transform.rotation;
            float pulse = 0.5f + 0.5f * Mathf.Sin(_animTime * 3.2f);
            for (int i = 0; i < _beacons.Count; i++)
            {
                DoorBeacon b = _beacons[i];
                float bob = Mathf.Sin(_animTime * 2.6f + i * 1.45f) * 0.08f;
                b.EyeGroup.localPosition = new Vector3(0f, 0.62f + bob, 0f);
                b.EyeGroup.rotation = camRot;

                bool isHovered = i == _hoveredSlot;
                bool isSelected = i == _selectedSlot;
                bool isCurrent = i == _currentSlot;

                float scaleMult = (isHovered || isSelected) ? 1.20f : (isCurrent ? 1.10f : 1.0f);
                b.EyeGroup.localScale = Vector3.one * scaleMult;

                if (b.Reticle != null && b.Reticle.gameObject.activeSelf)
                {
                    float rScale = isSelected ? (1.12f + 0.10f * pulse) : 1.0f;
                    b.Reticle.localScale = Vector3.one * rScale;
                    b.Reticle.localRotation = Quaternion.Euler(0f, 0f, isSelected ? (_animTime * 28f) : 0f);
                }
            }
        }

        public void RenderNow()
        {
            // No URP RenderGraph (Unity 6), a camera habilitada ja renderiza automaticamente no loop de renderizacao.
        }

        /// <summary>
        /// Testa qual porta 3D esta sob a coordenada normalizada (0..1) do RawImage.
        /// Retorna slotIndex (0..3) ou -1 se nenhuma.
        /// </summary>
        public int HitTestDoor(Vector2 viewportUv)
        {
            if (_miniCamera == null) return -1;

            int bestSlot = -1;
            float bestDistSq = 0.068f * 0.068f; // Raio generoso no viewport (corrigido pelo aspecto)
            float aspect = _miniCamera.aspect > 0.01f ? _miniCamera.aspect : 2f;

            for (int i = 0; i < _beacons.Count; i++)
            {
                Vector3 worldEye = _beacons[i].EyeGroup.position;
                Vector3 vp = _miniCamera.WorldToViewportPoint(worldEye);
                if (vp.z <= 0f) continue;

                float dx = (vp.x - viewportUv.x) * aspect;
                float dy = vp.y - viewportUv.y;
                float distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestSlot = i;
                }
            }

            return bestSlot;
        }

        private void UpdateCameraTransform()
        {
            if (_miniCamera == null) return;
            Vector3 target = RigOrigin + new Vector3(0f, 0.25f, 0f);

            float cosP = Mathf.Cos(_pitch);
            float sinP = Mathf.Sin(_pitch);
            float cosY = Mathf.Cos(_yaw);
            float sinY = Mathf.Sin(_yaw);

            Vector3 offset = new Vector3(
                sinY * cosP * _cameraDistance,
                sinP * _cameraDistance,
                -cosY * cosP * _cameraDistance);

            _miniCamera.transform.position = target + offset;
            _miniCamera.transform.LookAt(target, Vector3.up);
        }

        private void ApplyDoorVisualStates()
        {
            for (int i = 0; i < _beacons.Count; i++)
            {
                DoorBeacon b = _beacons[i];
                bool unlocked = i < _slotUnlocked.Length && _slotUnlocked[i];
                bool isCurrent = i == _currentSlot;
                bool isSelected = i == _selectedSlot;

                Color outline = !unlocked ? LockedOutlineColor : (isCurrent || isSelected ? GoldColor : BoneWhiteColor);
                Color fill = !unlocked ? LockedFillColor : (isCurrent ? ActiveFillColor : UnlockedFillColor);
                Color iris = isCurrent || isSelected ? GoldColor : CrimsonIrisColor;
                Color stem = !unlocked ? LockedOutlineColor : (isCurrent || isSelected ? GoldColor : BoneRimColor);

                SetMatColor(b.OuterEyeRenderer, outline);
                SetMatColor(b.InnerEyeRenderer, fill);
                SetMatColor(b.StemRenderer, stem);
                SetMatColor(b.AuraRenderer, isCurrent || isSelected ? GoldColor : (unlocked ? CrimsonIrisColor : LockedOutlineColor));

                if (b.IrisRenderer != null)
                {
                    b.IrisRenderer.gameObject.SetActive(unlocked);
                    SetMatColor(b.IrisRenderer, iris);
                }
                if (b.PupilRenderer != null)
                {
                    b.PupilRenderer.gameObject.SetActive(unlocked);
                }
                if (b.ClosedSlitRenderer != null)
                {
                    b.ClosedSlitRenderer.gameObject.SetActive(!unlocked);
                }
                if (b.Reticle != null)
                {
                    b.Reticle.gameObject.SetActive(unlocked && (isCurrent || isSelected));
                }
            }
        }

        private void BuildAcidPool()
        {
            _acidSurface = new GameObject("Acid_Lake_Root").transform;
            _acidSurface.SetParent(_worldRoot, false);
            _acidSurface.localPosition = new Vector3(0f, -0.62f, 0f);

            List<Vector2> outerRing = MakeOrganicRing(0f, 0f, 9.6f, 4.4f, 44, 0.9f, 0.06f);
            CreateFlatPolygonObject("Acid_Surface", _acidSurface, outerRing, 0f, CreateUnlitMaterial(AcidMainColor));
            CreateRingRibbonObject("Acid_OuterRim", _acidSurface, outerRing, 0.015f, 0.14f, CreateUnlitMaterial(AcidRimColor));

            List<Vector2> innerRing = MakeOrganicRing(0f, 0f, 7.6f, 3.4f, 36, 1.5f, 0.06f);
            CreateFlatPolygonObject("Acid_InnerShimmer", _acidSurface, innerRing, 0.008f, CreateUnlitMaterial(AcidShimmerColor));

            // 24 bolhas verdes
            Material bubbleMat = CreateUnlitMaterial(BubbleColor);
            for (int i = 0; i < 24; i++)
            {
                float t = (i / 24f) * Mathf.PI * 2f;
                float rx = 2.7f + (i % 4) * 1.95f;
                float rz = 1.55f + (i % 3) * 0.85f;

                GameObject bGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bGo.name = $"AcidBubble_{i}";
                DestroyImmediate(bGo.GetComponent<Collider>());
                bGo.transform.SetParent(_acidSurface, false);

                Vector3 basePos = new Vector3(Mathf.Cos(t) * rx, 0.04f, Mathf.Sin(t) * rz);
                float baseScale = 0.24f + (i % 3) * 0.11f;
                bGo.transform.localPosition = basePos;
                bGo.transform.localScale = new Vector3(baseScale, baseScale * 0.35f, baseScale);
                bGo.GetComponent<Renderer>().sharedMaterial = bubbleMat;

                _bubbles.Add(new BubbleItem
                {
                    Tr = bGo.transform,
                    BaseLocalPos = basePos,
                    Phase = i * 1.15f,
                    BaseScale = baseScale
                });
            }
        }

        private void BuildIslandsAndBridges()
        {
            Material islandTopMat = CreateUnlitMaterial(IslandTopColor);
            Material islandInnerMat = CreateUnlitMaterial(IslandInnerColor);
            Material islandSideMat = CreateUnlitMaterial(IslandSideColor);
            Material bridgeTopMat = CreateUnlitMaterial(BridgeTopColor);
            Material bridgeSideMat = CreateUnlitMaterial(BridgeSideColor);
            Material boneRimMat = CreateUnlitMaterial(BoneRimColor);
            Material acidBounceMat = CreateUnlitMaterial(AcidRimColor);

            // 3 Ilhas arredondadas em sequencia (SEM paredes, SEM ilha lateral!)
            // Ilha 1 (Esquerda)
            BuildExtrudedIsland("Island_Left", MakeOrganicRing(-5.6f, 0.22f, 2.15f, 1.95f, 28, 1.1f, 0.07f),
                0.24f, 0.72f, islandTopMat, islandInnerMat, islandSideMat, boneRimMat, acidBounceMat, true);

            // Ponte 1 -> 2
            BuildExtrudedIsland("Bridge_LeftToMid", MakeBridgeRing(-3.85f, 0.18f, -2.55f, 0.10f, 0.56f),
                0.10f, 0.52f, bridgeTopMat, null, bridgeSideMat, boneRimMat, acidBounceMat, false);

            // Ilha 2 (Meio - mais larga para comportar confortavelmente as 2 portas!)
            BuildExtrudedIsland("Island_Middle", MakeOrganicRing(0.0f, 0.04f, 3.15f, 2.45f, 32, 2.5f, 0.075f),
                0.16f, 0.64f, islandTopMat, islandInnerMat, islandSideMat, boneRimMat, acidBounceMat, true);

            // Ponte 2 -> 3
            BuildExtrudedIsland("Bridge_MidToRight", MakeBridgeRing(2.75f, -0.05f, 3.95f, -0.20f, 0.56f),
                0.18f, 0.54f, bridgeTopMat, null, bridgeSideMat, boneRimMat, acidBounceMat, false);

            // Ilha 3 (Direita - elevada)
            BuildExtrudedIsland("Island_Right", MakeOrganicRing(5.75f, -0.22f, 2.35f, 2.15f, 28, 3.9f, 0.065f),
                0.34f, 0.80f, islandTopMat, islandInnerMat, islandSideMat, boneRimMat, acidBounceMat, true);
        }

        private void BuildExtrudedIsland(
            string name,
            List<Vector2> ring,
            float yTop,
            float height,
            Material topMat,
            Material innerMat,
            Material sideMat,
            Material boneRimMat,
            Material acidBounceMat,
            bool addInnerHighlight)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(_worldRoot, false);

            // Topo arredondado vermelho
            CreateFlatPolygonObject("Top", root, ring, yTop, topMat);

            // Laterais extrudadas vermelho escuro
            CreateExtrudedSideObject("Sides", root, ring, yTop, yTop - height, sideMat);

            // Borda superior em osso envelhecido
            CreateRingRibbonObject("BoneRim", root, ring, yTop + 0.015f, 0.08f, boneRimMat);

            // Reflexo sutil do acido verde na base da ilha
            CreateRingRibbonObject("AcidBaseGlow", root, ring, yTop - height + 0.05f, 0.08f, acidBounceMat);

            if (addInnerHighlight && innerMat != null)
            {
                Vector2 center = ComputeCentroid(ring);
                List<Vector2> inner = new List<Vector2>(ring.Count);
                for (int i = 0; i < ring.Count; i++)
                {
                    inner.Add(Vector2.Lerp(center, ring[i], 0.76f));
                }
                CreateFlatPolygonObject("InnerHighlight", root, inner, yTop + 0.01f, innerMat);
            }
        }

        private void BuildLinkDots()
        {
            Material dotMat = CreateUnlitMaterial(LinkDotColor);
            int[,] pairs = { { 0, 1 }, { 1, 2 }, { 2, 3 } };

            for (int p = 0; p < pairs.GetLength(0); p++)
            {
                Vector3 a = DoorLocalPositions[pairs[p, 0]];
                Vector3 b = DoorLocalPositions[pairs[p, 1]];
                a.y = 0.28f;
                b.y = 0.28f;

                int steps = 7;
                for (int s = 1; s < steps; s++)
                {
                    float t = s / (float)steps;
                    Vector3 pos = Vector3.Lerp(a, b, t);
                    GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    dot.name = $"LinkDot_{p}_{s}";
                    DestroyImmediate(dot.GetComponent<Collider>());
                    dot.transform.SetParent(_worldRoot, false);
                    dot.transform.localPosition = pos;
                    dot.transform.localScale = new Vector3(0.11f, 0.04f, 0.11f);
                    dot.GetComponent<Renderer>().sharedMaterial = dotMat;
                }
            }
        }

        private void BuildDoorBeacons()
        {
            Mesh almondMesh = BuildAlmondMesh(0.62f, 0.36f, 20);
            Mesh almondInnerMesh = BuildAlmondMesh(0.52f, 0.28f, 20);
            Mesh reticleMesh = BuildDiamondFrameMesh(0.78f, 0.68f);

            for (int i = 0; i < DoorLocalPositions.Length; i++)
            {
                Vector3 basePos = DoorLocalPositions[i];
                GameObject rootGo = new GameObject($"DoorBeacon_{i}");
                rootGo.transform.SetParent(_worldRoot, false);
                rootGo.transform.localPosition = new Vector3(basePos.x, basePos.y - 0.62f, basePos.z);

                // Aura no chao
                GameObject auraGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                auraGo.name = "GroundAura";
                DestroyImmediate(auraGo.GetComponent<Collider>());
                auraGo.transform.SetParent(rootGo.transform, false);
                auraGo.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                auraGo.transform.localScale = new Vector3(0.92f, 0.015f, 0.92f);
                Renderer auraRend = auraGo.GetComponent<Renderer>();
                auraRend.sharedMaterial = CreateUnlitMaterial(CrimsonIrisColor);

                // Haste vertical
                GameObject stemGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stemGo.name = "Stem";
                DestroyImmediate(stemGo.GetComponent<Collider>());
                stemGo.transform.SetParent(rootGo.transform, false);
                stemGo.transform.localPosition = new Vector3(0f, 0.30f, 0f);
                stemGo.transform.localScale = new Vector3(0.06f, 0.28f, 0.06f);
                Renderer stemRend = stemGo.GetComponent<Renderer>();
                stemRend.sharedMaterial = CreateUnlitMaterial(BoneRimColor);

                // Grupo do Olho (billboard voltado para a camera)
                Transform eyeGroup = new GameObject("EyeBillboard").transform;
                eyeGroup.SetParent(rootGo.transform, false);
                eyeGroup.localPosition = new Vector3(0f, 0.62f, 0f);

                // Contorno externo em forma de olho
                Renderer outerRend = CreateMeshChild("OuterEye", eyeGroup, almondMesh, new Vector3(0f, 0f, 0.02f), CreateUnlitMaterial(BoneWhiteColor));

                // Fundo interno do olho
                Renderer innerRend = CreateMeshChild("InnerEye", eyeGroup, almondInnerMesh, new Vector3(0f, 0f, 0.01f), CreateUnlitMaterial(UnlockedFillColor));

                // Iris brilhante
                GameObject irisGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                irisGo.name = "Iris";
                DestroyImmediate(irisGo.GetComponent<Collider>());
                irisGo.transform.SetParent(eyeGroup, false);
                irisGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
                irisGo.transform.localScale = new Vector3(0.42f, 0.42f, 0.06f);
                Renderer irisRend = irisGo.GetComponent<Renderer>();
                irisRend.sharedMaterial = CreateUnlitMaterial(CrimsonIrisColor);

                // Pupila vertical
                GameObject pupilGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pupilGo.name = "Pupil";
                DestroyImmediate(pupilGo.GetComponent<Collider>());
                pupilGo.transform.SetParent(eyeGroup, false);
                pupilGo.transform.localPosition = new Vector3(0f, 0f, -0.03f);
                pupilGo.transform.localScale = new Vector3(0.11f, 0.34f, 0.06f);
                Renderer pupilRend = pupilGo.GetComponent<Renderer>();
                pupilRend.sharedMaterial = CreateUnlitMaterial(PupilColor);

                // Palpebra fechada (quando selada)
                GameObject closedGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                closedGo.name = "ClosedSlit";
                DestroyImmediate(closedGo.GetComponent<Collider>());
                closedGo.transform.SetParent(eyeGroup, false);
                closedGo.transform.localPosition = new Vector3(0f, 0f, -0.02f);
                closedGo.transform.localScale = new Vector3(0.68f, 0.05f, 0.04f);
                Renderer closedRend = closedGo.GetComponent<Renderer>();
                closedRend.sharedMaterial = CreateUnlitMaterial(LockedOutlineColor);

                // Reticulo losango em Ouro Osso (atual / selecionada)
                Renderer reticleRend = CreateMeshChild("Reticle", eyeGroup, reticleMesh, new Vector3(0f, 0f, -0.04f), CreateUnlitMaterial(GoldColor));

                _beacons.Add(new DoorBeacon
                {
                    SlotIndex = i,
                    LocalPos = basePos,
                    Root = rootGo.transform,
                    EyeGroup = eyeGroup,
                    Reticle = reticleRend.transform,
                    AuraRenderer = auraRend,
                    StemRenderer = stemRend,
                    OuterEyeRenderer = outerRend,
                    InnerEyeRenderer = innerRend,
                    IrisRenderer = irisRend,
                    PupilRenderer = pupilRend,
                    ClosedSlitRenderer = closedRend,
                    ReticleRenderer = reticleRend
                });
            }
        }

        private Renderer CreateMeshChild(string name, Transform parent, Mesh mesh, Vector3 localPos, Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        private void CreateFlatPolygonObject(string name, Transform parent, List<Vector2> ring, float y, Material mat)
        {
            Mesh mesh = new Mesh { name = name + "_Mesh" };
            int n = ring.Count;
            Vector2 center = ComputeCentroid(ring);

            Vector3[] verts = new Vector3[n + 1];
            verts[0] = new Vector3(center.x, y, center.y);
            for (int i = 0; i < n; i++)
            {
                verts[i + 1] = new Vector3(ring[i].x, y, ring[i].y);
            }

            // Dupla face para garantir visibilidade de qualquer angulo
            int[] tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                tris[i * 6 + 0] = 0;
                tris[i * 6 + 1] = next + 1;
                tris[i * 6 + 2] = i + 1;

                tris[i * 6 + 3] = 0;
                tris[i * 6 + 4] = i + 1;
                tris[i * 6 + 5] = next + 1;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _ownedMeshes.Add(mesh);

            CreateMeshChild(name, parent, mesh, Vector3.zero, mat);
        }

        private void CreateExtrudedSideObject(string name, Transform parent, List<Vector2> ring, float yTop, float yBot, Material mat)
        {
            Mesh mesh = new Mesh { name = name + "_Mesh" };
            int n = ring.Count;

            Vector3[] verts = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                verts[i * 2] = new Vector3(ring[i].x, yTop, ring[i].y);
                verts[i * 2 + 1] = new Vector3(ring[i].x, yBot, ring[i].y);
            }

            int[] tris = new int[n * 12];
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                int t0 = i * 2;
                int b0 = i * 2 + 1;
                int t1 = next * 2;
                int b1 = next * 2 + 1;

                int o = i * 12;
                tris[o + 0] = t0; tris[o + 1] = t1; tris[o + 2] = b1;
                tris[o + 3] = t0; tris[o + 4] = b1; tris[o + 5] = b0;

                tris[o + 6] = t0; tris[o + 7] = b1; tris[o + 8] = t1;
                tris[o + 9] = t0; tris[o + 10] = b0; tris[o + 11] = b1;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _ownedMeshes.Add(mesh);

            CreateMeshChild(name, parent, mesh, Vector3.zero, mat);
        }

        private void CreateRingRibbonObject(string name, Transform parent, List<Vector2> ring, float y, float width, Material mat)
        {
            Mesh mesh = new Mesh { name = name + "_Mesh" };
            int n = ring.Count;
            Vector2 center = ComputeCentroid(ring);

            Vector3[] verts = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                Vector2 dir = (ring[i] - center).normalized;
                Vector2 outer = ring[i] + dir * (width * 0.5f);
                Vector2 inner = ring[i] - dir * (width * 0.5f);
                verts[i * 2] = new Vector3(outer.x, y, outer.y);
                verts[i * 2 + 1] = new Vector3(inner.x, y, inner.y);
            }

            int[] tris = new int[n * 12];
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                int o0 = i * 2;
                int i0 = i * 2 + 1;
                int o1 = next * 2;
                int i1 = next * 2 + 1;

                int idx = i * 12;
                tris[idx + 0] = o0; tris[idx + 1] = o1; tris[idx + 2] = i1;
                tris[idx + 3] = o0; tris[idx + 4] = i1; tris[idx + 5] = i0;

                tris[idx + 6] = o0; tris[idx + 7] = i1; tris[idx + 8] = o1;
                tris[idx + 9] = o0; tris[idx + 10] = i0; tris[idx + 11] = i1;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _ownedMeshes.Add(mesh);

            CreateMeshChild(name, parent, mesh, Vector3.zero, mat);
        }

        private Mesh BuildAlmondMesh(float halfW, float halfH, int segsPerArc)
        {
            Mesh mesh = new Mesh { name = "AlmondEye_Mesh" };
            int count = segsPerArc * 2;
            Vector3[] verts = new Vector3[count + 1];
            verts[0] = Vector3.zero;

            for (int i = 0; i < segsPerArc; i++)
            {
                float t = i / (float)segsPerArc;
                float x = Mathf.Lerp(-halfW, halfW, t);
                float y = Mathf.Sin(t * Mathf.PI) * halfH;
                verts[1 + i] = new Vector3(x, y, 0f);
            }
            for (int i = 0; i < segsPerArc; i++)
            {
                float t = i / (float)segsPerArc;
                float x = Mathf.Lerp(halfW, -halfW, t);
                float y = -Mathf.Sin(t * Mathf.PI) * halfH;
                verts[1 + segsPerArc + i] = new Vector3(x, y, 0f);
            }

            int[] tris = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                tris[i * 6 + 0] = 0;
                tris[i * 6 + 1] = 1 + i;
                tris[i * 6 + 2] = 1 + next;

                tris[i * 6 + 3] = 0;
                tris[i * 6 + 4] = 1 + next;
                tris[i * 6 + 5] = 1 + i;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _ownedMeshes.Add(mesh);
            return mesh;
        }

        private Mesh BuildDiamondFrameMesh(float outerR, float innerR)
        {
            Mesh mesh = new Mesh { name = "DiamondReticle_Mesh" };
            Vector3[] verts =
            {
                new Vector3(0f, outerR, 0f),
                new Vector3(outerR, 0f, 0f),
                new Vector3(0f, -outerR, 0f),
                new Vector3(-outerR, 0f, 0f),

                new Vector3(0f, innerR, 0f),
                new Vector3(innerR, 0f, 0f),
                new Vector3(0f, -innerR, 0f),
                new Vector3(-innerR, 0f, 0f),
            };

            int[] tris = new int[4 * 12];
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                int o0 = i;
                int o1 = next;
                int i0 = 4 + i;
                int i1 = 4 + next;

                int k = i * 12;
                tris[k + 0] = o0; tris[k + 1] = o1; tris[k + 2] = i1;
                tris[k + 3] = o0; tris[k + 4] = i1; tris[k + 5] = i0;

                tris[k + 6] = o0; tris[k + 7] = i1; tris[k + 8] = o1;
                tris[k + 9] = o0; tris[k + 10] = i0; tris[k + 11] = i1;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _ownedMeshes.Add(mesh);
            return mesh;
        }

        private static List<Vector2> MakeOrganicRing(float cx, float cz, float rx, float rz, int segs, float seed, float wobble)
        {
            List<Vector2> pts = new List<Vector2>(segs);
            for (int i = 0; i < segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                float mod = 1f + wobble * Mathf.Sin(a * 3f + seed) + (wobble * 0.55f) * Mathf.Cos(a * 5f - seed * 1.6f);
                pts.Add(new Vector2(cx + Mathf.Cos(a) * rx * mod, cz + Mathf.Sin(a) * rz * mod));
            }
            return pts;
        }

        private static List<Vector2> MakeBridgeRing(float x1, float z1, float x2, float z2, float halfWidth)
        {
            float dx = x2 - x1;
            float dz = z2 - z1;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 0.001f) len = 1f;
            float nx = -dz / len;
            float nz = dx / len;

            List<Vector2> pts = new List<Vector2>(16);
            int capSegs = 6;
            float startAngle = Mathf.Atan2(nz, nx);

            for (int i = 0; i <= capSegs; i++)
            {
                float a = startAngle + (i / (float)capSegs) * Mathf.PI;
                pts.Add(new Vector2(x1 + Mathf.Cos(a) * halfWidth, z1 + Mathf.Sin(a) * halfWidth));
            }
            for (int i = 0; i <= capSegs; i++)
            {
                float a = startAngle + Mathf.PI + (i / (float)capSegs) * Mathf.PI;
                pts.Add(new Vector2(x2 + Mathf.Cos(a) * halfWidth, z2 + Mathf.Sin(a) * halfWidth));
            }
            return pts;
        }

        private static Vector2 ComputeCentroid(List<Vector2> pts)
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < pts.Count; i++) c += pts[i];
            return pts.Count > 0 ? c / pts.Count : Vector2.zero;
        }

        private Material CreateUnlitMaterial(Color color)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Unlit/Color")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Standard");
            Material mat = new Material(sh);
            ApplyColorToMaterial(mat, color);
            _ownedMaterials.Add(mat);
            return mat;
        }

        private static void SetMatColor(Renderer rend, Color color)
        {
            if (rend == null || rend.sharedMaterial == null) return;
            ApplyColorToMaterial(rend.sharedMaterial, color);
        }

        private static void ApplyColorToMaterial(Material mat, Color color)
        {
            if (mat == null) return;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mat.color = color;
        }

        private void OnDestroy()
        {
            if (_fogHookRegistered)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
                _fogHookRegistered = false;
            }
            if (_miniCamera != null) _miniCamera.targetTexture = null;
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
            for (int i = 0; i < _ownedMaterials.Count; i++)
            {
                if (_ownedMaterials[i] != null) Destroy(_ownedMaterials[i]);
            }
            for (int i = 0; i < _ownedMeshes.Count; i++)
            {
                if (_ownedMeshes[i] != null) Destroy(_ownedMeshes[i]);
            }
        }
    }
}
