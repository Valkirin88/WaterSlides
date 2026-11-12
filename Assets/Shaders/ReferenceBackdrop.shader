Shader "WaterSlides/ReferenceBackdrop"
{
    Properties
    {
        _MainTex ("World panorama", 2D) = "white" {}
        _Rotation ("Rotation", Range(0,360)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Rotation;
            struct v2f { float4 pos : SV_POSITION; float3 direction : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.direction = mul((float3x3)unity_ObjectToWorld, vertex.xyz);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                // Sample world direction, never screen UV: turning reveals another part of the world.
                float3 direction = normalize(i.direction);
                float2 uv = float2(frac(0.5 + atan2(direction.x, direction.z) / (2 * UNITY_PI) + _Rotation / 360),
                    0.5 + asin(clamp(direction.y, -1, 1)) / UNITY_PI);
                fixed3 color = tex2D(_MainTex, uv).rgb;
                // The source has no authored pole caps. Blend to sea/sky before the poles pinch.
                fixed3 cap = tex2D(_MainTex, float2(0.5, direction.y > 0 ? 0.995 : 0.005)).rgb;
                color = lerp(color, cap, smoothstep(0.86, 0.995, abs(direction.y)));
                return fixed4(color, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
