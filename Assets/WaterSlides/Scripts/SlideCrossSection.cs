using UnityEngine;

namespace WaterSlides
{
    public enum OpenFlumeShape
    {
        Square,
        Rounded
    }

    public static class SlideCrossSection
    {
        public struct OpenFlume
        {
            public float Width;
            public float Depth;
            public float LipHeight;
            public float CornerRadius;
            public float CopingOverhang;
            public OpenFlumeShape Shape;
        }

        public struct ClosedTube
        {
            public float Radius;
        }

        const float OpenProfileEnd = 0.68f;

        public static float OpenProfileMaxT => OpenProfileEnd;

        public static Vector2 SampleOpenInner(float t, in OpenFlume flume)
        {
            t = Mathf.Clamp01(t / OpenProfileEnd);

            return flume.Shape == OpenFlumeShape.Rounded
                ? SampleOpenRounded(t, flume)
                : SampleOpenSquare(t, flume);
        }

        static Vector2 SampleOpenSquare(float t, in OpenFlume flume)
        {
            float halfW = flume.Width * 0.5f;
            float d = flume.Depth;
            float lip = flume.LipHeight;
            float r = Mathf.Min(flume.CornerRadius, halfW * 0.4f, d * 0.8f);

            if (t <= 0.11f)
            {
                float u = t / 0.11f;
                return new Vector2(-halfW, Mathf.Lerp(lip, -d + r, u));
            }

            if (t <= 0.19f)
            {
                float u = (t - 0.11f) / 0.08f;
                float angle = Mathf.Lerp(Mathf.PI, Mathf.PI * 1.5f, u);
                var center = new Vector2(-halfW + r, -d + r);
                return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
            }

            if (t <= 0.5f)
            {
                float u = (t - 0.19f) / 0.31f;
                float x = Mathf.Lerp(-halfW + r, halfW - r, u);
                float crown = Mathf.Cos(u * Mathf.PI) * d * 0.035f;
                return new Vector2(x, -d + crown);
            }

            if (t <= 0.58f)
            {
                float u = (t - 0.5f) / 0.08f;
                float angle = Mathf.Lerp(Mathf.PI * 1.5f, Mathf.PI * 2f, u);
                var center = new Vector2(halfW - r, -d + r);
                return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
            }

            float v = (t - 0.58f) / 0.42f;
            return new Vector2(halfW, Mathf.Lerp(-d + r, lip, v));
        }

        static Vector2 SampleOpenRounded(float t, in OpenFlume flume)
        {
            float halfW = flume.Width * 0.5f;
            float d = flume.Depth;
            float lip = flume.LipHeight;
            const float lipFrac = 0.12f;

            // Left vertical lip above the trough.
            if (t <= lipFrac)
            {
                float u = t / lipFrac;
                return new Vector2(-halfW, Mathf.Lerp(lip, 0f, u));
            }

            // Right vertical lip above the trough.
            if (t >= 1f - lipFrac)
            {
                float u = (t - (1f - lipFrac)) / lipFrac;
                return new Vector2(halfW, Mathf.Lerp(0f, lip, u));
            }

            // Elliptical trough: left rim (angle = PI) -> bottom -> right rim (angle = 2PI).
            float a = (t - lipFrac) / (1f - 2f * lipFrac);
            float angle = Mathf.Lerp(Mathf.PI, Mathf.PI * 2f, a);
            return new Vector2(Mathf.Cos(angle) * halfW, Mathf.Sin(angle) * d);
        }

        public static Vector2 SampleClosedInner(float t, in ClosedTube tube)
        {
            float angle = t * Mathf.PI * 2f;
            return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * tube.Radius;
        }

        public static void SampleOpen(float t, in OpenFlume flume, float wallThickness, bool outer,
            out Vector2 point, out Vector2 normal)
        {
            float profileT = Mathf.Clamp01(t);
            var inner = SampleOpenInner(profileT, flume);
            var offset = GetOpenExtrusionOffset(profileT, flume, inner, wallThickness);
            normal = offset.sqrMagnitude > 1e-8f ? offset.normalized : Vector2.up;
            point = outer ? inner + offset : inner;
            if (!outer)
                normal = -normal;
        }

        public static void SampleClosed(float t, in ClosedTube tube, float wallThickness, bool outer,
            out Vector2 point, out Vector2 normal)
        {
            normal = SampleClosedInner(t, tube).normalized;
            var inner = normal * tube.Radius;
            point = outer ? inner + normal * wallThickness : inner;
            if (!outer)
                normal = -normal;
        }

        static Vector2 GetOpenExtrusionOffset(float profileT, in OpenFlume flume, Vector2 inner, float thickness)
        {
            return flume.Shape == OpenFlumeShape.Rounded
                ? GetOpenRoundedExtrusion(profileT, flume, inner, thickness)
                : GetOpenSquareExtrusion(profileT, flume, inner, thickness);
        }

        static Vector2 GetOpenSquareExtrusion(float profileT, in OpenFlume flume, Vector2 inner, float thickness)
        {
            float t = Mathf.Clamp01(profileT / OpenProfileEnd);
            float halfW = flume.Width * 0.5f;
            float d = flume.Depth;
            float r = Mathf.Min(flume.CornerRadius, halfW * 0.4f, d * 0.8f);

            if (t <= 0.11f)
                return Vector2.left * thickness;
            if (t <= 0.19f)
                return RadialCornerOffset(inner, new Vector2(-halfW + r, -d + r), thickness);
            if (t <= 0.5f)
                return Vector2.down * thickness;
            if (t <= 0.58f)
                return RadialCornerOffset(inner, new Vector2(halfW - r, -d + r), thickness);
            return Vector2.right * thickness;
        }

        static Vector2 GetOpenRoundedExtrusion(float profileT, in OpenFlume flume, Vector2 inner, float thickness)
        {
            float t = Mathf.Clamp01(profileT / OpenProfileEnd);
            const float lipFrac = 0.12f;

            if (t <= lipFrac)
                return Vector2.left * thickness;
            if (t >= 1f - lipFrac)
                return Vector2.right * thickness;

            var fromCenter = inner;
            if (fromCenter.sqrMagnitude < 1e-8f)
                return Vector2.down * thickness;
            return fromCenter.normalized * thickness;
        }

        static Vector2 RadialCornerOffset(Vector2 inner, Vector2 center, float thickness)
        {
            var dir = inner - center;
            if (dir.sqrMagnitude < 1e-8f)
                return Vector2.down * thickness;
            return dir.normalized * thickness;
        }
    }
}
