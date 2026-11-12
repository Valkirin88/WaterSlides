using UnityEngine;

namespace WaterSlides
{
    // Процедурные текстуры износа пластиковой горки для шейдера WaterSlides/SlidePlastic.
    // Создаются один раз на запуск и общие для всех изношенных горок.
    //
    // WearTexture — крупные пятна (без царапин), бесшовная плитка:
    //  * R — множитель цвета (0.5 = без изменений): выгоревшие пятна и потёртости
    //    чуть светлее, грязь темнее;
    //  * G — доля износа: в потёртостях пластик матовый.
    //
    // ScratchAtlas — атлас штампов царапин AtlasCells×AtlasCells. Царапины НЕ лежат
    // в плитке: одна плитка на 3 м повторялась вдоль горки и узор был заметен
    // (10.11.2026). Шейдер делит горку на клетки (ScratchCellLength м вдоль) и по
    // хэшу клетки и зерна горки решает, есть ли в ней штамп, какой и как отражён —
    // расположение случайное и не повторяется ни вдоль горки, ни между горками.
    //  * R, G — наклон нормали (0.5 = ровно), B — покрытие царапиной.
    // Штамп целиком внутри своей клетки атласа с пустыми полями — царапины не
    // режутся границами клеток и не затекают в соседние штампы на мипах.
    // Царапины в основном вдоль спуска (так их оставляют катающиеся).
    public static class WaterSlidePlasticWear
    {
        const int Size = 512;
        const int Seed = 20261106;

        // Атлас штампов: клетка StampW×StampH текселей (вытянута вдоль спуска).
        public const int AtlasCells = 4;
        const int StampW = 128, StampH = 256;
        const int StampMargin = 6;
        // Длина клетки шейдера вдоль горки, м (штамп растягивается на клетку):
        // ~8 мм на тексель, царапина до ~1.8 м.
        public const float ScratchCellLength = 2f;
        // Желаемая ширина клетки поперёк, м (число столбцов подбирает генератор).
        public const float ScratchCellWidth = 1.3f;

        static Texture2D _wear;
        static Texture2D _scratches;

        public static Texture2D WearTexture { get { EnsureBuilt(); return _wear; } }
        public static Texture2D ScratchAtlas { get { EnsureBuilt(); return _scratches; } }

        static void EnsureBuilt()
        {
            if (_wear != null && _scratches != null)
                return;

            BuildBlotches();
            BuildScratchAtlas();
        }

        // ---- Крупные пятна: выгорание, матовые затёртые зоны, грязь ----
        static void BuildBlotches()
        {
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = x / (float)Size, v = y / (float)Size;
                float fade = TileNoise(u, v, 4, 11) * 0.6f + TileNoise(u, v, 9, 12) * 0.4f;
                float grime = TileNoise(u, v, 7, 21) * 0.7f + TileNoise(u, v, 23, 22) * 0.3f;
                float scuff = TileNoise(u, v, 5, 31) * 0.5f + TileNoise(u, v, 16, 32) * 0.5f;

                float scuffed = Smooth(0.45f, 0.75f, scuff) * (0.4f + 0.6f * FloorBias(u));
                float albedo = 0.5f + (fade - 0.5f) * 0.14f
                    + scuffed * 0.05f
                    - Smooth(0.6f, 0.85f, grime) * 0.06f;
                px[y * Size + x] = new Color32(ToByte(albedo), ToByte(scuffed * 0.5f), 0, 255);
            }

            _wear = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
            {
                name = "SlidePlasticWear",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            _wear.SetPixels32(px);
            _wear.Apply(true, true);
        }

        // ---- Атлас штампов царапин ----
        static void BuildScratchAtlas()
        {
            int w = AtlasCells * StampW, h = AtlasCells * StampH;
            var rng = new System.Random(Seed);
            var cover = new float[w * h];

            for (int cy = 0; cy < AtlasCells; cy++)
            for (int cx = 0; cx < AtlasCells; cx++)
            {
                int ox = cx * StampW, oy = cy * StampH;
                // Одна-две длинные заметные царапины и немного коротких.
                int longCount = rng.NextDouble() < 0.35 ? 2 : 1;
                for (int s = 0; s < longCount; s++)
                    DrawScratch(rng, cover, w, ox, oy, longScratch: true);
                int shortCount = rng.Next(1, 4);
                for (int s = 0; s < shortCount; s++)
                    DrawScratch(rng, cover, w, ox, oy, longScratch: false);
            }

            // Нормаль из высоты (бороздка = -покрытие). С 3 бороздки давали тёмные
            // линии и читались как трещины. Соседи берутся с заворотом по атласу:
            // у границ клеток покрытие нулевое (поля), так что шва нет.
            const float bump = 1.2f;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float hl = -cover[y * w + Mod(x - 1, w)], hr = -cover[y * w + Mod(x + 1, w)];
                float hd = -cover[Mod(y - 1, h) * w + x], hu = -cover[Mod(y + 1, h) * w + x];
                var n = new Vector3((hl - hr) * bump, (hd - hu) * bump, 1f).normalized;
                px[y * w + x] = new Color32(ToByte(n.x * 0.5f + 0.5f), ToByte(n.y * 0.5f + 0.5f), ToByte(cover[y * w + x]), 255);
            }

            _scratches = new Texture2D(w, h, TextureFormat.RGBA32, true, true)
            {
                name = "SlidePlasticScratches",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            _scratches.SetPixels32(px);
            _scratches.Apply(true, true);
        }

        static void DrawScratch(System.Random rng, float[] cover, int atlasW, int ox, int oy, bool longScratch)
        {
            bool along = rng.NextDouble() < (longScratch ? 0.97 : 0.85);
            float angle = along
                ? Mathf.PI * 0.5f + Gauss(rng) * 7f * Mathf.Deg2Rad
                : (float)rng.NextDouble() * Mathf.PI;
            float length = longScratch ? Lerp(50f, 225f, (float)rng.NextDouble()) : Lerp(4f, 22f, (float)rng.NextDouble());
            float width = longScratch ? Lerp(1.1f, 2.4f, (float)rng.NextDouble()) : Lerp(0.7f, 1.2f, (float)rng.NextDouble());
            float strength = longScratch ? Lerp(0.6f, 1f, (float)rng.NextDouble()) : Lerp(0.3f, 0.6f, (float)rng.NextDouble());
            // Лёгкий изгиб: царапина — не идеальная прямая. Изгиб на всю длину не
            // больше нескольких градусов (с 0.0025 рад/шаг царапины закручивались
            // кольцами и читались как спутанные волосы).
            float bend = Gauss(rng) * (longScratch ? 0.00012f : 0f);

            // Середина — случайно в клетке, но так, чтобы вся царапина осталась внутри полей.
            float halfX = Mathf.Abs(Mathf.Cos(angle)) * length * 0.5f + width + 2f;
            float halfY = Mathf.Abs(Mathf.Sin(angle)) * length * 0.5f + width + 2f;
            float minX = StampMargin + halfX, maxX = StampW - StampMargin - halfX;
            float minY = StampMargin + halfY, maxY = StampH - StampMargin - halfY;
            float mx = minX < maxX ? Lerp(minX, maxX, (float)rng.NextDouble()) : StampW * 0.5f;
            float my = minY < maxY ? Lerp(minY, maxY, (float)rng.NextDouble()) : StampH * 0.5f;

            float x = mx - Mathf.Cos(angle) * length * 0.5f;
            float y = my - Mathf.Sin(angle) * length * 0.5f;
            int steps = Mathf.CeilToInt(length * 2f);
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                // Края царапины тоньше и слабее.
                float taper = Mathf.Sin(t * Mathf.PI);
                Splat(cover, atlasW, ox, oy, x, y, width * (0.4f + 0.6f * taper), strength * (0.3f + 0.7f * taper));
                float a = angle + bend * s;
                x += Mathf.Cos(a) * 0.5f; y += Mathf.Sin(a) * 0.5f;
            }
        }

        static void Splat(float[] cover, int atlasW, int ox, int oy, float cx, float cy, float radius, float strength)
        {
            int r = Mathf.CeilToInt(radius + 1f);
            int ix = Mathf.FloorToInt(cx), iy = Mathf.FloorToInt(cy);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int tx = ix + dx, ty = iy + dy;
                // За поля клетки не рисуем: штамп не должен касаться соседних.
                if (tx < StampMargin || tx >= StampW - StampMargin || ty < StampMargin || ty >= StampH - StampMargin)
                    continue;
                float px = tx + 0.5f - cx, py = ty + 0.5f - cy;
                float c = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(px * px + py * py)) * strength;
                if (c <= 0f)
                    continue;
                int i = (oy + ty) * atlasW + ox + tx;
                // Max, а не сумма: перекрытие шагов одной царапины не копится.
                cover[i] = Mathf.Max(cover[i], c);
            }
        }

        // 1 в середине плитки по X (дно жёлоба), ~0 у краёв (стенки).
        static float FloorBias(float u) => Mathf.Exp(-((u - 0.5f) * (u - 0.5f)) / (2f * 0.16f * 0.16f));

        // Бесшовный value-шум: решётка period×period с заворотом, сглаженная интерполяция.
        static float TileNoise(float u, float v, int period, int salt)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(Mod(x0, period), Mod(y0, period), salt);
            float b = Hash(Mod(x0 + 1, period), Mod(y0, period), salt);
            float c = Hash(Mod(x0, period), Mod(y0 + 1, period), salt);
            float d = Hash(Mod(x0 + 1, period), Mod(y0 + 1, period), salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2147483647 + Seed);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        static float Gauss(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static int Mod(int a, int m) => ((a % m) + m) % m;
        static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
