// TimeTrap/FlashlightBeam
// ---------------------------------------------------------------------------------------------------------------
// The visible shaft of light in front of the torch (and the pistol light). Drawn on a cone mesh that
// FlashlightBeamCone builds to match the spot light: apex at the light, axis along +Z, same angle and range.
//
// The player looks ALONG the beam from just behind it, so shading the cone's surface would show almost nothing.
// Instead the cone is treated as a volume of lit dust. For each pixel the fragment shader walks along the view ray
// in a few steps and adds up how much lit dust the ray passes through:
//   - brighter near the torch, fading along the cone's length
//   - brighter near the axis, fading to nothing at the cone's edge (same inner/outer angle as the spot light)
//   - a scrolling noise texture thins and thickens it, like drifting dust
// The ray stops at whatever the scene's depth texture says is in front of the camera, so where the beam meets a wall
// or the floor the amount of dust in front of it shrinks smoothly to zero: a soft fade, never a hard cut line.
//
// Blending is additive (the beam only ever adds light), with no depth write.
Shader "TimeTrap/FlashlightBeam"
{
    Properties
    {
        [HDR] _Color ("Beam Colour", Color) = (1, 0.97, 0.9, 1)
        _Intensity ("Intensity (set by script)", Range(0, 1)) = 1
        _Density ("Dust Density", Range(0, 0.5)) = 0.04

        [Header(Cone shape. Set by FlashlightBeamCone)]
        _Range ("Range (m)", Float) = 25
        _TanOuter ("Tan of outer half-angle", Float) = 0.52
        _TanInner ("Tan of inner half-angle", Float) = 0.315

        [Header(Falloff)]
        _LengthFalloff ("Falloff along the beam", Range(0, 2)) = 0.25
        _NearFade ("Fade-in distance from the lens (m)", Range(0.01, 2)) = 0.5
        _ViewFalloff ("Fade with distance from the camera (fog)", Range(0, 0.5)) = 0.07

        [Header(Dust)]
        _NoiseTex ("Dust Noise", 2D) = "gray" {}
        _NoiseScale ("Noise Tiling (per metre)", Float) = 0.35
        _NoiseScroll ("Noise Scroll (xy speed)", Vector) = (0.03, 0.02, 0, 0)
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One      // additive: result = beam + what is already on screen
            ZWrite Off         // transparent: never hides what is behind it
            ZTest Always       // depth is tested by hand against the depth texture (see Frag)
            Cull Front         // draw the cone's far side, so it is still drawn when the camera is inside the cone

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _NoiseScroll;
                half _Intensity;
                half _Density;
                float _Range;
                float _TanOuter;
                float _TanInner;
                half _LengthFalloff;
                half _NearFade;
                half _ViewFalloff;
                half _NoiseScale;
                half _NoiseStrength;
            CBUFFER_END

            #define STEPS 12

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            // How much lit dust there is at a point, in the cone's own space (apex at 0, axis along +Z).
            half DustAt(float3 p)
            {
                float along = p.z;                                   // distance down the beam
                float radius = length(p.xy);                         // distance from the axis
                float coneRadius = max(along, 0.0001) * _TanOuter;   // the cone's radius at this distance
                float innerRadius = max(along, 0.0001) * _TanInner;

                // edge falloff: full inside the inner cone, zero at the outer cone
                half edge = 1.0 - smoothstep(innerRadius, coneRadius, radius);
                // length falloff: light spreads out, so it thins with the square of the distance,
                // and reaches exactly zero at the light's range
                half reach = saturate(1.0 - along / _Range);
                half lengthFade = reach * reach / (1.0 + _LengthFalloff * along * along);
                // nothing right at the lens (and nothing behind the apex, where along is negative)
                half nearFade = smoothstep(0.0, _NearFade, along);

                // drifting dust: one noise lookup, scrolling slowly
                float2 noiseUV = (p.xy + p.z * float2(0.37, 0.21)) * _NoiseScale + _Time.y * _NoiseScroll.xy;
                half noise = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, noiseUV, 0).r;
                half dust = lerp(1.0, noise * 2.0, _NoiseStrength);

                return edge * lengthFade * nearFade * dust;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // --- the view ray through this pixel --------------------------------------------------------
                float3 cameraWS = GetCameraPositionWS();
                float3 toSurface = input.positionWS - cameraWS;      // camera -> far side of the cone
                float surfaceDistance = length(toSurface);
                float3 rayDirWS = toSurface / surfaceDistance;

                // --- soft depth: how far the ray can go before it hits the scene ----------------------------
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                // eye depth is measured along the camera's forward axis; convert it to a distance along this ray
                float3 cameraForward = -UNITY_MATRIX_V[2].xyz;
                float sceneDistance = sceneEyeDepth / max(dot(rayDirWS, cameraForward), 0.0001);

                // the ray ends at the cone's far side or the wall, whichever comes first
                float rayLength = min(surfaceDistance, sceneDistance);

                // --- march the ray in the cone's own space ------------------------------------------------
                float3 originOS = TransformWorldToObject(cameraWS);
                float3 rayDirOS = TransformWorldToObjectDir(rayDirWS);

                // start each pixel's ray at a slightly different offset; this turns banding into fine grain
                float jitter = InterleavedGradientNoise(input.positionCS.xy, 0);

                half dust = 0;
                for (int i = 0; i < STEPS; i++)
                {
                    // u runs 0..1 along the ray. Squaring it bunches the samples up near the camera, where the
                    // beam is narrow and bright and changes quickly; far away, where it is faint, steps are long.
                    float u = (i + jitter) / STEPS;
                    float distance01 = u * u;
                    float t = rayLength * distance01;
                    float weight = 2.0 * u;                          // how much of the ray this sample stands for
                    // light scattered far away is partly lost in the fog on its way back to the eye
                    float throughFog = exp(-_ViewFalloff * t);
                    dust += DustAt(originOS + rayDirOS * t) * weight * throughFog;
                }
                // average of the samples x the ray's length = total dust along the ray
                dust *= (rayLength / STEPS) * _Density;

                half3 colour = _Color.rgb * (dust * _Intensity);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
