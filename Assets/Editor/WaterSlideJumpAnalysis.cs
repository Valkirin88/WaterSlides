using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WaterSlides;

// Анализ прыжков между горками (06.11.2026, жалоба «горка кончается вниз, и даже
// прыжком не перепрыгнуть на следующую»). Для каждой пары соседних горок курса
// баллистика с конца горки по касательной на скоростях 6..34 м/с — без прыжка и с
// прыжком (как PlayerView.TryJumpFromSlide: + (нормаль + 0.65·вверх)·Slide Jump
// Force), гравитация × Gravity Multiplier игрока, без стрейфа. «Поймана» — траектория
// проходит сквозь пол следующей горки сверху вниз в пределах ширины жёлоба
// (с запасом на стрейф отдельно). Отчёт — AgentBridge/jump_report.txt.
public static class WaterSlideJumpAnalysis
{
    [MenuItem("Water Slides/Analyze Jumps")]
    public static void Analyze()
    {
        var course = Object.FindFirstObjectByType<WaterSlideCourse>();
        var player = Object.FindFirstObjectByType<PlayerView>();
        var sb = new StringBuilder();
        if (course == null) { Write("no course"); return; }

        float gMul = 2f, jumpForce = 9f, assistAccel = 25f, assistS = 4f;
        if (player != null)
        {
            var so = new SerializedObject(player);
            gMul = Mathf.Max(so.FindProperty("_gravityMultiplier")?.floatValue ?? 2f, 1f);
            jumpForce = so.FindProperty("_slideJumpForce")?.floatValue ?? 9f;
            assistAccel = so.FindProperty("_landingAssistAccel")?.floatValue ?? 25f;
            assistS = so.FindProperty("_landingAssistTargetS")?.floatValue ?? 4f;
            s_noJumpShortfall = so.FindProperty("_noJumpShortfall")?.floatValue ?? 3f;
        }
        float g = Physics.gravity.magnitude * gMul;
        s_assistS = assistS;
        sb.AppendLine($"g={g:0.0} jump={jumpForce:0.0} slides={course.Slides.Count}");

        var slides = course.Slides;
        for (int i = 0; i + 1 < slides.Count; i++)
        {
            var a = slides[i];
            var b = slides[i + 1];
            if (a == null || b == null) continue;
            var framesA = a.RideFrames;
            var framesB = b.RideFrames;
            if (framesA.Count < 2 || framesB.Count < 2) continue;

            Vector3 end = framesA[framesA.Count - 1].Position;
            Vector3 tan = framesA[framesA.Count - 1].Forward.normalized;
            Vector3 up = framesA[framesA.Count - 1].Up.normalized;
            float pitch = Mathf.Asin(Mathf.Clamp(tan.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 entry = framesB[0].Position;
            Vector3 d = entry - end;
            var horiz = new Vector3(d.x, 0f, d.z);
            sb.AppendLine($"#{i}->{i + 1} {a.name} exitPitch={pitch:0.0}° gap={horiz.magnitude:0.0}m drop={-d.y:0.0}m half={b.RideHalfWidth:0.00} glass={a.GlassTube}");

            var line1 = new StringBuilder("   no-jump: ");
            var line2 = new StringBuilder("   jump   : ");
            var line3 = new StringBuilder("   no-jump (rule): ");
            var line4 = new StringBuilder("   jump + assist : ");
            for (int s = 6; s <= 34; s += 2)
            {
                line1.Append(Simulate(end, tan * s, framesB, b.RideHalfWidth, g));
                Vector3 jumpDir = (up + Vector3.up * 0.65f).normalized;
                line2.Append(Simulate(end, tan * s + jumpDir * jumpForce, framesB, b.RideHalfWidth, g));
                line3.Append(Simulate(end, tan * s, framesB, b.RideHalfWidth, g, assistAccel, false));
                line4.Append(Simulate(end, tan * s + jumpDir * jumpForce, framesB, b.RideHalfWidth, g, assistAccel, true));
            }
            sb.AppendLine(line1.ToString());
            sb.AppendLine(line2.ToString());
            sb.AppendLine(line3.ToString());
            sb.AppendLine(line4.ToString());
        }
        sb.AppendLine("legend: per speed 6,8..34 m/s: C caught on floor, S caught only with strafe (lateral miss < 4 m), U under (passes below the slide / short), O over (never comes down onto it in reach)");
        Write(sb.ToString());
    }

    static float s_assistS = 4f;
    static float s_noJumpShortfall = 3f;

    // Та же помощь приземлению, что PlayerView.ApplyLandingAssist (без радиуса тела).
    static Vector3 AssistAccel(Vector3 p, Vector3 v, System.Collections.Generic.IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, float g, float limit, bool jumped)
    {
        float s = 0f; int k = 0;
        while (k < frames.Count - 1 && s < s_assistS) { s += Vector3.Distance(frames[k].Position, frames[k + 1].Position); k++; }
        if (!jumped) k = 0;
        Vector3 aim = frames[k].Position + frames[k].Up * 0.35f;
        float h = p.y - aim.y;
        float disc = v.y * v.y + 2f * g * h;
        if (disc <= 0f) return Vector3.zero;
        float tFall = (v.y + Mathf.Sqrt(disc)) / g;
        if (tFall < 0.12f) return Vector3.zero;
        Vector3 to = Vector3.ProjectOnPlane(aim - p, Vector3.up);
        float dist = to.magnitude;
        if (dist < 0.05f) return Vector3.zero;
        Vector3 dir = to / dist;
        float along = Vector3.Dot(Vector3.ProjectOnPlane(v, Vector3.up), dir);
        float desired = (jumped ? dist : Mathf.Max(dist - s_noJumpShortfall, 0f)) / tFall;
        float lim = jumped ? limit : limit * 3f;
        float settle = jumped ? Mathf.Max(tFall * 0.5f, 0.2f) : Mathf.Max(tFall * 0.25f, 0.1f);
        float acc = Mathf.Clamp((desired - along) / settle, -lim, lim);
        if (!jumped && acc > 0f) return Vector3.zero;
        return dir * acc;
    }

    static char Simulate(Vector3 p, Vector3 v, System.Collections.Generic.IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, float halfWidth, float g, float assist = 0f, bool jumped = true)
    {
        const float dt = 0.01f;
        float prevH = float.NaN;
        int prevJ = -1;
        bool wentBelow = false;
        float minLat = float.MaxValue;
        float lowestFrame = float.MaxValue;
        foreach (var f in frames) lowestFrame = Mathf.Min(lowestFrame, f.Position.y);
        for (int step = 0; step < 800; step++)
        {
            v += Vector3.down * g * dt;
            if (assist > 0f) v += AssistAccel(p, v, frames, g, assist, jumped) * dt;
            p += v * dt;
            if (p.y < lowestFrame - 30f) break;
            int j = Nearest(frames, p);
            var f = frames[j];
            Vector3 right = Vector3.Cross(f.Up, f.Forward).normalized;
            Vector3 rel = p - f.Position;
            float h = Vector3.Dot(rel, f.Up);
            float lat = Vector3.Dot(rel, right);
            float along = Vector3.Dot(rel, f.Forward);
            bool insideSpan = (j > 0 || along > -0.5f) && (j < frames.Count - 1 || along < 0.5f);
            if (!float.IsNaN(prevH) && prevH > 0f && h <= 0f && insideSpan && Mathf.Abs(j - prevJ) < 20)
            {
                if (Mathf.Abs(lat) <= halfWidth) return 'C';
                minLat = Mathf.Min(minLat, Mathf.Abs(lat) - halfWidth);
            }
            if (insideSpan && h < -2f && Mathf.Abs(lat) < halfWidth + 4f) wentBelow = true;
            prevH = h; prevJ = j;
        }
        if (minLat < 4f) return 'S';
        return wentBelow ? 'U' : 'O';
    }

    static int Nearest(System.Collections.Generic.IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, Vector3 p)
    {
        int best = 0; float bd = float.MaxValue;
        for (int k = 0; k < frames.Count; k++)
        {
            float dd = (frames[k].Position - p).sqrMagnitude;
            if (dd < bd) { bd = dd; best = k; }
        }
        return best;
    }

    static void Write(string text)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "AgentBridge");
        File.WriteAllText(Path.Combine(dir, "jump_report.txt"), text, new UTF8Encoding(false));
        Debug.Log("Jump analysis written: AgentBridge/jump_report.txt");
    }
}
