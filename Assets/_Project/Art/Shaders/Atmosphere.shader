// The atmosphere: a few screen passes before post-processing (Cabin.Core.AtmosphereFeature):
//  - haze by distance and height on everything, coloured by the sky and turning to the sun's colour when you look
//    toward it (Firewatch's layered hills);
//  - on the sky: a cloud layer (a tiling noise texture on a plane up at _CloudHeight, lit by how much thicker the
//    cloud is toward the sun), the sun's glow, and the haze band at the horizon;
//  - sun shafts: open sky near the sun (pass 1, at half resolution) smeared toward the sun's screen position by
//    three radial blurs of eight taps each (pass 2, 512 taps in all), so light rakes through the trees' gaps and is
//    blocked by their crowns without the stepped wedges a single short walk leaves; pass 0 adds the result.
// Atmosphere.cs feeds the sun direction and screen position, the cloud axes and scroll, and the cloud texture.
Shader "Cabin/Atmosphere"
{
    Properties
    {
        _SunColor ("Sun colour", Color) = (1, 0.9, 0.72, 1)
        _HazeColor ("Haze colour", Color) = (0.7, 0.79, 0.9, 1)
        _FogDensity ("Haze density", Float) = 0.0011
        _FogBase ("Haze base height (m)", Float) = 0
        _FogFalloff ("Haze height falloff", Float) = 0.006
        _SunInscatter ("Sun inscatter", Range(0, 1)) = 0.75
        _SunGlow ("Sun glow", Float) = 0.8
        _ShaftIntensity ("Shaft intensity", Float) = 0.4
        _ShaftLength ("Shaft length (of the way to the sun)", Range(0, 1)) = 0.8
        _ShaftDecay ("Shaft decay (weight at the far end)", Range(0.01, 1)) = 0.12
        _ShaftRadius ("Shaft reach (screen heights from the sun)", Range(0.1, 2)) = 0.7
        [NoScaleOffset] _CloudTex ("Clouds", 2D) = "black" {}
        _CloudHeight ("Cloud height (m)", Float) = 900
        _CloudScale ("Cloud tile (m)", Float) = 3000
        _CloudCoverage ("Cloud coverage", Range(0, 1)) = 0.48
        _CloudSoftness ("Cloud softness", Range(0.01, 0.5)) = 0.14
        _CloudColor ("Cloud lit", Color) = (1, 0.99, 0.97, 1)
        _CloudShade ("Cloud shade", Color) = (0.62, 0.68, 0.8, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off
        ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D(_CloudTex);
        SAMPLER(sampler_CloudTex);
        TEXTURE2D(_ShaftTex); // the blurred shaft mask (half resolution), for pass 0

        CBUFFER_START(UnityPerMaterial)
            half4 _SunColor, _HazeColor, _CloudColor, _CloudShade;
            float _FogDensity, _FogBase, _FogFalloff, _SunInscatter, _SunGlow;
            float _ShaftIntensity, _ShaftLength, _ShaftDecay, _ShaftRadius;
            float _CloudHeight, _CloudScale, _CloudCoverage, _CloudSoftness;
        CBUFFER_END

        // From Atmosphere.cs each frame.
        float4 _SunDir;     // xyz: toward the sun
        float4 _SunScreen;  // xy: the sun in viewport space, z: how visible (0 when behind or far off-screen)
        float4 _CloudAxisR; // xy: the cloud lattice's first axis in world xz (the sun's right)
        float4 _CloudAxisU; // xy: its second axis (the sun's ground-projected up, unit)
        float4 _CloudOffset; // xy: scroll in metres along those axes
        float4 _Toggles;    // shafts, haze, clouds, glow: each 0 or 1 (the settings)
        // From AtmosphereFeature per blur pass: x: the step per tap (a fraction of the way to the sun), z: the weight lost per tap.
        float4 _ShaftStep;

        #define BLUR_TAPS 8

        bool IsSky(float rawDepth)
        {
            #if UNITY_REVERSED_Z
                return rawDepth < 1e-6;
            #else
                return rawDepth > 0.999999;
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "Atmosphere"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.texcoord;
                half3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float raw = SampleSceneDepth(uv);
                bool sky = IsSky(raw);
                #if !UNITY_REVERSED_Z
                    raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 posWS = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 camPos = _WorldSpaceCameraPos;
                float3 ray = posWS - camPos;
                float dist = length(ray);
                float3 viewDir = ray / max(dist, 1e-4);
                float3 sunDir = normalize(_SunDir.xyz);
                float sunCos = saturate(dot(viewDir, sunDir));

                // The haze's colour: the sky's, warming to the sun's when looking that way.
                half3 fogCol = lerp(_HazeColor.rgb, _SunColor.rgb, pow(sunCos, 8) * _SunInscatter);

                if (sky)
                {
                    half3 s = col;
                    float cover = 0;
                    if (viewDir.y > 0.004)
                    {
                        float t = (_CloudHeight - camPos.y) / viewDir.y;
                        float3 p = camPos + viewDir * t;
                        float2 c = float2(dot(p.xz, _CloudAxisR.xy), dot(p.xz, _CloudAxisU.xy)) + _CloudOffset.xy;
                        float2 cuv = c / _CloudScale;
                        float d0 = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, cuv).r;
                        float2 toSun = float2(dot(sunDir.xz, _CloudAxisR.xy), dot(sunDir.xz, _CloudAxisU.xy)) * 0.012;
                        float d1 = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, cuv + toSun).r;
                        cover = smoothstep(_CloudCoverage, _CloudCoverage + _CloudSoftness, d0) * _Toggles.z;
                        float lit = saturate(0.6 + (d0 - d1) * 5.0); // thinner toward the sun: lit edge
                        half3 cloud = lerp(_CloudShade.rgb, _CloudColor.rgb, lit) * (0.9 + 0.3 * sunCos * sunCos);
                        cloud += _SunColor.rgb * pow(sunCos, 12) * 0.3 * lit;
                        cover *= saturate(viewDir.y * 5.0); // thin out toward the horizon
                        s = lerp(s, cloud, cover);
                    }
                    s += _SunColor.rgb * _SunGlow * _Toggles.w * (pow(sunCos, 600) * 2.0 + pow(sunCos, 60) * 0.18) * (1 - 0.85 * cover);
                    float horizon = pow(1 - saturate(viewDir.y), 10);
                    col = lerp(s, fogCol, horizon * 0.7 * _Toggles.y);
                }
                else
                {
                    float hMid = 0.5 * (camPos.y + posWS.y);
                    float dens = _FogDensity * exp(-max(hMid - _FogBase, 0) * _FogFalloff);
                    float fog = 1 - exp(-dens * dist);
                    col = lerp(col, fogCol, fog * _Toggles.y);
                }

                // Sun shafts: the blurred mask of open sky toward the sun, brightest looking into it.
                float vis = _SunScreen.z;
                if (vis > 0.001 && _ShaftIntensity * _Toggles.x > 0)
                {
                    half shaft = SAMPLE_TEXTURE2D(_ShaftTex, sampler_LinearClamp, uv).r;
                    col += _SunColor.rgb * shaft * _ShaftIntensity * vis * (0.3 + 0.7 * sunCos * sunCos);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShaftMask"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMask
            #pragma target 4.5

            // Open sky, weighted by how close to the sun it is on screen (in screen heights, so the reach is round).
            half4 FragMask(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.texcoord;
                float sky = IsSky(SampleSceneDepth(uv)) ? 1.0 : 0.0;
                float2 d = (uv - _SunScreen.xy) * float2(_ScreenParams.x / _ScreenParams.y, 1.0);
                float near = 1.0 - smoothstep(0.0, _ShaftRadius, length(d));
                return half4(sky * near, 0, 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShaftBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur
            #pragma target 4.5

            // One radial blur toward the sun: eight taps, each _ShaftStep.x of the way there, fading by _ShaftStep.z a tap.
            // Run three times with the step growing eightfold, the taps compose into 512 along the ray.
            half4 FragBlur(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.texcoord;
                float2 step = (_SunScreen.xy - uv) * _ShaftStep.x;
                float w = 1, acc = 0, total = 0;
                [unroll]
                for (int k = 0; k < BLUR_TAPS; k++)
                {
                    acc += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + step * k).r * w;
                    total += w;
                    w *= _ShaftStep.z;
                }
                return half4(acc / total, 0, 0, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
