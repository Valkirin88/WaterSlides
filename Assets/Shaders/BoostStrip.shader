// Ускоряющая полоса на дне жёлоба: бегущие вперёд шевроны (стрелки) поверх
// полупрозрачной подложки. Built-in RP, без освещения. UV.y — метры вдоль полосы.
Shader "WaterSlides/BoostStrip"
{
    Properties
    {
        _Color ("Цвет стрелок", Color) = (1, 0.85, 0.2, 1)
        _BaseColor ("Цвет подложки", Color) = (1, 0.5, 0.1, 0.35)
        _ChevronLength ("Шаг стрелок, м", Float) = 1.2
        _ScrollSpeed ("Скорость бега стрелок, м/с", Float) = 6
        _Sharpness ("Резкость края стрелки", Range(1, 40)) = 14
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _BaseColor;
            float _ChevronLength;
            float _ScrollSpeed;
            float _Sharpness;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Шеврон: остриё вперёд (+v) — центр полосы «впереди» краёв, поэтому
                // к v ПРИБАВЛЯЕМ |u−0.5| (с минусом галочки смотрели назад, проверено снимком).
                float across = abs(i.uv.x - 0.5) * 2.0;          // 0 в центре, 1 у края
                float v = i.uv.y + across * 0.45 * _ChevronLength - _Time.y * _ScrollSpeed;
                float phase = frac(v / max(_ChevronLength, 0.05));
                // Полоса стрелки — треть шага, мягкие края.
                float arrow = saturate((0.5 - abs(phase - 0.5) - 0.17) * _Sharpness);
                // Края полосы чуть прозрачнее.
                float edge = 1.0 - smoothstep(0.75, 1.0, across);
                fixed4 col = lerp(_BaseColor, _Color, arrow);
                col.a *= edge;
                return col;
            }
            ENDCG
        }
    }
}
