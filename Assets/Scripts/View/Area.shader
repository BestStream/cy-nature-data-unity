Shader "Custom/Area"
{
    Properties
    {
        _BaseColor("Fill Color", Color) = (0, 1, 0, 0.4)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent+10"
        }
        
        Cull Off

        // Standard alpha blending
        Blend SrcAlpha OneMinusSrcAlpha

        // Do not write to depth, we want overlay behavior
        ZWrite Off

        // Always pass depth test -> draw on top of all 3D geometry
        ZTest Always

        Pass
        {
            HLSLPROGRAM

            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _BaseColor;
            }

            ENDHLSL
        }
    }

    FallBack Off
}