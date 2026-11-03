// Lakes, rivers and the sea. One cheap transparent pass, no depth texture: the water's depth at each vertex is
// baked into the mesh (UV2.x; vertex colours would clamp it to 1) and its flow (UV1: direction × speed, zero on still water) by the map
// generator. Two copies of a tiling normal map drift across each other for small waves; on rivers they're carried
// downstream with a two-phase flow map so the surface visibly runs without stretching. Shallow water is clearer
// and lighter, deep water dark; a soft foam line at the edges; sun glint and a sky reflection by Fresnel; fog.
Shader "Cabin/Water"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.22, 0.40, 0.36, 0.35)
        _DeepColor ("Deep", Color) = (0.03, 0.11, 0.17, 0.94)
        _SkyColor ("Sky reflection", Color) = (0.62, 0.74, 0.86, 1)
        _FoamColor ("Foam", Color) = (0.88, 0.92, 0.92, 1)
        [NoScaleOffset] _NormalMap ("Waves (normal)", 2D) = "bump" {}
        _WaveScale ("Wave size (m)", Float) = 6
        _WaveStrength ("Wave strength", Range(0, 2)) = 0.7
        _WaveSpeed ("Wave drift (m/s)", Float) = 0.25
        _FlowSpeed ("River flow multiplier", Float) = 1
        _DeepAt ("Fully deep at (m)", Float) = 2.5
        _FoamWidth ("Foam depth (m)", Float) = 0.07
        _Gloss ("Sun glint sharpness", Float) = 180
        _FallSpeed ("Waterfall speed (m/s)", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _SkyColor, _FoamColor;
                float _WaveScale, _WaveStrength, _WaveSpeed, _FlowSpeed, _DeepAt, _FoamWidth, _Gloss, _FallSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 depth : TEXCOORD2;  // x: depth (metres), y: falling water (1 on a waterfall's sheet)
                float2 flow : TEXCOORD1;   // world xz, metres per second
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 flow : TEXCOORD1;
                float2 depth : TEXCOORD2;  // x: depth, y: falling
                half fog : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.flow = v.flow * _FlowSpeed;
                o.depth = v.depth;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half3 Waves(float2 uv)
            {
                return UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _WaveStrength);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 xz = i.positionWS.xz / _WaveScale;
                float t = _Time.y;

                // Still water: two layers drifting different ways at different sizes.
                float2 drift = float2(0.7, 0.4) * _WaveSpeed / _WaveScale;
                half3 n1 = Waves(xz + drift * t);
                half3 n2 = Waves(xz * 0.63 + float2(0.37, 0.71) - float2(-0.5, 0.8) * _WaveSpeed / _WaveScale * t);
                half3 still = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));

                // Running water: the pattern carried downstream, two phases half a cycle apart, cross-faded so
                // neither stretches too far before it resets.
                float2 flow = i.flow / _WaveScale;
                float speed = length(i.flow);
                float phase0 = frac(t * 0.5), phase1 = frac(t * 0.5 + 0.5);
                half3 f0 = Waves(xz * 1.4 - flow * phase0 * 2.0);
                half3 f1 = Waves(xz * 1.4 - flow * phase1 * 2.0 + 0.5);
                float blend = abs(1.0 - 2.0 * phase0);
                half3 running = normalize(lerp(f0, f1, blend));
                half3 nTS = normalize(lerp(still, running, saturate(speed * 2.0)));
                // Falling water: streaks running straight down the sheet, fast.
                float fall = i.depth.y;
                half3 fallN = Waves(float2((i.positionWS.x + i.positionWS.z) * 0.9, i.positionWS.y * 0.9 + t * _FallSpeed) / _WaveScale * 2.0);
                nTS = normalize(lerp(nTS, fallN, fall));
                half3 normalWS = normalize(half3(nTS.x, nTS.z, nTS.y));

                // Colour by depth; the edges clearer.
                float deep = saturate(i.depth.x / _DeepAt);
                half4 water = lerp(_ShallowColor, _DeepColor, deep * deep * (3.0 - 2.0 * deep));

                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light sun = GetMainLight();
                half nl = saturate(dot(normalWS, sun.direction));
                half3 lit = water.rgb * (0.55 + 0.45 * nl) * sun.color;
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), 4.0);
                lit = lerp(lit, _SkyColor.rgb, fresnel * 0.65);
                half3 h = normalize(sun.direction + viewDir);
                lit += sun.color * pow(saturate(dot(normalWS, h)), _Gloss) * 1.6;

                // Foam: a soft broken line where it's shallow, and a little on fast water.
                half3 n3 = Waves(xz * 2.3 + drift * t * 1.7);
                float broken = saturate(n3.x * 2.0 + 0.5);
                float foam = saturate(1.0 - i.depth.x / _FoamWidth) * broken * 0.6 + saturate(speed - 0.8) * broken * 0.25;
                foam = max(foam, fall * (0.25 + 0.6 * broken));
                lit = lerp(lit, _FoamColor.rgb, fall * 0.2);
                lit = lerp(lit, _FoamColor.rgb, foam * 0.7);
                half alpha = saturate(max(max(water.a + fresnel * 0.3, foam), fall * 0.9));

                lit = MixFog(lit, i.fog);
                return half4(lit, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
