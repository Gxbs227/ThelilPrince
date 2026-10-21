// URP version of SyntyStudios/VegitationShader and VegitationShader_Basic (POLYGON Nature Biomes
// plants: cacti, succulents, grass, brambles...). Same property names, so a material keeps its
// textures and colours when switched to this shader.
//   Vertex colour blue > 0.5 = leaf, otherwise trunk (as in the originals).
//   Vertex colour red = how much the wind moves that vertex.
Shader "SyntyStudios URP/Vegetation"
{
    Properties
    {
        _Cutoff ("Mask Clip Value", Range(0, 1)) = 0.5
        [Header(Leaves)]
        _LeafTex ("LeafTex", 2D) = "white" {}
        _LeafSmoothness ("LeafSmoothness", Range(0, 1)) = 0.2
        _LeafMetallic ("LeafMetallic", Range(0, 1)) = 0
        _LeafBaseColour ("LeafBaseColour", Color) = (0, 0, 0, 0)
        _LeafNoiseColour ("LeafNoiseColour", Color) = (0, 0, 0, 0)
        _LeafNoiseLargeColour ("LeafNoiseLargeColour", Color) = (0, 0, 0, 0)
        _ColourNoiseSmallScale ("ColourNoiseSmallScale", Range(0, 1)) = 0.5
        _ColourNoiseLargeScale ("ColourNoiseLargeScale", Range(0, 1)) = 0.5
        [Toggle] _LeafFlatColourSwitch ("LeafFlatColour", Float) = 0
        [Header(Trunk)]
        _TunkTex ("TunkTex", 2D) = "white" {}
        _TrunkSmoothness ("TrunkSmoothness", Range(0, 1)) = 0.2
        _TrunkMetallic ("TrunkMetallic", Range(0, 1)) = 0
        _TrunkBaseColour ("TrunkBaseColour", Color) = (0.078, 0.02, 0, 0)
        _TrunkNoiseColour ("TrunkNoiseColour", Color) = (0, 0, 0, 0)
        [Toggle] _TrunkFlatColourSwitch ("TrunkFlatColour", Float) = 0
        [Header(Emissive)]
        _EmissiveAmount ("EmissiveAmount", Range(0, 2)) = 0
        _EmissiveColour ("EmissiveColour", Color) = (0, 0, 0, 0)
        [Header(Wind)]
        _TotalWindAmount ("TotalWindAmount", Range(0, 1)) = 0
        _GustAmount ("GustAmount", Range(0, 1)) = 0.3
        _GustSmallFreq ("GustSmallFreq", Range(0, 1)) = 0.5
        _GustScale ("GustScale", Float) = 0.5
        _WindBaseline ("WindBaseline", Float) = 0
        _WindDirection ("WindDirection", Range(0, 1)) = 0
        _Y_multiplier ("Y_multiplier", Float) = 0.2
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "SyntyURP_Common.hlsl"

        TEXTURE2D(_LeafTex); SAMPLER(sampler_LeafTex);
        TEXTURE2D(_TunkTex); SAMPLER(sampler_TunkTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _LeafTex_ST;
            float4 _TunkTex_ST;
            half _Cutoff;
            half _LeafSmoothness, _LeafMetallic, _TrunkSmoothness, _TrunkMetallic;
            half4 _LeafBaseColour, _LeafNoiseColour, _LeafNoiseLargeColour, _TrunkBaseColour, _TrunkNoiseColour;
            float _ColourNoiseSmallScale, _ColourNoiseLargeScale;
            half _LeafFlatColourSwitch, _TrunkFlatColourSwitch;
            half _EmissiveAmount;
            half4 _EmissiveColour;
            float _TotalWindAmount, _GustAmount, _GustSmallFreq, _GustScale, _WindBaseline, _WindDirection, _Y_multiplier;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 WindOffsetWS(float3 positionWS, float weight)
        {
            if (_TotalWindAmount <= 0) return 0;
            float angle = _WindDirection * 6.28;
            float2 dir = float2(cos(angle) - sin(angle), sin(angle) + cos(angle)); // (1,1) rotated, like the original
            float big = SyntyNoise(float3(positionWS.xz * 0.05, 0));
            float wave = sin(dot(positionWS.xz, dir) * _GustScale - _Time.y * _GustSmallFreq * 10.0);
            float gust = _GustAmount * 4.0 * ((big * wave + 0.5) * 0.5) * (0.75 + 0.25 * sin(_Time.y * 0.7));
            float g = (gust + _WindBaseline) * weight;
            float total = _TotalWindAmount * 2.0;
            float2 xz = dir * g * total;
            float y = smoothstep(0, 2, abs(g)) * _Y_multiplier * total;
            return float3(xz.x, -y, xz.y);
        }

        float3 DisplacedWS(Attributes v)
        {
            float3 ws = TransformObjectToWorld(v.positionOS.xyz);
            return ws + WindOffsetWS(ws, v.color.r);
        }

        half4 SampleSurface(float2 uv, float leafMask)
        {
            return leafMask > 0.5
                ? SAMPLE_TEXTURE2D(_LeafTex, sampler_LeafTex, uv * _LeafTex_ST.xy + _LeafTex_ST.zw)
                : SAMPLE_TEXTURE2D(_TunkTex, sampler_TunkTex, uv * _TunkTex_ST.xy + _TunkTex_ST.zw);
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

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float leaf : TEXCOORD3;
                float fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = DisplacedWS(v);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.leaf = v.color.b;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 tex = SampleSurface(i.uv, i.leaf);
                clip(tex.a - _Cutoff);
                bool leaf = i.leaf > 0.5;

                // Colour variation across the world, like the originals (simplex noise there).
                float3 p = i.positionWS * 0.5;
                float small = SyntyNoise(p * max(_ColourNoiseSmallScale, 0.001) * 4.0);
                float large = SyntyNoise(p * max(_ColourNoiseLargeScale, 0.001) * 0.8);
                half3 leafCol = lerp(_LeafNoiseColour.rgb, _LeafBaseColour.rgb, small);
                leafCol = lerp(leafCol, _LeafNoiseLargeColour.rgb, large * any(_LeafNoiseLargeColour.rgb > 0));
                if (!any(_LeafNoiseColour.rgb > 0)) leafCol = _LeafBaseColour.rgb; // basic materials: no noise colours
                half3 trunkCol = any(_TrunkNoiseColour.rgb > 0) ? lerp(_TrunkNoiseColour.rgb, _TrunkBaseColour.rgb, small) : _TrunkBaseColour.rgb;

                half3 albedo = leaf ? lerp(max(tex.rgb, leafCol), leafCol, _LeafFlatColourSwitch)
                                    : lerp(max(tex.rgb, trunkCol), trunkCol, _TrunkFlatColourSwitch);
                half smooth = leaf ? _LeafSmoothness : _TrunkSmoothness;
                half metal = leaf ? _LeafMetallic : _TrunkMetallic;

                float3 n = normalize(i.normalWS) * (IS_FRONT_VFACE(face, true, false) ? 1 : -1);
                half3 emission = _EmissiveColour.rgb * _EmissiveAmount;
                return SyntyShade(i.positionWS, n, albedo, metal, smooth, emission, i.positionCS, i.fog);
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
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float leaf : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = SyntyShadowPositionCS(DisplacedWS(v), TransformObjectToWorldNormal(v.normalOS));
                o.uv = v.uv;
                o.leaf = v.color.b;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SampleSurface(i.uv, i.leaf).a - _Cutoff);
                return 0;
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
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float leaf : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformWorldToHClip(DisplacedWS(v));
                o.uv = v.uv;
                o.leaf = v.color.b;
                return o;
            }
            half frag(Varyings i) : SV_Target
            {
                clip(SampleSurface(i.uv, i.leaf).a - _Cutoff);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
