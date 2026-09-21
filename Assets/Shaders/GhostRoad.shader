// Translucent preview of a road stroke.
//
// Road geometry is built from overlapping pieces (see RoadMeshBuilder): opaque road hides the
// overlaps, but a plain transparent material would blend twice wherever two pieces meet and the
// junctions would read darker than the straights. The stencil lets each pixel blend only once, so
// the ghost shows one even tint across the whole stroke.
Shader "CityBuilder/GhostRoad"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.35, 0.90, 0.72, 0.50)
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
            Name "GhostRoad"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            Stencil
            {
                Ref 1
                ReadMask 1
                WriteMask 1
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                // Soft wrap lighting: enough to separate curb faces from the top, no more.
                Light light = GetMainLight();
                half facing = saturate(dot(normalize(input.normalWS), light.direction));
                half3 colour = _BaseColor.rgb * (0.6h + 0.4h * facing);
                return half4(colour, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
