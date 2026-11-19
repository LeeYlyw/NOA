Shader "NOA/Monster Cloak"
{
    Properties { _MotionStrength ("Motion strength", Range(0, 1)) = 0.25 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _MotionStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float3 normal = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(world);
                output.positionWS = world;
                output.normalWS = normal;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // No colored silhouette: only displace the background slightly.
                float facing = saturate(dot(normalize(input.normalWS), view));
                float edgeFade = smoothstep(0.05, 0.45, facing);
                float3 p = input.positionWS;
                float t = _Time.y;
                float2 flow = float2(
                    sin(p.y * 5.1 + sin(p.z * 3.7 + t) + t * 1.4),
                    cos(p.x * 4.3 + sin(p.y * 3.1 - t * 0.8) - t * 1.2));
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float2 offset = flow * (1.5 * _MotionStrength * edgeFade) / _ScaledScreenParams.xy;
                float2 margin = 2.0 / _ScaledScreenParams.xy;
                half3 background = SampleSceneColor(clamp(uv + offset, margin, 1 - margin));
                return half4(background, 0.28 * edgeFade);
            }
            ENDHLSL
        }
    }
}
