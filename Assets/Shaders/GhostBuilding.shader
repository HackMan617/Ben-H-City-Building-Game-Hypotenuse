Shader "CityBuilder/GhostBuilding"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.35, 0.70, 0.90, 0.45)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GhostBuilding"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            Stencil
            {
                Ref 2
                ReadMask 2
                WriteMask 2
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Fixed light from above and to one side, so the roof and each wall get their own
                // tone and the block reads as a solid shape rather than a flat silhouette.
                float3 lightDir = normalize(float3(0.45, 1.0, -0.3));
                half shade = 0.55h + 0.45h * saturate(dot(normalize(input.normalWS), lightDir));
                return half4(_BaseColor.rgb * shade, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
