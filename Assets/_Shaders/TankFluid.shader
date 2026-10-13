// TimeTrap/TankFluid
// ---------------------------------------------------------------------------------------------------------------
// The glowing teal liquid in the specimen tanks of the portal chamber (intro cutscene). A small shader: the column
// of liquid is see-through enough to show the body floating in it, and never looks still.
//
// Vertex shader:
//   - passes the world position on, so the noise is laid out in metres and every tank looks different
// Fragment shader:
//   - two noise layers drifting upward at different speeds through world-space coordinates: slow currents
//   - fresnel: the edge of the column, where the camera looks through more liquid, is denser and brighter
//   - a gradient that is brighter at the bottom, where the tank's light is
//   - emissive: the result is added on top of whatever lighting there is, so the tanks glow in the dark
//
// Transparent, alpha blended, no depth write. Drawn just before the glass around it.
Shader "TimeTrap/TankFluid"
{
    Properties
    {
        [HDR] _Color ("Fluid Colour", Color) = (0.05, 0.75, 0.7, 1)
        [HDR] _GlowColor ("Edge Glow Colour", Color) = (0.2, 1.6, 1.4, 1)
        _Alpha ("Opacity in the middle", Range(0, 1)) = 0.16
        _EdgeAlpha ("Opacity at the edge", Range(0, 1)) = 0.6
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.2

        [Header(Currents)]
        _NoiseTex ("Noise", 2D) = "gray" {}
        _NoiseScale ("Noise Tiling (per metre)", Float) = 0.45
        _RiseSpeed ("Rise Speed (m per s)", Range(0, 1)) = 0.08
        _NoiseStrength ("Current Strength", Range(0, 1)) = 0.55

        [Header(Gradient)]
        _BottomY ("Bottom of the fluid (object Y)", Float) = -1.4
        _Height ("Fluid Height (m)", Float) = 2.8
        _BottomBoost ("Extra brightness at the bottom", Range(0, 3)) = 0.9
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "TankFluid"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _GlowColor;
                half _Alpha;
                half _EdgeAlpha;
                half _FresnelPower;
                float _NoiseScale;
                half _RiseSpeed;
                half _NoiseStrength;
                float _BottomY;
                float _Height;
                half _BottomBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half   height01   : TEXCOORD2;   // 0 at the bottom of the column, 1 at the top
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.height01 = saturate((input.positionOS.y - _BottomY) / max(_Height, 0.001));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // --- currents: noise read around the column (x + z) and up it (y), rising over time -------
                float around = (input.positionWS.x + input.positionWS.z) * _NoiseScale;
                float up = input.positionWS.y * _NoiseScale - _Time.y * _RiseSpeed;
                half noiseA = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(around, up)).r;
                half noiseB = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(around * 1.9 + 0.37, up * 1.6 - _Time.y * _RiseSpeed * 0.7)).g;
                half currents = lerp(1.0, noiseA * noiseB * 4.0, _NoiseStrength);

                // --- fresnel: looking through the edge of the column means looking through more liquid ----
                half3 normalWS = normalize(input.normalWS);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewWS)), _FresnelPower);

                half bottom = 1.0 + _BottomBoost * (1.0 - input.height01) * (1.0 - input.height01);

                half3 colour = (_Color.rgb + _GlowColor.rgb * fresnel) * currents * bottom;
                half alpha = saturate(lerp(_Alpha, _EdgeAlpha, fresnel) * (0.75 + 0.25 * currents));
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
