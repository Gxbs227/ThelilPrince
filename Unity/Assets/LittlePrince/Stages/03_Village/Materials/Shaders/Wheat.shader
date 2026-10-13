// Wheat cards for WheatField: two-sided, alpha-clipped, swaying in the wind.
// The texture is white/grey (like the grass textures); _Color tints it.
Shader "LittlePrince/Wheat"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _Color ("Top Color", Color) = (0.95, 0.76, 0.38, 1)
        _BottomColor ("Bottom Color", Color) = (0.62, 0.55, 0.25, 1)
        _Cutoff ("Alpha Clip", Range(0, 1)) = 0.4
        _WindStrength ("Wind Strength", Range(0, 0.5)) = 0.12
        _WindSpeed ("Wind Speed", Float) = 1.3
        _WindScale ("Wind Wave Size", Float) = 0.15
        _Translucency ("Sun Through Wheat", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _Color;
            half4 _BottomColor;
            half _Cutoff;
            float _WindStrength;
            float _WindSpeed;
            float _WindScale;
            half _Translucency;
        CBUFFER_END

        // Sway the top of the card; the base (uv.y = 0) stays planted.
        float3 Wind(float3 positionWS, float height01)
        {
            float t = _Time.y * _WindSpeed;
            float2 p = positionWS.xz * _WindScale;
            float gust = sin(t + p.x * 2.1 + p.y * 1.3) * 0.6 + sin(t * 1.7 + p.x * 4.3 - p.y * 3.1) * 0.4;
            float bend = height01 * height01 * _WindStrength;
            return float3(gust * bend, -abs(gust) * bend * 0.3, gust * bend * 0.5);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fog : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                ws += Wind(ws, v.uv.y);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(tex.a - _Cutoff);
                half3 albedo = tex.rgb * lerp(_BottomColor.rgb, _Color.rgb, saturate(i.uv.y * 1.4));

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                half3 n = normalize(i.normalWS);
                half ndl = saturate(dot(n, light.direction)) * 0.7 + 0.3;              // soft, card-friendly
                half3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half back = saturate(dot(-viewDir, light.direction)) * _Translucency; // sun shining through
                half3 lit = light.color * light.shadowAttenuation * (ndl + back);
                half3 ambient = SampleSH(n);

                half3 color = albedo * (lit + ambient);
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                ws += Wind(ws, v.uv.y);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
