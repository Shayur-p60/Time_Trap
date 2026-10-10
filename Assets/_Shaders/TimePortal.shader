// TimeTrap/TimePortal
// ---------------------------------------------------------------------------------------------------------------
// The escape portal at Gate B: a swirling disc of energy that bends the world seen through it.
// Drawn on a flat disc mesh (radius 1, facing +Z, UVs 0-1 across it) with enough vertices to ripple.
//
// Vertex shader:
//   - ripple: a sine wave that travels outward from the centre (height depends on radius and time)
//   - breathe: the whole disc slowly swells and shrinks
//   - _Activate scales the disc up from nothing
// Fragment shader:
//   - polar UVs (angle, radius) instead of the usual (x, y), with the angle twisted more toward the centre: a swirl
//   - two noise layers scrolling at different speeds through those polar UVs: the energy vortex
//   - a bright ring at the rim, a fresnel glow on the ripples, and a cyan core that goes white in the middle
//   - refraction: the already-drawn scene (_CameraOpaqueTexture) is sampled at a screen position pushed around by
//     the noise, so whatever is behind the portal wobbles and distorts
//
// Transparent, alpha blended, no depth write. Needs the Opaque Texture switched on in the URP asset.
Shader "TimeTrap/TimePortal"
{
    Properties
    {
        _Activate ("Activate", Range(0, 1)) = 1

        [Header(Colour)]
        [HDR] _Color ("Energy Colour", Color) = (0.04, 0.5, 0.85, 1)
        [HDR] _CoreColor ("Core Colour", Color) = (1.1, 1.7, 1.8, 1)
        [HDR] _RingColor ("Ring Colour", Color) = (0.15, 1.0, 1.4, 1)
        _CorePower ("Core Size (higher = smaller)", Range(0.5, 8)) = 3.5
        _RingWidth ("Ring Width", Range(0.01, 0.5)) = 0.16
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2

        [Header(Vortex)]
        _NoiseTex ("Noise", 2D) = "gray" {}
        _Twist ("Swirl Twist (radians at the centre)", Range(-12, 12)) = 5
        _SpinSpeed ("Spin Speed", Range(-3, 3)) = 0.5
        _FlowSpeed ("Inward Flow Speed", Range(0, 2)) = 0.3
        _NoiseTiling ("Noise Tiling (around, outward)", Vector) = (3, 1.2, 0, 0)

        [Header(Refraction)]
        _Refraction ("Refraction Strength", Range(0, 0.2)) = 0.08
        _SceneTint ("Tint on the refracted scene", Range(0, 1)) = 0.35
        _SceneBoost ("Brightness of the refracted scene", Range(0.5, 6)) = 3

        [Header(Vertex animation)]
        _RippleAmp ("Ripple Height (m)", Range(0, 0.3)) = 0.05
        _RippleFreq ("Ripple Frequency", Range(0, 40)) = 14
        _RippleSpeed ("Ripple Speed", Range(0, 10)) = 3
        _BreatheAmount ("Breathe Amount", Range(0, 0.2)) = 0.04
        _BreatheSpeed ("Breathe Speed", Range(0, 5)) = 1.2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Portal"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha   // normal alpha blending
            ZWrite Off
            ZTest LEqual
            Cull Off                           // visible from both sides

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _CoreColor;
                half4 _RingColor;
                float4 _NoiseTiling;
                half _Activate;
                half _CorePower;
                half _RingWidth;
                half _FresnelPower;
                half _Twist;
                half _SpinSpeed;
                half _FlowSpeed;
                half _Refraction;
                half _SceneTint;
                half _SceneBoost;
                half _RippleAmp;
                half _RippleFreq;
                half _RippleSpeed;
                half _BreatheAmount;
                half _BreatheSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
            };

            // 0 -> 1 with a soft start and end
            half Ease(half x)
            {
                return x * x * (3.0 - 2.0 * x);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 p = input.positionOS.xyz;
                float radius = length(p.xy);                    // 0 at the centre, 1 at the rim
                float2 outward = p.xy / max(radius, 0.0001);

                half open = Ease(_Activate);

                // --- ripple: a wave travelling outward. Pinned to zero at the rim so the edge stays put. ------
                float pin = 1.0 - radius * radius;
                float wavePhase = radius * _RippleFreq - _Time.y * _RippleSpeed;
                float amplitude = _RippleAmp * pin * open;
                p.z += sin(wavePhase) * amplitude;

                // The disc is no longer flat, so its normal tilts. The slope of sin is cos: the normal leans
                // against the slope, along the outward direction.
                float slope = cos(wavePhase) * _RippleFreq * amplitude;
                float3 normalOS = normalize(float3(-outward * slope, 1.0));

                // --- breathe and grow --------------------------------------------------------------------------
                float breathe = 1.0 + sin(_Time.y * _BreatheSpeed) * _BreatheAmount;
                p.xy *= breathe * open;

                output.positionWS = TransformObjectToWorld(p);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half open = Ease(_Activate);

                // --- polar coordinates: where is this pixel as (angle, radius) from the disc's centre? ---------
                float2 centred = input.uv * 2.0 - 1.0;          // -1..1 across the disc
                float radius = length(centred);
                float angle = atan2(centred.y, centred.x);      // -PI..PI

                // swirl: turn the angle further the closer to the centre, and spin the whole thing over time
                angle += (1.0 - radius) * _Twist + _Time.y * _SpinSpeed;
                float2 polarUV = float2(angle / TWO_PI, radius);

                // --- two noise layers flowing through the polar UVs -------------------------------------------
                // The tiling around the disc is rounded to a whole number so the texture meets itself at the seam.
                float2 tiling = float2(round(_NoiseTiling.x), _NoiseTiling.y);
                float flow = _Time.y * _FlowSpeed;
                float2 uvA = polarUV * tiling + float2(0.0, flow);
                float2 uvB = polarUV * tiling * float2(2.0, 1.7) + float2(0.31, flow * 1.7);
                // explicit mip level 0: the angle jumps at the seam, which would otherwise pick a blurry mip there
                half noiseA = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, uvA, 0).r;
                half noiseB = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, uvB, 0).g;
                half vortex = noiseA * noiseB * 2.0;            // streaks where both layers are bright
                vortex = smoothstep(0.4, 1.0, vortex);

                // --- refraction: read the scene behind, at a screen position pushed by the noise -------------
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float2 push = (float2(noiseA, noiseB) - 0.5) * (_Refraction * open);
                half3 scene = SampleSceneColor(screenUV + push);
                // brighten it (the portal lights what is behind it) and tint it toward the energy colour
                scene *= _SceneBoost;
                scene = lerp(scene, scene * _Color.rgb * 2.0, _SceneTint);

                // --- glow terms ------------------------------------------------------------------------------------
                // core: brightest at the centre, cyan fading up to white
                half core = pow(saturate(1.0 - radius), _CorePower);
                half3 coreGlow = lerp(_Color.rgb, _CoreColor.rgb, core) * core;

                // ring: a band just inside the rim, flickering a little with the noise
                half ring = smoothstep(1.0 - _RingWidth * 2.0, 1.0 - _RingWidth, radius) * (1.0 - smoothstep(1.0 - _RingWidth * 0.5, 1.0, radius));
                half3 ringGlow = _RingColor.rgb * (ring * (0.7 + 0.6 * noiseA));

                // fresnel: ripples that tilt away from the viewer catch the light
                half3 normalWS = normalize(input.normalWS);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = pow(1.0 - saturate(abs(dot(normalWS, viewWS))), _FresnelPower);
                half3 fresnelGlow = _Color.rgb * fresnel;

                half3 energy = _Color.rgb * vortex + coreGlow + ringGlow + fresnelGlow;

                // while it opens: a white flash that peaks half-way and is gone when fully open
                energy += _CoreColor.rgb * (open * (1.0 - open));

                half3 colour = scene + energy * open;

                // soft outer edge, and fully transparent while closed
                half alpha = (1.0 - smoothstep(0.93, 1.0, radius)) * saturate(_Activate * 4.0);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
