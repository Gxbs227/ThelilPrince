// Shared bits for the URP versions of the Synty POLYGON Nature Biomes shaders.
#ifndef SYNTY_URP_COMMON_INCLUDED
#define SYNTY_URP_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Lit colour with URP's PBR lighting: main light + shadows, ambient (skybox SH), fog.
half4 SyntyShade(float3 positionWS, half3 normalWS, half3 albedo, half metallic, half smoothness, half3 emission, float4 positionCS, float fogCoord)
{
    InputData input = (InputData)0;
    input.positionWS = positionWS;
    input.positionCS = positionCS;
    input.normalWS = NormalizeNormalPerPixel(normalWS);
    input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
    input.shadowCoord = TransformWorldToShadowCoord(positionWS);
    input.fogCoord = fogCoord;
    input.bakedGI = SampleSH(input.normalWS);
    input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    input.shadowMask = half4(1, 1, 1, 1);

    SurfaceData surface = (SurfaceData)0;
    surface.albedo = albedo;
    surface.metallic = metallic;
    surface.smoothness = smoothness;
    surface.occlusion = 1;
    surface.emission = emission;
    surface.alpha = 1;
    surface.normalTS = half3(0, 0, 1);

    half4 color = UniversalFragmentPBR(input, surface);
    color.rgb = MixFog(color.rgb, fogCoord);
    color.a = 1;
    return color;
}

// Cheap smooth 3D value noise (0..1), used for the colour variation the originals do with simplex noise.
float SyntyHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float SyntyNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(SyntyHash(i + float3(0, 0, 0)), SyntyHash(i + float3(1, 0, 0)), f.x),
                     lerp(SyntyHash(i + float3(0, 1, 0)), SyntyHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(SyntyHash(i + float3(0, 0, 1)), SyntyHash(i + float3(1, 0, 1)), f.x),
                     lerp(SyntyHash(i + float3(0, 1, 1)), SyntyHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// Shadow caster helper (directional lights).
float3 _LightDirection;

float4 SyntyShadowPositionCS(float3 positionWS, float3 normalWS)
{
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return positionCS;
}

#endif
