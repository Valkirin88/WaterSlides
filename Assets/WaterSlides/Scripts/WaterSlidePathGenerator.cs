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
        Drop
    }

    public enum SlideLoopKind
    {
        None,
        Horizontal,
        Vertical
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

        // Loops to insert along the slide, in ride order. Kinds may repeat, and several
        // loops may land on the same slot — then they run back-to-back.
        public List<SlideLoopKind> Loops;
        public float LoopRadius;

        // Ski-jump lip at the slide end: the last metres bend upwards to this angle
        // (degrees; 0 = flat exit) while keeping the horizontal heading.
        public float ExitKickAngle;
        public float ExitKickLength;

        // Speed-aware corner fairness (open flumes). The player's cornering physics, in
        // the same units PlayerView uses: a corner of radius R is only holdable below
        // √(grip·R), so the generator tracks an estimated ride speed along the path and
        // widens turns / inserts braking straights so that "steep drop → immediate sharp
        // turn" never produces a GUARANTEED flyout. Zeros fall back to sane defaults.
        public float RideSpeedCap;      // потолок скорости езды, м/с
        public float CorneringGrip;     // сцепление в поворотах, м/с²
        public float BrakeDeceleration; // торможение игрока (S), м/с²
        public float GravityAccel;      // эффективная гравитация игрока, м/с²
        public float EntrySpeed;        // расчётная скорость на входе в горку, м/с
        // true — «нечестный» поворот расширяется под скорость подхода (старое поведение,
        // крутых поворотов на быстрой трассе почти не остаётся); false — радиус остаётся
        // крутым, перед поворотом лишь вставляется прямая, на которой игрок МОЖЕТ
        // затормозить сам (не тормозит — вылетает).
        public bool WidenUnfairTurns;
    }

    public struct GeneratedSlidePath
    {
        public List<Vector3> Points;

        // Index ranges of the path occupied by loops (protected from smoothing/decimation).
        public List<Vector2Int> LoopRanges;

        public bool HasLoop => LoopRanges != null && LoopRanges.Count > 0;
    }

    public static class WaterSlidePathGenerator
    {
        struct PathState
        {
            public Vector3 Position;
            public Vector3 Forward;
            // Estimated ride speed at this point (m/s) — energy model without friction
            // (overestimates, which errs on the SAFE side: wider turns, longer braking).
            public float Speed;
        }

        public static GeneratedSlidePath GenerateInWorldSpace(
            System.Random rng,
            WaterSlidePathSettings settings,
            Vector3 worldStart,
            Vector3 worldStartForward)
        {
            // Physics defaults matching PlayerView's script defaults, used when the
            // caller does not supply real values (settings is a by-value copy).
            if (settings.RideSpeedCap <= 0.1f) settings.RideSpeedCap = 16f;
            if (settings.CorneringGrip <= 0.1f) settings.CorneringGrip = 20f;
            if (settings.BrakeDeceleration <= 0.1f) settings.BrakeDeceleration = 12f;
            if (settings.GravityAccel <= 0.1f) settings.GravityAccel = 17.6f;

            var points = new List<Vector3> { worldStart };
            var forward = worldStartForward.sqrMagnitude > 0.0001f ? worldStartForward.normalized : Vector3.forward;
            var state = new PathState
            {
                Position = worldStart,
                Forward = forward,
                Speed = Mathf.Clamp(settings.EntrySpeed, 0f, settings.RideSpeedCap)
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

            var loops = settings.Loops;
            int loopCount = loops != null ? loops.Count : 0;
            var loopRanges = new List<Vector2Int>();
            bool anyVerticalLoop = false;
            int nextLoop = 0;

            for (int i = 0; i < features.Count; i++)
            {
                float featureDrop = dropBudget / (features.Count - i);
                float featureLength = lengthBudget / (features.Count - i);
                if (i > 0)
                    InsertBlendPoint(points, ref state);
                AppendFeature(points, ref state, rng, features[i], settings, featureLength, featureDrop);

                // Insert every loop assigned to this feature slot. Several loops may share
                // a slot — they then run back-to-back, one straight into the next.
                while (nextLoop < loopCount && LoopSlot(nextLoop, loopCount, features.Count) == i)
                {
                    InsertBlendPoint(points, ref state);
                    int loopStart = points.Count;
                    float loopStartY = state.Position.y;
                    if (loops[nextLoop] == SlideLoopKind.Vertical)
                    {
                        anyVerticalLoop = true;
                        AppendVerticalLoop(points, ref state, settings.LoopRadius, PickSign(rng), out var lateralDir);
                        AppendVerticalLoopExitRamp(points, ref state, settings.LoopRadius, lateralDir);
                    }
                    else
                    {
                        AppendHorizontalLoop(points, ref state, settings.LoopRadius, settings.LoopRadius * 0.5f, PickSign(rng));
                    }

                    loopRanges.Add(new Vector2Int(loopStart, points.Count - 1));
                    state.Speed = SpeedAfterDrop(state.Speed, loopStartY - state.Position.y, settings);
                    nextLoop++;
                }
            }

            AppendExitTangentSegment(points);

            SmoothCorners(points, iterations: anyVerticalLoop ? 1 : 3, factor: 0.3f, loopRanges);

            if (anyVerticalLoop)
            {
                // Only push the run AFTER the last loop downward, so the slide finishes below
                // its start and clear of the loops, without flattening the loops themselves.
                DescendTailAfter(points, loopRanges[^1].y, settings.TotalDrop * 0.5f);
            }
            else
            {
                // Horizontal loops already descend, so normal "always down" rules are safe.
                EnforceMonotonicDescent(points, rampEndIndex);
                EnsureMinimumTotalDrop(points, settings.TotalDrop, rampEndIndex);
                EnforceMonotonicDescent(points, rampEndIndex);
                EnsureStartAboveEnd(points, settings.TotalDrop, rampEndIndex);
            }

            // AFTER the descent enforcement: the "always down" clamps must never see an
            // upward lip (such a slide must also carry PreserveVerticalMotion so the mesh
            // builder's own clamps keep their hands off it too). Negative angles bend the
            // lip DOWN — e.g. onto the jump chord towards the next slide's entrance.
            if (Mathf.Abs(settings.ExitKickAngle) > 0.5f)
                AppendExitKick(points, settings.ExitKickAngle, Mathf.Max(settings.ExitKickLength, 1f));

            RemoveNearDuplicates(points, 0.15f, loopRanges);
            return new GeneratedSlidePath
            {
                Points = points,
                LoopRanges = loopRanges
            };
        }

        // Ski-jump lip: bends the last metres upwards while keeping the horizontal
        // heading, so the rider is thrown UP but still TOWARDS the next slide.
        static void AppendExitKick(List<Vector3> points, float angleDegrees, float length)
        {
            if (points.Count < 2)
                return;

            var dir = points[^1] - points[^2];
            var horiz = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (horiz.sqrMagnitude < 1e-4f)
                return;
            horiz.Normalize();

            float startPitch = Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude);
            float endPitch = angleDegrees * Mathf.Deg2Rad;

            const int steps = 7;
            float stepLength = length / steps;
            Vector3 pos = points[^1];
            for (int i = 1; i <= steps; i++)
            {
                // Ease-in: the ramp starts gently and tightens towards the lip.
                float t = i / (float)steps;
                float pitch = Mathf.Lerp(startPitch, endPitch, t * t);
                pos += (horiz * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)) * stepLength;
                points.Add(pos);
            }
        }

        // Feature index after which the given loop is inserted: loops are spread evenly
        // along the slide; rounding collisions put loops on the same slot (consecutively).
        static int LoopSlot(int loopIndex, int loopCount, int featureCount) =>
            Mathf.Clamp(Mathf.RoundToInt((loopIndex + 1) * featureCount / (loopCount + 1f)), 1, featureCount - 1);

        static bool InAnyRange(List<Vector2Int> ranges, int index)
        {
            for (int i = 0; i < ranges.Count; i++)
            {
                if (index >= ranges[i].x && index <= ranges[i].y)
                    return true;
            }

            return false;
        }

        // Lowers points after a given index along a ramp so the path ends clearly below its start.
        static void DescendTailAfter(List<Vector3> points, int afterIndex, float minDrop)
        {
            if (afterIndex < 0 || afterIndex >= points.Count - 1)
                return;

            float targetEndY = points[0].y - Mathf.Max(minDrop, 4f);
            float deficit = points[^1].y - targetEndY;
            if (deficit <= 0f)
                return;

            int from = afterIndex + 1;
            int span = points.Count - 1 - from;
            if (span <= 0)
                return;

            for (int k = from; k < points.Count; k++)
            {
                float t = (k - from) / (float)span;
                points[k] = new Vector3(points[k].x, points[k].y - deficit * t, points[k].z);
            }
        }

        // Drops consecutive points that are almost coincident; near-duplicate knots make the
        // centripetal Catmull-Rom spline overshoot and the tube fold/break.
        static void RemoveNearDuplicates(List<Vector3> points, float minSpacing, List<Vector2Int> protectedRanges)
        {
            if (points.Count < 3)
                return;

            float sqr = minSpacing * minSpacing;
            for (int i = points.Count - 2; i >= 1; i--)
            {
                if (InAnyRange(protectedRanges, i))
                    continue;

                if ((points[i] - points[i - 1]).sqrMagnitude < sqr)
                {
                    points.RemoveAt(i);
                    for (int r = 0; r < protectedRanges.Count; r++)
                    {
                        var range = protectedRanges[r];
                        if (i < range.x)
                            protectedRanges[r] = new Vector2Int(range.x - 1, range.y - 1);
                        else if (i <= range.y)
                            protectedRanges[r] = new Vector2Int(range.x, range.y - 1);
                    }
                }
            }
        }

        // Vertical loop-the-loop: up -> over the top -> down. A flat (single-plane) loop crosses
        // itself at mid-height (rising and falling strands meet), so we corkscrew it sideways: the
        // path drifts laterally across the loop, bringing the down-strand back BESIDE the up-strand
        // (separated by more than the tube width), plus a little forward so the exit keeps going.
        static void AppendVerticalLoop(List<Vector3> points, ref PathState state, float radius, float sign, out Vector3 lateralDir)
        {
            radius = Mathf.Max(radius, 1.5f);
            var h = Vector3.ProjectOnPlane(state.Forward, Vector3.up);
            if (h.sqrMagnitude < 1e-4f)
                h = Vector3.forward;
            h.Normalize();

            lateralDir = Vector3.Cross(Vector3.up, h).normalized * sign;
            var start = state.Position;
            float lateral = radius * 1.55f;
            float forwardDrift = radius * 0.9f;

            const int steps = 44;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                float ang = t * Mathf.PI * 2f;
                var pos = start
                          + h * (radius * Mathf.Sin(ang) + forwardDrift * t)
                          + Vector3.up * (radius * (1f - Mathf.Cos(ang)))
                          + lateralDir * (lateral * t);
                points.Add(pos);
            }

            state.Position = points[^1];
            state.Forward = GetDescentForward((points[^1] - points[^2]).normalized);
        }

        // Curves away from the loop volume so the next segment does not cut through the ring.
        static void AppendVerticalLoopExitRamp(List<Vector3> points, ref PathState state, float radius, Vector3 lateralDir)
        {
            var horiz = Vector3.ProjectOnPlane(state.Forward, Vector3.up);
            if (horiz.sqrMagnitude < 1e-4f)
                horiz = Vector3.forward;
            horiz.Normalize();

            var start = state.Position;
            for (int i = 1; i <= 5; i++)
            {
                float t = i / 5f;
                var pos = start
                          + horiz * (radius * 0.55f * t)
                          + Vector3.down * (radius * 0.65f * t)
                          + lateralDir * (radius * 0.2f * (1f - t));
                points.Add(pos);
            }

            state.Position = points[^1];
            state.Forward = GetDescentForward((points[^1] - points[^2]).normalized);
        }

        // Horizontal loop: a single vertical-axis descending coil (like a spiral slide). The drop
        // over the turn is larger than the loop diameter, so the entry and exit strands stay well
        // apart vertically and the coil can never look joined or pass through itself.
        static void AppendHorizontalLoop(List<Vector3> points, ref PathState state, float radius, float dropHint, float sign)
        {
            radius = Mathf.Max(radius, 1.5f);
            var h = Vector3.ProjectOnPlane(state.Forward, Vector3.up);
            if (h.sqrMagnitude < 1e-4f)
                h = Vector3.forward;
            h.Normalize();

            var right = Vector3.Cross(Vector3.up, h).normalized * sign;
            var center = state.Position + right * radius;
            var startOffset = state.Position - center;
            float startY = state.Position.y;

            // Pitch (drop over the full turn) must clear the diameter so the coil is an open helix,
            // not a flat stack of overlapping rings.
            float totalDrop = Mathf.Max(radius * 2.2f, dropHint, 5f);

            const int steps = 32;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                var rot = Quaternion.AngleAxis(sign * 360f * t, Vector3.up) * startOffset;
                var pos = center + rot;
                pos.y = startY - totalDrop * t;
                points.Add(pos);
            }

            state.Position = points[^1];
            state.Forward = GetDescentForward((points[^1] - points[^2]).normalized);
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

        // Rounds every interior corner with a few Laplacian passes; endpoints and loop points stay fixed.
        static void SmoothCorners(List<Vector3> points, int iterations, float factor, List<Vector2Int> protectedRanges)
        {
            if (points.Count < 3)
                return;

            for (int it = 0; it < iterations; it++)
            {
                var copy = new List<Vector3>(points);
                for (int i = 1; i < points.Count - 1; i++)
                {
                    if (InAnyRange(protectedRanges, i))
                        continue;
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
                    state.Speed = SpeedAfterDrop(state.Speed, Mathf.Max(drop * 0.5f, length * 0.06f), settings);
                    break;
                case SlidePathFeature.WideCurve:
                {
                    float r = EnsureCornerFair(points, ref state, settings, radius * 1.4f, drop);
                    AppendArc(points, ref state, r, PickSign(rng) * Mathf.PI * 0.45f, drop, 6);
                    FinishCorner(ref state, settings, r, drop);
                    break;
                }
                case SlidePathFeature.SharpTurn:
                {
                    float r = EnsureCornerFair(points, ref state, settings, radius * 0.75f, drop);
                    AppendArc(points, ref state, r, PickSign(rng) * Mathf.PI * 0.55f, drop, 5);
                    FinishCorner(ref state, settings, r, drop);
                    break;
                }
                case SlidePathFeature.SCurve:
                {
                    // Both arcs of the S share the base radius (the second is ×1.1), so
                    // fairness on the base covers the tighter of the two.
                    float r = EnsureCornerFair(points, ref state, settings, radius, drop);
                    AppendSCurve(points, ref state, r, drop, rng);
                    FinishCorner(ref state, settings, r, drop);
                    break;
                }
                case SlidePathFeature.Corkscrew:
                    AppendCorkscrew(points, ref state, radius * 0.85f, 0.45f + (float)rng.NextDouble() * 0.35f, length, drop, 16);
                    state.Speed = SpeedAfterDrop(state.Speed, drop, settings);
                    break;
                case SlidePathFeature.Helix:
                    AppendCorkscrew(points, ref state, radius * 1.05f, 0.65f + (float)rng.NextDouble() * 0.45f, length * 1.05f, drop, 22);
                    state.Speed = SpeedAfterDrop(state.Speed, drop, settings);
                    break;
                case SlidePathFeature.BowlTurn:
                {
                    float r = EnsureCornerFair(points, ref state, settings, radius * 1.2f, drop);
                    AppendBowlTurn(points, ref state, r, PickSign(rng), drop, rng);
                    FinishCorner(ref state, settings, r, drop);
                    break;
                }
                case SlidePathFeature.Drop:
                    AppendDrop(points, ref state, length * 0.45f, drop * 1.35f, rng);
                    state.Speed = SpeedAfterDrop(state.Speed, drop * 1.35f, settings);
                    break;
            }
        }

        // Energy-model speed update over a descent (no friction — overestimates, i.e.
        // errs towards wider turns and longer braking runs), capped by the ride ceiling.
        static float SpeedAfterDrop(float speed, float drop, in WaterSlidePathSettings settings)
        {
            float v2 = speed * speed + 2f * settings.GravityAccel * Mathf.Max(drop, 0f);
            return Mathf.Min(settings.RideSpeedCap, Mathf.Sqrt(Mathf.Max(v2, 0f)));
        }

        // Corner fairness for OPEN flumes (a closed tube holds the rider geometrically —
        // untouched). The player holds a corner of radius R only below √(grip·R); after
        // a steep drop the arrival speed exceeds that for any sharp radius, and since
        // nothing between the drop and the turn allows braking, the flyout was
        // GUARANTEED, not a skill fail. Returns the radius to actually build:
        //  1) widen the turn up to the widest ordinary curve so the arrival speed fits;
        //  2) if even that is not enough — insert a braking straight sized so the player
        //     CAN slow down to the corner speed (12 m/s² braking), then keep the wide
        //     radius. Flyouts remain possible for a player who does not brake — the
        //     mechanic stays, only the impossible situations go away.
        static float EnsureCornerFair(
            List<Vector3> points,
            ref PathState state,
            in WaterSlidePathSettings settings,
            float radius,
            float drop)
        {
            if (settings.Enclosure != SlideEnclosure.Open)
                return radius;

            float grip = settings.CorneringGrip;
            // The turn itself keeps descending and re-accelerating the rider, so judge
            // fairness against the projected MID-turn speed, not just the entry speed.
            float vMid = SpeedAfterDrop(state.Speed, Mathf.Max(drop, 0f) * 0.5f, settings);
            float vAllowed = Mathf.Sqrt(grip * radius) * 0.95f;
            if (vMid <= vAllowed)
                return radius;

            if (settings.WidenUnfairTurns)
            {
                float radiusCap = Mathf.Max(settings.TurnRadiusMax * 1.4f, radius);
                float radiusFit = vMid * vMid / (grip * 0.85f);
                if (radiusFit <= radiusCap)
                    return Mathf.Max(radius, radiusFit);

                radius = radiusCap;
                vAllowed = Mathf.Sqrt(grip * radius) * 0.95f;
            }

            // Радиус остаётся; прямая перед поворотом даёт игроку место затормозить.
            float brakeLength = (vMid * vMid - vAllowed * vAllowed) / (2f * settings.BrakeDeceleration);
            brakeLength = Mathf.Clamp(brakeLength * 1.2f + 2f, 3f, 30f);
            // Gentle slope on the braking run: it must keep descending (the monotonic
            // clamp would bend it anyway) without feeding much speed back.
            AppendStraight(points, ref state, brakeLength, brakeLength * 0.08f);
            state.Speed = Mathf.Min(state.Speed, vAllowed);
            return radius;
        }

        // After the turn: cornering forces shed everything above the holdable speed
        // (the attentive player braked to make it), then the turn's own descent
        // re-accelerates as usual.
        static void FinishCorner(ref PathState state, in WaterSlidePathSettings settings, float radius, float drop)
        {
            if (settings.Enclosure == SlideEnclosure.Open)
                state.Speed = Mathf.Min(state.Speed, Mathf.Sqrt(settings.CorneringGrip * radius) * 0.95f);
            state.Speed = SpeedAfterDrop(state.Speed, Mathf.Max(drop, 0f), settings);
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
