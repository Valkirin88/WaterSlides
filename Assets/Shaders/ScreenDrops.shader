// Water droplets on the lens after launching off a slide. Two layers of a tileable
// drops mask refract the image slightly and crawl down while drying out. Tiling is a
// script parameter — Suimono's own transition magnifies its mask ~3x and gives huge
// unreadable blobs, which is why this shader exists.
Shader "Hidden/WaterSlides/ScreenDrops"
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
            sampler2D _DropsTex;
            float _Strength;  // 0..1 — fade of the whole effect
            float _Tiles;     // drops density (higher — smaller drops)
            float _Refract;   // uv shift under a drop
            float _Slide;     // accumulated downward crawl
            float2 _Offset;   // random per-trigger pattern offset

            fixed4 frag (v2f_img i) : SV_Target
            {
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 duv = i.uv * float2(aspect, 1.0) * _Tiles;

                float m1 = tex2D(_DropsTex, duv + _Offset + float2(0.0, _Slide)).r;
                float m2 = tex2D(_DropsTex, duv * 1.63 + _Offset.yx + float2(0.0, _Slide * 0.55)).r;
                float m = saturate(max(m1, m2 * 0.85)) * _Strength;

                fixed4 col = tex2D(_MainTex, i.uv + m * _Refract);
                // Slight darkening so the drops read even against bright water.
                col.rgb *= 1.0 - m * 0.13;
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
