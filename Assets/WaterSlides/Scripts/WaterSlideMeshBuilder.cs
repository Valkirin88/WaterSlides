using System.Collections.Generic;
using UnityEngine;

namespace WaterSlides
{
    public static class WaterSlideMeshBuilder
    {
        public struct Settings
        {
            public SlideEnclosure Enclosure;
            public float WallThickness;
            public int RingSegments;
            public int SamplesPerSegment;
            public float BankStrength;
            public bool PreserveVerticalMotion;
            public bool HasLoop;

            public SlideCrossSection.OpenFlume OpenFlume;
            public SlideCrossSection.ClosedTube ClosedTube;
        }

        // A sample of the rendered centreline with its orientation. Used at runtime to guide
        // the rider along the slide (stable tangent/up, lateral bounds) without depending on
        // noisy per-triangle contact normals.
        public struct RideFrame
        {
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Up;
        }

        public static List<RideFrame> BuildRideFrames(IReadOnlyList<Vector3> controlPoints, Settings settings)
        {
            var result = new List<RideFrame>();
            var path = BuildProcessedPath(controlPoints, settings);
            if (path.Count < 2)
                return result;

            var frames = BuildFrames(path, settings);
            result.Capacity = frames.Count;
            foreach (var f in frames)
                result.Add(new RideFrame { Position = f.Position, Forward = f.Forward, Up = f.Up });
            return result;
        }

        // The exact centreline the mesh is built on (after smoothing + descent clamping).
        // Use this for connecting slides so the jump lines up with what is actually rendered.
        // Every path (including loops) goes through the same smoothing + densify pipeline so
        // the tube is always built from one continuous, evenly spaced polyline — no raw
        // sparse corners, no chord jumps, no separate mesh pieces.
        public static List<Vector3> BuildProcessedPath(IReadOnlyList<Vector3> controlPoints, Settings settings)
        {
            if (controlPoints == null || controlPoints.Count < 2)
                return new List<Vector3>();

            var path = BuildPathFromControls(controlPoints, settings.SamplesPerSegment);

            if (path.Count < 2)
                return path;

            // Loops go up/over (vertical) or already descend by construction (horizontal
            // helix), so the "always descend" clamps must not touch them.
            bool clampY = !settings.PreserveVerticalMotion && !settings.HasLoop;

            // Chaikin corner cutting instead of neighbour averaging: each pass replaces
            // every corner with points at 1/4 and 3/4 of the adjacent segments, and the
            // polyline converges to a smooth quadratic B-spline. Every new point is a
            // convex combination of the inputs, so the curve never overshoots — loops
            // keep their shape (the reason Catmull-Rom was rejected) and a descending
            // path stays descending with no per-point Y-clamps (whose kinks were visible
            // in the old averaging smoother).
            path = ChaikinSmooth(path, iterations: 4);

            // Ring spacing sets the dihedral angle at every ring seam of the collision
            // mesh — the angle the sliding capsule "hits" when crossing a seam. The old
            // 0.28–0.55 m spacing made every seam a small step at riding speed (felt as
            // constant bumping); denser rings shrink those impacts quadratically.
            float ringSpacing = Mathf.Clamp(0.22f - settings.SamplesPerSegment * 0.008f, 0.12f, 0.22f);
            path = ResampleUniform(path, ringSpacing);
            RemoveTinySteps(path, 0.02f);
            if (clampY)
                EnforcePathMonotonicDescent(path);

            return path;
        }

        // Always polyline: Catmull-Rom overshoots through loops and creates start↔end chord faces.
        static List<Vector3> BuildPathFromControls(IReadOnlyList<Vector3> controlPoints, int samplesPerSegment)
        {
            if (controlPoints == null || controlPoints.Count < 2)
                return new List<Vector3>();

            if (controlPoints.Count >= 24)
                return new List<Vector3>(controlPoints);

            return SamplePathLinear(controlPoints, samplesPerSegment);
        }

        static List<Vector3> ChaikinSmooth(List<Vector3> path, int iterations)
        {
            if (path.Count < 3 || iterations <= 0)
                return path;

            var current = path;
            for (int it = 0; it < iterations; it++)
            {
                var next = new List<Vector3>(current.Count * 2) { current[0] };
                for (int i = 0; i < current.Count - 1; i++)
                {
                    Vector3 a = current[i];
                    Vector3 b = current[i + 1];
                    next.Add(Vector3.Lerp(a, b, 0.25f));
                    next.Add(Vector3.Lerp(a, b, 0.75f));
                }

                next.Add(current[^1]);
                current = next;
            }

            return current;
        }

        // Even arc-length sampling (works both up and down in density): rings spaced
        // unevenly along the path make the tube tessellation — and the ride feel —
        // depend on where the control points happened to be.
        static List<Vector3> ResampleUniform(List<Vector3> path, float spacing)
        {
            if (path.Count < 2)
                return path;

            spacing = Mathf.Max(spacing, 0.05f);
            var result = new List<Vector3> { path[0] };
            float distToNext = spacing;

            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 a = path[i];
                Vector3 b = path[i + 1];
                float segLen = Vector3.Distance(a, b);
                float traversed = 0f;

                while (segLen - traversed >= distToNext)
                {
                    traversed += distToNext;
                    result.Add(Vector3.Lerp(a, b, traversed / segLen));
                    distToNext = spacing;
                }

                distToNext -= segLen - traversed;
            }

            if ((result[^1] - path[^1]).sqrMagnitude > 1e-8f)
                result.Add(path[^1]);

            return result;
        }

        // Coincident points produce zero-length tangents and garbage rings; drop them.
        static void RemoveTinySteps(List<Vector3> path, float minStep)
        {
            float sqr = minStep * minStep;
            for (int i = path.Count - 1; i >= 1; i--)
            {
                if (path.Count <= 2)
                    return;
                if ((path[i] - path[i - 1]).sqrMagnitude >= sqr)
                    continue;

                // Keep the endpoints: remove the inner point of the offending pair.
                if (i == path.Count - 1)
                    path.RemoveAt(i - 1);
                else
                    path.RemoveAt(i);
            }
        }

        static List<Vector3> SamplePathLinear(IReadOnlyList<Vector3> controlPoints, int samplesPerSegment)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < controlPoints.Count - 1; i++)
            {
                int steps = i == controlPoints.Count - 2 ? samplesPerSegment + 1 : samplesPerSegment;
                int start = i == 0 ? 0 : 1;
                for (int s = start; s < steps; s++)
                {
                    float t = s / (float)samplesPerSegment;
                    result.Add(Vector3.Lerp(controlPoints[i], controlPoints[i + 1], t));
                }
            }

            return result;
        }

        // Visual mesh: inner + outer shell + wall thickness caps (double-sided).
        public static Mesh BuildTube(IReadOnlyList<Vector3> controlPoints, Settings settings)
        {
            var path = BuildProcessedPath(controlPoints, settings);
            if (path.Count < 2)
                return null;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            AppendTubeSegment(path, settings, vertices, normals, uvs, triangles);

            if (vertices.Count == 0)
                return null;

            var mesh = new Mesh { name = "WaterSlide" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AppendTubeSegment(
            List<Vector3> segmentPath,
            Settings settings,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles)
        {
            if (segmentPath.Count < 2)
                return;

            int vertexBase = vertices.Count;
            int ringVerts = settings.RingSegments;
            int rings = segmentPath.Count;
            bool isOpen = settings.Enclosure == SlideEnclosure.Open;
            int segmentCount = isOpen ? ringVerts - 1 : ringVerts;
            var frames = BuildFrames(segmentPath, settings);

            // UV.y — длина дуги в МЕТРАХ (не 0..1): так полосы раскраски имеют
            // одинаковую длину на любой горке, а текстура не растягивается по длине.
            var ringDistance = BuildRingDistances(segmentPath);

            for (int ring = 0; ring < rings; ring++)
            {
                float v = ringDistance[ring];
                var frame = frames[ring];

                for (int seg = 0; seg < ringVerts; seg++)
                {
                    float t = isOpen
                        ? (seg / (float)(ringVerts - 1)) * SlideCrossSection.OpenProfileMaxT
                        : seg / (float)ringVerts;
                    if (!isOpen && seg == ringVerts - 1)
                        t = 1f;

                    SampleCrossSection(settings, t, outer: true, out var outerPt, out var outerN);
                    SampleCrossSection(settings, t, outer: false, out var innerPt, out var innerN);

                    vertices.Add(ToWorld(frame, outerPt));
                    normals.Add(ToWorldNormal(frame, outerN));
                    uvs.Add(new Vector2(t, v));

                    vertices.Add(ToWorld(frame, innerPt));
                    normals.Add(ToWorldNormal(frame, innerN));
                    uvs.Add(new Vector2(t, v));
                }
            }

            for (int ring = 0; ring < rings - 1; ring++)
            {
                for (int seg = 0; seg < segmentCount; seg++)
                {
                    int next = isOpen ? seg + 1 : (seg + 1) % ringVerts;
                    int o0 = vertexBase + (ring * ringVerts + seg) * 2;
                    int o1 = vertexBase + (ring * ringVerts + next) * 2;
                    int o2 = vertexBase + ((ring + 1) * ringVerts + seg) * 2;
                    int o3 = vertexBase + ((ring + 1) * ringVerts + next) * 2;
                    AddDoubleSidedQuad(triangles, o0, o1, o3, o2);

                    int i0 = o0 + 1;
                    int i1 = o1 + 1;
                    int i2 = o2 + 1;
                    int i3 = o3 + 1;
                    AddDoubleSidedQuad(triangles, i0, i2, i3, i1);
                }
            }

            AddWallThicknessCaps(vertices, normals, uvs, triangles, vertexBase, rings, ringVerts, isOpen, ringDistance);
        }

        static float[] BuildRingDistances(List<Vector3> path)
        {
            var result = new float[path.Count];
            float total = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                total += Vector3.Distance(path[i - 1], path[i]);
                result[i] = total;
            }

            return result;
        }

        // A thin water ribbon sitting at a fill level inside the tube/flume, following the path.
        // UV.x = width in meters (0..surface width), UV.y = distance along path in meters.
        public static Mesh BuildWaterSurface(IReadOnlyList<Vector3> controlPoints, Settings settings, float fillFraction, float minWidthFraction = 0f)
        {
            var path = BuildProcessedPath(controlPoints, settings);
            if (path.Count < 2)
                return null;

            ComputeWaterChord(settings, Mathf.Clamp01(fillFraction), out float waterY, out float halfChord, minWidthFraction);
            if (halfChord <= 0.001f)
                return null;

            // Профиль ленты поперёк: плоское зеркало на высоте waterY, а там, где
            // лента шире естественной хорды (MinWaterWidth), — тонкая плёнка по
            // эллиптической стенке открытого Rounded-жёлоба (его профиль —
            // x = cos·halfW, y = sin·d, см. SlideCrossSection.SampleOpenRounded).
            // Раньше лишняя ширина просто пряталась под стенкой, и видимая вода
            // оставалась узкой хордой (при WaterFill 0.12 — меньше половины жёлоба).
            bool sheetOnWalls = settings.Enclosure == SlideEnclosure.Open
                && settings.OpenFlume.Shape == OpenFlumeShape.Rounded;
            const float film = 0.02f;
            int across = sheetOnWalls ? 11 : 2;
            var profile = new Vector2[across];
            var profileNormal = new Vector2[across];
            for (int j = 0; j < across; j++)
            {
                float x = Mathf.Lerp(-halfChord, halfChord, j / (float)(across - 1));
                float y = waterY;
                var n = new Vector2(0f, 1f);
                if (sheetOnWalls)
                {
                    float halfW = settings.OpenFlume.Width * 0.5f;
                    float d = settings.OpenFlume.Depth;
                    float k = Mathf.Clamp01(1f - (x * x) / Mathf.Max(halfW * halfW, 1e-6f));
                    float wallY = -d * Mathf.Sqrt(k) + film;
                    if (wallY > y)
                    {
                        y = wallY;
                        n = new Vector2(-x / (halfW * halfW), Mathf.Sqrt(k) / d).normalized;
                    }
                }
                profile[j] = new Vector2(x, y);
                profileNormal[j] = n;
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float vAccum = 0f;

            var frames = BuildFrames(path, settings);

            for (int ring = 0; ring < path.Count; ring++)
            {
                var frame = frames[ring];
                if (ring > 0)
                    vAccum += Vector3.Distance(frame.Position, frames[ring - 1].Position);

                for (int j = 0; j < across; j++)
                {
                    vertices.Add(ToWorld(frame, profile[j]));
                    normals.Add(ToWorldNormal(frame, profileNormal[j]));
                    uvs.Add(new Vector2(profile[j].x + halfChord, vAccum));
                }
            }

            for (int ring = 0; ring < path.Count - 1; ring++)
            for (int j = 0; j < across - 1; j++)
            {
                int i0 = ring * across + j;
                int i1 = i0 + 1;
                int i2 = (ring + 1) * across + j;
                int i3 = i2 + 1;
                AddQuad(triangles, i0, i2, i3, i1);
            }

            var mesh = new Mesh { name = "WaterSlideWater" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Thin water ribbons fail Unity frustum culling up close; pad bounds without changing visuals.
        public static void ExpandMeshBounds(Mesh mesh, float padding)
        {
            if (mesh == null || padding <= 0f)
                return;

            mesh.RecalculateBounds();
            var center = mesh.bounds.center;
            var extents = mesh.bounds.extents + Vector3.one * padding;

            var vertices = new List<Vector3>(mesh.vertices);
            var triangles = new List<int>(mesh.triangles);
            int cornerBase = vertices.Count;

            vertices.Add(center + new Vector3(-extents.x, -extents.y, -extents.z));
            vertices.Add(center + new Vector3(extents.x, -extents.y, -extents.z));
            vertices.Add(center + new Vector3(-extents.x, extents.y, -extents.z));
            vertices.Add(center + new Vector3(extents.x, extents.y, -extents.z));
            vertices.Add(center + new Vector3(-extents.x, -extents.y, extents.z));
            vertices.Add(center + new Vector3(extents.x, -extents.y, extents.z));
            vertices.Add(center + new Vector3(-extents.x, extents.y, extents.z));
            vertices.Add(center + new Vector3(extents.x, extents.y, extents.z));

            for (int i = 0; i < 8; i++)
                triangles.AddRange(new[] { cornerBase + i, cornerBase + i, cornerBase + i });

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        // Exit point of the water ribbon at the end of the slide (for waterfalls / spill effects).
        public static bool TryGetWaterExitInfo(
            IReadOnlyList<Vector3> controlPoints,
            Settings settings,
            float fillFraction,
            out Vector3 surfaceCenter,
            out Vector3 surfaceRight,
            out float halfWidth,
            out Vector3 flowDirection)
        {
            surfaceCenter = default;
            surfaceRight = Vector3.right;
            halfWidth = 0f;
            flowDirection = Vector3.forward;

            var path = BuildProcessedPath(controlPoints, settings);
            if (path.Count < 2)
                return false;

            var frames = BuildFrames(path, settings);
            var frame = frames[^1];
            ComputeWaterChord(settings, Mathf.Clamp01(fillFraction), out float waterY, out float halfChord);
            if (halfChord <= 0.001f)
                return false;

            surfaceCenter = ToWorld(frame, new Vector2(0f, waterY));
            surfaceRight = frame.Right;
            halfWidth = halfChord;
            var dir = path[^1] - path[^2];
            flowDirection = dir.sqrMagnitude > 1e-6f ? dir.normalized : frame.Forward;
            return true;
        }

        // Water surface height (local Y, along frame Up) and half-width of the surface at that height.
        public static void ComputeWaterChord(Settings settings, float fill, out float waterY, out float halfChord, float minWidthFraction = 0f)
        {
            if (settings.Enclosure == SlideEnclosure.Open)
            {
                float d = settings.OpenFlume.Depth;
                float halfW = settings.OpenFlume.Width * 0.5f;
                waterY = -d + fill * d;

                if (settings.OpenFlume.Shape == OpenFlumeShape.Rounded)
                {
                    float k = Mathf.Clamp01(1f - (waterY / d) * (waterY / d));
                    halfChord = halfW * Mathf.Sqrt(k) * 0.98f;
                }
                else
                {
                    halfChord = halfW * 0.92f;
                }

                // Лента шире хорды: лишнее уходит под стенки жёлоба и там не видно.
                halfChord = Mathf.Max(halfChord, halfW * Mathf.Clamp01(minWidthFraction));
            }
            else
            {
                float r = settings.ClosedTube.Radius;
                waterY = -r + fill * 2f * r;
                halfChord = Mathf.Sqrt(Mathf.Max(r * r - waterY * waterY, 0f)) * 0.98f;
            }
        }

        public static Mesh BuildRideSurface(IReadOnlyList<Vector3> controlPoints, Settings settings)
        {
            var path = BuildProcessedPath(controlPoints, settings);
            if (path.Count < 2)
                return null;

            int ringVerts = settings.RingSegments;
            bool isOpen = settings.Enclosure == SlideEnclosure.Open;
            int segmentCount = isOpen ? ringVerts - 1 : ringVerts;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            var frames = BuildFrames(path, settings);
            int rings = path.Count;

            for (int ring = 0; ring < rings; ring++)
            {
                float v = ring / (float)(rings - 1);
                var frame = frames[ring];

                for (int seg = 0; seg < ringVerts; seg++)
                {
                    float t = isOpen
                        ? (seg / (float)(ringVerts - 1)) * SlideCrossSection.OpenProfileMaxT
                        : seg / (float)ringVerts;
                    if (!isOpen && seg == ringVerts - 1)
                        t = 1f;

                    SampleCrossSection(settings, t, outer: false, out var p2, out var n2);

                    vertices.Add(ToWorld(frame, p2));
                    normals.Add(-ToWorldNormal(frame, n2));
                    uvs.Add(new Vector2(t, v));
                }
            }

            for (int ring = 0; ring < rings - 1; ring++)
            {
                for (int seg = 0; seg < segmentCount; seg++)
                {
                    int next = isOpen ? seg + 1 : (seg + 1) % ringVerts;
                    int i0 = ring * ringVerts + seg;
                    int i1 = ring * ringVerts + next;
                    int i2 = (ring + 1) * ringVerts + seg;
                    int i3 = (ring + 1) * ringVerts + next;

                    AddQuad(triangles, i0, i2, i3, i1);
                }
            }

            var mesh = new Mesh { name = "WaterSlideRideSurface" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void SampleCrossSection(Settings settings, float t, bool outer, out Vector2 point, out Vector2 normal)
        {
            if (settings.Enclosure == SlideEnclosure.Open)
                SlideCrossSection.SampleOpen(t, settings.OpenFlume, settings.WallThickness, outer, out point, out normal);
            else
                SlideCrossSection.SampleClosed(t, settings.ClosedTube, settings.WallThickness, outer, out point, out normal);
        }

        static Vector3 ToWorld(Frame frame, Vector2 profile) =>
            frame.Position + frame.Right * profile.x + frame.Up * profile.y;

        static Vector3 ToWorldNormal(Frame frame, Vector2 profileNormal) =>
            (frame.Right * profileNormal.x + frame.Up * profileNormal.y).normalized;

        public static Vector3 GetPathEndTangent(IReadOnlyList<Vector3> controlPoints)
        {
            if (controlPoints == null || controlPoints.Count < 2)
                return Vector3.forward;

            return (controlPoints[controlPoints.Count - 1] - controlPoints[controlPoints.Count - 2]).normalized;
        }

        public static bool TryGetRideStartPose(
            IReadOnlyList<Vector3> controlPoints,
            Settings settings,
            float heightAboveSurface,
            float bodyClearance,
            out Vector3 position,
            out Vector3 forward)
        {
            position = default;
            forward = Vector3.forward;

            var path = BuildProcessedPath(controlPoints, settings);
            if (path.Count < 2)
                return false;

            var frames = BuildFrames(path, settings);

            // The first slide begins with an ASCENDING launch ramp. Spawning above its lowest
            // ring dropped the player onto an up-slope (downhill points back off the entry
            // edge), and applying the clearance along the tilted floor normal pushed the spawn
            // point BEHIND the slide — the body fell past the entrance entirely. Spawn above
            // the CREST of the ramp instead: the first ring where the path starts descending.
            int startIndex = 0;
            while (startIndex < frames.Count - 1
                   && frames[startIndex + 1].Position.y >= frames[startIndex].Position.y - 0.001f)
                startIndex++;
            startIndex = Mathf.Min(startIndex + 1, frames.Count - 1);

            var frame = frames[startIndex];
            forward = frame.Forward.sqrMagnitude > 1e-6f ? frame.Forward.normalized : Vector3.forward;

            // The lateral CENTRE of the floor, per profile: the spawn must sit exactly on
            // the channel axis or the very first landing starts with a sideways slide.
            //  * Square: the floor branch spans normalized t 0.19..0.5, its middle is
            //    t=0.345 (x=0 by construction);
            //  * Rounded: the elliptical trough bottom is at normalized t=0.5 — the old
            //    0.345 landed ~0.6·halfW LEFT of the axis on rounded first slides;
            //  * closed tube: t=0.5 is the bottom of the ring (t=0.25 was its side wall).
            float floorT = settings.Enclosure == SlideEnclosure.Open
                ? (settings.OpenFlume.Shape == OpenFlumeShape.Square ? 0.345f : 0.5f)
                    * SlideCrossSection.OpenProfileMaxT
                : 0.5f;

            SampleCrossSection(settings, floorT, outer: false, out var profile, out _);
            var surfacePoint = ToWorld(frame, profile);

            // Clearance straight up in WORLD space: the drop is vertical, so the body lands
            // exactly on the sampled floor point regardless of the ramp tilt.
            float clearance = Mathf.Max(heightAboveSurface, 0.5f) + Mathf.Max(bodyClearance, 0f);
            position = surfacePoint + Vector3.up * clearance;
            return true;
        }

        struct Frame
        {
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Right;
            public Vector3 Up;
        }

        // Open flumes must stay upright (water inside), so up is world up and right is the
        // horizontal perpendicular. Closed tubes use parallel transport so the ring plane
        // stays perpendicular to the path even through vertical loops.
        static List<Frame> BuildFrames(List<Vector3> path, in Settings settings)
        {
            var frames = new List<Frame>(path.Count);
            float prevBank = 0f;
            bool isOpen = settings.Enclosure == SlideEnclosure.Open;

            for (int i = 0; i < path.Count; i++)
            {
                Vector3 forward = GetTangent(path, i);
                Vector3 up;
                Vector3 right;

                if (isOpen)
                {
                    up = Vector3.up;
                    right = BuildOpenFlumeRight(forward, frames, i);
                }
                else
                {
                    if (i == 0)
                        up = GetReferenceUp(forward, Vector3.up);
                    else
                        up = ParallelTransport(frames[i - 1].Up, frames[i - 1].Forward, forward);

                    float targetBank = ComputeBankAngle(path, i) * settings.BankStrength;
                    prevBank = i == 0 ? targetBank : Mathf.Lerp(prevBank, targetBank, 0.3f);
                    up = Quaternion.AngleAxis(prevBank, forward) * up;

                    right = Vector3.Cross(up, forward).normalized;
                    if (right.sqrMagnitude < 0.0001f)
                        right = Vector3.Cross(GetReferenceUp(forward, Vector3.up), forward).normalized;
                    up = Vector3.Cross(forward, right).normalized;
                }

                frames.Add(new Frame
                {
                    Position = path[i],
                    Forward = forward,
                    Right = right,
                    Up = up
                });
            }

            return frames;
        }

        static Vector3 BuildOpenFlumeRight(Vector3 forward, List<Frame> frames, int index)
        {
            Vector3 right;
            var cross = Vector3.Cross(Vector3.up, forward);

            // Near-vertical tangents make the cross product unstable (its direction is set by a
            // tiny horizontal remainder) — reuse the previous ring's right to avoid ring twisting.
            if (cross.sqrMagnitude > 1e-2f)
                right = cross.normalized;
            else if (index > 0)
                right = frames[index - 1].Right;
            else
                right = Vector3.right;

            if (index > 0 && Vector3.Dot(right, frames[index - 1].Right) < 0f)
                right = -right;

            return right;
        }

        static void EnforcePathMonotonicDescent(List<Vector3> path)
        {
            if (path.Count < 2)
                return;

            // The entry ramp may ascend; protect it in full (it can span many densified points).
            int rampEnd = 0;
            for (int i = 1; i < path.Count; i++)
            {
                if (path[i].y >= path[i - 1].y - 0.01f)
                    rampEnd = i;
                else
                    break;
            }

            for (int i = rampEnd + 1; i < path.Count; i++)
            {
                // Grade-proportional (12%), NOT a fixed drop per point: a constant
                // per-point drop turns into a sawtooth as soon as the sampling gets
                // denser. The grade is measured over the HORIZONTAL projection only —
                // it is unaffected by the Y-clamp below. Using the full 3D distance fed
                // the clamp back into itself: on ascending stretches every clamp grew
                // the next step, and the heights ran away exponentially to -infinity.
                Vector3 delta = path[i] - path[i - 1];
                float horizontal = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
                float maxY = path[i - 1].y - 0.12f * horizontal;
                if (path[i].y > maxY)
                    path[i] = new Vector3(path[i].x, maxY, path[i].z);
            }
        }

        static Vector3 ParallelTransport(Vector3 up, Vector3 fromForward, Vector3 toForward)
        {
            Vector3 axis = Vector3.Cross(fromForward, toForward);
            if (axis.sqrMagnitude < 1e-6f)
                return up;

            float angle = Vector3.Angle(fromForward, toForward);
            return Quaternion.AngleAxis(angle, axis.normalized) * up;
        }

        static Vector3 GetReferenceUp(Vector3 forward, Vector3 referenceUp)
        {
            var up = Vector3.ProjectOnPlane(referenceUp, forward).normalized;
            return up.sqrMagnitude < 1e-6f ? Vector3.up : up;
        }

        static Vector3 GetTangent(List<Vector3> path, int i)
        {
            Vector3 forward;
            if (i == 0)
                forward = path[1] - path[0];
            else if (i == path.Count - 1)
                forward = path[i] - path[i - 1];
            else
                forward = path[i + 1] - path[i - 1];

            return forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
        }

        static float ComputeBankAngle(List<Vector3> path, int i)
        {
            if (i <= 0 || i >= path.Count - 1)
                return 0f;

            var prev = GetTangent(path, i - 1);
            var next = GetTangent(path, i + 1);
            float turn = Vector3.SignedAngle(prev, next, Vector3.up);
            float span = Vector3.Distance(path[i - 1], path[i + 1]);
            if (span < 1e-4f)
                return 0f;

            // Degrees of turn per METRE of path, not per point: a per-point angle
            // collapses towards zero whenever the sampling density goes up, and the
            // tubes silently lose their banking.
            return Mathf.Clamp(turn / span * 0.45f, -32f, 32f);
        }

        // Caps are only added where the wall interior would otherwise be visible:
        // - the ring annulus at the FIRST and LAST ring (tube ends),
        // - and, on open flumes, the top edge of both walls along the whole path.
        // Adding cross-section caps on every interior ring (as before) buries thousands of
        // hidden quads inside the wall and, on twisted rings, shows up as rungs across the flume.
        static void AddWallThicknessCaps(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            int vertexBase,
            int rings,
            int ringVerts,
            bool isOpen,
            float[] ringDistance)
        {
            int segmentCount = isOpen ? ringVerts - 1 : ringVerts;

            // End caps: seal the wall annulus at both tube ends (full profile, no gaps).
            for (int e = 0; e < 2; e++)
            {
                int ring = e == 0 ? 0 : rings - 1;
                float v = ringDistance[ring];
                for (int seg = 0; seg < segmentCount; seg++)
                {
                    int next = isOpen ? seg + 1 : (seg + 1) % ringVerts;
                    int o0 = vertexBase + (ring * ringVerts + seg) * 2;
                    int o1 = vertexBase + (ring * ringVerts + next) * 2;
                    int i0 = o0 + 1;
                    int i1 = o1 + 1;
                    float u = seg / (float)Mathf.Max(segmentCount - 1, 1);
                    AddCapQuad(vertices, normals, uvs, triangles,
                        vertices[i0], vertices[o0], vertices[o1], vertices[i1],
                        new Vector2(u, v));
                }
            }

            if (!isOpen)
                return;

            // Wall-top bands: close the gap between inner and outer shell along the top edge
            // of the left (seg 0) and right (seg ringVerts-1) lips.
            for (int ring = 0; ring < rings - 1; ring++)
            {
                float v = ringDistance[ring];
                for (int k = 0; k < 2; k++)
                {
                    int seg = k == 0 ? 0 : ringVerts - 1;
                    int o0 = vertexBase + (ring * ringVerts + seg) * 2;
                    int o2 = vertexBase + ((ring + 1) * ringVerts + seg) * 2;
                    int i0 = o0 + 1;
                    int i2 = o2 + 1;
                    AddCapQuad(vertices, normals, uvs, triangles,
                        vertices[i0], vertices[o0], vertices[o2], vertices[i2],
                        new Vector2(k, v));
                }
            }
        }

        static void AddCapQuad(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector2 uv)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f)
                return;
            n.Normalize();

            int baseIdx = vertices.Count;
            vertices.Add(a); normals.Add(n); uvs.Add(uv);
            vertices.Add(b); normals.Add(n); uvs.Add(uv);
            vertices.Add(c); normals.Add(n); uvs.Add(uv);
            vertices.Add(d); normals.Add(n); uvs.Add(uv);
            AddDoubleSidedQuad(triangles, baseIdx, baseIdx + 1, baseIdx + 2, baseIdx + 3);
        }

        static void AddQuad(List<int> tris, int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(c); tris.Add(d); tris.Add(a);
        }

        static void AddDoubleSidedQuad(List<int> tris, int a, int b, int c, int d)
        {
            AddQuad(tris, a, b, c, d);
            AddQuad(tris, a, d, c, b);
        }
    }
}
