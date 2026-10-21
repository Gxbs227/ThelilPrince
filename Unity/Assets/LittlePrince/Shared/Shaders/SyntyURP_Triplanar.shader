// URP version of SyntyStudios/TriplanarBasic (rocks of POLYGON Nature Biomes).
// Same property names, so a material keeps its textures when switched to this shader.
Shader "SyntyStudios URP/Triplanar"
{
    Properties
    {
        _Top ("Top", 2D) = "white" {}
        _Sides ("Sides", 2D) = "white" {}
        _TopNormal ("TopNormal", 2D) = "bump" {}
        _SidesNormal ("SidesNormal", 2D) = "bump" {}
        _Tiling ("Tiling", Range(0, 10)) = 1
        _FallOff ("FallOff", Range(0, 100)) = 10
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "SyntyURP_Common.hlsl"

        TEXTURE2D(_Top);   SAMPLER(sampler_Top);
        TEXTURE2D(_Sides); SAMPLER(sampler_Sides);

        CBUFFER_START(UnityPerMaterial)
            float4 _Top_ST;
            float4 _Sides_ST;
            float _Tiling;
            float _FallOff;
            half4 _Color;
            half _Glossiness;
        CBUFFER_END

        half3 Triplanar(float3 positionWS, float3 normalWS)
        {
            float3 w = pow(abs(normalWS), max(_FallOff, 0.001));
            w /= (w.x + w.y + w.z + 1e-5);
            float3 s = sign(normalWS);
            float top = max(0, w.y * s.y);
            float bottom = max(0, w.y * -s.y);
            float2 t = _Tiling;
            half3 x  = SAMPLE_TEXTURE2D(_Sides, sampler_Sides, t * positionWS.zy * float2( s.x, 1)).rgb;
            half3 yT = SAMPLE_TEXTURE2D(_Top,   sampler_Top,   t * positionWS.xz * float2( s.y, 1)).rgb;
            half3 yB = SAMPLE_TEXTURE2D(_Sides, sampler_Sides, t * positionWS.xz * float2( s.y, 1)).rgb;
            half3 z  = SAMPLE_TEXTURE2D(_Sides, sampler_Sides, t * positionWS.xy * float2(-s.z, 1)).rgb;
            return x * w.x + yT * top + yB * bottom + z * w.z;
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                half3 albedo = Triplanar(i.positionWS, n) * _Color.rgb;
                return SyntyShade(i.positionWS, n, albedo, 0, _Glossiness, 0, i.positionCS, i.fog);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return SyntyShadowPositionCS(TransformObjectToWorld(v.positionOS.xyz), TransformObjectToWorldNormal(v.normalOS));
            }
            half4 frag() : SV_Target { return 0; }
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
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformObjectToHClip(v.positionOS.xyz);
            }
            half frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }
    }
    FallBack Off
}
