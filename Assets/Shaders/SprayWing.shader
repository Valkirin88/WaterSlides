// Крыло пены от тела на горке: полотна-меши (SlideSprayWings, два слоя на крыло)
// со скользящей по ним маской шума. Белая полупрозрачная пена с псевдоосвещением
// по нормали (верх светлее, бока и изнанка темнее, светлая кромка на просвет) —
// так два слоя читаются как объёмный гребень, а не плоская плёнка. Без записи
// глубины; рисуется после воды и стекла.
Shader "WaterSlides/SprayWing"
{
    Properties
    {
        _NoiseTex ("Шум (R, G)", 2D) = "gray" {}
        _Color ("Цвет и максимальная непрозрачность", Color) = (1, 1, 1, 0.95)
        _Scroll ("Скорость бега пены вдоль крыла", Float) = 2.5
        _Tiling ("Тайлинг шума (вдоль, поперёк)", Vector) = (1.2, 2.5, 0, 0)
        _Contrast ("Контраст пены", Float) = 2.2
        _Shade ("Глубина теней по нормали (0 — плоско)", Range(0, 1)) = 0.45
        _Strength ("Сила (ставит скрипт)", Float) = 1
        _BreakStart ("Где вдоль крыла пена начинает рваться на островки (ставит скрипт)", Range(0, 1)) = 0.55
        _WingStrength ("Сила левого (x) и правого (y) крыла (ставит скрипт)", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _NoiseTex;
            float4 _Color;
            float _Scroll, _Contrast, _Shade, _Strength, _BreakStart;
            float4 _Tiling;
            float4 _WingStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1; // x — номер слоя (0 нижний, 1 верхний), y — номер крыла (0 левое, 1 правое)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 layerWing : TEXCOORD1;
                float3 wn : TEXCOORD2;
                float3 wpos : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.layerWing = v.uv2;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Два слоя шума бегут от тела к концу крыла; у верхнего слоя свой
                // сдвиг и скорость — слои не совпадают, между ними виден объём.
                float layer = i.layerWing.x;
                float wingStrength = lerp(_WingStrength.x, _WingStrength.y, i.layerWing.y);
                float speed = _Scroll * (1.0 + 0.35 * layer);
                float2 uv1 = float2(i.uv.x * _Tiling.x - _Time.y * speed * 0.3 + layer * 0.41, i.uv.y * _Tiling.y + layer * 0.23);
                float2 uv2 = float2(i.uv.x * _Tiling.x * 1.9 - _Time.y * speed * 0.5, i.uv.y * _Tiling.y * 1.7 + 0.37 + layer * 0.5);
                float n = tex2D(_NoiseTex, uv1).r * 0.6 + tex2D(_NoiseTex, uv2).g * 0.4;
                n = saturate((n - 0.5) * _Contrast + 0.5);

                // Распад: от _BreakStart к концу порог пены растёт — сплошная пена
                // рвётся на островки, они сжимаются до крупинок и исчезают ровно там,
                // где скрипт сыплет всё больше капель (SlideSprayWings.EmitTipDrops).
                // Резкого среза полотна нет: альфа гаснет мягко, форму кончика задают
                // сжимающиеся островки.
                float brk = smoothstep(_BreakStart, 1.0, i.uv.x);
                float foam = smoothstep(0.2 + 0.5 * brk, 0.65 + 0.3 * brk, n);

                // Полотно постепенно прозрачнее к концу по всей длине (не только в зоне распада).
                float along = (1.0 - 0.85 * pow(i.uv.x, 1.5)) * smoothstep(0.0, 0.08, i.uv.x);
                float across = 1.0 - pow(abs(i.uv.y * 2.0 - 1.0), 2.5);
                float solid = saturate(1.0 - i.uv.x / max(_BreakStart, 0.05)) * 0.7; // сплошная подложка у корня, тает к началу распада
                float a = saturate(foam + solid) * along * across * _Color.a * _Strength * wingStrength;

                // Псевдоосвещение: нормаль к камере, свет солнца сверху-сбоку,
                // светлая кромка на просвет (Fresnel). Нижний слой чуть темнее.
                float3 N = normalize(i.wn);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                if (dot(N, V) < 0) N = -N;
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float lit = saturate(dot(N, L)) * 0.6 + saturate(N.y) * 0.4;
                float rim = pow(1.0 - saturate(dot(N, V)), 2.0);
                float shade = lerp(1.0, 0.55 + 0.45 * lit, _Shade) * (1.0 - 0.12 * (1.0 - layer)) + rim * 0.25;
                float3 col = _Color.rgb * shade;
                return fixed4(col, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
