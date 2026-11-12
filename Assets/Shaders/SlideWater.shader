// Вода на горке (Built-in RP): тонкий прозрачный быстрый поток по жёлобу. UV ленты —
// метры: x поперёк (0..ширина), y вдоль пути. Три слоя бесшовного шума WaterNoise:
//  • «волны» — крупный гладкий слой: им гнётся картинка дна (преломление через
//    GrabPass: дно с полосами уже нарисовано, лента сдвигает его по наклону волны),
//    качается блик и колышутся вершины ленты вдоль нормали (небольшие волны по длине);
//  • «струи» — два прокручивающихся по течению вытянутых слоя: гребни дают белую
//    пену, их мелкая рябь — только искры блика.
// Все размеры в свойствах — размер САМОЙ КРУПНОЙ детали в метрах: в текстуре базовый
// период шума всего 6 (R) и 5 (G) ячеек на текстуру (WaterSlideEnvironmentSetup.Fbm),
// поэтому UV делится на размер × период. Без этого (как было) струи выходили
// шириной 6 см, а рефракция по таким производным разбрасывала пиксели дна в крошку.
// Освещение нормировано по яркости солнца: в сцене оно ~4.5, прямое умножение
// выбеливало ленту в белый. Обычный vertex/fragment-шейдер (surface-вариант на этом
// меше молча не рисовался).
Shader "WaterSlides/SlideWater"
{
    Properties
    {
        _Color ("Старый единый тон воды — не используется с 06.11.2026 (см. Center/Edge Color)", Color) = (0.4, 0.86, 0.92, 0.24)
        _CenterColor ("Цвет воды в центре ленты (альфа — плотность)", Color) = (0.0, 0.55, 0.68, 0.62)
        _EdgeColor ("Цвет воды у краёв ленты (альфа — плотность)", Color) = (0.42, 0.9, 0.9, 0.34)
        _CenterToEdge ("Переход центр → край (степень)", Range(0.5, 4)) = 1.6
        _FoamColor ("Цвет пены", Color) = (1, 1, 1, 1)
        _NoiseTex ("Шум (R, G — два слоя, бесшовный)", 2D) = "gray" {}
        _FlowSpeed ("Скорость течения, м/с", Float) = 13
        _StreakAcross ("Струи: ширина поперёк, м", Float) = 0.3
        _StreakAlong ("Струи: длина вдоль, м", Float) = 6.0
        _FoamAmount ("Количество пены", Range(0, 3)) = 1.0
        _FoamSharpness ("Резкость пены", Range(1, 8)) = 2
        _Glossiness ("Блики (сила)", Range(0, 3)) = 1.0
        _BumpStrength ("Сила ряби", Range(0, 3)) = 0.8
        _WaveAcross ("Волны: ширина поперёк, м", Float) = 1.5
        _WaveLength ("Волны: длина вдоль потока, м", Float) = 3.0
        _WaveHeight ("Волны: высота (вершины), м", Range(0, 0.2)) = 0.03
        _RefractStrength ("Преломление дна (сдвиг экрана)", Range(0, 0.2)) = 0.035
        _RibbonWidth ("Ширина ленты, м", Float) = 2
        _EdgeFade ("Растворение краёв, м", Float) = 0.25
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 200

        // Снимок экрана под водой: именованный — берётся один раз за кадр на все горки.
        GrabPass { "_SlideWaterGrab" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            // Базовые периоды шума в WaterNoise.png (см. генератор RebuildWaterNoise).
            #define NOISE_PERIOD_R 6.0
            #define NOISE_PERIOD_G 5.0
            // Шаг конечных разностей: струи — два текселя текстуры 512; волны —
            // ЦЕНТРАЛЬНЫЕ разности с широким шагом 8 текселей: широкий шаг усредняет
            // мелкие октавы шума (гладкая волна без «пилы» на границах полос), а
            // центральная схема по билинейной текстуре даёт непрерывный градиент.
            // Мип-смещение для сглаживания НЕ годится: на крупном мипе шаг меньше
            // текселя, градиент билинейной выборки кусочно-постоянный — дно в квадратиках.
            #define NOISE_EPS (2.0 / 512.0)
            #define WAVE_EPS (8.0 / 512.0)

            sampler2D _NoiseTex;
            sampler2D _SlideWaterGrab;
            fixed4 _Color;
            fixed4 _CenterColor;
            fixed4 _EdgeColor;
            half _CenterToEdge;
            fixed4 _FoamColor;
            float _FlowSpeed, _StreakAcross, _StreakAlong, _FoamAmount, _FoamSharpness;
            float _Glossiness, _BumpStrength, _WaveAcross, _WaveLength, _WaveHeight, _RefractStrength;
            float _RibbonWidth, _EdgeFade;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wpos : TEXCOORD1;
                float3 wn : TEXCOORD2;
                float4 grabPos : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            // UV слоя волн (G): крупные детали _WaveAcross × _WaveLength, плывут по течению.
            float2 WaveUV(float2 m)
            {
                return float2(
                    m.x / max(_WaveAcross * NOISE_PERIOD_G, 0.01),
                    (m.y - _Time.y * _FlowSpeed) / max(_WaveLength * NOISE_PERIOD_G, 0.01));
            }

            // UV слоя струй (R / G): детали _StreakAcross × _StreakAlong (× масштаб слоя).
            float2 StreakUV(float2 m, float speedMul, float2 scaleMul, float period)
            {
                return float2(
                    m.x / max(_StreakAcross * scaleMul.x * period, 0.01),
                    (m.y - _Time.y * _FlowSpeed * speedMul) / max(_StreakAlong * scaleMul.y * period, 0.01));
            }

            // Высота волны в вершине: лента имеет всего две вершины поперёк, поэтому
            // волна берётся по длине в середине ленты — оба края качаются вместе.
            float WaveHeightAt(float2 m)
            {
                float2 wuv = WaveUV(float2(_RibbonWidth * 0.5, m.y));
                return (tex2Dlod(_NoiseTex, float4(wuv, 0, 0)).g - 0.5) * 2.0 * _WaveHeight;
            }

            v2f vert(appdata_full v)
            {
                v2f o;
                v.vertex.xyz += v.normal * WaveHeightAt(v.texcoord.xy);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ---- Волны (крупный гладкий слой): наклон поверхности ----
                float2 wuv = WaveUV(i.uv);
                float w0 = tex2D(_NoiseTex, wuv).g;
                float wx = (tex2D(_NoiseTex, wuv + float2(WAVE_EPS, 0)).g - tex2D(_NoiseTex, wuv - float2(WAVE_EPS, 0)).g) * 0.5;
                float wy = (tex2D(_NoiseTex, wuv + float2(0, WAVE_EPS)).g - tex2D(_NoiseTex, wuv - float2(0, WAVE_EPS)).g) * 0.5;

                // ---- Струи: два слоя, пена на гребнях ----
                // Шум в текстуре узкий (медиана 0.5, 99-й перцентиль ~0.66), поэтому
                // перед порогом растягиваем контраст: пена — верхние ~10 % гребней.
                float2 uv1 = StreakUV(i.uv, 1.0, float2(1.0, 1.0), NOISE_PERIOD_R);
                float2 uv2 = StreakUV(i.uv, 0.8, float2(1.6, 0.6), NOISE_PERIOD_G) + 0.37;
                float n1 = tex2D(_NoiseTex, uv1).r;
                float n2 = tex2D(_NoiseTex, uv2).g;
                float n = saturate((n1 * 0.6 + n2 * 0.4 - 0.5) * 2.0 + 0.5);
                float foam = pow(smoothstep(0.62, 0.82, n), _FoamSharpness * 0.5) * _FoamAmount;

                float hx = tex2D(_NoiseTex, uv1 + float2(NOISE_EPS, 0)).r - n1;
                float hy = tex2D(_NoiseTex, uv1 + float2(0, NOISE_EPS)).r - n1;

                // Базис ленты: вдоль — проекция мирового «вперёд» на плоскость,
                // поперёк — его перпендикуляр.
                float3 N0 = normalize(i.wn);
                if (dot(N0, float3(0, 1, 0)) < 0) N0 = -N0;
                float3 T = normalize(cross(N0, float3(0, 0, 1)) + 1e-4);
                float3 B = cross(N0, T);
                float3 Nw = normalize(N0 - (T * wx + B * wy) * _BumpStrength * 8.0);
                float3 N = normalize(Nw - (T * hx + B * hy) * _BumpStrength * 3.0);

                // Солнце нормируем по яркости: только оттенок, интенсивность не выше 1.
                float3 lc = _LightColor0.rgb / max(1.0, max(_LightColor0.r, max(_LightColor0.g, _LightColor0.b)));
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 H = normalize(L + V);
                // Искры — по мелкой ряби струй, мягкий отлив — по крупным волнам.
                // Отлив полубелый: чистый цвет солнца красил волны в рыжий.
                float spec = pow(saturate(dot(N, H)), 140) * _Glossiness
                           + pow(saturate(dot(Nw, H)), 24) * 0.15 * _Glossiness;
                float fres = pow(1 - saturate(dot(Nw, V)), 3);
                // Гребни волн чуть светлее, ложбины темнее — волны видны и без блика.
                float diffuse = (0.6 + 0.4 * saturate(dot(Nw, L))) * (1.0 + (w0 - 0.5) * 3.0);

                // ---- Преломление: дно сдвигается по наклону волны в осях экрана ----
                // Сдвиг в долях экрана постоянен по дальности — вблизи заметен, вдали мелок.
                float3 dv = mul((float3x3)UNITY_MATRIX_V, Nw - N0);
                float4 grab = i.grabPos;
                grab.xy += dv.xy * _RefractStrength * grab.w;
                // Фон ограничиваем единицей: HDR-камера хранит пересвеченные светлые
                // полосы ярче 1, и вода над ними становилась чисто белой.
                float3 bg = min(tex2Dproj(_SlideWaterGrab, grab).rgb, 1.0);

                // Струи видны и без пены: тон воды чуть светлее на гребнях струй —
                // так читается именно быстрый поток, а не гладкий гель.
                // Цвет как у моря на картинке окружающего мира (замер панорамы
                // 06.11.2026: глубокая бирюза 0/0.58/0.68, отмели светлее): к центру
                // ленты темнее и плотнее, к краям светлее и прозрачнее.
                float across = saturate(abs(i.uv.x / max(_RibbonWidth, 0.01) * 2.0 - 1.0));
                float k = pow(across, _CenterToEdge);
                fixed4 waterCol = lerp(_CenterColor, _EdgeColor, k);
                float3 tint = waterCol.rgb * diffuse * lc * (0.85 + 0.5 * (n - 0.5));
                float3 water = lerp(bg, tint, waterCol.a);
                float3 foamCol = _FoamColor.rgb * (0.75 + 0.25 * saturate(dot(N, L))) * lc;
                float3 col = lerp(water, foamCol, saturate(foam))
                             + spec * lc + fres * 0.12 * lc;

                float edge = saturate(min(i.uv.x, _RibbonWidth - i.uv.x) / max(_EdgeFade, 0.01));
                fixed4 result = fixed4(col, edge);
                UNITY_APPLY_FOG(i.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
