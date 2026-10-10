// TimeTrap/ZombieSurface
// ---------------------------------------------------------------------------------------------------------------
// The skin and clothes of every zombie. A hand-written lit surface shader for URP (Forward+).
//
// Lighting model (all written out below, not URP's PBR):
//   diffuse   half-Lambert      soft wrap-around light, so a zombie never goes pitch black on its unlit side
//   specular  Blinn-Phong       a tight, pale highlight: the wet, sickly sheen
//   rim       fresnel           edges facing away from the viewer glow in the colour of whatever light hits them
//   ambient   spherical harmonics (the scene's ambient probe)
// The main light and every additional light (the torch is an additional spot light) go through the same function.
//
// Effects, each driven by one 0-1 property that ZombieShaderFX sets through a MaterialPropertyBlock:
//   _Dissolve   noise-threshold clip with a glowing cyan edge, the portal's colours (death: erased from time)
//   _HitFlash   white/red emissive pulse (damage)
//   _Freeze     icy tint, cold fresnel rim, frosted specular (Zombie Freeze power-up)
//   _Stun       subtle brightness flicker (Flash Pulse power-up)
//
// Passes: UniversalForward (colour), ShadowCaster, DepthOnly, DepthNormals. The last three repeat the same clip()
// so a dissolving zombie's shadow and depth dissolve with it.
Shader "TimeTrap/ZombieSurface"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)
        _Tint ("Tint (runner)", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
        _SpecMap ("Specular Mask", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0, 1)) = 0.6
        _SpecColor ("Specular Colour", Color) = (0.8, 0.9, 0.75, 1)
        _SpecStrength ("Specular Strength", Range(0, 2)) = 0.35
        _Cutoff ("Alpha Cutoff (0 = off)", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        [Header(Rim)]
        _RimColor ("Rim Colour", Color) = (0.75, 0.9, 0.8, 1)
        _RimPower ("Rim Power (higher = thinner)", Range(0.5, 8)) = 3
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.2

        [Header(Dissolve)]
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _NoiseTex ("Dissolve Noise", 2D) = "gray" {}
        _NoiseScale ("Noise Tiling", Float) = 3
        [HDR] _EdgeColor ("Edge Colour at the cut (core)", Color) = (2.2, 5, 5.5, 1)
        [HDR] _EdgeColorOuter ("Edge Colour further in (outer)", Color) = (0.05, 0.35, 2.2, 1)
        _EdgeWidth ("Edge Width", Range(0.001, 0.3)) = 0.09

        [Header(States)]
        _HitFlash ("Hit Flash", Range(0, 1)) = 0
        [HDR] _HitColor ("Hit Flash Colour", Color) = (1.1, 0.1, 0.06, 1)
        _Freeze ("Freeze", Range(0, 1)) = 0
        [HDR] _FreezeColor ("Freeze Colour", Color) = (0.45, 0.8, 1.4, 1)
        _Stun ("Stun", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        // Shared by every pass: the material's constant buffer and the two clip tests.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);
        TEXTURE2D(_SpecMap);  SAMPLER(sampler_SpecMap);
        TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

        // SRP Batcher: every per-material value lives in this one buffer, identical in all passes.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _Tint;
            half4 _SpecColor;
            half4 _RimColor;
            half4 _EdgeColor;
            half4 _EdgeColorOuter;
            half4 _HitColor;
            half4 _FreezeColor;
            half _BumpScale;
            half _Smoothness;
            half _SpecStrength;
            half _Cutoff;
            half _Cull;
            half _RimPower;
            half _RimStrength;
            half _Dissolve;
            half _NoiseScale;
            half _EdgeWidth;
            half _HitFlash;
            half _Freeze;
            half _Stun;
        CBUFFER_END

        // Dissolve. The noise texture gives every point on the body a value 0-1. Points whose value is below the
        // threshold are cut away; as _Dissolve rises the threshold sweeps up through the noise, eating the body.
        // Returns how far the pixel is above the threshold (small = right at the cut, where the edge glows).
        half DissolveClip(float2 uv)
        {
            half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv * _NoiseScale).r;
            // -_EdgeWidth at 0 (nothing clipped, no edge showing) up to just over 1 (everything clipped)
            half threshold = lerp(-_EdgeWidth, 1.001, _Dissolve);
            half above = noise - threshold;
            clip(above);
            return above;
        }

        // Cut-out for the hair and torn clothes (albedo alpha). A cutoff of 0 never clips.
        void AlphaClip(half alpha)
        {
            clip(alpha - _Cutoff);
        }
        ENDHLSL

        // ------------------------------------------------------------------------------------------------------
        // Colour pass
        // ------------------------------------------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Light and shadow variants URP may ask for.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // Forward+ (clustered) light loop. URP 17.1+ names the keyword _CLUSTER_LIGHT_LOOP; it is what older
            // versions called _FORWARD_PLUS. Without it the torch (an additional light) would not reach this shader.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  tangentWS  : TEXCOORD3;   // w = bitangent sign
                half   fogFactor  : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Everything about the surface that the light function needs.
            struct Surface
            {
                half3 albedo;
                half3 normal;        // world space, normalised
                half3 view;          // world space, surface -> camera
                half3 specColor;
                half  specPower;     // Blinn-Phong exponent
                half  fresnel;       // 0 facing the camera, 1 at grazing angles
            };

            // One light's contribution. This is the lighting model.
            half3 ShadeLight(Light light, Surface s)
            {
                // how much of the light arrives: distance and cone falloff x shadows
                half attenuation = light.distanceAttenuation * light.shadowAttenuation;

                // Half-Lambert: N.L runs -1..1, remap to 0..1 and square it. Light wraps round to the far side.
                half NdotL = dot(s.normal, light.direction);
                half halfLambert = NdotL * 0.5 + 0.5;
                half3 diffuse = s.albedo * (halfLambert * halfLambert);

                // Blinn-Phong: the highlight is strongest where the normal lines up with the half vector
                // (half-way between the light direction and the view direction).
                half3 halfVector = SafeNormalize(light.direction + s.view);
                half NdotH = saturate(dot(s.normal, halfVector));
                half3 specular = s.specColor * pow(NdotH, s.specPower) * saturate(NdotL * 4.0);

                // Rim: fresnel, but multiplied by the light, so the silhouette only glows when the torch is on it.
                half3 rim = _RimColor.rgb * (s.fresnel * _RimStrength);

                return (diffuse + specular + rim) * light.color * attenuation;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                // --- textures -------------------------------------------------------------------------------
                half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                AlphaClip(albedoSample.a * _BaseColor.a);
                half above = DissolveClip(input.uv);

                half3 albedo = albedoSample.rgb * _BaseColor.rgb * _Tint.rgb;
                half specMask = SAMPLE_TEXTURE2D(_SpecMap, sampler_SpecMap, input.uv).r;

                // --- normal map: tangent space -> world space ------------------------------------------------
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                half3 vertexNormal = normalize(input.normalWS);
                half3 tangent = normalize(input.tangentWS.xyz);
                half3 bitangent = cross(vertexNormal, tangent) * input.tangentWS.w;
                half3 normalWS = normalize(normalTS.x * tangent + normalTS.y * bitangent + normalTS.z * vertexNormal);
                // two-sided cloth: flip the normal on back faces
                normalWS = IS_FRONT_VFACE(face, normalWS, -normalWS);

                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnelBase = 1.0 - saturate(dot(normalWS, viewWS));

                // --- freeze changes the surface itself: desaturate toward ice, and make it glossier -----------
                half grey = dot(albedo, half3(0.299, 0.587, 0.114));
                albedo = lerp(albedo, grey * _FreezeColor.rgb, _Freeze * 0.75);
                half smoothness = lerp(_Smoothness, max(_Smoothness, 0.88), _Freeze);
                half specStrength = lerp(_SpecStrength, _SpecStrength * 2.5 + 0.3, _Freeze);

                Surface s;
                s.albedo = albedo;
                s.normal = normalWS;
                s.view = viewWS;
                s.specColor = lerp(_SpecColor.rgb, half3(1, 1, 1), _Freeze) * (specMask * specStrength);
                s.specPower = exp2(10.0 * smoothness + 1.0);   // smoothness 0..1 -> exponent 2..2048
                s.fresnel = pow(fresnelBase, _RimPower);

                // --- lights -----------------------------------------------------------------------------------
                // ambient: the scene's ambient probe, stored as spherical harmonics, looked up by the normal
                half3 colour = SampleSH(normalWS) * albedo;

                // main (directional) light, with its shadow map
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                colour += ShadeLight(mainLight, s);

                #if defined(_ADDITIONAL_LIGHTS)
                // LIGHT_LOOP_BEGIN reads these two fields from a variable called inputData to find which cluster
                // (screen tile + depth slice) this pixel is in, and so which lights can reach it.
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                uint lightCount = GetAdditionalLightsCount();

                #if USE_CLUSTER_LIGHT_LOOP
                // extra directional lights are not in the clusters; they come first in the light list
                for (uint d = 0; d < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); d++)
                {
                    Light dirLight = GetAdditionalLight(d, input.positionWS, half4(1, 1, 1, 1));
                    colour += ShadeLight(dirLight, s);
                }
                #endif

                // every point and spot light that reaches this pixel: the torch, room lights, the portal glow
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    colour += ShadeLight(light, s);
                LIGHT_LOOP_END
                #endif

                // --- stun: a nervous flicker, two sine waves multiplied so it never looks regular ---------------
                half flicker = sin(_Time.y * 37.0) * sin(_Time.y * 23.7);
                colour *= 1.0 + _Stun * 0.22 * flicker;

                // --- emissive terms (add light of their own, so they show in the dark) -------------------------
                half3 emission = 0;
                // freeze: strong cold rim, wider than the normal rim
                emission += _FreezeColor.rgb * (pow(fresnelBase, 2.0) * _Freeze * 1.4);
                // stun: faint pale shimmer on the edges
                emission += half3(0.9, 0.85, 0.5) * (s.fresnel * _Stun * (0.25 + 0.25 * flicker));
                // hit flash: red over the whole body, going white toward the edges
                emission += (_HitColor.rgb + fresnelBase * fresnelBase * 1.5) * _HitFlash;
                // dissolve: a glowing band where the pixel is only just above the clip threshold.
                // edge = 1 right at the cut, 0 one edge-width further in. The colour runs from deep blue (outer)
                // to cyan-white (core) to match the time portal: the zombie is erased from time, not burnt.
                half edge = 1.0 - smoothstep(0.0, _EdgeWidth, above);
                half3 edgeColour = lerp(_EdgeColorOuter.rgb, _EdgeColor.rgb, edge * edge);
                emission += edgeColour * (edge * step(0.0001, _Dissolve));

                colour = MixFog(colour, input.fogFactor) + emission;
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------------------------
        // Shadow pass: draws the body into the shadow maps. Same clips, so the shadow dissolves too.
        // ------------------------------------------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            // spot and point lights (the torch) cast from a position, directional lights along a direction
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif

                // push the vertex slightly away from the light so the surface does not shadow itself (acne)
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                // keep it inside the light's near plane
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                AlphaClip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a);
                DissolveClip(input.uv);
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------------------------
        // Depth pass: fills _CameraDepthTexture, which the beam, dust and portal shaders read.
        // ------------------------------------------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                AlphaClip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a);
                DissolveClip(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------------------------
        // Depth + normals pass: used instead of DepthOnly when a renderer feature (such as SSAO) needs normals.
        // ------------------------------------------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                AlphaClip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a);
                DissolveClip(input.uv);
                return half4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
