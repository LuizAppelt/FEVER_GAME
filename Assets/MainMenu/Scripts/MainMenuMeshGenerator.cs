using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StomachAbyss.MainMenu
{
    /// <summary>
    /// Procedural 3D mesh generator for the Stomach Abyss Main Menu scene.
    /// Recreates the colossal suspended glowing heart, outer gnarled myocardial vessel cage,
    /// crown aortic trunks, recursive vascular branch canopy, chiseled blood crystal monoliths,
    /// terraced foreground/midground terrain, and concentric ribbed cavern dome.
    /// </summary>
    public static class MainMenuMeshGenerator
    {
        // =====================================================================
        // 1. HEART INNER CORE MESH
        // =====================================================================

        /// <summary>
        /// Builds the anatomical tapered inner heart core mesh:
        /// wider upper lobes/atria, pointed lower apex/ventricle, and organic cellular surface bumps.
        /// Local bounds: approx Y in [-2.8, +2.4], X/Z radius ~1.9 at upper lobes.
        /// </summary>
        public static Mesh BuildHeartInnerCoreMesh()
        {
            const int radialSegments = 52;
            const int verticalRings = 48;
            const float minY = -2.80f;
            const float maxY = 2.35f;

            var verts = new List<Vector3>((radialSegments + 1) * (verticalRings + 1));
            var uvs = new List<Vector2>((radialSegments + 1) * (verticalRings + 1));
            var colors = new List<Color>((radialSegments + 1) * (verticalRings + 1));
            var tris = new List<int>(radialSegments * verticalRings * 6);

            for (int iy = 0; iy <= verticalRings; iy++)
            {
                float v = iy / (float)verticalRings; // 0 = bottom apex, 1 = top crown
                float y = Mathf.Lerp(minY, maxY, v);

                for (int ix = 0; ix <= radialSegments; ix++)
                {
                    float u = ix / (float)radialSegments;
                    float theta = u * Mathf.PI * 2f;

                    Vector3 dir = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
                    Vector3 pos = EvaluateAnatomicalHeartPoint(v, y, theta, dir, isOuterShell: false, out float heat);

                    verts.Add(pos);
                    uvs.Add(new Vector2(u * 2f, v * 2f));

                    // Encode visceral ember intensity in vertex color
                    Color emberColor = Color.Lerp(
                        new Color(0.55f, 0.04f, 0.02f, 1f),
                        new Color(1.00f, 0.42f, 0.08f, 1f),
                        Mathf.Clamp01(heat));
                    colors.Add(emberColor);
                }
            }

            int stride = radialSegments + 1;
            for (int iy = 0; iy < verticalRings; iy++)
            {
                for (int ix = 0; ix < radialSegments; ix++)
                {
                    int a = iy * stride + ix;
                    int b = a + 1;
                    int c = (iy + 1) * stride + ix;
                    int d = c + 1;

                    // Outward-facing winding
                    tris.Add(a);
                    tris.Add(c);
                    tris.Add(b);

                    tris.Add(b);
                    tris.Add(c);
                    tris.Add(d);
                }
            }

            Mesh mesh = CreateMesh("HeartInnerCoreMesh", verts, uvs, tris, colors);
            return mesh;
        }

        // =====================================================================
        // 2. HEART OUTER VESSEL CAGE MESH
        // =====================================================================

        /// <summary>
        /// Builds the outer gnarled myocardial/vascular cage mesh wrapping around the core
        /// with organic ridges, lumpy lateral lobes, coronary vessel trunks, and fenestrated
        /// gaps/crevices so the bright glowing inner core shines through the dark outer cage.
        /// </summary>
        public static Mesh BuildHeartOuterVesselCageMesh()
        {
            var verts = new List<Vector3>(16384);
            var uvs = new List<Vector2>(16384);
            var colors = new List<Color>(16384);
            var tris = new List<int>(32768);

            // Part A: Thick 3D Fenestrated Myocardial Crust & Bulbous Lateral Lobes
            AppendFenestratedMyocardialShell(verts, uvs, colors, tris);

            // Part B: Gnarled 3D Coronary Vessel Trunks wrapping over the core & gaps
            AppendCoronaryVesselCageTrunks(verts, uvs, colors, tris);

            // Part C: Protruding Lateral Arterial Spurs / Horns (matching reference silhouette)
            AppendLateralMyocardialSpurs(verts, uvs, colors, tris);

            Mesh mesh = CreateMesh("HeartOuterVesselCageMesh", verts, uvs, tris, colors);
            return mesh;
        }

        // =====================================================================
        // 3. HEART CROWN ARTERIES MESH
        // =====================================================================

        /// <summary>
        /// Builds thick aorta, pulmonary, and vena cava trunks emerging from the top of the heart
        /// and curving upward/outward into the ceiling vascular canopy.
        /// </summary>
        public static Mesh BuildHeartCrownArteriesMesh()
        {
            var verts = new List<Vector3>(8192);
            var uvs = new List<Vector2>(8192);
            var colors = new List<Color>(8192);
            var tris = new List<int>(16384);

            // 9 major aortic/venous trunks emerging from the upper heart crown (y ~ 1.55 to 2.25)
            int trunkCount = 9;
            for (int i = 0; i < trunkCount; i++)
            {
                float angle = (i / (float)trunkCount) * Mathf.PI * 2f + 0.18f * Mathf.Sin(i * 2.1f);
                float radialStart = (i % 2 == 0) ? 0.65f : 1.05f;
                float startY = (i % 2 == 0) ? 1.95f : 1.65f;

                Vector3 p0 = new Vector3(Mathf.Cos(angle) * radialStart, startY, Mathf.Sin(angle) * radialStart * 0.85f);
                Vector3 outDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized;

                float heightSpan = 2.6f + 0.45f * ((i * 3) % 3);
                float outwardReach = 2.4f + 0.65f * ((i + 1) % 3);

                Vector3 p1 = p0 + new Vector3(outDir.x * 0.35f, heightSpan * 0.36f, outDir.z * 0.35f);
                Vector3 p2 = p0 + new Vector3(outDir.x * outwardReach * 0.55f, heightSpan * 0.78f, outDir.z * outwardReach * 0.50f);
                Vector3 p3 = p0 + new Vector3(outDir.x * outwardReach, heightSpan, outDir.z * outwardReach * 0.90f);

                float rStart = 0.38f + 0.08f * (i % 3);
                float rEnd = rStart * 0.48f;

                AppendOrganicSplineTube(
                    verts, uvs, colors, tris,
                    new[] { p0, p1, p2, p3 },
                    rStart, rEnd,
                    lengthSegments: 18,
                    radialSegments: 12,
                    bulgeAmplitude: 0.14f,
                    bulgeFrequency: 5f,
                    seedOffset: i * 17.3f);

                // Secondary offshoot vessel splitting off mid-trunk
                Vector3 splitOrigin = EvaluateCubicBezier(p0, p1, p2, p3, 0.48f);
                float sideAngle = angle + ((i % 2 == 0) ? 0.52f : -0.52f);
                Vector3 sideDir = new Vector3(Mathf.Cos(sideAngle), 0.45f, Mathf.Sin(sideAngle)).normalized;
                Vector3 s1 = splitOrigin + sideDir * 0.85f + Vector3.up * 0.45f;
                Vector3 s2 = s1 + sideDir * 1.15f + Vector3.up * 0.55f;
                Vector3 s3 = s2 + sideDir * 1.20f + Vector3.up * 0.25f;

                AppendOrganicSplineTube(
                    verts, uvs, colors, tris,
                    new[] { splitOrigin, s1, s2, s3 },
                    rStart * 0.62f, rEnd * 0.45f,
                    lengthSegments: 12,
                    radialSegments: 9,
                    bulgeAmplitude: 0.10f,
                    bulgeFrequency: 4f,
                    seedOffset: i * 31.7f + 5f);
            }

            Mesh mesh = CreateMesh("HeartCrownArteriesMesh", verts, uvs, tris, colors);
            return mesh;
        }

        // =====================================================================
        // 4. VASCULAR BRANCH TREE MESH
        // =====================================================================

        /// <summary>
        /// Builds a recursive branching 3D vein/tendon tree mesh that radiates across the ceiling
        /// and hangs down the upper-left and upper-right corners of the frame.
        /// Root starts at (0,0,0) and grows initially along +Y.
        /// </summary>
        public static Mesh BuildVascularBranchTreeMesh(
            int seed = 1337,
            float length = 10f,
            float baseRadius = 0.42f,
            int depth = 4,
            float spreadAngle = 38f)
        {
            var verts = new List<Vector3>(16384);
            var uvs = new List<Vector2>(16384);
            var colors = new List<Color>(16384);
            var tris = new List<int>(32768);

            System.Random rng = new System.Random(seed);
            int clampedDepth = Mathf.Clamp(depth, 1, 5);
            float safeLength = Mathf.Max(1.0f, length);
            float safeRadius = Mathf.Max(0.04f, baseRadius);
            float safeSpread = Mathf.Clamp(spreadAngle, 10f, 80f);

            BuildRecursiveVascularNode(
                verts, uvs, colors, tris,
                rng,
                origin: Vector3.zero,
                direction: Vector3.up,
                segLength: safeLength * 0.36f,
                startRadius: safeRadius,
                currentDepth: 0,
                maxDepth: clampedDepth,
                spreadAngleDeg: safeSpread);

            Mesh mesh = CreateMesh($"VascularBranchTreeMesh_{seed}", verts, uvs, tris, colors);
            return mesh;
        }

        private static void BuildRecursiveVascularNode(
            List<Vector3> verts,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> tris,
            System.Random rng,
            Vector3 origin,
            Vector3 direction,
            float segLength,
            float startRadius,
            int currentDepth,
            int maxDepth,
            float spreadAngleDeg)
        {
            // Keep vertex count well below 60,000 even for high depth
            if (verts.Count > 52000)
                return;

            float endRadius = startRadius * ((currentDepth == maxDepth) ? 0.22f : 0.64f);

            // Build an organically curved spline for this branch segment
            Vector3 ortho1 = Vector3.Cross(direction, Vector3.right);
            if (ortho1.sqrMagnitude < 0.001f)
                ortho1 = Vector3.Cross(direction, Vector3.forward);
            ortho1.Normalize();
            Vector3 ortho2 = Vector3.Cross(direction, ortho1).normalized;

            float bend1 = ((float)rng.NextDouble() * 2f - 1f) * segLength * 0.22f;
            float bend2 = ((float)rng.NextDouble() * 2f - 1f) * segLength * 0.22f;
            float bend3 = ((float)rng.NextDouble() * 2f - 1f) * segLength * 0.18f;

            Vector3 p0 = origin;
            Vector3 p1 = origin + direction * (segLength * 0.33f) + ortho1 * bend1 + ortho2 * (bend2 * 0.5f);
            Vector3 p2 = origin + direction * (segLength * 0.68f) - ortho1 * (bend2 * 0.4f) + ortho2 * bend3;
            Vector3 p3 = origin + direction * segLength + ortho1 * (bend3 * 0.4f) + ortho2 * (bend1 * 0.4f);

            int radialSegs = Mathf.Clamp(10 - currentDepth * 2, 5, 10);
            int lengthSegs = Mathf.Clamp(9 - currentDepth, 4, 9);

            AppendOrganicSplineTube(
                verts, uvs, colors, tris,
                new[] { p0, p1, p2, p3 },
                startRadius, endRadius,
                lengthSegs, radialSegs,
                bulgeAmplitude: 0.08f,
                bulgeFrequency: 3.5f,
                seedOffset: (float)rng.NextDouble() * 50f);

            // Spawn fine lateral capillary tendrils along mid-segment for extra organic density
            if (currentDepth >= 1 && currentDepth <= maxDepth - 1 && verts.Count < 48000)
            {
                Vector3 midPt = EvaluateCubicBezier(p0, p1, p2, p3, 0.52f);
                float twigAngle = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 twigDir = (direction * 0.55f + (ortho1 * Mathf.Cos(twigAngle) + ortho2 * Mathf.Sin(twigAngle)) * 0.85f).normalized;
                float twigLen = segLength * 0.48f;
                Vector3 tp1 = midPt + twigDir * (twigLen * 0.5f);
                Vector3 tp2 = midPt + twigDir * twigLen + ortho2 * (twigLen * 0.18f);

                AppendOrganicSplineTube(
                    verts, uvs, colors, tris,
                    new[] { midPt, tp1, tp2, tp2 + twigDir * (twigLen * 0.25f) },
                    endRadius * 0.58f, endRadius * 0.12f,
                    lengthSegments: 4,
                    radialSegments: 5,
                    bulgeAmplitude: 0.04f,
                    bulgeFrequency: 2f,
                    seedOffset: (float)rng.NextDouble() * 20f);
            }

            if (currentDepth >= maxDepth)
                return;

            // Bifurcate or trifurcate into child branches
            int childCount = (currentDepth == 0 || (currentDepth == 1 && rng.NextDouble() < 0.55)) ? 3 : 2;
            float baseAzimuth = (float)rng.NextDouble() * Mathf.PI * 2f;
            Vector3 endTangent = (p3 - p2).normalized;
            if (endTangent.sqrMagnitude < 0.001f)
                endTangent = direction;

            for (int c = 0; c < childCount; c++)
            {
                float az = baseAzimuth + (c / (float)childCount) * Mathf.PI * 2f + ((float)rng.NextDouble() - 0.5f) * 0.45f;
                float polarDeg = spreadAngleDeg * (0.75f + 0.45f * (float)rng.NextDouble());
                float polarRad = polarDeg * Mathf.Deg2Rad;

                Vector3 radialVec = (ortho1 * Mathf.Cos(az) + ortho2 * Mathf.Sin(az)).normalized;
                Vector3 childDir = (endTangent * Mathf.Cos(polarRad) + radialVec * Mathf.Sin(polarRad)).normalized;

                float childLen = segLength * (0.74f + 0.14f * (float)rng.NextDouble());
                BuildRecursiveVascularNode(
                    verts, uvs, colors, tris,
                    rng,
                    origin: p3,
                    direction: childDir,
                    segLength: childLen,
                    startRadius: endRadius * 0.94f,
                    currentDepth: currentDepth + 1,
                    maxDepth: maxDepth,
                    spreadAngleDeg: spreadAngleDeg * 1.05f);
            }
        }

        // =====================================================================
        // 5. BLOOD CRYSTAL MONOLITH MESH
        // =====================================================================

        /// <summary>
        /// Builds a faceted hexagonal/octagonal chiseled crystal pillar with an angled
        /// fractured top cleavage face and secondary basal splinters. Uses crisp flat-shaded
        /// per-facet normals so specular highlights and edges catch the red light dramatically.
        /// </summary>
        public static Mesh BuildBloodCrystalMonolithMesh(
            float height = 4.5f,
            float radius = 0.82f,
            int sides = 6,
            float slant = 0.32f)
        {
            int nSides = Mathf.Clamp(sides, 4, 12);
            float h = Mathf.Max(0.5f, height);
            float r = Mathf.Max(0.1f, radius);

            var verts = new List<Vector3>(512);
            var normals = new List<Vector3>(512);
            var uvs = new List<Vector2>(512);
            var colors = new List<Color>(512);
            var tris = new List<int>(1024);

            // Primary crystal monolith column with angled chiseled top cleavage plane
            AppendChiseledCrystalColumn(
                verts, normals, uvs, colors, tris,
                baseCenter: Vector3.zero,
                height: h,
                baseRadius: r,
                sides: nSides,
                slantX: slant,
                slantZ: slant * 0.35f,
                sliceTiltX: 0.28f,
                sliceTiltZ: -0.18f,
                seed: 11);

            // Secondary stepped side crystal shard hugging the left/front flank (like the reference monoliths)
            AppendChiseledCrystalColumn(
                verts, normals, uvs, colors, tris,
                baseCenter: new Vector3(-r * 0.48f, 0f, -r * 0.22f),
                height: h * 0.56f,
                baseRadius: r * 0.46f,
                sides: nSides,
                slantX: slant * 1.18f,
                slantZ: slant * 0.25f,
                sliceTiltX: 0.34f,
                sliceTiltZ: 0.12f,
                seed: 29);

            // Tertiary smaller basal crystal splinter on the opposite side
            AppendChiseledCrystalColumn(
                verts, normals, uvs, colors, tris,
                baseCenter: new Vector3(r * 0.42f, 0f, r * 0.28f),
                height: h * 0.34f,
                baseRadius: r * 0.35f,
                sides: Mathf.Max(5, nSides - 1),
                slantX: slant * 0.85f,
                slantZ: -slant * 0.30f,
                sliceTiltX: -0.25f,
                sliceTiltZ: 0.22f,
                seed: 47);

            Mesh mesh = new Mesh
            {
                name = "BloodCrystalMonolithMesh"
            };
            if (verts.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AppendChiseledCrystalColumn(
            List<Vector3> verts,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> tris,
            Vector3 baseCenter,
            float height,
            float baseRadius,
            int sides,
            float slantX,
            float slantZ,
            float sliceTiltX,
            float sliceTiltZ,
            int seed)
        {
            Vector3[] bottomRing = new Vector3[sides];
            Vector3[] shoulderRing = new Vector3[sides];
            Vector3[] crownBevelRing = new Vector3[sides];

            float topTaper = 0.86f;
            float bevelTaper = 0.48f;

            for (int i = 0; i < sides; i++)
            {
                float angle = (i / (float)sides) * Mathf.PI * 2f + 0.22f;
                // Deterministic per-facet asymmetry so crystal faces have varying widths like real mineral prisms
                float radiusJitter = 1.0f + 0.14f * Mathf.Sin(i * 2.3f + seed) + 0.08f * Mathf.Cos(i * 3.7f + seed);
                float rx = Mathf.Cos(angle) * baseRadius * radiusJitter;
                float rz = Mathf.Sin(angle) * baseRadius * radiusJitter * 0.92f;

                bottomRing[i] = baseCenter + new Vector3(rx, 0f, rz);

                // Angled cleavage height per vertex around the perimeter
                float normX = Mathf.Cos(angle);
                float normZ = Mathf.Sin(angle);
                float sliceOffset = (normX * sliceTiltX + normZ * sliceTiltZ) * baseRadius * 0.85f;
                float shoulderY = Mathf.Max(height * 0.45f, height * 0.86f + sliceOffset);

                Vector3 slantOffset = new Vector3(slantX * shoulderY, 0f, slantZ * shoulderY);
                shoulderRing[i] = baseCenter + new Vector3(rx * topTaper, shoulderY, rz * topTaper) + slantOffset;

                float bevelY = Mathf.Max(shoulderY + height * 0.04f, height * 0.96f + sliceOffset * 0.65f);
                Vector3 bevelSlant = new Vector3(slantX * bevelY, 0f, slantZ * bevelY);
                crownBevelRing[i] = baseCenter + new Vector3(rx * bevelTaper, bevelY, rz * bevelTaper) + bevelSlant;
            }

            // Off-center chiseled fracture apex on top cleavage plane
            Vector3 topApex = baseCenter
                + new Vector3(slantX * height - baseRadius * 0.12f, height * 1.01f, slantZ * height + baseRadius * 0.08f);

            Color baseCol = new Color(0.22f, 0.02f, 0.04f, 1f);
            Color midCol = new Color(0.75f, 0.08f, 0.12f, 1f);
            Color tipCol = new Color(1.00f, 0.25f, 0.28f, 1f);

            // Build flat-shaded vertical prism facets + upper bevel facets + top fracture facets
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                float u0 = i / (float)sides;
                float u1 = (i + 1) / (float)sides;

                // 1. Main vertical shaft quad (b0, b1, s1, s0)
                AddFlatQuad(
                    verts, normals, uvs, colors, tris,
                    bottomRing[i], bottomRing[next], shoulderRing[next], shoulderRing[i],
                    new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 0.82f), new Vector2(u0, 0.82f),
                    baseCol, baseCol, midCol, midCol);

                // 2. Chiseled upper bevel quad (s0, s1, c1, c0)
                AddFlatQuad(
                    verts, normals, uvs, colors, tris,
                    shoulderRing[i], shoulderRing[next], crownBevelRing[next], crownBevelRing[i],
                    new Vector2(u0, 0.82f), new Vector2(u1, 0.82f), new Vector2(u1, 0.94f), new Vector2(u0, 0.94f),
                    midCol, midCol, tipCol, tipCol);

                // 3. Fractured top cleavage triangle (c0, c1, topApex)
                AddFlatTriangle(
                    verts, normals, uvs, colors, tris,
                    crownBevelRing[i], crownBevelRing[next], topApex,
                    new Vector2(u0, 0.94f), new Vector2(u1, 0.94f), new Vector2((u0 + u1) * 0.5f, 1.0f),
                    tipCol, tipCol, tipCol);
            }
        }

        // =====================================================================
        // 6. FOREGROUND TERRAIN MESH
        // =====================================================================

        /// <summary>
        /// Builds the broad, undulating dark rocky/organic foreground ridge and terraced
        /// mid-ground cliffs matching the silhouette of the reference image (elevated rocky
        /// ledges on the left and right mid-ground framing the central valley below the heart/spine).
        /// </summary>
        public static Mesh BuildForegroundTerrainMesh()
        {
            const int cols = 96;
            const int rows = 72;
            const float width = 64f;   // X in [-32, +32]
            const float depth = 44f;   // Z in [-12, +32]

            var verts = new List<Vector3>((cols + 1) * (rows + 1));
            var uvs = new List<Vector2>((cols + 1) * (rows + 1));
            var colors = new List<Color>((cols + 1) * (rows + 1));
            var tris = new List<int>(cols * rows * 6);

            for (int iz = 0; iz <= rows; iz++)
            {
                float vz = iz / (float)rows;
                float z = Mathf.Lerp(-12f, 32f, vz);

                for (int ix = 0; ix <= cols; ix++)
                {
                    float ux = ix / (float)cols;
                    float x = Mathf.Lerp(-width * 0.5f, width * 0.5f, ux);

                    float y = EvaluateTerrainHeight(x, z, out float cliffMask);
                    verts.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2(ux * 6f, vz * 6f));

                    // Subtle vertex color variation for rocky ledges vs dark organic soil
                    Color c = Color.Lerp(
                        new Color(0.08f, 0.05f, 0.06f, 1f),
                        new Color(0.24f, 0.11f, 0.13f, 1f),
                        Mathf.Clamp01(cliffMask));
                    colors.Add(c);
                }
            }

            int stride = cols + 1;
            for (int iz = 0; iz < rows; iz++)
            {
                for (int ix = 0; ix < cols; ix++)
                {
                    int a = iz * stride + ix;
                    int b = a + 1;
                    int c = (iz + 1) * stride + ix;
                    int d = c + 1;

                    tris.Add(a);
                    tris.Add(c);
                    tris.Add(b);

                    tris.Add(b);
                    tris.Add(c);
                    tris.Add(d);
                }
            }

            Mesh mesh = CreateMesh("ForegroundTerrainMesh", verts, uvs, tris, colors);
            return mesh;
        }

        private static float EvaluateTerrainHeight(float x, float z, out float cliffMask)
        {
            // Foreground undulating ridge (z in [-12, +4])
            float fgFactor = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(-8f, 8f, z));
            float fgRidge = Mathf.Sin(x * 0.16f + 0.4f) * 0.45f
                          + Mathf.Cos(z * 0.25f - x * 0.08f) * 0.35f;

            // Slight foreground rise on bottom-right where the blood crystal monoliths stand
            float rightCrystalMound = Mathf.Exp(-((x - 7.5f) * (x - 7.5f) * 0.045f + (z + 3.2f) * (z + 3.2f) * 0.06f)) * 1.15f;
            float leftForegroundMound = Mathf.Exp(-((x + 9.0f) * (x + 9.0f) * 0.035f + (z + 2.5f) * (z + 2.5f) * 0.05f)) * 0.85f;

            // Mid-ground left and right rocky cliff terraces (z in [5, 24]), opening a central valley (x in [-5.5, +5.5])
            float midZWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 11f, z))
                             * Mathf.SmoothStep(1f, 0.25f, Mathf.InverseLerp(22f, 32f, z));

            // Left mid-ground stepped plateau (centered around x = -11..-20, z = 12..20)
            float leftPlateauRaw = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-4.2f, -10.5f, x));
            // Right mid-ground stepped plateau (centered around x = +7..+20, z = 10..20)
            float rightPlateauRaw = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.8f, 9.8f, x));

            float plateauMask = Mathf.Max(leftPlateauRaw * 1.05f, rightPlateauRaw * 1.18f) * midZWeight;

            // Quantize/terrace the plateau into stratified rock ledges like the reference image
            float rawCliffHeight = plateauMask * 4.6f;
            float terracedCliff = Mathf.Floor(rawCliffHeight * 1.35f) / 1.35f
                                + Mathf.SmoothStep(0f, 1f, (rawCliffHeight * 1.35f) % 1f) * 0.55f;

            // Multi-octave organic rock roughness
            float noise1 = Mathf.Sin(x * 0.42f + z * 0.37f) * Mathf.Cos(x * 0.29f - z * 0.51f) * 0.38f;
            float noise2 = Mathf.Sin(x * 1.15f - z * 0.95f) * 0.14f;
            float noise3 = Mathf.Cos(x * 2.4f + z * 2.1f) * 0.06f;

            // Central chasm dip behind midground so the colossal spine base emerges from a misty depression
            float centralChasm = Mathf.Exp(-(x * x) * 0.045f) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 18f, z)) * -1.35f;

            cliffMask = Mathf.Clamp01(plateauMask + (noise1 + 0.38f) * 0.35f);
            float totalY = fgFactor * fgRidge
                         + rightCrystalMound
                         + leftForegroundMound
                         + terracedCliff
                         + centralChasm
                         + noise1 * (0.45f + 0.55f * plateauMask)
                         + noise2
                         + noise3;

            return totalY;
        }

        // =====================================================================
        // 7. CONCENTRIC CAVERN DOME MESH
        // =====================================================================

        /// <summary>
        /// Builds the huge background cavern wall/dome with concentric horizontal rib-like rings
        /// fading into the crimson mist behind the heart and spine.
        /// Generated as a thick double-sided semi-domed amphitheater shell so normals face
        /// cleanly toward the camera regardless of transform orientation or back-face culling.
        /// </summary>
        public static Mesh BuildConcentricCavernDomeMesh()
        {
            const int azimuthSegments = 72;
            const int elevationRings = 56;
            const float radiusX = 34f;
            const float radiusZ = 28f;
            const float minY = -6f;
            const float maxY = 34f;

            var verts = new List<Vector3>((azimuthSegments + 1) * (elevationRings + 1) * 2);
            var uvs = new List<Vector2>((azimuthSegments + 1) * (elevationRings + 1) * 2);
            var colors = new List<Color>((azimuthSegments + 1) * (elevationRings + 1) * 2);
            var tris = new List<int>(azimuthSegments * elevationRings * 12);

            // Sweep azimuth across the rear 210 degrees (-15 deg to +195 deg in X-Z plane)
            float startAz = -15f * Mathf.Deg2Rad;
            float endAz = 195f * Mathf.Deg2Rad;

            // Inner wall (facing inward toward camera/heart) + outer backing shell
            for (int shell = 0; shell < 2; shell++)
            {
                float shellOffset = (shell == 0) ? 0f : 0.65f;

                for (int iy = 0; iy <= elevationRings; iy++)
                {
                    float v = iy / (float)elevationRings;
                    float y = Mathf.Lerp(minY, maxY, v);

                    // Dome arches inward at the top (v > 0.55) to form an overarching cathedral vault
                    float domeArch = 1.0f - 0.42f * Mathf.Pow(Mathf.Clamp01((v - 0.45f) / 0.55f), 1.85f);

                    // 10 concentric horizontal rib arches along the cavern height
                    float ribPhase = v * 10.5f * Mathf.PI * 2f;
                    float ribProtrusion = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ribPhase), 1.65f) * 2.15f;

                    for (int ix = 0; ix <= azimuthSegments; ix++)
                    {
                        float u = ix / (float)azimuthSegments;
                        float az = Mathf.Lerp(startAz, endAz, u);

                        // Subtle organic wave along each concentric horizontal rib ring
                        float organicWarp = Mathf.Sin(az * 6f + v * 8f) * 0.42f
                                          + Mathf.Cos(az * 14f - v * 12f) * 0.18f;

                        // Inward rib protrusion reduces radial distance from center
                        float currentRx = (radiusX * domeArch) - ribProtrusion + organicWarp + shellOffset;
                        float currentRz = (radiusZ * domeArch) - ribProtrusion + organicWarp + shellOffset;

                        float px = Mathf.Cos(az) * currentRx;
                        float pz = Mathf.Sin(az) * currentRz;

                        // Slight vertical arch in the middle behind the heart (sin(az) near 1)
                        float centerArchY = y + Mathf.Sin(az) * 1.8f * Mathf.Sin(v * Mathf.PI);

                        verts.Add(new Vector3(px, centerArchY, pz));
                        uvs.Add(new Vector2(u * 8f, v * 10f));

                        float ribHighlight = Mathf.Clamp01(ribProtrusion / 2.15f);
                        Color col = Color.Lerp(
                            new Color(0.10f, 0.02f, 0.03f, 1f),
                            new Color(0.32f, 0.07f, 0.09f, 1f),
                            ribHighlight);
                        colors.Add(col);
                    }
                }
            }

            int stride = azimuthSegments + 1;
            int shellVertexCount = (azimuthSegments + 1) * (elevationRings + 1);

            for (int iy = 0; iy < elevationRings; iy++)
            {
                for (int ix = 0; ix < azimuthSegments; ix++)
                {
                    // Shell 0: Inward-facing triangles (visible from inside the cavern looking toward +Z)
                    int a0 = iy * stride + ix;
                    int b0 = a0 + 1;
                    int c0 = (iy + 1) * stride + ix;
                    int d0 = c0 + 1;

                    tris.Add(a0);
                    tris.Add(b0);
                    tris.Add(c0);

                    tris.Add(b0);
                    tris.Add(d0);
                    tris.Add(c0);

                    // Shell 1: Outward-facing triangles (visible if transform is rotated 180 degrees)
                    int a1 = shellVertexCount + a0;
                    int b1 = a1 + 1;
                    int c1 = shellVertexCount + c0;
                    int d1 = c1 + 1;

                    tris.Add(a1);
                    tris.Add(c1);
                    tris.Add(b1);

                    tris.Add(b1);
                    tris.Add(c1);
                    tris.Add(d1);
                }
            }

            Mesh mesh = CreateMesh("ConcentricCavernDomeMesh", verts, uvs, tris, colors);
            return mesh;
        }

        // =====================================================================
        // INTERNAL ANATOMICAL HEART & VESSEL SCULPTING HELPERS
        // =====================================================================

        private static Vector3 EvaluateAnatomicalHeartPoint(
            float v,
            float y,
            float theta,
            Vector3 radialDir,
            bool isOuterShell,
            out float heatFactor)
        {
            // Anatomical vertical envelope: pointed lower apex at v=0, broad upper ventricular/atrial mass at v=0.68..0.84, rounded crown at v=1
            float apexTaper = Mathf.Pow(Mathf.Clamp01(v / 0.72f), 0.58f);
            float crownCap = 1.0f - Mathf.Pow(Mathf.Clamp01((v - 0.72f) / 0.28f), 2.1f);
            float baseRadius = 1.82f * apexTaper * crownCap;

            // Slight anatomical flattened Z profile and asymmetric apex tilt
            float cosT = Mathf.Cos(theta);
            float sinT = Mathf.Sin(theta);

            // Interventricular & interatrial sulcus groove (diagonal indentation front & back)
            float sulcusAngle = theta - (v - 0.5f) * 0.75f;
            float sulcusIndent = Mathf.Pow(Mathf.Abs(Mathf.Sin(sulcusAngle)), 0.65f);
            float grooveDepth = (1.0f - sulcusIndent) * 0.22f * Mathf.Sin(v * Mathf.PI);

            // Upper double-lobe atrial/aortic shoulders (v in [0.55, 0.95])
            float upperLobeWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.82f, v))
                                  * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.88f, 1.0f, v));
            float dualAtriaBulge = Mathf.Abs(cosT) * 0.36f * upperLobeWeight;

            // Prominent bulbous lateral myocardial nodules matching the reference heart silhouette:
            // Left mid-lower bulge (cosT < 0, v ~ 0.42), Left upper bulge (cosT < 0, v ~ 0.76)
            // Right mid bulge (cosT > 0, v ~ 0.54), Right upper-side bulge (cosT > 0, v ~ 0.72)
            float leftMidLobe = Mathf.Exp(-((cosT + 0.92f) * (cosT + 0.92f) * 3.5f + (v - 0.44f) * (v - 0.44f) * 28f)) * 0.46f;
            float leftUpperLobe = Mathf.Exp(-((cosT + 0.85f) * (cosT + 0.85f) * 3.2f + (v - 0.76f) * (v - 0.76f) * 26f)) * 0.42f;
            float rightMidLobe = Mathf.Exp(-((cosT - 0.92f) * (cosT - 0.92f) * 3.5f + (v - 0.52f) * (v - 0.52f) * 26f)) * 0.48f;
            float rightUpperLobe = Mathf.Exp(-((cosT - 0.82f) * (cosT - 0.82f) * 3.2f + (v - 0.74f) * (v - 0.74f) * 28f)) * 0.38f;

            float lateralNodules = (leftMidLobe + leftUpperLobe + rightMidLobe + rightUpperLobe) * (isOuterShell ? 1.28f : 1.0f);

            // Multi-frequency 3D organic cellular bumps
            float cell1 = Mathf.Sin(theta * 5f + v * 11f) * Mathf.Cos(theta * 4f - v * 9f);
            float cell2 = Mathf.Sin(theta * 11f - v * 17f) * 0.5f + Mathf.Cos(theta * 9f + v * 15f) * 0.5f;
            float organicBump = (cell1 * 0.11f + cell2 * 0.06f) * Mathf.Sin(v * Mathf.PI);

            float shellMultiplier = isOuterShell ? 1.055f : 1.0f;
            float finalRadius = (baseRadius - grooveDepth + dualAtriaBulge + lateralNodules + organicBump) * shellMultiplier;

            float px = radialDir.x * finalRadius + 0.08f * Mathf.Sin(v * Mathf.PI);
            float pz = radialDir.z * finalRadius * 0.86f;

            // Heat is brightest inside the deep ventricular chambers and glowing fissures
            heatFactor = Mathf.Clamp01(0.55f + 0.35f * cell1 + 0.25f * (1f - v * 0.4f) - grooveDepth * 0.8f);

            return new Vector3(px, y, pz);
        }

        private static void AppendFenestratedMyocardialShell(
            List<Vector3> verts,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> tris)
        {
            const int radialSegments = 56;
            const int verticalRings = 50;
            const float minY = -2.82f;
            const float maxY = 2.40f;
            const float crustThickness = 0.14f;

            int stride = radialSegments + 1;
            Vector3[] outerGrid = new Vector3[(radialSegments + 1) * (verticalRings + 1)];
            Vector3[] innerGrid = new Vector3[(radialSegments + 1) * (verticalRings + 1)];
            Vector2[] uvGrid = new Vector2[(radialSegments + 1) * (verticalRings + 1)];
            float[] densityGrid = new float[(radialSegments + 1) * (verticalRings + 1)];

            for (int iy = 0; iy <= verticalRings; iy++)
            {
                float v = iy / (float)verticalRings;
                float y = Mathf.Lerp(minY, maxY, v);

                for (int ix = 0; ix <= radialSegments; ix++)
                {
                    float u = ix / (float)radialSegments;
                    float theta = u * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));

                    Vector3 baseOuter = EvaluateAnatomicalHeartPoint(v, y, theta, dir, isOuterShell: true, out _);

                    // Compute organic fenestration field:
                    // Positive (> 0) = thick dark myocardial crust / lumpy lateral lobe
                    // Negative (< 0) = open glowing crevice/window exposing the inner ember core!
                    float cosT = Mathf.Cos(theta);
                    float sinT = Mathf.Sin(theta);

                    // Keep lateral silhouette lobes, diagonal muscular ribs, and crown rim solid,
                    // while carving glowing windows in the central-front, upper-right, and lower-apex faces
                    float lateralCrust = Mathf.Pow(Mathf.Abs(cosT), 1.4f) * 0.55f;
                    float crownAndBaseRim = Mathf.SmoothStep(0.76f, 0.94f, v) * 0.50f;

                    // Diagonal intersecting myocardial bands wrapping the heart
                    float band1 = Mathf.Sin(theta * 3f + v * 10.5f);
                    float band2 = Mathf.Cos(theta * 4f - v * 9.0f);
                    float cellular = Mathf.Sin(theta * 7f + v * 15f) * Mathf.Cos(theta * 5f - v * 12f);

                    // Open dramatic glowing windows on the front & back faces (|sinT| > 0.35)
                    float frontWindowCarve = Mathf.Pow(Mathf.Abs(sinT), 1.2f) * 0.42f;

                    float maskVal = lateralCrust + crownAndBaseRim + 0.38f * band1 + 0.34f * band2 + 0.25f * cellular - frontWindowCarve - 0.08f;

                    int idx = iy * stride + ix;
                    densityGrid[idx] = maskVal;

                    float bumpExtrude = Mathf.Clamp01(maskVal * 1.8f) * crustThickness;
                    Vector3 outNormal = new Vector3(baseOuter.x, (y - 0.1f) * 0.35f, baseOuter.z).normalized;

                    outerGrid[idx] = baseOuter + outNormal * (0.03f + bumpExtrude);
                    innerGrid[idx] = baseOuter - outNormal * 0.03f;
                    uvGrid[idx] = new Vector2(u * 3f, v * 3f);
                }
            }

            // Emit 3D solid quads (outer face + inner face + beveled boundary walls) where maskVal > 0
            bool[,] activeCell = new bool[verticalRings, radialSegments];
            for (int iy = 0; iy < verticalRings; iy++)
            {
                for (int ix = 0; ix < radialSegments; ix++)
                {
                    int a = iy * stride + ix;
                    int b = a + 1;
                    int c = (iy + 1) * stride + ix;
                    int d = c + 1;

                    float avg = (densityGrid[a] + densityGrid[b] + densityGrid[c] + densityGrid[d]) * 0.25f;
                    activeCell[iy, ix] = avg > 0.0f;
                }
            }

            // Map grid vertices to mesh vertices
            int baseVertOffset = verts.Count;
            int totalGridVerts = (radialSegments + 1) * (verticalRings + 1);

            for (int i = 0; i < totalGridVerts; i++)
            {
                verts.Add(outerGrid[i]);
                uvs.Add(uvGrid[i]);
                float darkShade = Mathf.Clamp01(densityGrid[i] * 1.5f);
                colors.Add(Color.Lerp(new Color(0.35f, 0.05f, 0.06f, 1f), new Color(0.10f, 0.02f, 0.03f, 1f), darkShade));
            }

            int innerVertOffset = verts.Count;
            for (int i = 0; i < totalGridVerts; i++)
            {
                verts.Add(innerGrid[i]);
                uvs.Add(uvGrid[i]);
                colors.Add(new Color(0.22f, 0.03f, 0.04f, 1f));
            }

            for (int iy = 0; iy < verticalRings; iy++)
            {
                for (int ix = 0; ix < radialSegments; ix++)
                {
                    if (!activeCell[iy, ix])
                        continue;

                    int a = iy * stride + ix;
                    int b = a + 1;
                    int c = (iy + 1) * stride + ix;
                    int d = c + 1;

                    // Outer shell triangles
                    tris.Add(baseVertOffset + a);
                    tris.Add(baseVertOffset + c);
                    tris.Add(baseVertOffset + b);

                    tris.Add(baseVertOffset + b);
                    tris.Add(baseVertOffset + c);
                    tris.Add(baseVertOffset + d);

                    // Inner shell triangles (opposite winding)
                    tris.Add(innerVertOffset + a);
                    tris.Add(innerVertOffset + b);
                    tris.Add(innerVertOffset + c);

                    tris.Add(innerVertOffset + b);
                    tris.Add(innerVertOffset + d);
                    tris.Add(innerVertOffset + c);

                    // Stitch 3D rim walls along any edge bordering an open window!
                    int prevX = (ix - 1 + radialSegments) % radialSegments;
                    int nextX = (ix + 1) % radialSegments;

                    if (!activeCell[iy, prevX])
                        AddRimBridgeQuad(tris, baseVertOffset + c, baseVertOffset + a, innerVertOffset + a, innerVertOffset + c);
                    if (!activeCell[iy, nextX])
                        AddRimBridgeQuad(tris, baseVertOffset + b, baseVertOffset + d, innerVertOffset + d, innerVertOffset + b);
                    if (iy > 0 && !activeCell[iy - 1, ix])
                        AddRimBridgeQuad(tris, baseVertOffset + a, baseVertOffset + b, innerVertOffset + b, innerVertOffset + a);
                    if (iy < verticalRings - 1 && !activeCell[iy + 1, ix])
                        AddRimBridgeQuad(tris, baseVertOffset + d, baseVertOffset + c, innerVertOffset + c, innerVertOffset + d);
                }
            }
        }

        private static void AddRimBridgeQuad(List<int> tris, int o0, int o1, int i1, int i0)
        {
            tris.Add(o0);
            tris.Add(o1);
            tris.Add(i1);

            tris.Add(o0);
            tris.Add(i1);
            tris.Add(i0);
        }

        private static void AppendCoronaryVesselCageTrunks(
            List<Vector3> verts,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> tris)
        {
            // 14 thick gnarly coronary artery trunks wrapping from the upper crown down toward the apex
            const int vesselCount = 14;
            const int pathSteps = 16;

            for (int vIdx = 0; vIdx < vesselCount; vIdx++)
            {
                float startTheta = (vIdx / (float)vesselCount) * Mathf.PI * 2f;
                float twistRate = ((vIdx % 2 == 0) ? 1.15f : -0.95f) + 0.25f * Mathf.Sin(vIdx * 1.7f);
                float vStart = 0.94f - 0.08f * (vIdx % 3);
                float vEnd = 0.08f + 0.12f * (vIdx % 4);

                Vector3[] path = new Vector3[pathSteps];
                for (int s = 0; s < pathSteps; s++)
                {
                    float t = s / (float)(pathSteps - 1);
                    float v = Mathf.Lerp(vStart, vEnd, t);
                    float y = Mathf.Lerp(-2.80f, 2.35f, v);
                    float theta = startTheta + twistRate * t + 0.18f * Mathf.Sin(t * Mathf.PI * 3f + vIdx);

                    Vector3 dir = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
                    Vector3 surfacePt = EvaluateAnatomicalHeartPoint(v, y, theta, dir, isOuterShell: true, out _);
                    Vector3 outNorm = new Vector3(surfacePt.x, (y - 0.2f) * 0.25f, surfacePt.z).normalized;

                    path[s] = surfacePt + outNorm * 0.11f;
                }

                float rStart = 0.19f + 0.04f * (vIdx % 3);
                float rEnd = 0.05f;

                AppendOrganicPolylineTube(
                    verts, uvs, colors, tris,
                    path,
                    rStart, rEnd,
                    radialSegments: 8,
                    bulgeAmplitude: 0.16f,
                    bulgeFrequency: 6f,
                    seedOffset: vIdx * 13.1f);
            }
        }

        private static void AppendLateralMyocardialSpurs(
            List<Vector3> verts,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> tris)
        {
            // In the reference image, there are distinct dark pointed/curved vascular spurs jutting out
            // from the upper-left and mid-right lateral flanks of the heart.
            (Vector3 start, Vector3 mid, Vector3 tip, float radius)[] spurs =
            {
                // Upper-left lateral vessel horn
                (new Vector3(-1.55f, 1.15f, -0.15f), new Vector3(-2.15f, 1.42f, -0.10f), new Vector3(-2.65f, 1.58f, -0.05f), 0.26f),
                // Mid-left lateral nodule spur
                (new Vector3(-1.62f, 0.15f, 0.10f),  new Vector3(-2.12f, 0.22f, 0.15f),  new Vector3(-2.45f, 0.12f, 0.18f),  0.22f),
                // Mid-right prominent horizontal arterial horn (clearly visible in reference image!)
                (new Vector3(1.60f, 0.48f, -0.12f),  new Vector3(2.22f, 0.56f, -0.08f),  new Vector3(2.78f, 0.50f, -0.04f),  0.25f),
                // Upper-right lateral vessel spur
                (new Vector3(1.48f, 1.35f, 0.10f),   new Vector3(2.05f, 1.65f, 0.12f),   new Vector3(2.48f, 1.82f, 0.15f),   0.23f),
                // Lower-left ventricular vessel root
                (new Vector3(-1.18f, -1.05f, -0.18f), new Vector3(-1.58f, -1.25f, -0.22f), new Vector3(-1.85f, -1.48f, -0.25f), 0.18f)
            };

            for (int i = 0; i < spurs.Length; i++)
            {
                var sp = spurs[i];
                Vector3 p1 = Vector3.Lerp(sp.start, sp.mid, 0.5f);
                AppendOrganicSplineTube(
                    verts, uvs, colors, tris,
                    new[] { sp.start, p1, sp.mid, sp.tip },
                    sp.radius, 0.03f,
                    lengthSegments: 8,
                    radialSegments: 8,
                    bulgeAmplitude: 0.12f,
                    bulgeFrequency: 3f,
                    seedOffset: i * 19.7f);
            }
        }

        // =====================================================================
        // GENERIC SPLINE TUBE & FLAT FACET BUILDERS
        // =====================================================================

        private static void AppendOrganicSplineTube(
            List<Vector3> verts,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> tris,
            Vector3[] controlPoints, // expects 4 control points (cubic Bezier)
            float startRadius,
            float endRadius,
            int lengthSegments,
            int radialSegments,
            float bulgeAmplitude,
            float bulgeFrequency,
            float seedOffset)
        {
            Vector3[] path = new Vector3[lengthSegments + 1];
            for (int i = 0; i <= lengthSegments; i++)
            {
                float t = i / (float)lengthSegments;
                path[i] = EvaluateCubicBezier(
                    controlPoints[0],
                    controlPoints[1],
                    controlPoints[2],
                    controlPoints[3],
                    t);
            }

            AppendOrganicPolylineTube(
                verts, uvs, colors, tris,
                path,
                startRadius, endRadius,
                radialSegments,
                bulgeAmplitude,
                bulgeFrequency,
                seedOffset);
        }

        private static void AppendOrganicPolylineTube(
            List<Vector3> verts,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> tris,
            Vector3[] path,
            float startRadius,
            float endRadius,
            int radialSegments,
            float bulgeAmplitude,
            float bulgeFrequency,
            float seedOffset)
        {
            int lengthSegments = path.Length - 1;
            if (lengthSegments < 1)
                return;

            int baseVert = verts.Count;
            Vector3 prevNormal = Vector3.right;

            for (int i = 0; i <= lengthSegments; i++)
            {
                float t = i / (float)lengthSegments;
                Vector3 tangent;
                if (i == 0)
                    tangent = (path[1] - path[0]).normalized;
                else if (i == lengthSegments)
                    tangent = (path[lengthSegments] - path[lengthSegments - 1]).normalized;
                else
                    tangent = (path[i + 1] - path[i - 1]).normalized;

                if (tangent.sqrMagnitude < 0.0001f)
                    tangent = Vector3.up;

                // Parallel transport frame to prevent twisting artifacts
                Vector3 binormal = Vector3.Cross(tangent, prevNormal);
                if (binormal.sqrMagnitude < 0.0001f)
                {
                    Vector3 fallback = Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.forward;
                    binormal = Vector3.Cross(tangent, fallback);
                }
                binormal.Normalize();
                Vector3 normal = Vector3.Cross(binormal, tangent).normalized;
                prevNormal = normal;

                float baseR = Mathf.Lerp(startRadius, endRadius, Mathf.Pow(t, 0.85f));
                float peristalticBulge = 1.0f + bulgeAmplitude * Mathf.Sin(t * Mathf.PI * 2f * bulgeFrequency + seedOffset);

                for (int j = 0; j <= radialSegments; j++)
                {
                    float u = j / (float)radialSegments;
                    float angle = u * Mathf.PI * 2f;

                    // Subtle longitudinal muscular ridges along the vessel wall
                    float ridge = 1.0f + 0.08f * Mathf.Sin(angle * 4f + t * 8f + seedOffset);
                    float r = baseR * peristalticBulge * ridge;

                    Vector3 offset = (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * r;
                    verts.Add(path[i] + offset);
                    uvs.Add(new Vector2(u, t * 3f));

                    Color vesselCol = Color.Lerp(
                        new Color(0.28f, 0.04f, 0.05f, 1f),
                        new Color(0.55f, 0.09f, 0.11f, 1f),
                        t);
                    colors.Add(vesselCol);
                }
            }

            int stride = radialSegments + 1;
            for (int i = 0; i < lengthSegments; i++)
            {
                for (int j = 0; j < radialSegments; j++)
                {
                    int a = baseVert + i * stride + j;
                    int b = a + 1;
                    int c = baseVert + (i + 1) * stride + j;
                    int d = c + 1;

                    tris.Add(a);
                    tris.Add(c);
                    tris.Add(b);

                    tris.Add(b);
                    tris.Add(c);
                    tris.Add(d);
                }
            }
        }

        private static Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            return uuu * p0
                 + 3f * uu * t * p1
                 + 3f * u * tt * p2
                 + ttt * p3;
        }

        private static void AddFlatQuad(
            List<Vector3> verts,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> tris,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
            Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3,
            Color c0, Color c1, Color c2, Color c3)
        {
            Vector3 faceNormal = Vector3.Cross(v3 - v0, v1 - v0).normalized;
            if (faceNormal.sqrMagnitude < 0.0001f)
                faceNormal = Vector3.up;

            int start = verts.Count;
            verts.Add(v0);
            verts.Add(v1);
            verts.Add(v2);
            verts.Add(v3);

            normals.Add(faceNormal);
            normals.Add(faceNormal);
            normals.Add(faceNormal);
            normals.Add(faceNormal);

            uvs.Add(uv0);
            uvs.Add(uv1);
            uvs.Add(uv2);
            uvs.Add(uv3);

            colors.Add(c0);
            colors.Add(c1);
            colors.Add(c2);
            colors.Add(c3);

            tris.Add(start + 0);
            tris.Add(start + 3);
            tris.Add(start + 2);

            tris.Add(start + 0);
            tris.Add(start + 2);
            tris.Add(start + 1);
        }

        private static void AddFlatTriangle(
            List<Vector3> verts,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> tris,
            Vector3 v0, Vector3 v1, Vector3 v2,
            Vector2 uv0, Vector2 uv1, Vector2 uv2,
            Color c0, Color c1, Color c2)
        {
            Vector3 faceNormal = Vector3.Cross(v2 - v0, v1 - v0).normalized;
            if (faceNormal.sqrMagnitude < 0.0001f)
                faceNormal = Vector3.up;

            int start = verts.Count;
            verts.Add(v0);
            verts.Add(v1);
            verts.Add(v2);

            normals.Add(faceNormal);
            normals.Add(faceNormal);
            normals.Add(faceNormal);

            uvs.Add(uv0);
            uvs.Add(uv1);
            uvs.Add(uv2);

            colors.Add(c0);
            colors.Add(c1);
            colors.Add(c2);

            tris.Add(start + 0);
            tris.Add(start + 2);
            tris.Add(start + 1);
        }

        private static Mesh CreateMesh(
            string name,
            List<Vector3> verts,
            List<Vector2> uvs,
            List<int> tris,
            List<Color> colors = null)
        {
            Mesh mesh = new Mesh
            {
                name = name
            };

            if (verts.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            if (colors != null && colors.Count == verts.Count)
                mesh.SetColors(colors);

            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
