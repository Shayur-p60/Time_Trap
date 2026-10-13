// TimeTrap/TankGlass
// ---------------------------------------------------------------------------------------------------------------
// The glass of the specimen tanks in the portal chamber (intro cutscene). A small shader: clear when looked at
// straight on, more visible and slightly reflective toward its edges.
//
// Vertex shader:
//   - nothing special: position and normal to world space
// Fragment shader:
//   - fresnel: opacity and brightness rise where the glass turns away from the camera
//   - reflection: there is nothing to reflect in a dark hall, so two soft vertical streaks are faked from the
//     direction the reflected view ray points in (they slide round the cylinder as the camera moves)
//   - Blinn-Phong highlights from the additional lights (the tank lights and the red lamps), through the Forward+
//     light loop
//
// Transparent, alpha blended, no depth write. Drawn after the fluid inside it.
Shader "TimeTrap/TankGlass"
{
    Properties
    {
        _Tint ("Glass Tint", Color) = (0.55, 0.9, 0.95, 1)
        _Alpha ("Opacity facing the camera", Range(0, 1)) = 0.04
        _EdgeAlpha ("Opacity at the edge", Range(0, 1)) = 0.45
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3

        [Header(Reflection)]
        [HDR] _ReflectColor ("Reflection Colour", Color) = (0.5, 0.95, 1.1, 1)
        _StreakPower ("Streak Sharpness", Range(1, 64)) = 14
        _StreakIntensity ("Streak Intensity", Range(0, 2)) = 0.35
        _Smoothness ("Smoothness (light highlights)", Range(0, 1)) = 0.9
        _SpecStrength ("Highlight Strength", Range(0, 3)) = 0.8
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "TankGlass"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            // Forward+ light loop (URP 17 keyword; older versions called it _FORWARD_PLUS)
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half4 _ReflectColor;
                half _Alpha;
                half _EdgeAlpha;
                half _FresnelPower;
                half _StreakPower;
                half _StreakIntensity;
                half _Smoothness;
                half _SpecStrength;
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
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // Blinn-Phong highlight for one light
            half3 Highlight(Light light, half3 normalWS, half3 viewWS, half shininess)
            {
                half3 halfway = normalize(light.direction + viewWS);
                half spec = pow(saturate(dot(normalWS, halfway)), shininess);
                return light.color * (spec * light.distanceAttenuation);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewWS)), _FresnelPower);

                // --- faked reflection: two vertical streaks, placed by where the reflected ray points ------
                half3 reflected = reflect(-viewWS, normalWS);
                half2 around = normalize(reflected.xz + 0.0001);
                half streakA = pow(saturate(dot(around, half2(0.83, 0.56))), _StreakPower);
                half streakB = pow(saturate(dot(around, half2(-0.6, 0.8))), _StreakPower * 2.0) * 0.6;
                half streaks = (streakA + streakB) * _StreakIntensity;

                // --- highlights from the lights around the tank ---------------------------------------------
                half shininess = exp2(10.0 * _Smoothness + 1.0);
                half3 spec = 0;
                #if defined(_ADDITIONAL_LIGHTS)
                // LIGHT_LOOP_BEGIN reads these two fields from a variable called inputData to find this pixel's cluster
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    spec += Highlight(GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1)), normalWS, viewWS, shininess);
                LIGHT_LOOP_END
                #endif
                spec *= _SpecStrength;

                half3 colour = _Tint.rgb * (0.15 + fresnel) + _ReflectColor.rgb * (streaks + fresnel * 0.35) + spec;
                half alpha = saturate(lerp(_Alpha, _EdgeAlpha, fresnel) + streaks + dot(spec, half3(0.33, 0.33, 0.33)));
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
