using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossTenia
{
    /// <summary>
    /// Procedural low-poly PS1 mesh builder for Boss Tenia (Option 3: Blind Scolex with Iron Nail Halo).
    /// All generated meshes are watertight, UV-mapped cleanly, and verified for
    /// Unity left-handed clockwise outward triangle winding.
    /// </summary>
    public static class TeniaMeshBuilder
    {
        private sealed class MeshBuilder
        {
            private readonly List<Vector3> _verts = new List<Vector3>(512);
            private readonly List<Vector2> _uvs = new List<Vector2>(512);
            private readonly List<Color> _colors = new List<Color>(512);
            private readonly List<int> _tris = new List<int>(1024);

            public int AddVertex(Vector3 pos, Vector2 uv)
            {
                return AddVertex(pos, uv, Color.white);
            }

            public int AddVertex(Vector3 pos, Vector2 uv, Color color)
            {
                int idx = _verts.Count;
                _verts.Add(pos);
                _uvs.Add(uv);
                _colors.Add(color);
                return idx;
            }

            public void AddTriangle(int a, int b, int c)
            {
                _tris.Add(a);
                _tris.Add(b);
                _tris.Add(c);
            }

            /// <summary>
            /// Adds a quad (a -> b -> c -> d). Winding produces outward normal along Cross(b - a, d - a).
            /// </summary>
            public void AddQuad(int a, int b, int c, int d)
            {
                _tris.Add(a);
                _tris.Add(b);
                _tris.Add(c);

                _tris.Add(a);
                _tris.Add(c);
                _tris.Add(d);
            }

            public Mesh ToMesh(string meshName)
            {
                Mesh mesh = new Mesh
                {
                    name = meshName
                };
                mesh.SetVertices(_verts);
                mesh.SetUVs(0, _uvs);
                mesh.SetColors(_colors);
                mesh.SetTriangles(_tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private readonly struct ProfileRing
        {
            public readonly float Z;
            public readonly float Radius;
            public readonly float AxialT;
            public readonly Color Tint;
            public readonly float LumpAmplitude;
            public readonly float FoldAmplitude;
            public readonly int FoldFrequency;

            public ProfileRing(
                float z,
                float radius,
                float axialT,
                Color tint,
                float lumpAmplitude = 0f,
                float foldAmplitude = 0f,
                int foldFrequency = 0)
            {
                Z = z;
                Radius = radius;
                AxialT = axialT;
                Tint = tint;
                LumpAmplitude = lumpAmplitude;
                FoldAmplitude = foldAmplitude;
                FoldFrequency = foldFrequency;
            }
        }

        /// <summary>
        /// 1. Blind scolex head oriented along +Z:
        /// Back neck attachment at z = -0.35f, bulbous diameter ~0.95m radius, circular mouth rim at z = +1.05f,
        /// and deep watertight inward-recessed throat cavity curving back inside to z = +0.15f.
        /// 16 radial segments x 14 length rings between poles.
        /// </summary>
        public static Mesh BuildScolexHeadMesh()
        {
            const int radialSegs = 16;

            Color exteriorCol = Color.white;
            Color lipCol = new Color(0.95f, 0.88f, 0.88f, 1f);
            Color midThroatCol = new Color(0.50f, 0.36f, 0.38f, 1f);
            Color deepThroatCol = new Color(0.18f, 0.12f, 0.14f, 1f);

            // Continuous closed profile: back neck pole -> outer bulbous head -> front mouth lip -> inner throat -> pit pole.
            // Uniform ring winding along this profile yields outward exterior normals and lumen-facing throat normals.
            Vector3 backPole = new Vector3(0f, 0f, -0.35f);
            Vector3 frontPole = new Vector3(0f, 0f, 0.15f);

            // 14 length rings between back pole (z = -0.35) and throat pit pole (z = +0.15)
            ProfileRing[] rings = new ProfileRing[]
            {
                new ProfileRing(-0.30f, 0.56f, 0.04f, exteriorCol, lumpAmplitude: 0.015f),
                new ProfileRing(-0.16f, 0.78f, 0.12f, exteriorCol, lumpAmplitude: 0.032f),
                new ProfileRing( 0.04f, 0.90f, 0.22f, exteriorCol, lumpAmplitude: 0.048f),
                new ProfileRing( 0.26f, 0.95f, 0.32f, exteriorCol, lumpAmplitude: 0.055f),
                new ProfileRing( 0.48f, 0.92f, 0.42f, exteriorCol, lumpAmplitude: 0.048f),
                new ProfileRing( 0.68f, 0.84f, 0.50f, exteriorCol, lumpAmplitude: 0.036f, foldAmplitude: 0.018f, foldFrequency: 8),
                new ProfileRing( 0.86f, 0.75f, 0.58f, exteriorCol, lumpAmplitude: 0.024f, foldAmplitude: 0.022f, foldFrequency: 8),
                new ProfileRing( 0.99f, 0.69f, 0.65f, lipCol,      lumpAmplitude: 0.012f, foldAmplitude: 0.020f, foldFrequency: 8),
                new ProfileRing( 1.05f, 0.61f, 0.70f, lipCol,      lumpAmplitude: 0.006f, foldAmplitude: 0.015f, foldFrequency: 8),
                new ProfileRing( 1.02f, 0.555f, 0.74f, lipCol,     lumpAmplitude: 0.004f),
                new ProfileRing( 0.85f, 0.385f, 0.81f, midThroatCol, lumpAmplitude: 0.005f),
                new ProfileRing( 0.62f, 0.29f, 0.88f, midThroatCol,  lumpAmplitude: 0.010f),
                new ProfileRing( 0.38f, 0.22f, 0.94f, deepThroatCol, lumpAmplitude: 0.008f),
                new ProfileRing( 0.20f, 0.14f, 0.98f, deepThroatCol, lumpAmplitude: 0.004f),
            };

            MeshBuilder mb = new MeshBuilder();
            AddClosedProfileSurface(
                mb,
                radialSegs,
                rings,
                backPole,
                0f,
                exteriorCol,
                frontPole,
                1f,
                deepThroatCol,
                axialAsU: true,
                (ringIdx, theta, ring) =>
                {
                    float organic =
                        Mathf.Sin(theta * 3f + ring.Z * 3.1f) * 0.55f +
                        Mathf.Cos(theta * 5f - ring.Z * 4.7f + 0.8f) * 0.30f +
                        Mathf.Sin(theta * 2f + 1.3f) * 0.15f;

                    // Subtle bulbous forehead asymmetry (+Y slightly fuller) on the exterior cranium
                    float foreheadBulge = (ringIdx >= 2 && ringIdx <= 6)
                        ? Mathf.Max(0f, Mathf.Sin(theta)) * 0.038f
                        : 0f;

                    float fold = (ring.FoldFrequency > 0)
                        ? Mathf.Cos(theta * ring.FoldFrequency) * ring.FoldAmplitude
                        : 0f;

                    float r = ring.Radius + organic * ring.LumpAmplitude + foreheadBulge + fold;
                    float zJitter = (ringIdx >= 1 && ringIdx <= 6)
                        ? Mathf.Sin(theta * 3f + ringIdx) * 0.014f
                        : 0f;

                    return new Vector3(r * Mathf.Cos(theta), r * Mathf.Sin(theta), ring.Z + zJitter);
                });

            return mb.ToMesh("Mesh_Tenia_ScolexHead");
        }

        /// <summary>
        /// 2. Two concentric rings of sharp conical/curved inward-pointing teeth:
        /// 16 outer teeth around r ~ 0.56f at z = 1.02f + 12 inner smaller teeth around r ~ 0.38f at z = 0.85f.
        /// </summary>
        public static Mesh BuildTeethRingMesh()
        {
            MeshBuilder mb = new MeshBuilder();

            // Outer ring: 16 larger curved fangs around the mouth lip
            const int outerCount = 16;
            for (int i = 0; i < outerCount; i++)
            {
                float theta = (i / (float)outerCount) * Mathf.PI * 2f;
                float sizeJitter = 1f + 0.10f * Mathf.Sin(i * 2.37f + 0.5f);
                float angleJitter = 0.04f * Mathf.Cos(i * 1.91f);

                Vector3 radialOut = new Vector3(Mathf.Cos(theta + angleJitter), Mathf.Sin(theta + angleJitter), 0f);
                Vector3 rootPos = radialOut * 0.575f + new Vector3(0f, 0f, 1.02f);

                // Point inward toward center axis and slightly forward (+Z)
                Vector3 initialDir = (-radialOut * 0.90f + Vector3.forward * 0.44f).normalized;
                Vector3 curveDir = (-radialOut * 0.72f + Vector3.forward * 0.69f).normalized;

                AddCurvedTooth(
                    mb,
                    rootPos,
                    initialDir,
                    curveDir,
                    toothLength: 0.255f * sizeJitter,
                    baseRadius: 0.066f * (0.94f + 0.06f * sizeJitter),
                    sides: 5,
                    rollAngle: theta);
            }

            // Inner ring: 12 smaller deeper fangs staggered around the inner throat
            const int innerCount = 12;
            for (int i = 0; i < innerCount; i++)
            {
                float theta = ((i + 0.5f) / innerCount) * Mathf.PI * 2f;
                float sizeJitter = 1f + 0.10f * Mathf.Cos(i * 2.71f + 1.1f);

                Vector3 radialOut = new Vector3(Mathf.Cos(theta), Mathf.Sin(theta), 0f);
                Vector3 rootPos = radialOut * 0.395f + new Vector3(0f, 0f, 0.85f);

                Vector3 initialDir = (-radialOut * 0.93f + Vector3.forward * 0.36f).normalized;
                Vector3 curveDir = (-radialOut * 0.78f + Vector3.forward * 0.62f).normalized;

                AddCurvedTooth(
                    mb,
                    rootPos,
                    initialDir,
                    curveDir,
                    toothLength: 0.185f * sizeJitter,
                    baseRadius: 0.048f * (0.95f + 0.05f * sizeJitter),
                    sides: 5,
                    rollAngle: theta + 0.3f);
            }

            return mb.ToMesh("Mesh_Tenia_TeethRing");
        }

        /// <summary>
        /// 3. Option 3 Iron Nail Halo around z = 0.22f:
        /// - Thick angular forged iron ring (majorRadius = 1.18f, tubeRadius = 0.065f, 16 x 6 segments), slightly tilted/irregular.
        /// - 14 heavy forged iron nails piercing inward through the ring into the flesh (flat square heads at outer tips r = 1.65f..1.95f, sharp spikes inward to r = 0.65f..0.78f).
        /// - 5 smaller iron nails driven directly into the forehead and cheeks around the halo.
        /// </summary>
        public static Mesh BuildIronHaloMesh()
        {
            MeshBuilder mb = new MeshBuilder();

            const float ringCenterZ = 0.22f;
            const float majorRadius = 1.18f;
            const float tubeRadius = 0.065f;
            const int majorSegs = 16;
            const int minorSegs = 6;

            // Slight hammered halo tilt so the crown sits organically around the scolex brow
            Quaternion haloTilt = Quaternion.Euler(4.5f, -3.0f, 2.5f);
            Vector3 haloOrigin = new Vector3(0f, 0.03f, ringCenterZ);

            Vector3 EvalRingPoint(float theta, out Vector3 radialOutDir)
            {
                float rHammered = majorRadius +
                    0.024f * Mathf.Sin(theta * 3f + 0.6f) +
                    0.015f * Mathf.Cos(theta * 5f - 1.1f);
                float zHammered =
                    0.022f * Mathf.Cos(theta * 2f + 0.3f) +
                    0.012f * Mathf.Sin(theta * 4f + 1.4f);

                Vector3 localRingPos = new Vector3(rHammered * Mathf.Cos(theta), rHammered * Mathf.Sin(theta), zHammered);
                Vector3 localRadial = new Vector3(Mathf.Cos(theta), Mathf.Sin(theta), 0f);
                radialOutDir = (haloTilt * localRadial).normalized;
                return haloOrigin + haloTilt * localRingPos;
            }

            // a) Thick angular forged iron ring (16 x 6 watertight torus)
            int[,] ringVerts = new int[majorSegs + 1, minorSegs + 1];
            for (int i = 0; i <= majorSegs; i++)
            {
                int iWrapped = (i == majorSegs) ? 0 : i;
                float theta = (iWrapped / (float)majorSegs) * Mathf.PI * 2f;

                Vector3 ringPos = EvalRingPoint(theta, out Vector3 radialOut);
                Vector3 forwardAxis = (haloTilt * Vector3.forward).normalized;

                Vector3 tangent = Vector3.Cross(forwardAxis, radialOut).normalized;
                Vector3 binormal = Vector3.Cross(radialOut, tangent).normalized;

                for (int j = 0; j <= minorSegs; j++)
                {
                    int jWrapped = (j == minorSegs) ? 0 : j;
                    float phi = (jWrapped / (float)minorSegs) * Mathf.PI * 2f;

                    float localTubeR = tubeRadius * (1f + 0.09f * Mathf.Cos(phi * 2f + iWrapped * 0.9f));
                    Vector3 offset = (radialOut * Mathf.Cos(phi) + binormal * Mathf.Sin(phi)) * localTubeR;

                    Vector2 uv = new Vector2(i / (float)majorSegs, j / (float)minorSegs);
                    ringVerts[i, j] = mb.AddVertex(ringPos + offset, uv);
                }
            }

            for (int i = 0; i < majorSegs; i++)
            {
                for (int j = 0; j < minorSegs; j++)
                {
                    int a = ringVerts[i, j];
                    int b = ringVerts[i + 1, j];
                    int c = ringVerts[i + 1, j + 1];
                    int d = ringVerts[i, j + 1];
                    mb.AddQuad(a, b, c, d);
                }
            }

            // b) 14 heavy forged iron nails radiating outward through the ring (outer flat head, sharp spike piercing inward into flesh)
            const int haloNailCount = 14;
            for (int k = 0; k < haloNailCount; k++)
            {
                float baseTheta = (k / (float)haloNailCount) * Mathf.PI * 2f + 0.11f;
                float theta = baseTheta + 0.045f * Mathf.Sin(k * 3.17f);

                Vector3 ringPoint = EvalRingPoint(theta, out Vector3 radialOut);
                Vector3 tangentDir = Vector3.Cross((haloTilt * Vector3.forward).normalized, radialOut).normalized;
                Vector3 ringNormalDir = Vector3.Cross(radialOut, tangentDir).normalized;

                Vector3 spikeInwardDir = (
                    -radialOut +
                    ringNormalDir * (0.055f * Mathf.Sin(k * 2.13f + 0.4f)) +
                    tangentDir * (0.035f * Mathf.Cos(k * 1.79f))
                ).normalized;

                // Nail spans from outer head (r = 1.67f..1.91f) through ring (r ~ 1.18f) to sharp point inside head (r = 0.65f..0.79f)
                float outerDistFromRing = 0.49f + 0.24f * (0.5f + 0.5f * Mathf.Sin(k * 2.41f + 0.7f));
                float nailLength = 0.94f + 0.20f * (0.5f + 0.5f * Mathf.Cos(k * 1.93f + 0.2f));

                Vector3 outerHeadCenter = ringPoint - spikeInwardDir * outerDistFromRing;
                float headHalfW = 0.068f + 0.012f * Mathf.Sin(k * 1.7f);
                float roll = k * 0.47f;

                AddForgedNail(
                    mb,
                    outerHeadCenter,
                    spikeInwardDir,
                    totalLength: nailLength,
                    headHalfWidth: headHalfW,
                    headThickness: 0.052f,
                    shaftTopHalfWidth: 0.034f,
                    shaftTipHalfWidth: 0.011f,
                    rollAngle: roll);
            }

            // c) 5 smaller iron nails driven straight into the forehead/cheeks in front of the halo
            float[] browAnglesDeg = new float[] { 22f, 58f, 92f, 126f, 158f };
            float[] browZOffsets = new float[] { 0.44f, 0.56f, 0.50f, 0.54f, 0.42f };
            float[] browOutDist = new float[] { 1.03f, 1.06f, 1.08f, 1.05f, 1.02f };

            for (int n = 0; n < browAnglesDeg.Length; n++)
            {
                float rad = browAnglesDeg[n] * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);

                Vector3 headCenter = radial * browOutDist[n] + new Vector3(0f, 0f, browZOffsets[n]);
                Vector3 inwardTarget = new Vector3(0f, 0f, 0.34f);
                Vector3 spikeDir = (inwardTarget - headCenter).normalized;

                AddForgedNail(
                    mb,
                    headCenter,
                    spikeDir,
                    totalLength: 0.38f + 0.04f * Mathf.Sin(n * 1.9f),
                    headHalfWidth: 0.044f,
                    headThickness: 0.032f,
                    shaftTopHalfWidth: 0.022f,
                    shaftTipHalfWidth: 0.008f,
                    rollAngle: 0.35f + n * 0.6f);
            }

            return mb.ToMesh("Mesh_Tenia_IronHalo");
        }

        /// <summary>
        /// 4. Plump segmented tapeworm proglottid oriented along Z axis:
        /// Centered at (0,0,0), spanning z = -length * 0.62f to z = +length * 0.62f with rounded overlapping end caps.
        /// 16 radial segments x 8 length rings with slight end pinch and deterministic organic deformation from seed.
        /// </summary>
        public static Mesh BuildBodySegmentMesh(float radius, float length, int seed)
        {
            radius = Mathf.Max(0.05f, radius);
            length = Mathf.Max(0.05f, length);

            const int radialSegs = 16;
            float halfSpan = length * 0.62f;

            float p1 = (seed * 1.713f) % (Mathf.PI * 2f);
            float p2 = (seed * 2.941f + 1.1f) % (Mathf.PI * 2f);
            float p3 = (seed * 4.319f + 2.3f) % (Mathf.PI * 2f);

            float[] tNorm = new float[] { -0.92f, -0.74f, -0.48f, -0.18f, 0.10f, 0.40f, 0.70f, 0.91f };
            float[] rScale = new float[] { 0.48f, 0.80f, 0.97f, 1.04f, 0.97f, 1.03f, 0.85f, 0.50f };

            ProfileRing[] rings = new ProfileRing[tNorm.Length];
            for (int i = 0; i < tNorm.Length; i++)
            {
                float t = tNorm[i];
                float axialU = (t + 1f) * 0.5f;
                float lumpAmp = (i >= 2 && i <= 5) ? 0.065f : 0.022f;
                rings[i] = new ProfileRing(t * halfSpan, radius * rScale[i], axialU, Color.white, lumpAmplitude: lumpAmp);
            }

            Vector3 backPole = new Vector3(0f, 0f, -halfSpan);
            Vector3 frontPole = new Vector3(0f, 0f, halfSpan);

            MeshBuilder mb = new MeshBuilder();
            AddClosedProfileSurface(
                mb,
                radialSegs,
                rings,
                backPole,
                0f,
                Color.white,
                frontPole,
                1f,
                Color.white,
                axialAsU: true,
                (ringIdx, theta, ring) =>
                {
                    float t = tNorm[ringIdx];
                    float squashX = 1.04f;
                    float squashY = 0.96f;

                    float lump =
                        Mathf.Sin(theta * 3f + p1 + t * 2.2f) * 0.50f +
                        Mathf.Cos(theta * 5f + p2 - t * 3.1f) * 0.30f +
                        Mathf.Sin(theta * 2f + p3) * 0.20f;

                    float r = ring.Radius * (1f + lump * ring.LumpAmplitude);

                    float midMask = Mathf.Max(0f, 1f - t * t * 1.4f);
                    float offsetX = Mathf.Sin(p1 + t * 2.5f) * radius * 0.045f * midMask;
                    float offsetY = Mathf.Cos(p2 + t * 2.0f) * radius * 0.035f * midMask;

                    return new Vector3(
                        r * squashX * Mathf.Cos(theta) + offsetX,
                        r * squashY * Mathf.Sin(theta) + offsetY,
                        ring.Z);
                });

            return mb.ToMesh($"Mesh_Tenia_BodySegment_{seed}");
        }

        /// <summary>
        /// 5. Lumpy glowing gastric organ sac protruding through the lower/lateral (+Y / +-X) belly
        /// of a mid-body segment (reaches radius * 1.06f+ on +Y/+-X lobes, tucked inside on -Y).
        /// </summary>
        public static Mesh BuildAcidOrganMesh(float radius, float length)
        {
            radius = Mathf.Max(0.05f, radius);
            length = Mathf.Max(0.05f, length);

            const int radialSegs = 16;
            float halfSpan = length * 0.46f;

            float[] tNorm = new float[] { -0.90f, -0.70f, -0.45f, -0.18f, 0.12f, 0.42f, 0.68f, 0.89f };
            float[] rEnv = new float[] { 0.44f, 0.78f, 0.96f, 1.03f, 0.94f, 1.02f, 0.84f, 0.46f };

            ProfileRing[] rings = new ProfileRing[tNorm.Length];
            for (int i = 0; i < tNorm.Length; i++)
            {
                float t = tNorm[i];
                rings[i] = new ProfileRing(t * halfSpan, radius * rEnv[i], (t + 1f) * 0.5f, Color.white);
            }

            Vector3 backPole = new Vector3(0f, 0f, -halfSpan);
            Vector3 frontPole = new Vector3(0f, 0f, halfSpan);

            MeshBuilder mb = new MeshBuilder();
            AddClosedProfileSurface(
                mb,
                radialSegs,
                rings,
                backPole,
                0f,
                Color.white,
                frontPole,
                1f,
                Color.white,
                axialAsU: false,
                (ringIdx, theta, ring) =>
                {
                    float t = tNorm[ringIdx];
                    float sinT = Mathf.Sin(theta);
                    float cosT = Mathf.Cos(theta);

                    float sideMask = SmoothRange(-0.85f, 0.15f, sinT);
                    float lateralBoost = 0.035f * Mathf.Abs(cosT);

                    float bladderLobes =
                        0.55f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 1.5f + 0.35f)) +
                        0.45f * (0.5f + 0.5f * Mathf.Sin(theta * 3f + t * 4.0f));

                    float radialFactor = Mathf.Lerp(0.76f, 1.06f, sideMask) + (lateralBoost + 0.065f * bladderLobes) * sideMask;
                    float r = ring.Radius * radialFactor;

                    float midWeight = Mathf.Max(0f, 1f - t * t);
                    float yShift = radius * 0.05f * midWeight;

                    return new Vector3(r * cosT, r * sinT + yShift, ring.Z);
                });

            return mb.ToMesh("Mesh_Tenia_AcidOrgan");
        }

        /// <summary>
        /// 6. Tapering segmented tail cone from z = +length * 0.5f down to a blunt rounded tip at z = -length * 1.2f.
        /// </summary>
        public static Mesh BuildTailSegmentMesh(float baseRadius, float length)
        {
            baseRadius = Mathf.Max(0.05f, baseRadius);
            length = Mathf.Max(0.05f, length);

            const int radialSegs = 14;

            float zFront = length * 0.50f;
            float zBack = -length * 1.20f;

            float[] uPosBackToFront = new float[] { 0.97f, 0.90f, 0.78f, 0.64f, 0.48f, 0.32f, 0.18f, 0.07f, 0.02f };
            float[] rProfile = new float[]        { 0.16f, 0.25f, 0.37f, 0.50f, 0.65f, 0.79f, 0.92f, 0.96f, 0.58f };

            ProfileRing[] rings = new ProfileRing[uPosBackToFront.Length];
            for (int i = 0; i < uPosBackToFront.Length; i++)
            {
                float u = uPosBackToFront[i];
                float z = Mathf.Lerp(zFront, zBack, u);
                float segCrease = (i >= 1 && i <= 6) ? (1f + 0.045f * Mathf.Sin(u * Mathf.PI * 5f)) : 1f;
                rings[i] = new ProfileRing(z, baseRadius * rProfile[i] * segCrease, 1f - u, Color.white);
            }

            Vector3 backTipPole = new Vector3(0f, 0f, zBack);
            Vector3 frontCapPole = new Vector3(0f, 0f, zFront);

            MeshBuilder mb = new MeshBuilder();
            AddClosedProfileSurface(
                mb,
                radialSegs,
                rings,
                backTipPole,
                0f,
                Color.white,
                frontCapPole,
                1f,
                Color.white,
                axialAsU: true,
                (ringIdx, theta, ring) =>
                {
                    float organic = 1f + 0.04f * Mathf.Sin(theta * 3f + ringIdx * 1.2f);
                    float r = ring.Radius * organic;
                    return new Vector3(r * Mathf.Cos(theta), r * Mathf.Sin(theta), ring.Z);
                });

            return mb.ToMesh("Mesh_Tenia_TailSegment");
        }

        /// <summary>
        /// 7. Tattered ecclesiastical priest stole hanging straight DOWN along -Y from anchor (0,0,0) to y = -length.
        /// Watertight thin 2-sided slab with wave curvature, jagged torn bottom teeth/fringes,
        /// and clean (0..1, 0..1) top-to-bottom UVs (v=1 at top anchor, v=0 at bottom tip).
        /// </summary>
        public static Mesh BuildVestmentBannerMesh(float width, float length, int seed)
        {
            width = Mathf.Max(0.04f, width);
            length = Mathf.Max(0.05f, length);

            const int cols = 6;
            const int rows = 7;

            float thickness = Mathf.Max(0.014f, width * 0.038f);
            float halfThick = thickness * 0.5f;

            float s1 = (seed * 1.91f) % (Mathf.PI * 2f);
            float s2 = (seed * 3.37f + 0.7f) % (Mathf.PI * 2f);

            // Jagged torn bottom lengths per column in [0.80, 1.00] so gold hem border stays intact above ragged teeth
            float[] bottomFrac = new float[cols + 1];
            for (int c = 0; c <= cols; c++)
            {
                bool isNotch = (c == 2 || c == 4);
                float baseTooth = isNotch ? 0.83f : (c == 0 || c == cols ? 0.88f : 0.96f);
                float jitter = 0.035f * Mathf.Sin(c * 2.13f + s1);
                bottomFrac[c] = Mathf.Clamp(baseTooth + jitter, 0.80f, 1.0f);
            }
            bottomFrac[3] = 1.0f;

            Vector3[,] centerGrid = new Vector3[rows + 1, cols + 1];
            Vector3[,] frontPos = new Vector3[rows + 1, cols + 1];
            Vector3[,] backPos = new Vector3[rows + 1, cols + 1];
            Vector2[,] uvGrid = new Vector2[rows + 1, cols + 1];

            for (int r = 0; r <= rows; r++)
            {
                float rowT = r / (float)rows;
                for (int c = 0; c <= cols; c++)
                {
                    float u = c / (float)cols;
                    float xNorm = u - 0.5f;

                    float colLength = length * bottomFrac[c];
                    float y = -rowT * colLength;

                    float gather = Mathf.Lerp(0.84f, 1.06f, SmoothRange(0f, 0.35f, rowT));
                    float edgeFray = (rowT > 0.55f && (c == 0 || c == cols))
                        ? Mathf.Sin(rowT * 14f + s2 + c) * width * 0.045f
                        : 0f;
                    float swayX = Mathf.Sin(rowT * Mathf.PI * 1.2f + s1) * width * 0.06f * rowT;
                    float x = xNorm * width * gather + swayX + edgeFray;

                    float zWave = Mathf.Sin(rowT * Mathf.PI * 1.7f + s2) * width * 0.12f * SmoothRange(0f, 0.25f, rowT);
                    float edgeCurl = Mathf.Cos(xNorm * Mathf.PI) * width * 0.03f * rowT;

                    centerGrid[r, c] = new Vector3(x, y, zWave + edgeCurl);
                    uvGrid[r, c] = new Vector2(u, Mathf.Clamp01(1f + y / length));
                }
            }

            for (int r = 0; r <= rows; r++)
            {
                for (int c = 0; c <= cols; c++)
                {
                    int rPrev = Mathf.Max(0, r - 1);
                    int rNext = Mathf.Min(rows, r + 1);
                    int cPrev = Mathf.Max(0, c - 1);
                    int cNext = Mathf.Min(cols, c + 1);

                    Vector3 dDown = centerGrid[rNext, c] - centerGrid[rPrev, c];
                    Vector3 dRight = centerGrid[r, cNext] - centerGrid[r, cPrev];
                    Vector3 n = Vector3.Cross(dDown, dRight);
                    if (n.sqrMagnitude < 1e-6f) n = Vector3.forward;
                    n.Normalize();

                    frontPos[r, c] = centerGrid[r, c] + n * halfThick;
                    backPos[r, c] = centerGrid[r, c] - n * halfThick;
                }
            }

            MeshBuilder mb = new MeshBuilder();

            int[,] frontVerts = new int[rows + 1, cols + 1];
            for (int r = 0; r <= rows; r++)
            {
                for (int c = 0; c <= cols; c++)
                {
                    frontVerts[r, c] = mb.AddVertex(frontPos[r, c], uvGrid[r, c]);
                }
            }

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int tl = frontVerts[r, c];
                    int tr = frontVerts[r, c + 1];
                    int bl = frontVerts[r + 1, c];
                    int br = frontVerts[r + 1, c + 1];

                    mb.AddTriangle(tl, bl, br);
                    mb.AddTriangle(tl, br, tr);
                }
            }

            int[,] backVerts = new int[rows + 1, cols + 1];
            for (int r = 0; r <= rows; r++)
            {
                for (int c = 0; c <= cols; c++)
                {
                    backVerts[r, c] = mb.AddVertex(backPos[r, c], uvGrid[r, c]);
                }
            }

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int tl = backVerts[r, c];
                    int tr = backVerts[r, c + 1];
                    int bl = backVerts[r + 1, c];
                    int br = backVerts[r + 1, c + 1];

                    mb.AddTriangle(tl, br, bl);
                    mb.AddTriangle(tl, tr, br);
                }
            }

            List<(int r, int c)> rim = new List<(int r, int c)>((rows + cols) * 2);
            for (int r = 0; r < rows; r++) rim.Add((r, 0));
            for (int c = 0; c < cols; c++) rim.Add((rows, c));
            for (int r = rows; r > 0; r--) rim.Add((r, cols));
            for (int c = cols; c > 0; c--) rim.Add((0, c));

            int rimCount = rim.Count;
            int[] rimFront = new int[rimCount];
            int[] rimBack = new int[rimCount];
            for (int k = 0; k < rimCount; k++)
            {
                var (rr, cc) = rim[k];
                rimFront[k] = mb.AddVertex(frontPos[rr, cc], uvGrid[rr, cc]);
                rimBack[k] = mb.AddVertex(backPos[rr, cc], uvGrid[rr, cc]);
            }

            for (int k = 0; k < rimCount; k++)
            {
                int next = (k + 1) % rimCount;
                int f0 = rimFront[k];
                int f1 = rimFront[next];
                int b0 = rimBack[k];
                int b1 = rimBack[next];

                mb.AddTriangle(f0, b0, b1);
                mb.AddTriangle(f0, b1, f1);
            }

            return mb.ToMesh($"Mesh_Tenia_VestmentBanner_{seed}");
        }

        /// <summary>
        /// 8. Single heavy forged iron nail to pin vestments into worm segments.
        /// Pivot (0,0,0) sits at the collar right under the square flat head:
        /// head protrudes along +Z (0 to +headThickness) and tapered square spike plunges inward along -Z.
        /// </summary>
        public static Mesh BuildBodyNailMesh(float length)
        {
            length = Mathf.Max(0.05f, length);
            MeshBuilder mb = new MeshBuilder();

            float headThickness = Mathf.Clamp(length * 0.14f, 0.025f, 0.075f);
            float headHalfW = Mathf.Clamp(length * 0.16f, 0.035f, 0.095f);
            float shaftTopW = headHalfW * 0.48f;
            float shaftTipW = headHalfW * 0.16f;

            Vector3 outerHeadCenter = new Vector3(0f, 0f, headThickness);
            AddForgedNail(
                mb,
                outerHeadCenter,
                spikeInwardDir: Vector3.back,
                totalLength: length,
                headHalfWidth: headHalfW,
                headThickness: headThickness,
                shaftTopHalfWidth: shaftTopW,
                shaftTipHalfWidth: shaftTipW,
                rollAngle: 0f);

            return mb.ToMesh("Mesh_Tenia_BodyNail");
        }

        /// <summary>
        /// Builds a closed, watertight latitude-longitude surface from backPole through rings to frontPole.
        /// Verified outward winding in Unity left-handed coordinates.
        /// </summary>
        private static void AddClosedProfileSurface(
            MeshBuilder mb,
            int radialSegs,
            ProfileRing[] rings,
            Vector3 backPole,
            float backAxialT,
            Color backTint,
            Vector3 frontPole,
            float frontAxialT,
            Color frontTint,
            bool axialAsU,
            Func<int, float, ProfileRing, Vector3> evalPosition)
        {
            Vector2 MakeUv(float radialT, float axialT)
            {
                return axialAsU ? new Vector2(axialT, radialT) : new Vector2(radialT, axialT);
            }

            int ringCount = rings.Length;
            int backPoleIdx = mb.AddVertex(backPole, MakeUv(0.5f, backAxialT), backTint);

            int[,] grid = new int[ringCount, radialSegs + 1];
            for (int i = 0; i < ringCount; i++)
            {
                ProfileRing ring = rings[i];
                for (int j = 0; j <= radialSegs; j++)
                {
                    int jWrapped = (j == radialSegs) ? 0 : j;
                    float theta = (jWrapped / (float)radialSegs) * Mathf.PI * 2f;
                    Vector3 pos = evalPosition(i, theta, ring);
                    Vector2 uv = MakeUv(j / (float)radialSegs, ring.AxialT);
                    grid[i, j] = mb.AddVertex(pos, uv, ring.Tint);
                }
            }

            int frontPoleIdx = mb.AddVertex(frontPole, MakeUv(0.5f, frontAxialT), frontTint);

            for (int j = 0; j < radialSegs; j++)
            {
                mb.AddTriangle(backPoleIdx, grid[0, j + 1], grid[0, j]);
            }

            for (int i = 0; i < ringCount - 1; i++)
            {
                for (int j = 0; j < radialSegs; j++)
                {
                    int a = grid[i, j];
                    int b = grid[i + 1, j];
                    int c = grid[i + 1, j + 1];
                    int d = grid[i, j + 1];
                    mb.AddQuad(a, d, c, b);
                }
            }

            int lastRing = ringCount - 1;
            for (int j = 0; j < radialSegs; j++)
            {
                mb.AddTriangle(grid[lastRing, j], grid[lastRing, j + 1], frontPoleIdx);
            }
        }

        private static void AddCurvedTooth(
            MeshBuilder mb,
            Vector3 rootPos,
            Vector3 initialDir,
            Vector3 curveDir,
            float toothLength,
            float baseRadius,
            int sides,
            float rollAngle)
        {
            Vector3 midPos = rootPos + initialDir * (toothLength * 0.54f);
            Vector3 tipPos = midPos + curveDir * (toothLength * 0.50f);

            BuildBasis(initialDir, rollAngle, out Vector3 r0, out Vector3 u0);
            Vector3 midTangent = (tipPos - rootPos).normalized;
            BuildBasis(midTangent, rollAngle, out Vector3 r1, out Vector3 u1);

            Vector3[] ring0 = new Vector3[sides];
            Vector3[] ring1 = new Vector3[sides];
            for (int s = 0; s < sides; s++)
            {
                float a = (s / (float)sides) * Mathf.PI * 2f;
                float ca = Mathf.Cos(a);
                float sa = Mathf.Sin(a);
                ring0[s] = rootPos + (r0 * ca + u0 * sa) * baseRadius;
                ring1[s] = midPos + (r1 * ca + u1 * sa) * (baseRadius * 0.44f);
            }

            int rootCenterIdx = mb.AddVertex(rootPos, new Vector2(0.5f, 0f));
            int[] rootCapIdx = new int[sides];
            for (int s = 0; s < sides; s++)
            {
                rootCapIdx[s] = mb.AddVertex(ring0[s], new Vector2(s / (float)sides, 0f));
            }
            for (int s = 0; s < sides; s++)
            {
                int next = (s + 1) % sides;
                mb.AddTriangle(rootCenterIdx, rootCapIdx[next], rootCapIdx[s]);
            }

            for (int s = 0; s < sides; s++)
            {
                int next = (s + 1) % sides;
                float uA = s / (float)sides;
                float uB = (s + 1) / (float)sides;

                int a0 = mb.AddVertex(ring0[s], new Vector2(uA, 0.0f));
                int a1 = mb.AddVertex(ring0[next], new Vector2(uB, 0.0f));
                int b0 = mb.AddVertex(ring1[s], new Vector2(uA, 0.55f));
                int b1 = mb.AddVertex(ring1[next], new Vector2(uB, 0.55f));

                mb.AddTriangle(a0, a1, b1);
                mb.AddTriangle(a0, b1, b0);

                int c0 = mb.AddVertex(ring1[s], new Vector2(uA, 0.55f));
                int c1 = mb.AddVertex(ring1[next], new Vector2(uB, 0.55f));
                int tip = mb.AddVertex(tipPos, new Vector2((uA + uB) * 0.5f, 1.0f));

                mb.AddTriangle(c0, c1, tip);
            }
        }

        private static void AddForgedNail(
            MeshBuilder mb,
            Vector3 outerHeadCenter,
            Vector3 spikeInwardDir,
            float totalLength,
            float headHalfWidth,
            float headThickness,
            float shaftTopHalfWidth,
            float shaftTipHalfWidth,
            float rollAngle)
        {
            Vector3 axis = spikeInwardDir.normalized;
            BuildBasis(axis, rollAngle, out Vector3 right, out Vector3 up);

            Vector3[] SquareCorners(float s, float hw)
            {
                Vector3 center = outerHeadCenter + axis * s;
                return new Vector3[]
                {
                    center + ( right + up) * hw,
                    center + (-right + up) * hw,
                    center + (-right - up) * hw,
                    center + ( right - up) * hw
                };
            }

            float collarS = headThickness;
            float midShaftS = headThickness + (totalLength - headThickness) * 0.45f;
            float tipBaseS = totalLength * 0.84f;
            float midShaftW = Mathf.Lerp(shaftTopHalfWidth, shaftTipHalfWidth, 0.45f);

            Vector3[] headOuter = SquareCorners(0f, headHalfWidth);
            Vector3[] headInner = SquareCorners(collarS, headHalfWidth * 0.94f);
            Vector3[] shaftTop = SquareCorners(collarS, shaftTopHalfWidth);
            Vector3[] shaftMid = SquareCorners(midShaftS, midShaftW);
            Vector3[] shaftTipBase = SquareCorners(tipBaseS, shaftTipHalfWidth);
            Vector3 spikeTip = outerHeadCenter + axis * totalLength;

            int h0 = mb.AddVertex(headOuter[0], new Vector2(1f, 1f));
            int h1 = mb.AddVertex(headOuter[1], new Vector2(0f, 1f));
            int h2 = mb.AddVertex(headOuter[2], new Vector2(0f, 0f));
            int h3 = mb.AddVertex(headOuter[3], new Vector2(1f, 0f));
            mb.AddTriangle(h0, h3, h2);
            mb.AddTriangle(h0, h2, h1);

            AddFacetedSquareBand(mb, headOuter, headInner, 0.00f, 0.14f);
            AddFacetedSquareBand(mb, headInner, shaftTop, 0.14f, 0.22f);
            AddFacetedSquareBand(mb, shaftTop, shaftMid, 0.22f, 0.55f);
            AddFacetedSquareBand(mb, shaftMid, shaftTipBase, 0.55f, 0.85f);

            for (int k = 0; k < 4; k++)
            {
                int next = (k + 1) % 4;
                int a0 = mb.AddVertex(shaftTipBase[k], new Vector2(0f, 0.85f));
                int a1 = mb.AddVertex(shaftTipBase[next], new Vector2(1f, 0.85f));
                int tip = mb.AddVertex(spikeTip, new Vector2(0.5f, 1.0f));
                mb.AddTriangle(a0, a1, tip);
            }
        }

        private static void AddFacetedSquareBand(MeshBuilder mb, Vector3[] ringA, Vector3[] ringB, float vA, float vB)
        {
            for (int k = 0; k < 4; k++)
            {
                int next = (k + 1) % 4;
                int a0 = mb.AddVertex(ringA[k], new Vector2(0f, vA));
                int a1 = mb.AddVertex(ringA[next], new Vector2(1f, vA));
                int b0 = mb.AddVertex(ringB[k], new Vector2(0f, vB));
                int b1 = mb.AddVertex(ringB[next], new Vector2(1f, vB));

                mb.AddTriangle(a0, a1, b1);
                mb.AddTriangle(a0, b1, b0);
            }
        }

        private static void BuildBasis(Vector3 forwardAxis, float rollAngle, out Vector3 right, out Vector3 up)
        {
            Vector3 refUp = (Mathf.Abs(Vector3.Dot(forwardAxis, Vector3.up)) > 0.92f)
                ? Vector3.forward
                : Vector3.up;

            Vector3 baseRight = Vector3.Cross(refUp, forwardAxis).normalized;
            Vector3 baseUp = Vector3.Cross(forwardAxis, baseRight).normalized;

            float c = Mathf.Cos(rollAngle);
            float s = Mathf.Sin(rollAngle);
            right = baseRight * c + baseUp * s;
            up = -baseRight * s + baseUp * c;
        }

        private static float SmoothRange(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
