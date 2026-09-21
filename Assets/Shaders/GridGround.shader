Shader "CityBuilder/GridGround"
{
    Properties
    {
        _BaseColor    ("Ground Colour", Color) = (0.17, 0.19, 0.18, 1)
        _LineColor    ("Minor Line", Color)    = (0.27, 0.30, 0.29, 1)
        _MajorColor   ("Major Line", Color)    = (0.40, 0.45, 0.43, 1)
        _CellSize     ("Cell Size", Float)     = 1
        _MajorEvery   ("Major Every N Cells", Float) = 10
        _LineWidth    ("Line Width (pixels)", Float) = 1.2
        _GridStrength ("Grid Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _LineColor;
                float4 _MajorColor;
                float  _CellSize;
                float  _MajorEvery;
                float  _LineWidth;
                float  _GridStrength;
            CBUFFER_END

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                return OUT;
            }

            // Coverage of the nearest grid line, measured in pixels rather than world units, so the
            // lines stay one hairline wide at every zoom instead of thickening as the camera drops.
            float GridCoverage(float2 coord, float width)
            {
                float2 distanceToLine = abs(frac(coord + 0.5) - 0.5) / max(fwidth(coord), 1e-6);
                float nearest = min(distanceToLine.x, distanceToLine.y);
                return 1.0 - saturate(nearest - width + 1.0);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 cell = IN.positionWS.xz / max(_CellSize, 1e-4);

                float minor = GridCoverage(cell, _LineWidth);
                float major = GridCoverage(cell / max(_MajorEvery, 1.0), _LineWidth);

                half3 colour = _BaseColor.rgb;
                colour = lerp(colour, _LineColor.rgb,  minor * _GridStrength);
                colour = lerp(colour, _MajorColor.rgb, major * _GridStrength);

                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // Keeps the ground present in the depth prepass so depth-based effects see it.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _LineColor;
                float4 _MajorColor;
                float  _CellSize;
                float  _MajorEvery;
                float  _LineWidth;
                float  _GridStrength;
            CBUFFER_END

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
