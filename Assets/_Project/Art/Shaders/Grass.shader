// The grass tufts (GrassField draws them instanced), in place of the pack's shader graph so each tuft can take a
// tint from the ground it stands on (_Tint per instance, from the tile's paint: golden on meadow, dark on the forest
// floor) and stop glowing lime over brown earth. The pack's look otherwise: a bottom-to-top colour gradient by the
// mesh's vertex colour (red rises from the base to the tips), a noise wind swaying the tips, lit both sides with the
// normal pushed toward the tuft's own up (it leans with the ground's slope) so no blade turns black. Ours on top: no specular (the glint made every blade flare when
// looking toward the sun), the roots shaded dark so the tufts sit in the ground, and the terrain's own macro colour
// drift sampled per tuft so the grass darkens and warms with the hillside it stands on.
Shader "Cabin/Grass"
{
    Properties
    {
        BottomColor ("Bottom colour", Color) = (0.3, 0.44, 0.15, 1)
        TopColor ("Top colour", Color) = (0.56, 0.74, 0.2, 1)
        GradientHeight ("Gradient height", Range(0.05, 1)) = 0.85
        GradientSmoothness ("Gradient smoothness", Range(0, 1)) = 1
        WindDirection ("Wind direction (xz)", Vector) = (1, 0, 0.3, 0)
        WindSpeed ("Wind speed", Float) = 1
        WindStrength ("Wind strength", Float) = 0.25
        _Smoothness ("Smoothness", Range(0, 1)) = 0
        _RootShade ("Root shade (brightness at the base)", Range(0, 1)) = 0.55
        [NoScaleOffset] _MacroTex ("Macro noise (the terrain's)", 2D) = "grey" {}
        _MacroScale ("Macro scale (m)", Float) = 260
        _MacroStrength ("Macro brightness drift", Range(0, 1)) = 0.3
        _MacroWarmth ("Macro warmth drift", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 BottomColor, TopColor;
            float4 WindDirection;
            float GradientHeight, GradientSmoothness, WindSpeed, WindStrength;
            half _Smoothness, _RootShade;
            float _MacroScale;
            half _MacroStrength, _MacroWarmth;
        CBUFFER_END

        TEXTURE2D(_MacroTex);
        SAMPLER(sampler_MacroTex);

        // The same drift the terrain shader applies to the ground (TerrainPasses.hlsl), so the grass follows it.
        half3 MacroDrift(half3 albedo, float3 positionWS)
        {
            half m0 = SAMPLE_TEXTURE2D_LOD(_MacroTex, sampler_MacroTex, positionWS.xz / _MacroScale, 0).r - 0.5h;
            half m1 = SAMPLE_TEXTURE2D_LOD(_MacroTex, sampler_MacroTex, positionWS.xz / (_MacroScale * 4.7) + 0.31, 0).r - 0.5h;
            albedo *= 1.0h + (m0 * 0.6h + m1 * 0.4h) * _MacroStrength * 2.0h;
            half warm = saturate(0.5h + m1 * _MacroWarmth * 2.0h);
            return albedo * lerp(half3(0.95h, 1.0h, 1.05h), half3(1.05h, 1.0h, 0.93h), warm);
        }

        UNITY_INSTANCING_BUFFER_START(Grass)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
        UNITY_INSTANCING_BUFFER_END(Grass)

        // Where a vertex ends up after the wind: the tips (vertex red) sway along the wind, by a slow noise over the
        // world so neighbouring tufts move together, a quicker ripple on top.
        float3 Sway(float3 positionWS, float weight)
        {
            float2 dir = normalize(WindDirection.xz + float2(1e-4, 0));
            float t = _Time.y * WindSpeed;
            float phase = dot(positionWS.xz, dir) * 0.35;
            float slow = sin(t * 0.9 + phase) * 0.6 + sin(t * 0.37 + phase * 1.7 + 1.3) * 0.4;
            float ripple = sin(t * 2.3 + positionWS.x * 1.1 + positionWS.z * 0.7) * 0.25;
            float bend = (0.5 + slow + ripple) * WindStrength * weight * weight;
            positionWS.xz += dir * bend;
            positionWS.y -= bend * bend * 0.5; // the tip arcs down as it leans
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 colour : TEXCOORD2; // rgb: the gradient colour, tinted; a: fog
                half3 sh : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float weight = v.color.r;
                float3 positionWS = Sway(TransformObjectToWorld(v.positionOS.xyz), weight);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                half3 n = TransformObjectToWorldNormal(v.normalOS);
                half3 up = normalize(TransformObjectToWorldDir(half3(0, 1, 0))); // the ground's normal where the tuft leans with the slope
                o.normalWS = normalize(lerp(n, up, 0.75h));
                half t = smoothstep(0.0h, max(GradientHeight, 0.05h), weight) ;
                t = lerp(weight / max(GradientHeight, 0.05h), t, GradientSmoothness);
                half3 col = lerp(BottomColor.rgb, TopColor.rgb, saturate(t));
                col *= lerp(_RootShade, 1.0h, saturate(weight / 0.8h)); // dark in the crowded base, full at the tips
                col *= UNITY_ACCESS_INSTANCED_PROP(Grass, _Tint).rgb;
                col = MacroDrift(col, positionWS);
                o.colour = half4(col, ComputeFogFactor(o.positionCS.z));
                o.sh = SampleSHVertex(o.normalWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = normalize(i.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.colour.a);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.bakedGI = SampleSHPixel(i.sh, inputData.normalWS);
                half4 color = UniversalFragmentPBR(inputData, i.colour.rgb, 0.0h, half3(0, 0, 0), _Smoothness, 1.0h, half3(0, 0, 0), 1.0h);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformWorldToHClip(Sway(TransformObjectToWorld(v.positionOS.xyz), v.color.r));
                return o;
            }

            half4 DepthFrag(Varyings i) : SV_Target
            {
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
