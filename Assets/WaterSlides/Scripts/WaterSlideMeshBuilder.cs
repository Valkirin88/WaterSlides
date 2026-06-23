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

            public SlideCrossSection.OpenFlume OpenFlume;
            public SlideCrossSection.ClosedTube ClosedTube;
        }

        public static Mesh BuildTube(IReadOnlyList<Vector3> controlPoints, Settings settings)
        {
            var path = SamplePath(controlPoints, settings.SamplesPerSegment);
            if (path.Count < 2)
                return null;

            path = StabilizePath(path);
            EnforcePathMonotonicDescent(path);
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
                    SampleCrossSection(settings, t, outer: true, out var p2o, out _);

                    var inner = ToWorld(frame, p2);
                    var outer = ToWorld(frame, p2o);
                    var normal = ToWorldNormal(frame, n2);

                    vertices.Add(inner);
                    normals.Add(normal);
                    uvs.Add(new Vector2(t, v));

                    vertices.Add(outer);
                    normals.Add(normal);
                    uvs.Add(new Vector2(t, v));
                }
            }

            for (int ring = 0; ring < rings - 1; ring++)
            {
                int segmentCount = isOpen ? ringVerts - 1 : ringVerts;
                for (int seg = 0; seg < segmentCount; seg++)
                {
                    int next = isOpen ? seg + 1 : (seg + 1) % ringVerts;
                    int i0 = (ring * ringVerts + seg) * 2;
                    int i1 = (ring * ringVerts + next) * 2;
                    int i2 = ((ring + 1) * ringVerts + seg) * 2;
                    int i3 = ((ring + 1) * ringVerts + next) * 2;

                    AddQuad(triangles, i0, i2, i3, i1);
                    AddQuad(triangles, i0 + 1, i1 + 1, i3 + 1, i2 + 1);
                }
            }

            if (!isOpen)
                AddFlangeRings(settings, frames, vertices, normals, uvs, triangles, ringVerts, rings);

            var mesh = new Mesh { name = "WaterSlide" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh BuildRideSurface(IReadOnlyList<Vector3> controlPoints, Settings settings)
        {
            var path = SamplePath(controlPoints, settings.SamplesPerSegment);
            if (path.Count < 2)
                return null;

            path = StabilizePath(path);
            EnforcePathMonotonicDescent(path);
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

        static List<Vector3> StabilizePath(List<Vector3> path)
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
                    float maxY = Mathf.Min(result[i].y, result[i - 1].y);
                    if (smoothed.y > maxY)
                        smoothed.y = maxY;
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

        static void AddFlangeRings(
            Settings settings,
            List<Frame> frames,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            int ringVerts,
            int rings)
        {
            AddFlangeRing(settings, frames[0], vertices, normals, uvs, triangles, ringVerts, rings, 0, -1f);
            AddFlangeRing(settings, frames[^1], vertices, normals, uvs, triangles, ringVerts, rings, rings - 1, 1f);
        }

        static void AddFlangeRing(
            Settings settings,
            Frame frame,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            int ringVerts,
            int rings,
            int ringIndex,
            float forwardSign)
        {
            float flange = settings.WallThickness * 1.6f;
            for (int seg = 0; seg < ringVerts; seg++)
            {
                float t = seg / (float)ringVerts;
                float tNext = (seg + 1) / (float)ringVerts;

                SampleCrossSection(settings, t, true, out var p0, out var n0);
                SampleCrossSection(settings, tNext, true, out var p1, out _);

                var w0 = ToWorld(frame, p0);
                var w1 = ToWorld(frame, p1);
                var f0 = w0 + frame.Forward * flange * forwardSign;
                var f1 = w1 + frame.Forward * flange * forwardSign;

                var normal = ToWorldNormal(frame, n0);
                var flangeNormal = frame.Forward * forwardSign;
                int b = vertices.Count;
                vertices.Add(w0); normals.Add(normal); uvs.Add(new Vector2(t, ringIndex / (float)(rings - 1)));
                vertices.Add(f0); normals.Add(flangeNormal); uvs.Add(new Vector2(t, 0f));
                vertices.Add(w1); normals.Add(normal); uvs.Add(new Vector2(tNext, ringIndex / (float)(rings - 1)));
                vertices.Add(f1); normals.Add(flangeNormal); uvs.Add(new Vector2(tNext, 0f));
                AddQuad(triangles, b, b + 2, b + 3, b + 1);
            }
        }

        static void AddRimWalls(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            int rings,
            int ringVerts,
            Settings settings)
        {
            AddRimWallStrip(vertices, normals, uvs, triangles, rings, ringVerts, 0, settings, leftEdge: true);
            AddRimWallStrip(vertices, normals, uvs, triangles, rings, ringVerts, ringVerts - 1, settings, leftEdge: false);
        }

        static void AddRimWallStrip(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            int rings,
            int ringVerts,
            int seg,
            Settings settings,
            bool leftEdge)
        {
            for (int ring = 0; ring < rings - 1; ring++)
            {
                int i0 = (ring * ringVerts + seg) * 2;
                int i1 = ((ring + 1) * ringVerts + seg) * 2;
                int o0 = i0 + 1;
                int o1 = i1 + 1;

                var innerA = vertices[i0];
                var outerA = vertices[o0];
                var innerB = vertices[i1];
                var outerB = vertices[o1];

                var n = Vector3.Cross(outerA - innerA, innerB - innerA).normalized;
                if (n.sqrMagnitude < 0.0001f)
                    continue;

                int baseIdx = vertices.Count;
                vertices.Add(innerA); normals.Add(n); uvs.Add(new Vector2(0f, ring / (float)(rings - 1)));
                vertices.Add(outerA); normals.Add(n); uvs.Add(new Vector2(1f, ring / (float)(rings - 1)));
                vertices.Add(innerB); normals.Add(n); uvs.Add(new Vector2(0f, (ring + 1) / (float)(rings - 1)));
                vertices.Add(outerB); normals.Add(n); uvs.Add(new Vector2(1f, (ring + 1) / (float)(rings - 1)));

                if (leftEdge)
                    AddQuad(triangles, baseIdx, baseIdx + 2, baseIdx + 3, baseIdx + 1);
                else
                    AddQuad(triangles, baseIdx, baseIdx + 1, baseIdx + 3, baseIdx + 2);
            }
        }

        static void AddQuad(List<int> tris, int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(c); tris.Add(d); tris.Add(a);
        }
    }
}
