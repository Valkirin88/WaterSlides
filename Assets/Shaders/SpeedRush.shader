// Nitro-style speed post effect for the built-in pipeline: radial smear that grows
// towards the screen edges plus a slight stretch (zoom) from the centre.
Shader "Hidden/WaterSlides/SpeedRush"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Strength;   // 0..1 — overall effect amount
            float _Stretch;    // centre zoom at full strength
            float _BlurLength; // radial smear length at full strength

            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 centre = float2(0.5, 0.5);
                float2 fromCentre = i.uv - centre;

                // The centre stays readable; the smear ramps up towards the edges.
                float mask = saturate(dot(fromCentre, fromCentre) * 4.0);
                float amount = _Strength * mask;

                // "Stretch": pull the picture outwards from the centre.
                float2 uv = centre + fromCentre * (1.0 - _Stretch * _Strength * 0.3);

                fixed4 acc = 0;
                [unroll]
                for (int s = 0; s < 8; s++)
                {
                    float t = s / 7.0;
                    acc += tex2D(_MainTex, uv - fromCentre * (t * _BlurLength * amount * 0.2));
                }

                return acc / 8.0;
            }
            ENDCG
        }
    }
    Fallback Off
}
