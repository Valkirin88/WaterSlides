using System;
using System.Collections.Generic;
using UnityEngine;

namespace WaterSlides
{
    public enum SlidePathFeature
    {
        Straight,
        WideCurve,
        SharpTurn,
        SCurve,
        Corkscrew,
        Helix,
        BowlTurn,
        Zigzag,
        Drop
    }

    public struct WaterSlidePathSettings
    {
        public float TargetLength;
        public float TotalDrop;
        public float TurnRadiusMin;
        public float TurnRadiusMax;
        public int MinFeatures;
        public int MaxFeatures;
        public bool AllowLoops;
        public SlideEnclosure Enclosure;
        public float InitialLaunchLength;
        public bool AllowEntryRamp;
        public bool AscendingStart;
        public Vector3 WorldStart;
        public Vector3 WorldStartForward;
        public bool UseWorldSpace;
    }

    public static class WaterSlidePathGenerator
    {
        struct PathState
        {
            public Vector3 Position;
            public Vector3 Forward;
        }

        public static List<Vector3> Generate(System.Random rng, WaterSlidePathSettings settings)
        {
            if (settings.UseWorldSpace)
                return GenerateInWorldSpace(rng, settings, settings.WorldStart, settings.WorldStartForward);

            return GenerateInWorldSpace(rng, settings, Vector3.zero, Vector3.forward);
        }

        public static List<Vector3> GenerateInWorldSpace(
            System.Random rng,
            WaterSlidePathSettings settings,
            Vector3 worldStart,
            Vector3 worldStartForward)
        {
            var points = new List<Vector3> { worldStart };
            var forward = worldStartForward.sqrMagnitude > 0.0001f ? worldStartForward.normalized : Vector3.forward;
            var state = new PathState
            {
                Position = worldStart,
                Forward = forward
            };

            int rampEndIndex = 0;
            if (settings.AllowEntryRamp && settings.InitialLaunchLength > 0.01f)
            {
                // First slide: gentle ascending launch tower. Chained slides: continue straight
                // along the incoming jump direction so the entrance lines up with the flight path.
                var rampDir = settings.AscendingStart ? EnsureAscendingDirection(forward) : forward;
                var launchEnd = state.Position + rampDir * settings.InitialLaunchLength;
                points.Add(launchEnd);
                state.Position = launchEnd;
                rampEndIndex = points.Count - 1;
                if (settings.AscendingStart)
                    EnsureAscendingRamp(points, rampEndIndex, Mathf.Max(settings.InitialLaunchLength * 0.25f, 0.75f));
            }

            state.Forward = GetDescentForward(state.Forward);

            var features = PickFeatures(rng, settings);
            float dropBudget = settings.TotalDrop;
            float lengthBudget = settings.TargetLength;

            for (int i = 0; i < features.Count; i++)
            {
                float featureDrop = dropBudget / (features.Count - i);
                float featureLength = lengthBudget / (features.Count - i);
                if (i > 0)
                    InsertBlendPoint(points, ref state);
                AppendFeature(points, ref state, rng, features[i], settings, featureLength, featureDrop);
            }

            AppendExitTangentSegment(points);
            EnforceMonotonicDescent(points, rampEndIndex);
            EnsureMinimumTotalDrop(points, settings.TotalDrop, rampEndIndex);
            SmoothCorners(points, rampEndIndex, iterations: 3, factor: 0.32f);
            EnforceMonotonicDescent(points, rampEndIndex);
            EnsureStartAboveEnd(points, settings.TotalDrop, rampEndIndex);
            return points;
        }

        // Gentle upward launch (~20 deg). Never near-vertical (avoids the "stick" artefact).
        static Vector3 EnsureAscendingDirection(Vector3 hint)
        {
            var horiz = Vector3.ProjectOnPlane(hint, Vector3.up);
            if (horiz.sqrMagnitude < 1e-4f)
                horiz = Vector3.forward;
            horiz.Normalize();
            return (horiz + Vector3.up * 0.36f).normalized;
        }

        // Rounds every interior corner with a few Laplacian passes; endpoints stay fixed.
        static void SmoothCorners(List<Vector3> points, int rampEndIndex, int iterations, float factor)
        {
            if (points.Count < 3)
                return;

            for (int it = 0; it < iterations; it++)
            {
                var copy = new List<Vector3>(points);
                for (int i = 1; i < points.Count - 1; i++)
                {
                    var avg = (copy[i - 1] + copy[i + 1]) * 0.5f;
                    points[i] = Vector3.Lerp(copy[i], avg, factor);
                }
            }
        }

        // Short, gentle, mostly-horizontal exit so the jump launches forward (codirectional with next entry).
        static void AppendExitTangentSegment(List<Vector3> points)
        {
            if (points.Count < 2)
                return;

            var dir = (points[^1] - points[^2]).normalized;
            var horiz = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (horiz.sqrMagnitude < 1e-4f)
                horiz = Vector3.ProjectOnPlane(points[^2] - points[Mathf.Max(points.Count - 3, 0)], Vector3.up);
            if (horiz.sqrMagnitude < 1e-4f)
                horiz = Vector3.forward;
            horiz.Normalize();

            var exitDir = (horiz + Vector3.down * 0.22f).normalized;
            points.Add(points[^1] + exitDir * 2.5f);
        }

        static void EnsureAscendingRamp(List<Vector3> points, int rampEndIndex, float minRise)
        {
            if (rampEndIndex < 1)
                return;

            float deficit = (points[0].y + minRise) - points[rampEndIndex].y;
            if (deficit <= 0f)
                return;

            for (int i = 1; i <= rampEndIndex; i++)
            {
                float t = i / (float)rampEndIndex;
                points[i] = new Vector3(points[i].x, points[i].y + deficit * t, points[i].z);
            }
        }

        static void EnsureStartAboveEnd(List<Vector3> points, float targetDrop, int rampEndIndex)
        {
            if (points.Count < 2)
                return;

            float startY = points[0].y;
            float minEndY = startY - Mathf.Max(targetDrop, 3f);
            if (points[^1].y <= minEndY)
                return;

            float excess = points[^1].y - minEndY;
            int descentStart = Mathf.Clamp(rampEndIndex + 1, 1, points.Count - 1);
            int span = points.Count - descentStart;
            if (span <= 0)
            {
                points[^1] = new Vector3(points[^1].x, minEndY, points[^1].z);
                return;
            }

            for (int i = descentStart; i < points.Count; i++)
            {
                float t = (i - descentStart + 1) / (float)span;
                float y = points[i].y - excess * t;
                if (y >= points[i - 1].y)
                    y = points[i - 1].y - 0.08f;
                points[i] = new Vector3(points[i].x, y, points[i].z);
            }
        }

        static Vector3 GetDescentForward(Vector3 forward)
        {
            var horiz = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (horiz.sqrMagnitude < 0.0001f)
                horiz = Vector3.forward;
            horiz.Normalize();
            return (horiz + Vector3.down * 0.35f).normalized;
        }

        static void EnsureMinimumTotalDrop(List<Vector3> points, float targetDrop, int rampEndIndex)
        {
            if (points.Count < 2)
                return;

            float startY = points[0].y;
            float peakY = startY;
            for (int i = 0; i <= rampEndIndex && i < points.Count; i++)
                peakY = Mathf.Max(peakY, points[i].y);

            float minEndY = startY - Mathf.Max(targetDrop, 3f);
            if (points[^1].y <= minEndY)
                return;

            float excess = points[^1].y - minEndY;
            int start = Mathf.Max(rampEndIndex + 1, 1);
            int span = points.Count - start;
            if (span <= 0)
            {
                points[^1] = new Vector3(points[^1].x, minEndY, points[^1].z);
                return;
            }

            for (int i = start; i < points.Count; i++)
            {
                float t = (i - start + 1) / (float)span;
                float y = points[i].y - excess * t;
                if (i > 0 && y > points[i - 1].y)
                    y = points[i - 1].y - 0.05f;
                points[i] = new Vector3(points[i].x, y, points[i].z);
            }
        }

        static List<SlidePathFeature> PickFeatures(System.Random rng, WaterSlidePathSettings settings)
        {
            int count = rng.Next(settings.MinFeatures, settings.MaxFeatures + 1);
            var result = new List<SlidePathFeature>(count) { SlidePathFeature.Straight };

            var pool = new List<SlidePathFeature>
            {
                SlidePathFeature.WideCurve,
                SlidePathFeature.SharpTurn,
                SlidePathFeature.SCurve,
                SlidePathFeature.Drop
            };

            if (settings.Enclosure == SlideEnclosure.Open)
                pool.Add(SlidePathFeature.WideCurve);

            if (settings.AllowLoops)
            {
                pool.Add(SlidePathFeature.Corkscrew);
                pool.Add(SlidePathFeature.Helix);
                pool.Add(SlidePathFeature.BowlTurn);
            }

            Shuffle(rng, pool);

            while (result.Count < count)
            {
                var pick = pool[rng.Next(pool.Count)];
                if (result.Count > 1 && result[^1] == pick && pick != SlidePathFeature.Straight)
                    continue;
                if (pick == SlidePathFeature.BowlTurn && !settings.AllowLoops && rng.NextDouble() > 0.35)
                    continue;
                result.Add(pick);
            }

            if (result[^1] != SlidePathFeature.Straight && result[^1] != SlidePathFeature.Drop)
                result.Add(SlidePathFeature.Straight);

            return result;
        }

        static void AppendFeature(
            List<Vector3> points,
            ref PathState state,
            System.Random rng,
            SlidePathFeature feature,
            WaterSlidePathSettings settings,
            float length,
            float drop)
        {
            float radius = Mathf.Lerp(settings.TurnRadiusMin, settings.TurnRadiusMax, (float)rng.NextDouble());

            switch (feature)
            {
                case SlidePathFeature.Straight:
                    AppendStraight(points, ref state, length * 0.6f, drop * 0.5f);
                    break;
                case SlidePathFeature.WideCurve:
                    AppendArc(points, ref state, radius * 1.4f, PickSign(rng) * Mathf.PI * 0.45f, drop, 6);
                    break;
                case SlidePathFeature.SharpTurn:
                    AppendArc(points, ref state, radius * 0.75f, PickSign(rng) * Mathf.PI * 0.55f, drop, 5);
                    break;
                case SlidePathFeature.SCurve:
                    AppendSCurve(points, ref state, radius, drop, rng);
                    break;
                case SlidePathFeature.Corkscrew:
                    AppendCorkscrew(points, ref state, radius * 0.85f, 0.45f + (float)rng.NextDouble() * 0.35f, length, drop, 16);
                    break;
                case SlidePathFeature.Helix:
                    AppendCorkscrew(points, ref state, radius * 1.05f, 0.65f + (float)rng.NextDouble() * 0.45f, length * 1.05f, drop, 22);
                    break;
                case SlidePathFeature.BowlTurn:
                    AppendBowlTurn(points, ref state, radius * 1.2f, PickSign(rng), drop, rng);
                    break;
                case SlidePathFeature.Zigzag:
                    AppendZigzag(points, ref state, radius * 0.9f, drop, rng);
                    break;
                case SlidePathFeature.Drop:
                    AppendDrop(points, ref state, length * 0.45f, drop * 1.35f, rng);
                    break;
            }
        }

        static void InsertBlendPoint(List<Vector3> points, ref PathState state)
        {
            var horiz = Vector3.ProjectOnPlane(state.Forward, Vector3.up);
            if (horiz.sqrMagnitude < 0.0001f)
                horiz = Vector3.forward;
            horiz.Normalize();

            var blend = state.Position + horiz * 2.5f + Vector3.down * 0.6f;
            points.Add(blend);
            state.Position = blend;
            state.Forward = GetDescentForward((blend - points[^2]).normalized);
        }

        static void AppendStraight(List<Vector3> points, ref PathState state, float length, float drop)
        {
            var start = state.Position;
            var horiz = Vector3.ProjectOnPlane(state.Forward, Vector3.up);
            if (horiz.sqrMagnitude < 0.0001f)
                horiz = Vector3.forward;
            horiz.Normalize();

            drop = Mathf.Max(drop, length * 0.1f);
            var end = state.Position + horiz * length + Vector3.down * drop;
            points.Add(end);
            state.Position = end;
            state.Forward = GetDescentForward((end - start).normalized);
        }

        static void AppendArc(
            List<Vector3> points,
            ref PathState state,
            float radius,
            float totalAngle,
            float drop,
            int steps)
        {
            totalAngle = Mathf.Clamp(totalAngle, -Mathf.PI * 0.8f, Mathf.PI * 0.8f);
            steps = Mathf.Max(steps, 6);
            var forward = state.Forward.normalized;
            var right = GetRight(forward);
            var center = state.Position + right * radius * Mathf.Sign(totalAngle);
            var startOffset = state.Position - center;
            float startY = state.Position.y;

            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                var rotated = Quaternion.AngleAxis(totalAngle * t * Mathf.Rad2Deg, Vector3.up) * startOffset;
                var pos = center + rotated;
                pos.y = startY - drop * t;
                points.Add(pos);
            }

            state.Position = points[^1];
            var radial = state.Position - center;
            var tangent = Vector3.Cross(Vector3.up, radial).normalized;
            state.Forward = GetDescentForward(tangent * Mathf.Sign(totalAngle));
        }

        static void AppendSCurve(List<Vector3> points, ref PathState state, float radius, float drop, System.Random rng)
        {
            float sign = PickSign(rng);
            AppendArc(points, ref state, radius, sign * Mathf.PI * 0.42f, drop * 0.45f, 5);
            AppendArc(points, ref state, radius * 1.1f, -sign * Mathf.PI * 0.42f, drop * 0.55f, 5);
        }

        static void AppendCorkscrew(
            List<Vector3> points,
            ref PathState state,
            float radius,
            float revolutions,
            float length,
            float drop,
            int steps)
        {
            var start = state.Position;
            var forward = GetDescentForward(state.Forward);
            var horiz = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            var right = GetRight(horiz);
            var tangent = Vector3.Cross(Vector3.up, right).normalized;

            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                float angle = t * revolutions * Mathf.PI * 2f;
                float tighten = 1f - t * 0.15f;

                var spiral = right * (Mathf.Cos(angle) * radius * tighten)
                             + tangent * (Mathf.Sin(angle) * radius * tighten);
                var pos = start + horiz * (length * t) + spiral + Vector3.down * (drop * t);
                points.Add(pos);
            }

            state.Position = points[^1];
            state.Forward = GetDescentForward((points[^1] - points[^2]).normalized);
        }

        static void AppendBowlTurn(
            List<Vector3> points,
            ref PathState state,
            float radius,
            float sign,
            float drop,
            System.Random rng)
        {
            float sweep = Mathf.Lerp(Mathf.PI * 0.35f, Mathf.PI * 0.75f, (float)rng.NextDouble());
            AppendArc(points, ref state, radius, sign * sweep, drop, 10);
        }

        static void AppendZigzag(List<Vector3> points, ref PathState state, float radius, float drop, System.Random rng)
        {
            int legs = 3 + rng.Next(2);
            float turnSign = PickSign(rng);
            float dropPerLeg = drop / legs;
            float legAngle = Mathf.PI * 0.34f;

            for (int leg = 0; leg < legs; leg++)
            {
                AppendArc(points, ref state, radius, turnSign * legAngle, dropPerLeg, 4);
                AppendStraight(points, ref state, radius * 0.45f, dropPerLeg * 0.12f);
                turnSign *= -1f;
            }
        }

        static void AppendDrop(
            List<Vector3> points,
            ref PathState state,
            float length,
            float drop,
            System.Random rng)
        {
            int steps = 4;
            var forward = GetDescentForward(state.Forward);
            var wobble = GetRight(forward) * ((float)rng.NextDouble() - 0.5f) * 2f;

            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                float ease = t * t;
                var horiz = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
                var pos = state.Position + horiz * (length * t) + wobble * t + Vector3.down * (drop * ease);
                points.Add(pos);
            }

            state.Position = points[^1];
            state.Forward = GetDescentForward((points[^1] - points[^2]).normalized);
        }

        static void EnforceMonotonicDescent(List<Vector3> points, int rampEndIndex)
        {
            const float minStep = 0.08f;
            int start = Mathf.Clamp(rampEndIndex + 1, 1, points.Count - 1);
            for (int i = start; i < points.Count; i++)
            {
                float maxY = points[i - 1].y - minStep;
                if (points[i].y > maxY)
                    points[i] = new Vector3(points[i].x, maxY, points[i].z);
            }
        }

        static Vector3 GetRight(Vector3 forward)
        {
            var right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.Cross(Vector3.forward, forward);
            return right.normalized;
        }

        static float PickSign(System.Random rng) => rng.NextDouble() > 0.5 ? 1f : -1f;

        static void Shuffle<T>(System.Random rng, IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
