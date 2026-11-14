Shader "NOA/Monster Cloak"
{
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float3 normal = TransformObjectToWorldNormal(input.normalOS);
                float ripple = sin(world.y * 13 + world.x * 5 + _Time.y * 3.5) * 0.012;
                world += normal * ripple;
                output.positionCS = TransformWorldToHClip(world);
                output.positionWS = world;
                output.normalWS = normal;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float rim = pow(1 - saturate(dot(normalize(input.normalWS), view)), 3);
                float wave = 0.5 + 0.5 * sin(input.positionWS.y * 18 + input.positionWS.x * 7 - _Time.y * 4);
                return half4(0.38, 0.55, 0.59, (0.015 + rim * 0.10) * (0.35 + wave * 0.65));
            }
            ENDHLSL
        }
    }
}
