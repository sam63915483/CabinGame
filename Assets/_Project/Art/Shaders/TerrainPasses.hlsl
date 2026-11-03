// Cabin/Terrain's passes. The forward pass mixes all eight layers in one go (URP's own shader draws layers 4-7 as
// a second pass, which also breaks its height blend across them): the two control maps give eight weights, the
// layers' heights (the colour textures' alpha) push the taller layer through the shorter at every edge, the rock
// layer is projected from three sides so cliffs are not smeared, and a world-space macro noise drifts the colour
// over hundreds of metres so no hillside is one flat tone. Shadow, depth, depth-normals and scene-selection passes
// are URP's.
#ifndef CABIN_TERRAIN_PASSES_INCLUDED
#define CABIN_TERRAIN_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#define LAYERS 8
#define ROCK 3

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 uvMainAndLM              : TEXCOORD0; // xy: control, zw: lightmap
    half3 normal                    : TEXCOORD1;
    half3 vertexSH                  : TEXCOORD2;
    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        half4 fogFactorAndVertexLight   : TEXCOORD3; // x: fogFactor, yzw: vertex light
    #else
        half  fogFactor                 : TEXCOORD3;
    #endif
    float3 positionWS               : TEXCOORD4;
    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        float4 shadowCoord              : TEXCOORD5;
    #endif
    #if defined(DYNAMICLIGHTMAP_ON)
        float2 dynamicLightmapUV        : TEXCOORD6;
    #endif
    #ifdef USE_APV_PROBE_OCCLUSION
        float4 probeOcclusion           : TEXCOORD7;
    #endif
    float4 clipPos                  : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

// The ground's own normal at a pixel: from the terrain's normal map when instancing, else the vertex's.
half3 GroundNormal(float2 uv, half3 vertexNormal)
{
    #if defined(ENABLE_TERRAIN_PERPIXEL_NORMAL)
        float2 sampleCoords = (uv / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
        return TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1));
    #else
        return normalize(vertexNormal);
    #endif
}

void InitializeInputData(Varyings IN, half3 normalWS, out InputData inputData)
{
    inputData = (InputData)0;
    inputData.positionWS = IN.positionWS;
    inputData.positionCS = IN.clipPos;
    inputData.normalWS = NormalizeNormalPerPixel(normalWS);
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        inputData.shadowCoord = IN.shadowCoord;
    #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
    #else
        inputData.shadowCoord = float4(0, 0, 0, 0);
    #endif

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        inputData.fogCoord = InitializeInputDataFog(float4(IN.positionWS, 1.0), IN.fogFactorAndVertexLight.x);
        inputData.vertexLighting = IN.fogFactorAndVertexLight.yzw;
    #else
        inputData.fogCoord = InitializeInputDataFog(float4(IN.positionWS, 1.0), IN.fogFactor);
    #endif

    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.clipPos);

#if defined(_SCREEN_SPACE_IRRADIANCE)
    inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, inputData.positionCS.xy);
#elif defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(IN.uvMainAndLM.zw, IN.dynamicLightmapUV, IN.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(IN.uvMainAndLM.zw);
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(IN.vertexSH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        inputData.positionCS.xy,
        IN.probeOcclusion,
        inputData.shadowMask);
#else
    inputData.bakedGI = SAMPLE_GI(IN.uvMainAndLM.zw, IN.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(IN.uvMainAndLM.zw);
#endif
}

///////////////////////////////////////////////////////////////////////////////
//                  Forward pass                                             //
///////////////////////////////////////////////////////////////////////////////

Varyings TerrainVert(Attributes v)
{
    Varyings o = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TerrainInstancing(v.positionOS, v.normalOS, v.texcoord);

    VertexPositionInputs Attributes = GetVertexPositionInputs(v.positionOS.xyz);

    o.uvMainAndLM.xy = v.texcoord;
    o.uvMainAndLM.zw = v.texcoord * unity_LightmapST.xy + unity_LightmapST.zw;
#if defined(DYNAMICLIGHTMAP_ON)
    o.dynamicLightmapUV = v.texcoord * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif

    o.normal = TransformObjectToWorldNormal(v.normalOS);
    OUTPUT_SH4(Attributes.positionWS, o.normal.xyz, GetWorldSpaceNormalizeViewDir(Attributes.positionWS), o.vertexSH, o.probeOcclusion);

    half fogFactor = 0;
    #if !defined(_FOG_FRAGMENT)
        fogFactor = ComputeFogFactor(Attributes.positionCS.z);
    #endif
    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        o.fogFactorAndVertexLight.x = fogFactor;
        o.fogFactorAndVertexLight.yzw = VertexLighting(Attributes.positionWS, o.normal.xyz);
    #else
        o.fogFactor = fogFactor;
    #endif

    o.positionWS = Attributes.positionWS;
    o.clipPos = Attributes.positionCS;
    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        o.shadowCoord = GetShadowCoord(Attributes);
    #endif
    return o;
}

#define SAMPLE_LAYER(i, uv) SAMPLE_TEXTURE2D(_Splat##i, sampler_Splat0, uv)
#define SAMPLE_LAYER_NORMAL(i, uv) UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal##i, sampler_Normal0, uv), _NormalScale##i)

// The rock layer from three sides, blended by which way the ground faces (a whiteout blend for the normals).
void SampleRock(float3 p, half3 n, out half4 albedo, out half3 normalWS)
{
    half3 bw = pow(abs(n), _TriplanarSharpness);
    bw /= max(bw.x + bw.y + bw.z, 1e-4);
    half3 axisSign = half3(n.x < 0 ? -1 : 1, n.y < 0 ? -1 : 1, n.z < 0 ? -1 : 1);
    float tile = _Tiles0.w;
    float2 uvX = p.zy / tile, uvY = p.xz / tile, uvZ = p.xy / tile;
    uvX.x *= axisSign.x;
    uvY.x *= axisSign.y;
    uvZ.x *= -axisSign.z;
    albedo = SAMPLE_LAYER(3, uvX) * bw.x + SAMPLE_LAYER(3, uvY) * bw.y + SAMPLE_LAYER(3, uvZ) * bw.z;
    half3 tX = SAMPLE_LAYER_NORMAL(3, uvX), tY = SAMPLE_LAYER_NORMAL(3, uvY), tZ = SAMPLE_LAYER_NORMAL(3, uvZ);
    tX.x *= axisSign.x;
    tY.x *= axisSign.y;
    tZ.x *= -axisSign.z;
    tX = half3(tX.xy + n.zy, abs(tX.z) * n.x);
    tY = half3(tY.xy + n.xz, abs(tY.z) * n.y);
    tZ = half3(tZ.xy + n.xy, abs(tZ.z) * n.z);
    normalWS = normalize(tX.zyx * bw.x + tY.xzy * bw.y + tZ.xyz * bw.z);
}

void TerrainFrag(
    Varyings IN
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
    )
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
#ifdef _ALPHATEST_ON
    ClipHoles(IN.uvMainAndLM.xy);
#endif

    float3 p = IN.positionWS;
    half3 geoN = GroundNormal(IN.uvMainAndLM.xy, IN.normal);
    half3 tangentWS = normalize(cross(GetObjectToWorldMatrix()._13_23_33, geoN));
    half3x3 tangentToWorld = half3x3(-tangentWS, cross(geoN, tangentWS), geoN);

    // The eight weights.
    float2 splatUV = (IN.uvMainAndLM.xy * (_Control_TexelSize.zw - 1.0f) + 0.5f) * _Control_TexelSize.xy;
    half4 c0 = SAMPLE_TEXTURE2D(_Control, sampler_Control, splatUV);
    half4 c1 = SAMPLE_TEXTURE2D(_Control1, sampler_Control, splatUV);
    half c[LAYERS] = { c0.r, c0.g, c0.b, c0.a, c1.r, c1.g, c1.b, c1.a };
    // Cliffs by the ground's own slope: gorge walls and ledges are narrower than the paint's 2 m cells, so the
    // weights alone leave turf smeared down them.
    half cliff = 1.0h - smoothstep(cos(radians(_CliffSlope.y)), cos(radians(_CliffSlope.x)), geoN.y);
    [unroll]
    for (int ci = 0; ci < LAYERS; ci++) c[ci] = ci == ROCK ? lerp(c[ci], 1.0h, cliff) : c[ci] * (1.0h - cliff);

    // The layers, in world space so the tiles run on across the terrain's tiles; the rock from three sides.
    half4 a[LAYERS];
    half3 n[LAYERS];
    float2 uv0 = p.xz / _Tiles0.x, uv1 = p.xz / _Tiles0.y, uv2 = p.xz / _Tiles0.z;
    float2 uv4 = p.xz / _Tiles1.x, uv5 = p.xz / _Tiles1.y, uv6 = p.xz / _Tiles1.z, uv7 = p.xz / _Tiles1.w;
    a[0] = SAMPLE_LAYER(0, uv0); n[0] = SAMPLE_LAYER_NORMAL(0, uv0);
    a[1] = SAMPLE_LAYER(1, uv1); n[1] = SAMPLE_LAYER_NORMAL(1, uv1);
    a[2] = SAMPLE_LAYER(2, uv2); n[2] = SAMPLE_LAYER_NORMAL(2, uv2);
    a[4] = SAMPLE_LAYER(4, uv4); n[4] = SAMPLE_LAYER_NORMAL(4, uv4);
    a[5] = SAMPLE_LAYER(5, uv5); n[5] = SAMPLE_LAYER_NORMAL(5, uv5);
    a[6] = SAMPLE_LAYER(6, uv6); n[6] = SAMPLE_LAYER_NORMAL(6, uv6);
    a[7] = SAMPLE_LAYER(7, uv7); n[7] = SAMPLE_LAYER_NORMAL(7, uv7);
    half3 rockN;
    SampleRock(p, geoN, a[ROCK], rockN);
    n[ROCK] = half3(0, 0, 1);

    // Height blend: the tallest layer (height scaled by its weight) shows, the rest only within the transition of it.
    half w[LAYERS];
    half maxH = 0;
    [unroll]
    for (int i = 0; i < LAYERS; i++)
    {
        w[i] = a[i].a * c[i];
        maxH = max(maxH, w[i]);
    }
    half transition = max(_HeightTransition, 1e-4);
    half sum = 0;
    [unroll]
    for (int j = 0; j < LAYERS; j++)
    {
        w[j] = (max(0, w[j] + transition - maxH) + 1e-6) * c[j];
        sum += w[j];
    }
    sum = max(sum, 1e-6);

    half3 albedo = 0;
    half3 nTS = half3(0, 0, 1e-5);
    [unroll]
    for (int k = 0; k < LAYERS; k++)
    {
        w[k] /= sum;
        albedo += a[k].rgb * w[k];
        nTS += n[k] * w[k];
    }
    half3 normalWS = TransformTangentToWorld(normalize(nTS), tangentToWorld);
    normalWS = normalize(lerp(normalWS, rockN, w[ROCK]));

    // Macro variation: brightness and warmth drifting over hundreds of metres, twice as gently on the rock.
    half m0 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, p.xz / _MacroScale).r - 0.5h;
    half m1 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, p.xz / (_MacroScale * 4.7) + 0.31).r - 0.5h;
    half strength = _MacroStrength * (1.0h - 0.5h * w[ROCK]);
    albedo *= 1.0h + (m0 * 0.6h + m1 * 0.4h) * strength * 2.0h;
    half warm = saturate(0.5h + m1 * _MacroWarmth * 2.0h);
    albedo *= lerp(half3(0.95h, 1.0h, 1.05h), half3(1.05h, 1.0h, 0.93h), warm);

    InputData inputData;
    InitializeInputData(IN, normalWS, inputData);

    half4 color = UniversalFragmentPBR(inputData, albedo, 0.0h, half3(0.0h, 0.0h, 0.0h), _Smoothness, 1.0h, half3(0, 0, 0), 1.0h);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    outColor = half4(color.rgb, 1.0h);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

///////////////////////////////////////////////////////////////////////////////
//                  Shadow, depth, depth-normals (URP's)                     //
///////////////////////////////////////////////////////////////////////////////

float3 _LightDirection;
float3 _LightPosition;

struct AttributesLean
{
    float4 position     : POSITION;
    float3 normalOS       : NORMAL;
    float2 texcoord     : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VaryingsLean
{
    float4 clipPos      : SV_POSITION;
    float2 texcoord     : TEXCOORD0;
    half3 normal        : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};

VaryingsLean ShadowPassVertex(AttributesLean v)
{
    VaryingsLean o = (VaryingsLean)0;
    UNITY_SETUP_INSTANCE_ID(v);
    TerrainInstancing(v.position, v.normalOS, v.texcoord);

    float3 positionWS = TransformObjectToWorld(v.position.xyz);
    float3 normalWS = TransformObjectToWorldNormal(v.normalOS);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 clipPos = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

#if UNITY_REVERSED_Z
    clipPos.z = min(clipPos.z, UNITY_NEAR_CLIP_VALUE);
#else
    clipPos.z = max(clipPos.z, UNITY_NEAR_CLIP_VALUE);
#endif

    o.clipPos = clipPos;
    o.texcoord = v.texcoord;
    return o;
}

half4 ShadowPassFragment(VaryingsLean IN) : SV_TARGET
{
#ifdef _ALPHATEST_ON
    ClipHoles(IN.texcoord);
#endif
    return 0;
}

VaryingsLean DepthOnlyVertex(AttributesLean v)
{
    VaryingsLean o = (VaryingsLean)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TerrainInstancing(v.position, v.normalOS, v.texcoord);
    o.clipPos = TransformObjectToHClip(v.position.xyz);
    o.texcoord = v.texcoord;
    o.normal = TransformObjectToWorldNormal(v.normalOS);
    return o;
}

half4 DepthOnlyFragment(VaryingsLean IN) : SV_TARGET
{
#ifdef _ALPHATEST_ON
    ClipHoles(IN.texcoord);
#endif
#ifdef SCENESELECTIONPASS
    return half4(_ObjectId, _PassValue, 1.0, 1.0);
#endif
    return IN.clipPos.z;
}

void DepthNormalsFragment(
    VaryingsLean IN
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out float4 outRenderingLayers : SV_Target1
#endif
    )
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
#ifdef _ALPHATEST_ON
    ClipHoles(IN.texcoord);
#endif
    outNormalWS = half4(GroundNormal(IN.texcoord, IN.normal), 0.0h);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = float4((float)EncodeMeshRenderingLayer(), 0, 0, 0);
#endif
}

#endif
