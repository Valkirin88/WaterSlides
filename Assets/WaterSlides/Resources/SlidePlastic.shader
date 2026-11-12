// Пластик непрозрачной горки (Built-in RP, модель освещения Standard).
// Цвет — текстура-палитра полос (_MainTex, свой тайлинг, UV.y — метры вдоль
// горки), поверх — износ: карта пятен (_WearTex, свой тайлинг: потёртости светлее
// и матовее, грязь темнее) и царапины. Царапины — не плитка (плитка на 3 м заметно
// повторялась, 10.11.2026), а штампы из атласа (_ScratchTex), разбросанные по
// клеткам: хэш клетки и зерна горки решает, есть ли штамп, какой и как отражён.
// Два слоя клеток со сдвигом на полклетки — решётка не читается.
// Собственное свечение = итоговый цвет × _EmissionStrength (горки не тускнеют
// рядом с неосвещённой панорамой, см. ПРОГРЕСС.md 06.11.2026).
// Лежит в Resources — попадает в сборку, хотя материалы создаются из кода.
Shader "WaterSlides/SlidePlastic"
{
    Properties
    {
        _Color ("Цвет", Color) = (1, 1, 1, 1)
        _MainTex ("Палитра полос", 2D) = "white" {}
        _WearTex ("Износ: R — множитель цвета (0.5 = без изменений), G — доля износа (матовость)", 2D) = "gray" {}
        _ScratchTex ("Атлас царапин 4x4: RG — наклон нормали, B — покрытие", 2D) = "black" {}
        _ScratchCells ("Царапины: x — столбцов поперёк, y — клеток на плитку износа вдоль, z — доля клеток со штампом, w — зерно горки", Vector) = (4, 1.5, 0.5, 0)
        _WearStrength ("Сила износа (0 — новый пластик)", Range(0, 1)) = 0
        _WearNormalScale ("Глубина царапин", Range(0, 2)) = 1
        _WearFade ("Выцветание старого пластика", Range(0, 1)) = 0
        _Glossiness ("Глянец нового пластика", Range(0, 1)) = 0.92
        _WornGlossiness ("Глянец в потёртостях", Range(0, 1)) = 0.2
        _Metallic ("Металличность", Range(0, 1)) = 0.02
        _EmissionStrength ("Собственное свечение (доля цвета)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _WearTex;
        sampler2D _ScratchTex;
        float4 _ScratchCells;
        fixed4 _Color;
        half _WearStrength;
        half _WearNormalScale;
        half _WearFade;
        half _Glossiness;
        half _WornGlossiness;
        half _Metallic;
        half _EmissionStrength;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_WearTex;
        };

        float3 Hash3(float2 p)
        {
            float3 q = float3(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)), dot(p, float2(419.2, 371.9)));
            return frac(sin(q) * 43758.5453);
        }

        // Один слой клеток. xy — наклон нормали, z — покрытие царапиной.
        // Градиенты берутся от непрерывных координат: у frac() на границе клетки
        // разрыв, и обычная выборка рисовала бы там полоску с мелкого мипа.
        half3 ScratchLayer(float2 cc, float2 gx, float2 gy, float2 shift)
        {
            float2 p = cc + shift;
            float2 cell = floor(p);
            float2 f = p - cell;
            float cols = max(_ScratchCells.x, 1.0);
            // Заворот столбца: у трубы шов по окружности не виден.
            float col = cell.x - cols * floor(cell.x / cols);
            float3 h = Hash3(float2(col, cell.y) + shift * 37.0 + _ScratchCells.w);
            // Ближе к середине профиля (дно, где едут) царапин больше.
            float u = (cell.x + 0.5 - shift.x) / cols - 0.5;
            float present = step(h.z, _ScratchCells.z * lerp(0.35, 1.0, exp(-u * u / 0.0512)));
            float idx = floor(h.x * 15.999);
            float2 stamp = float2(fmod(idx, 4.0), floor(idx * 0.25));
            // Случайное отражение штампа по обеим осям.
            float2 sgn = float2(h.y < 0.5 ? -1.0 : 1.0, frac(h.y * 2.0) < 0.5 ? -1.0 : 1.0);
            float2 fl = lerp(1.0 - f, f, sgn * 0.5 + 0.5);
            fixed4 t = tex2Dgrad(_ScratchTex, (stamp + fl) * 0.25, gx * 0.25, gy * 0.25);
            return half3((t.rg * 2.0 - 1.0) * sgn, t.b) * present;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed3 col = tex2D(_MainTex, IN.uv_MainTex).rgb * _Color.rgb;
            // Пятна растянуты вдоль спуска втрое с лишним: повтор реже и не кратен клеткам царапин.
            fixed4 wear = tex2D(_WearTex, IN.uv_WearTex * float2(1.0, 0.29));
            half k = _WearStrength;

            float2 cc = IN.uv_WearTex * _ScratchCells.xy;
            float2 gx = ddx(cc), gy = ddy(cc);
            half3 s1 = ScratchLayer(cc, gx, gy, float2(0.0, 0.0));
            half3 s2 = ScratchLayer(cc, gx, gy, float2(0.5, 0.5));
            half scratch = max(s1.z, s2.z);

            // Старый пластик выцветает: цвет уходит к светлому серому той же яркости.
            half luma = dot(col, half3(0.299h, 0.587h, 0.114h));
            col = lerp(col, luma * 0.9h + 0.12h, _WearFade * k);
            col *= lerp(1.0h, wear.r * 2.0h, k);
            // Поцарапанный пластик белеет.
            col *= 1.0h + 0.64h * scratch * k;
            o.Albedo = col;
            o.Metallic = _Metallic;
            o.Smoothness = lerp(_Glossiness, _WornGlossiness, saturate(max(wear.g, 0.95h * scratch) * k));
            o.Normal = normalize(half3((s1.xy + s2.xy) * _WearNormalScale * k, 1.0h));
            o.Emission = col * _EmissionStrength;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
