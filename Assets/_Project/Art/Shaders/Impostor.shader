// Far-forest impostors: one quad per distant tree, turned to face the camera and wearing the species' picture taken
// from the matching direction (FarForestData's atlas). Drawn with RenderMeshPrimitives: _Trees is the chunk's buffer
// of (position; species, yaw, width scale, height scale) read by instance id; _Width/_Height the species' sizes. Quads nearer than _Near are clipped (the terrain draws
// the real tree there). Alpha-tested with alpha-to-coverage so the edges soften under MSAA. Fog applies. No shadows.
Shader "Cabin/Impostor"
{
    Properties
    {
        [NoScaleOffset] _Atlas ("Impostor atlas", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.45
        _Near ("Clipped nearer than (m)", Float) = 285
        _Views ("Views per species", Float) = 8
        _Columns ("Atlas columns", Float) = 16
        _Rows ("Atlas rows", Float) = 8
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Impostor"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Tree
            {
                float4 position; // xyz
                float4 instance; // species, yaw (radians), width scale, height scale
            };
            StructuredBuffer<Tree> _Trees;

            TEXTURE2D(_Atlas); SAMPLER(sampler_Atlas);
            CBUFFER_START(UnityPerMaterial)
                float _Cutoff, _Near, _Views, _Columns, _Rows;
                half4 _Tint;
                float _Width[16];
                float _Height[16];
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint id : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float dist : TEXCOORD1;
                half fog : TEXCOORD2;
                half3 tint : TEXCOORD3;
                float height01 : TEXCOORD4;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                Tree tree = _Trees[v.id];
                float4 inst = tree.instance;
                int species = (int)(inst.x + 0.5);
                float w = _Width[species] * inst.z, h = _Height[species] * inst.w;
                float3 origin = tree.position.xyz;
                float3 toCam = _WorldSpaceCameraPos - origin;
                float dist = length(toCam.xz);
                float3 fwd = normalize(float3(toCam.x, 0.0, toCam.z + 1e-4));
                float3 right = float3(fwd.z, 0.0, -fwd.x);
                float3 positionWS = origin + right * (v.positionOS.x * w) + float3(0.0, v.positionOS.y * h, 0.0);
                // Each tree its own: a little brighter or darker and warmer or cooler by a hash of where it stands, and
                // darker toward its base (the shade under a canopy), so a far hillside is a forest, not a stamp.
                float hash = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453);
                float hash2 = frac(sin(dot(origin.xz, float2(39.3467, 11.135))) * 24634.6345);
                o.tint = half3(1.0 + (hash2 - 0.5) * 0.12, 1.0, 1.0 - (hash2 - 0.5) * 0.10) * (0.86 + hash * 0.28);
                o.height01 = v.positionOS.y;
                // The picture taken from the direction the camera is in, relative to how the tree is turned.
                float camAngle = atan2(fwd.x, fwd.z);
                float rel = (inst.y + PI - camAngle) / (2.0 * PI);
                float view = fmod(floor(rel * _Views + 0.5) + 16.0 * _Views, _Views);
                float index = species * _Views + view;
                float col = fmod(index, _Columns), row = floor(index / _Columns);
                float2 cell = float2(col / _Columns, 1.0 - (row + 1.0) / _Rows);
                o.uv = cell + v.uv * float2(1.0 / _Columns, 1.0 / _Rows);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.dist = dist;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                clip(i.dist - _Near);
                half4 c = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, i.uv) * _Tint;
                c.rgb *= i.tint * lerp(0.72, 1.0, smoothstep(0.0, 0.55, i.height01));
                clip(c.a - _Cutoff);
                c.rgb = MixFog(c.rgb, i.fog);
                return half4(c.rgb, c.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
