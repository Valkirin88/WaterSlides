Shader "WaterSlides/Night City Sky"
{
    Properties
    {
        _CityPanorama ("City panorama", 2D) = "black" {}
        _PanoramaWeight ("Panorama blend", Range(0,1)) = 0
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
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _CityPanorama;
            float _PanoramaWeight;
            struct v2f { float4 pos:SV_POSITION; float3 ray:TEXCOORD0; };
            v2f vert(appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.ray = v.vertex.xyz;
                return o;
            }
            float hash(float2 p) {
                p = frac(p*float2(.1031,.1379));
                p += dot(p,p.yx+19.19);
                return frac((p.x+p.y)*p.x);
            }
            fixed4 frag(v2f i):SV_Target {
                float3 d = normalize(i.ray);
                float h = saturate(d.y*1.4);
                float band = exp(-abs(d.y+.06)*5.5);
                float3 col = lerp(float3(.060,.102,.215),float3(.015,.026,.089),pow(h,.65));
                // Зарево огней города у горизонта (10.11.2026, «ярче цвета»): фиолетово-
                // розовое, узкая яркая полоса у самого горизонта.
                col = col*.62 + band*float3(.10,.045,.16) + exp(-abs(d.y+.02)*16)*float3(.09,.035,.10);
                float2 s = float2(atan2(d.x,d.z),asin(d.y))*100;
                float2 id = floor(s);
                float2 st = frac(s) - float2(.18+.64*hash(id+13),.18+.64*hash(id+37));
                float star = (1-smoothstep(.045,.17,length(st)))*step(.968,hash(id))*smoothstep(.04,.3,d.y);
                col += star*float3(.54,.73,1.0);
                float3 moonDir = normalize(float3(-.27,.33,.90));
                float m = length(d-moonDir);
                float phase = length(d-normalize(moonDir+float3(.013,.006,-.004)));
                float disk = (1-smoothstep(.023,.025,m))*smoothstep(.022,.024,phase);
                col += exp(-m*21)*float3(.085,.13,.26)+disk*float3(.74,.86,1.0);
                if (_PanoramaWeight > 0)
                {
                    float longitude = atan2(d.x,d.z)/(2*UNITY_PI)+.5;
                    float latitude = asin(clamp(d.y,-1,1))/UNITY_PI+.5;
                    float2 uv = float2(longitude,latitude);
                    float2 dx = ddx(uv);
                    float2 dy = ddy(uv);
                    dx.x -= round(dx.x);
                    dy.x -= round(dy.x);
                    float3 panorama = tex2Dgrad(_CityPanorama,uv,dx,dy).rgb;
                    // Feather the join in the painted texture without blurring the skyline elsewhere.
                    float join = 1-smoothstep(0,.04,min(longitude,1-longitude));
                    if (join > 0)
                    {
                        float3 opposite = tex2Dgrad(_CityPanorama,float2(1-longitude,latitude),dx,dy).rgb;
                        panorama = lerp(panorama,opposite,join*.5);
                    }
                    float poles = smoothstep(.90,.997,abs(d.y));
                    col = lerp(col,panorama,_PanoramaWeight*(1-poles));
                }
                return fixed4(col,1);
            }
            ENDCG
        }
    }
}
