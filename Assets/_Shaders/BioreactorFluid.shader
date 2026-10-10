// TimeTrap/BioreactorFluid
// ---------------------------------------------------------------------------------------------------------------
// The toxic green liquid in the Bioreactors room. Two materials use it:
//   BioreactorFluid_Pit    the pool on the pit floor: waves, depth colour, foam where it meets walls and machines
//   BioreactorFluid_Tank   the sight-glass strip on each culture tank: no waves, opaque, bubbles rising
//
// Vertex shader:
//   - three sine waves travelling in different directions are added together (sum of sines)
//   - the normal is rebuilt from the waves' slopes (their derivatives), so light reacts to the moving surface
// Fragment shader:
//   - depth colour: the depth texture says how far the floor is behind the surface; thicker liquid is darker and
//     more opaque
//   - foam: where that thickness is nearly zero the surface is touching something, so a foam line is drawn
//   - lighting: Blinn-Phong specular from the main light and every additional light (Forward+ light loop)
//   - fresnel: at grazing angles the surface shows a reflection instead of the liquid's own colour
//   - bubbles: two noise layers scrolling upward (tanks) or drifting (pool)
//   - a soft emissive glow, so the liquid is visible with the lights out
//
// Transparent, alpha blended, no depth write: it must not be in the depth texture it reads.
Shader "TimeTrap/BioreactorFluid"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow Colour", Color) = (0.35, 0.75, 0.12, 1)
        _DeepColor ("Deep Colour", Color) = (0.02, 0.12, 0.03, 1)
        _DepthDensity ("Depth Density (per metre)", Range(0, 8)) = 2.2
        _ShallowAlpha ("Opacity where shallow", Range(0, 1)) = 0.45
        _MinOpacity ("Minimum Opacity", Range(0, 1)) = 0

        [Header(Glow)]
        [HDR] _GlowColor ("Glow Colour", Color) = (0.25, 0.9, 0.1, 1)
        _Glow ("Glow Strength", Range(0, 3)) = 0.35

        [Header(Waves. xy is direction and z is wavelength m and w is height m)]
        _WaveA ("Wave A", Vector) = (1, 0.3, 5.5, 0.035)
        _WaveB ("Wave B", Vector) = (-0.4, 1, 3.1, 0.022)
        _WaveC ("Wave C", Vector) = (0.6, -0.8, 1.7, 0.012)
        _WaveSpeed ("Wave Speed", Range(0, 3)) = 0.6
        _WaveHeight ("Wave Height (multiplier)", Range(0, 4)) = 1

        [Header(Surface)]
        _NoiseTex ("Noise", 2D) = "gray" {}
        _RippleScale ("Ripple Tiling (per metre)", Float) = 0.35
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.25
        _Smoothness ("Smoothness", Range(0, 1)) = 0.85
        _SpecStrength ("Specular Strength", Range(0, 3)) = 1
        _ReflectColor ("Reflection Colour", Color) = (0.25, 0.4, 0.22, 1)
        _FresnelPower ("Fresnel Power", Range(1, 8)) = 4

        [Header(Bubbles)]
        [HDR] _BubbleColor ("Bubble Colour", Color) = (0.7, 1.4, 0.35, 1)
        _BubbleScale ("Bubble Tiling (per metre)", Float) = 2.5
        _BubbleSpeed ("Bubble Rise Speed (m per s)", Range(0, 2)) = 0.35
        _BubbleAmount ("Bubble Amount", Range(0, 1)) = 0.2

        [Header(Foam)]
        _FoamColor ("Foam Colour", Color) = (0.75, 0.95, 0.55, 1)
        _FoamWidth ("Foam Width (m of depth)", Range(0.01, 1)) = 0.18
        _FoamAmount ("Foam Amount", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Fluid"
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
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _GlowColor;
                float4 _WaveA;
                float4 _WaveB;
                float4 _WaveC;
                half4 _ReflectColor;
                half4 _BubbleColor;
                half4 _FoamColor;
                half _DepthDensity;
                half _ShallowAlpha;
                half _MinOpacity;
                half _Glow;
                half _WaveSpeed;
                half _WaveHeight;
                half _RippleScale;
                half _RippleStrength;
                half _Smoothness;
                half _SpecStrength;
                half _FresnelPower;
                half _BubbleScale;
                half _BubbleSpeed;
                half _BubbleAmount;
                half _FoamWidth;
                half _FoamAmount;
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
                half   upness     : TEXCOORD2;   // 1 on a flat surface facing up (the pool), 0 on a vertical one (a tank)
                float  eyeDepth   : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
            };

            // One sine wave. wave.xy = direction, wave.z = wavelength, wave.w = height.
            // Adds its height to 'height' and its slope along x and z to 'slope'.
            void AddWave(float4 wave, float2 xz, inout float height, inout float2 slope)
            {
                float2 direction = normalize(wave.xy);
                float k = TWO_PI / wave.z;                                    // waves per metre, in radians
                float phase = dot(direction, xz) * k + _Time.y * _WaveSpeed * k;
                float amplitude = wave.w * _WaveHeight;
                height += amplitude * sin(phase);
                // the derivative of sin is cos: this is how steeply the surface tilts here
                slope += direction * (amplitude * k * cos(phase));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                half3 meshNormalWS = TransformObjectToWorldNormal(input.normalOS);
                half upness = saturate(meshNormalWS.y);

                // --- sum of sines ------------------------------------------------------------------------------
                float height = 0.0;
                float2 slope = 0.0;
                AddWave(_WaveA, positionWS.xz, height, slope);
                AddWave(_WaveB, positionWS.xz, height, slope);
                AddWave(_WaveC, positionWS.xz, height, slope);
                positionWS.y += height * upness;

                // --- normal from the slopes: a surface y = h(x, z) has the normal (-dh/dx, 1, -dh/dz) ----------
                half3 waveNormalWS = normalize(half3(-slope.x, 1.0, -slope.y));

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = lerp(meshNormalWS, waveNormalWS, upness);
                output.upness = upness;
                output.eyeDepth = -TransformWorldToView(positionWS).z;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Blinn-Phong highlight plus a little diffuse from one light.
            void AddLight(Light light, half3 normalWS, half3 viewWS, half specPower, inout half3 diffuse, inout half3 specular)
            {
                half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation);
                half NdotL = saturate(dot(normalWS, light.direction));
                diffuse += radiance * NdotL;
                half3 halfVector = SafeNormalize(light.direction + viewWS);
                specular += radiance * (pow(saturate(dot(normalWS, halfVector)), specPower) * NdotL);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half upness = input.upness;
                float time = _Time.y;

                // --- two noise lookups, reused for ripples, bubbles and the foam edge ------------------------
                // on the pool the noise lies flat (x, z) and drifts; on a tank it faces sideways and scrolls up
                float2 flatUV = input.positionWS.xz;
                float2 sideUV = float2(input.positionWS.x + input.positionWS.z, input.positionWS.y - time * _BubbleSpeed);
                half rippleA = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, flatUV * _RippleScale + time * float2(0.021, 0.013)).r;
                half rippleB = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, flatUV * _RippleScale * 1.9 - time * float2(0.017, 0.024)).g;

                // --- normal: wave normal from the vertex shader + fine ripples from the noise ----------------
                half3 normalWS = normalize(input.normalWS);
                normalWS = normalize(normalWS + half3(rippleA - 0.5, 0.0, rippleB - 0.5) * (_RippleStrength * upness));
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // --- depth: how much liquid is there between the surface and whatever is behind it? ----------
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float thickness = max(sceneEyeDepth - input.eyeDepth, 0.0);
                // Beer's law: light is absorbed exponentially, so colour saturates with thickness
                half depth01 = 1.0 - exp(-thickness * _DepthDensity);
                half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);
                half alpha = max(lerp(_ShallowAlpha, 1.0, depth01), _MinOpacity);

                // --- lights -----------------------------------------------------------------------------------
                half specPower = exp2(10.0 * _Smoothness + 1.0);
                half3 diffuse = SampleSH(normalWS);          // ambient
                half3 specular = 0;

                AddLight(GetMainLight(), normalWS, viewWS, specPower, diffuse, specular);

                #if defined(_ADDITIONAL_LIGHTS)
                // LIGHT_LOOP_BEGIN reads these two fields from a variable called inputData to find this pixel's cluster
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                uint lightCount = GetAdditionalLightsCount();

                #if USE_CLUSTER_LIGHT_LOOP
                for (uint d = 0; d < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); d++)
                    AddLight(GetAdditionalLight(d, input.positionWS, half4(1, 1, 1, 1)), normalWS, viewWS, specPower, diffuse, specular);
                #endif

                LIGHT_LOOP_BEGIN(lightCount)
                    AddLight(GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1)), normalWS, viewWS, specPower, diffuse, specular);
                LIGHT_LOOP_END
                #endif

                // --- bubbles -----------------------------------------------------------------------------------
                float2 bubbleUV = lerp(sideUV, flatUV + time * float2(0.03, -0.02), upness) * _BubbleScale;
                half bubbleA = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, bubbleUV).r;
                half bubbleB = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, bubbleUV * 2.3 + float2(0.37, time * -0.05)).g;
                // only where both layers are high at once: small bright blobs. More of them as _BubbleAmount rises.
                half bubbleThreshold = lerp(0.8, 0.35, _BubbleAmount);
                half bubbles = smoothstep(bubbleThreshold, bubbleThreshold + 0.08, bubbleA * bubbleB);

                // --- fresnel: reflection takes over at grazing angles ---------------------------------------------
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewWS)), _FresnelPower);
                half3 reflection = GlossyEnvironmentReflection(reflect(-viewWS, normalWS), 1.0 - _Smoothness, 1.0) + _ReflectColor.rgb;

                // --- put it together -------------------------------------------------------------------------------
                // the glow is dimmer where the liquid is thick, so depth still reads with the lights out
                half3 glow = _GlowColor.rgb * (_Glow * (0.8 + 0.4 * rippleA) * lerp(1.0, 0.3, depth01));
                half3 colour = body * diffuse + glow + _BubbleColor.rgb * bubbles;
                colour = lerp(colour, reflection, fresnel * 0.6);
                colour += specular * _SpecStrength;
                alpha = saturate(alpha + fresnel * 0.4 + bubbles * 0.5);

                // --- foam: where the liquid is thinner than the foam width, it is touching something ---------------
                half foamEdge = thickness + (rippleB - 0.5) * _FoamWidth * 0.9;      // ragged edge
                half foam = (1.0 - smoothstep(0.0, _FoamWidth, foamEdge)) * upness * _FoamAmount;
                colour = lerp(colour, _FoamColor.rgb * (diffuse + _Glow * 1.6), foam);
                alpha = max(alpha, foam);

                colour = MixFog(colour, input.fogFactor);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
