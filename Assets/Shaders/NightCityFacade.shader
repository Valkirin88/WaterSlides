// Фасады ночного города (Built-in RP, без освещения): один материал на весь
// квартал, вид поверхности задан цветом вершины (NightCitySetup.Style):
//   R — оттенок башни (0..1 по цветовому кругу), G — вид (0 стекло, 1 бетон,
//   2 неон, 3 дорога, 4 маяк), B — ширина стены в окнах / 64 (для неона по
//   рёбрам), A — сила неона.
// 10.11.2026 («ярче цвета»): оттенок башни — полный цветовой круг (было
// бирюза↔фиолет), стекло подкрашено цветом башни, окна трёх цветов (тёплые,
// холодные, в цвет башни) и гуще, неон по рёбрам ярче, светящиеся пояса через
// каждые N этажей, общий множитель 0.62 → 0.85.
Shader "WaterSlides/Night City Facade"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct app { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 style:COLOR; };
            struct v2f {
                float4 pos:SV_POSITION;
                float3 world:TEXCOORD0;
                float3 normal:TEXCOORD1;
                float2 uv:TEXCOORD2;
                float4 style:COLOR;
                UNITY_FOG_COORDS(3)
            };
            v2f vert(app v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.uv = v.uv;
                o.style = v.style;
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            float hash(float2 p) {
                p = frac(p * float2(.1031,.1379));
                p += dot(p,p.yx+19.19);
                return frac((p.x+p.y)*p.x);
            }
            // Чистый цвет по кругу оттенков (насыщенность и яркость 1).
            float3 neon(float h) {
                return saturate(abs(frac(h + float3(0.0, 2.0/3.0, 1.0/3.0))*6.0-3.0)-1.0);
            }
            fixed4 frag(v2f i):SV_Target {
                float3 n = normalize(i.normal);
                float3 view = normalize(_WorldSpaceCameraPos-i.world);
                float3 col;
                float kind = floor(i.style.g*4.0+.5);
                float hue = i.style.r;
                float3 tone = neon(hue);
                if (kind == 0) {
                    float fresnel = pow(1.0-saturate(dot(n,view)),2.2);
                    float3 r = reflect(-view,n);
                    // Тёмное стекло, подкрашенное цветом башни.
                    float3 glass = float3(.03,.07,.16) + tone*.16;
                    float reflectionBand = pow(saturate(.5+.5*r.x),8.0);
                    float sheen = .47 + .85 * reflectionBand + .36*max(r.y,0.0);
                    col = glass*sheen + fresnel*(float3(.04,.12,.22) + tone*.22);
                    float2 uv = i.uv;
                    float2 f = frac(uv);
                    float2 a = max(fwidth(uv)*.75,.015);
                    float2 pane = smoothstep(float2(.045,.10)-a,float2(.045,.10)+a,min(f,1-f));
                    col *= lerp(.53,1.0,pane.x*pane.y);
                    float2 cell = floor(uv);
                    float h = hash(cell + hue*811.0);
                    float suite = hash(floor(cell/float2(3,2)) + hue*93.0);
                    float lit = step(.80-.32*suite,h);
                    // Цвет окна: тёплый, холодный или в цвет башни.
                    float pick = hash(cell + hue*437.0);
                    float3 office = pick < .4 ? float3(1.0,.72,.34)
                                  : pick < .7 ? float3(.42,.82,1.0)
                                  : lerp(tone, float3(1,1,1), .25);
                    float2 b = smoothstep(float2(.12,.22)-a,float2(.12,.22)+a,min(f,1-f));
                    float resolved = 1-smoothstep(.25,1.05,max(a.x,a.y));
                    float interior = smoothstep(.26,.29,f.y)*lerp(.55,1.0,smoothstep(.38,.76,f.y));
                    float windowLight = lit*b.x*b.y*(.65+.95*h)*interior;
                    float2 halo = smoothstep(float2(.025,.06)-a,float2(.12,.22)+a,min(f,1-f));
                    // Вдали окна сливаются в ровное свечение цвета башни.
                    float3 farGlow = lerp(float3(.75,.75,.8), tone, .55) * .16;
                    col += office*((windowLight+.18*lit*halo.x*halo.y)*resolved) + farGlow*(1-resolved);
                    // Неон по вертикальным рёбрам стены.
                    float edge = min(i.uv.x,i.style.b*64-i.uv.x);
                    float trim = 1-smoothstep(.05,.26+min(a.x,.22),edge);
                    col += trim*tone*1.7*i.style.a;
                    // Светящийся пояс через каждые N этажей (N своё у каждой башни).
                    float period = 7.0 + floor(hash(float2(hue*311.0, 7.0))*9.0);
                    float row = cell.y - period*floor(cell.y/period);
                    float belt = step(row,.5) * (1-smoothstep(.16,.26+a.y,f.y)) * step(.5,cell.y);
                    col += belt*tone*1.5*(.35+.65*i.style.a);
                } else if (kind == 1) {
                    col = (lerp(float3(.047,.077,.134),float3(.116,.19,.25),hue) + tone*.035)
                        * (.79+.21*max(n.y,0.0));
                } else if (kind == 2) {
                    col = lerp(tone, float3(1,1,1), .18) * (1.05+i.style.a*.75);
                } else if (kind == 3) {
                    col = lerp(float3(.035,.046,.085),float3(.077,.085,.125),hue);
                    float dashed = step(.47,frac(i.uv.y*.12));
                    float divider = 1-smoothstep(.006,.020+fwidth(i.uv.x),abs(i.uv.x-.5));
                    float edge = 1-smoothstep(.007,.024+fwidth(i.uv.x),abs(min(i.uv.x,1-i.uv.x)-.13));
                    float lamp = 1-smoothstep(.035,.15+fwidth(i.uv.y*.085),abs(frac(i.uv.y*.085)-.5));
                    col += float3(.42,.31,.2)*divider*dashed+edge*(float3(.16,.3,.46)+lamp*float3(1.05,.62,.26));
                } else {
                    col = lerp(float3(1.0,1.22,1.45),float3(1.47,.63,.24),hue);
                }
                col *= .85;
                UNITY_APPLY_FOG(i.fogCoord,col);
                return fixed4(col,1);
            }
            ENDCG
        }
    }
}
