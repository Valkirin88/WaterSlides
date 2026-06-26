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

            public SlideCrossSection.OpenFlume OpenFlume;
            public SlideCrossSection.ClosedTube ClosedTube;
        }

        // The exact centreline the mesh is built on (after smoothing + descent clamping).
        // Use this for connecting slides so the jump lines up with what is actually rendered.
        public static List<Vector3> BuildProcessedPath(IReadOnlyList<Vector3> controlPoints, Settings settings)
        {
            var path = SamplePath(controlPoints, settings.SamplesPerSegment);
            if (path.Count < 2)
                return path;

            path = StabilizePath(path, clampY: !settings.PreserveVerticalMotion);
            if (!settings.PreserveVerticalMotion)
                EnforcePathMonotonicDescent(path);

            float clearance = settings.Enclosure == SlideEnclosure.Open
                ? Mathf.Max(settings.OpenFlume.Width, 0.9f)
                : Mathf.Max(settings.ClosedTube.Radius * 2f + settings.WallThickness * 2f, 0.9f);
            RemoveSampledLoops(path, clearance);
            return path;
        }

        // Removes tiny self-intersecting loops the spline can form at hard turns: if a later sample
        // comes back within one tube-width of an earlier one, the points between them form a loop
        // that is cut out. Real loops stay clear in 3D (helix pitch / corkscrew offset) so survive.
        static void RemoveSampledLoops(List<Vector3> path, float clearance)
        {
            if (path.Count < 5)
                return;

            float sqr = clearance * clearance;
            for (int i = 0; i < path.Count - 4; i++)
            {
                for (int j = i + 4; j < path.Count; j++)
                {
                    if ((path[j] - path[i]).sqrMagnitude < sqr)
                    {
                        path.RemoveRange(i + 1, j - i - 1);
                        break;
                    }
                }
            }
        }

        // Visual mesh: inner + outer shell + wall thickness caps (double-sided).
        public static Mesh BuildTube(IReadOnlyList<Vector3> controlPoints, Settings settings)
        {
            var path = BuildProcessedPath(controlPoints, settings);
            if (path.Count < 2)
                return null;

            var frames = BuildFrames(path, settings.BankStrength, settings.Enclosure);
            int ringVerts = settings.RingSegments;
            int rings = path.Count;
            bool isOpen = settings.Enclosure == SlideEnclosure.Open;
            int segmentCount = isOpen ? ringVerts - 1 : ringVerts;

            var vertices = new List<Vector3>(rings * ringVerts * 2);
            var normals = new List<Vector3>(rings * ringVerts * 2);
            var uvs = new List<Vector2>(rings * ringVerts * 2);
            var triangles = new List<int>();

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
                    int o0 = (ring * ringVerts + seg) * 2;
                    int o1 = (ring * ringVerts + next) * 2;
                    int o2 = ((ring + 1) * ringVerts + seg) * 2;
                    int o3 = ((ring + 1) * ringVerts + next) * 2;
                    AddDoubleSidedQuad(triangles, o0, o1, o3, o2);

                    int i0 = o0 + 1;
                    int i1 = o1 + 1;
                    int i2 = o2 + 1;
                    int i3 = o3 + 1;
                    AddDoubleSidedQuad(triangles, i0, i2, i3, i1);
                }
            }

            AddWallThicknessCaps(vertices, normals, uvs, triangles, rings, ringVerts, isOpen);

            var mesh = new Mesh { name = "WaterSlide" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // A thin water ribbon sitting at a fill level inside the tube/flume, following the path.
        // UV.x = width (0–1), UV.y = distance along the slide — matches Suimono flow along V.
        public static Mesh BuildWaterSurface(IReadOnlyList<Vector3> controlPoints, Settings settings, float fillFraction)
        {
            var path = BuildProcessedPath(controlPoints, settings);
            if (path.Count < 2)
                return null;

            var frames = BuildFrames(path, settings.BankStrength, settings.Enclosure);
            int rings = path.Count;

            var vertices = new List<Vector3>(rings * 2);
            var normals = new List<Vector3>(rings * 2);
            var uvs = new List<Vector2>(rings * 2);
            var triangles = new List<int>();

            ComputeWaterChord(settings, Mathf.Clamp01(fillFraction), out float waterY, out float halfChord);
            if (halfChord <= 0.001f)
                return null;

            float vAccum = 0f;
            for (int ring = 0; ring < rings; ring++)
            {
                var frame = frames[ring];
                if (ring > 0)
                    vAccum += Vector3.Distance(frame.Position, frames[ring - 1].Position);

                vertices.Add(ToWorld(frame, new Vector2(-halfChord, waterY)));
                normals.Add(frame.Up);
                uvs.Add(new Vector2(0f, vAccum));

                vertices.Add(ToWorld(frame, new Vector2(halfChord, waterY)));
                normals.Add(frame.Up);
                uvs.Add(new Vector2(1f, vAccum));
            }

            for (int ring = 0; ring < rings - 1; ring++)
            {
                int i0 = ring * 2;
                int i1 = ring * 2 + 1;
                int i2 = (ring + 1) * 2;
                int i3 = (ring + 1) * 2 + 1;
                AddQuad(triangles, i0, i2, i3, i1);
            }

            var mesh = new Mesh { name = "WaterSlideWater" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
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

            var frames = BuildFrames(path, settings.BankStrength, settings.Enclosure);
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
        public static void ComputeWaterChord(Settings settings, float fill, out float waterY, out float halfChord)
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

            var frames = BuildFrames(path, settings.BankStrength, settings.Enclosure);
            int ringVerts = settings.RingSegments;
            int rings = path.Count;
            bool isOpen = settings.Enclosure == SlideEnclosure.Open;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

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
                int segmentCount = isOpen ? ringVerts - 1 : ringVerts;
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

        public static List<Vector3> SamplePath(IReadOnlyList<Vector3> controlPoints, int samplesPerSegment)
        {
            var result = new List<Vector3>();
            if (controlPoints == null || controlPoints.Count < 2)
                return result;

            if (controlPoints.Count == 2)
            {
                int steps = samplesPerSegment + 1;
                for (int i = 0; i < steps; i++)
                    result.Add(Vector3.Lerp(controlPoints[0], controlPoints[1], i / (float)(steps - 1)));
                return result;
            }

            for (int i = 0; i < controlPoints.Count - 1; i++)
            {
                var p0 = controlPoints[Mathf.Max(i - 1, 0)];
                var p1 = controlPoints[i];
                var p2 = controlPoints[i + 1];
                var p3 = controlPoints[Mathf.Min(i + 2, controlPoints.Count - 1)];

                int steps = i == controlPoints.Count - 2 ? samplesPerSegment + 1 : samplesPerSegment;
                int start = i == 0 ? 0 : 1;

                for (int s = start; s < steps; s++)
                {
                    float t = s / (float)samplesPerSegment;
                    result.Add(CentripetalCatmullRom(p0, p1, p2, p3, t));
                }
            }

            return result;
        }

        static Vector3 CentripetalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            const float alpha = 0.5f;
            float t0 = 0f;
            float t1 = t0 + SplineKnot(p0, p1, alpha);
            float t2 = t1 + SplineKnot(p1, p2, alpha);
            float t3 = t2 + SplineKnot(p2, p3, alpha);

            if (t2 - t1 < 1e-5f)
                return Vector3.Lerp(p1, p2, t);

            float tt = Mathf.Lerp(t1, t2, t);
            var a1 = SplineLerp(p0, p1, t0, t1, tt);
            var a2 = SplineLerp(p1, p2, t1, t2, tt);
            var a3 = SplineLerp(p2, p3, t2, t3, tt);
            var b1 = SplineLerp(a1, a2, t0, t2, tt);
            var b2 = SplineLerp(a2, a3, t1, t3, tt);
            return SplineLerp(b1, b2, t1, t2, tt);
        }

        static float SplineKnot(Vector3 a, Vector3 b, float alpha)
        {
            float d = (b - a).sqrMagnitude;
            return d < 1e-8f ? 1e-4f : Mathf.Pow(d, 0.5f * alpha);
        }

        static Vector3 SplineLerp(Vector3 a, Vector3 b, float ta, float tb, float t)
        {
            if (Mathf.Abs(tb - ta) < 1e-6f)
                return a;
            return a + (b - a) * ((t - ta) / (tb - ta));
        }

        public static Vector3 GetPathEndTangent(IReadOnlyList<Vector3> controlPoints)
        {
            if (controlPoints == null || controlPoints.Count < 2)
                return Vector3.forward;

            return (controlPoints[controlPoints.Count - 1] - controlPoints[controlPoints.Count - 2]).normalized;
        }

        struct Frame
        {
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Right;
            public Vector3 Up;
        }

        static List<Vector3> StabilizePath(List<Vector3> path, bool clampY)
        {
            if (path.Count < 3)
                return path;

            var result = new List<Vector3>(path);
            for (int i = 1; i < result.Count - 1; i++)
            {
                var incoming = (result[i] - result[i - 1]).normalized;
                var outgoing = (result[i + 1] - result[i]).normalized;
                if (Vector3.Angle(incoming, outgoing) > 42f)
                {
                    var mid = (result[i - 1] + result[i + 1]) * 0.5f;
                    var smoothed = Vector3.Lerp(result[i], mid, 0.22f);
                    if (clampY)
                    {
                        float maxY = Mathf.Min(result[i].y, result[i - 1].y);
                        if (smoothed.y > maxY)
                            smoothed.y = maxY;
                    }
                    result[i] = smoothed;
                }
            }

            return result;
        }

        static List<Frame> BuildFrames(List<Vector3> path, float bankStrength, SlideEnclosure enclosure)
        {
            var frames = new List<Frame>(path.Count);
            float prevBank = 0f;
            bool isOpen = enclosure == SlideEnclosure.Open;

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

                    float targetBank = ComputeBankAngle(path, i) * bankStrength;
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
            if (cross.sqrMagnitude > 1e-4f)
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

            int rampEnd = 0;
            for (int i = 1; i < Mathf.Min(5, path.Count); i++)
            {
                if (path[i].y >= path[i - 1].y - 0.01f)
                    rampEnd = i;
                else
                    break;
            }

            for (int i = rampEnd + 1; i < path.Count; i++)
            {
                float maxY = path[i - 1].y - 0.05f;
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
            return Mathf.Clamp(turn * 0.55f, -32f, 32f);
        }

        static void AddWallThicknessCaps(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            int rings,
            int ringVerts,
            bool isOpen)
        {
            int segmentCount = isOpen ? ringVerts - 1 : ringVerts;

            // Own vertices + face normals so shell normals are not averaged (RecalculateNormals breaks shading).
            for (int ring = 0; ring < rings; ring++)
            {
                float v = ring / (float)(rings - 1);
                for (int seg = 0; seg < segmentCount; seg++)
                {
                    int next = isOpen ? seg + 1 : (seg + 1) % ringVerts;
                    int o0 = (ring * ringVerts + seg) * 2;
                    int o1 = (ring * ringVerts + next) * 2;
                    int i0 = o0 + 1;
                    int i1 = o1 + 1;
                    float u = seg / (float)Mathf.Max(segmentCount - 1, 1);
                    AddCapQuad(vertices, normals, uvs, triangles,
                        vertices[i0], vertices[o0], vertices[o1], vertices[i1],
                        new Vector2(u, v));
                }
            }

            for (int ring = 0; ring < rings - 1; ring++)
            {
                float v = ring / (float)(rings - 1);
                for (int seg = 0; seg < ringVerts; seg++)
                {
                    int o0 = (ring * ringVerts + seg) * 2;
                    int o2 = ((ring + 1) * ringVerts + seg) * 2;
                    int i0 = o0 + 1;
                    int i2 = o2 + 1;
                    float u = seg / (float)Mathf.Max(ringVerts - 1, 1);
                    AddCapQuad(vertices, normals, uvs, triangles,
                        vertices[i0], vertices[o0], vertices[o2], vertices[i2],
                        new Vector2(u, v));
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
