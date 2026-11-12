Shader "WaterSlides/TropicalOcean"
{
    Properties
    {
        _DeepColor ("Deep Blue", Color) = (0.015, 0.36, 0.72, 1)
        _ShallowColor ("Lagoon Blue", Color) = (0.02, 0.72, 0.88, 1)
        _FoamColor ("Sunlit Wave", Color) = (0.62, 0.94, 1, 1)
        _WaveScale ("Wave Scale", Float) = 3
        _WaveSpeed ("Wave Speed", Float) = 0.18
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "TropicalOcean"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShallowColor;
                half4 _FoamColor;
                float _WaveScale;
                float _WaveSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * _WaveScale;
                float t = _Time.y * _WaveSpeed;
                float a = sin(p.x * 1.35 + p.y * 0.62 + t);
                float b = cos(p.y * 1.18 - p.x * 0.48 - t * 0.82);
                float waves = a * b;
                float glint = smoothstep(0.79, 0.98, waves);
                half3 lagoon = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(0.48 + waves * 0.23));
                lagoon = lerp(lagoon, _FoamColor.rgb, glint * 0.28);
                return half4(lagoon, 1);
            }
            ENDHLSL
        }
    }
    // The project currently runs the Built-in pipeline. Keep a matching pass
    // so the lagoon plane is visible rather than exposing the skybox below it.
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            fixed4 _DeepColor, _ShallowColor, _FoamColor;
            float _WaveSpeed;
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
            };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float2 hash(float2 p)
            {
                return frac(sin(float2(dot(p, float2(127.1,311.7)), dot(p,float2(269.5,183.3)))) * 43758.5453);
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _WaveSpeed;
                float2 p = i.world.xz * 0.2;
                p += 0.4 * float2(sin(p.y * 2.3 + t), cos(p.x * 2.1 - t));
                p += 0.12 * float2(sin(p.y * 5.7 - t), cos(p.x * 5.1 + t));
                float2 cell = floor(p), local = frac(p);
                float first = 10, second = 10;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 offset = float2(x,y);
                    float2 seed = hash(cell + offset);
                    float2 delta = offset + 0.5 + 0.3 * sin(t + 6.2831 * seed) - local;
                    float distance = dot(delta, delta);
                    if (distance < first) { second = first; first = distance; }
                    else second = min(second, distance);
                }
                float edge = sqrt(second) - sqrt(first);
                float aa = max(fwidth(edge), 0.018);
                float caustic = 1 - smoothstep(0.025, 0.025 + aa * 1.5, edge);
                float depth = 0.65 + 0.18 * sin(i.world.x * 0.012 + sin(i.world.z * 0.01));
                fixed3 color = lerp(_DeepColor.rgb, _ShallowColor.rgb, depth);
                color = lerp(color, _FoamColor.rgb, caustic * 0.25);
                fixed4 result = fixed4(color, 1);
                // A huge four-vertex plane requires per-pixel fog distance.
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                UNITY_CALC_FOG_FACTOR_RAW(length(_WorldSpaceCameraPos - i.world));
                result.rgb = lerp(unity_FogColor.rgb, result.rgb, saturate(unityFogFactor));
                #endif
                return result;
            }
            ENDCG
        }
    }
    FallBack Off
}
