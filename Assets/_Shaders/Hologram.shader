// TimeTrap/Hologram
// ---------------------------------------------------------------------------------------------------------------
// Dr Birkin in the intro cutscene: a man made of light. Goes on every part of his body (one material per original
// material, so each keeps its own texture). BirkinHologramFX drives _Reveal, _Flicker and _GlitchAmount through a
// MaterialPropertyBlock.
//
// Vertex shader:
//   - glitch: the body is cut into horizontal world-space bands; a few random bands jump sideways (along the camera's
//     right vector) and stay there for a few frames before a new set is picked
// Fragment shader:
//   - colour: _Color, with some of the texture's brightness (its luminance) kept so the face still reads
//   - rim: a fresnel edge glow, brightest where the surface turns away from the camera
//   - scanlines: horizontal lines in world space that scroll upward
//   - flicker: the whole figure dips in brightness at random moments
//   - reveal: everything above the reveal line is cut away (clip), and the line itself glows; _Reveal 0 -> 1
//     materialises him from the feet up
//
// Two passes:
//   HologramDepth   writes depth only (no colour). A "Render Objects" renderer feature on PC_Renderer draws this pass
//                   for every hologram renderer BEFORE the transparent objects, so the nearest surface of the whole
//                   figure is in the depth buffer first.
//   Hologram        the colour, alpha blended. With the depth already there, only the nearest surface passes the
//                   depth test: no back faces, no teeth through the cheeks, no arm seen through the chest.
// Both passes move the vertices with the same function, so their depths match exactly.
Shader "TimeTrap/Hologram"
{
    Properties
    {
        _BaseMap ("Albedo (its brightness is kept as detail)", 2D) = "white" {}
        [HDR] _Color ("Hologram Colour", Color) = (0.25, 1.3, 1.6, 1)
        _DetailStrength ("Detail kept from the albedo", Range(0, 1)) = 0.75
        _Alpha ("Opacity", Range(0, 1)) = 0.6
        _Cutoff ("Alpha Cutoff (hair cards; 0 = off)", Range(0, 1)) = 0

        [Header(Rim)]
        _RimPower ("Rim Power (higher = thinner)", Range(0.5, 8)) = 2.2
        _RimIntensity ("Rim Intensity", Range(0, 6)) = 1.8

        [Header(Scanlines)]
        _ScanDensity ("Scanlines per metre", Range(10, 400)) = 120
        _ScanSpeed ("Scroll Speed (lines per second, upward)", Range(0, 40)) = 6
        _ScanStrength ("Scanline Strength", Range(0, 1)) = 0.35

        [Header(Flicker and glitch. Driven by BirkinHologramFX)]
        _Flicker ("Flicker", Range(0, 1)) = 0.15
        _FlickerSpeed ("Flicker Speed (changes per second)", Range(1, 60)) = 18
        _GlitchAmount ("Glitch Amount", Range(0, 1)) = 0
        _GlitchBands ("Glitch Bands per metre", Range(2, 60)) = 14
        _GlitchShift ("Glitch Shift (m)", Range(0, 0.6)) = 0.18

        [Header(Reveal. Driven by BirkinHologramFX)]
        _Reveal ("Reveal", Range(0, 1)) = 1
        _RevealMinY ("Feet height (world Y)", Float) = 0
        _RevealHeight ("Figure height (m)", Float) = 1.9
        _RevealEdgeWidth ("Reveal Edge Width (fraction of height)", Range(0.005, 0.2)) = 0.04
        [HDR] _EdgeColor ("Reveal Edge Colour", Color) = (1.2, 3.5, 4, 1)

        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (Off for hair cards)", Float) = 2
        [Toggle] _ZWrite ("Write depth in the pre-pass (off for the glasses lens)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _Color;
            half4 _EdgeColor;
            half _DetailStrength;
            half _Alpha;
            half _Cutoff;
            half _RimPower;
            half _RimIntensity;
            half _ScanDensity;
            half _ScanSpeed;
            half _ScanStrength;
            half _Flicker;
            half _FlickerSpeed;
            half _GlitchAmount;
            half _GlitchBands;
            half _GlitchShift;
            half _Reveal;
            float _RevealMinY;
            float _RevealHeight;
            half _RevealEdgeWidth;
            half _Cull;
            half _ZWrite;
        CBUFFER_END

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
            float3 positionWS : TEXCOORD1;
            half3  normalWS   : TEXCOORD2;
            float  height01   : TEXCOORD3;   // 0 at the feet, 1 at the top of the head (before the glitch moves anything)
        };

        // a pseudo-random number 0-1 from two numbers
        float Hash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        // Glitch: which horizontal band is this vertex in, and is that band displaced right now?
        float3 GlitchOffset(float3 positionWS)
        {
            // the time is cut into steps of 1/14 s, so an offset holds for a few frames and then jumps
            float tick = floor(_Time.y * 14.0);
            float band = floor(positionWS.y * _GlitchBands);
            float chance = Hash(float2(band, tick));
            // more bands join in as the glitch gets stronger
            float on = step(1.0 - 0.55 * _GlitchAmount, chance);
            float shift = (Hash(float2(tick, band) + 7.3) - 0.5) * 2.0 * _GlitchShift * _GlitchAmount;
            // sideways as the camera sees it
            return UNITY_MATRIX_V[0].xyz * (on * shift);
        }

        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.height01 = (positionWS.y - _RevealMinY) / max(_RevealHeight, 0.001);
            positionWS += GlitchOffset(positionWS);

            output.positionWS = positionWS;
            output.positionCS = TransformWorldToHClip(positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            return output;
        }

        // How far below the reveal line this pixel is (negative = above it, not materialised yet).
        // The line runs a little past the top so that _Reveal = 1 shows everything.
        half RevealDistance(float height01)
        {
            return _Reveal * (1.0 + _RevealEdgeWidth) - height01;
        }
        ENDHLSL

        // ---------------------------------------------------------------------------------------------------
        // Pass 1: depth only. Drawn by the "Hologram Depth" Render Objects feature, before the transparents.
        Pass
        {
            Name "HologramDepth"
            Tags { "LightMode" = "HologramDepth" }

            ZWrite [_ZWrite]
            ZTest LEqual
            ColorMask 0          // no colour at all
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth

            half4 FragDepth(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                clip(alpha - _Cutoff);
                clip(RevealDistance(input.height01));
                return 0;
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------------------------------------------
        // Pass 2: the hologram itself.
        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual         // equal to the pre-pass depth on the nearest surface, behind it everywhere else
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(albedo.a - _Cutoff);

                // --- reveal: cut away everything above the line -------------------------------------------
                half below = RevealDistance(input.height01);
                clip(below);
                // the line glows; once he is fully there the glow is gone
                half edge = (1.0 - smoothstep(0.0, _RevealEdgeWidth, below)) * step(_Reveal, 0.999);

                // --- colour: cyan, with the albedo's brightness kept as detail -------------------------------
                half luminance = dot(albedo.rgb, half3(0.299, 0.587, 0.114));
                half detail = lerp(1.0, saturate(luminance * 2.2 + 0.15), _DetailStrength);
                half3 colour = _Color.rgb * detail;

                // --- rim: fresnel ---------------------------------------------------------------------------
                half3 normalWS = normalize(input.normalWS) * (frontFace ? 1.0 : -1.0);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rim = pow(1.0 - saturate(dot(normalWS, viewWS)), _RimPower);
                colour += _Color.rgb * (rim * _RimIntensity);

                // --- scanlines: horizontal in world space, scrolling upward --------------------------------
                half scan = 0.5 + 0.5 * sin((input.positionWS.y * _ScanDensity - _Time.y * _ScanSpeed) * TWO_PI);
                colour *= lerp(1.0, 0.35 + 1.1 * scan, _ScanStrength);

                // --- flicker: random dips in brightness ---------------------------------------------------------
                half dip = Hash(float2(floor(_Time.y * _FlickerSpeed), 3.1));
                half shimmer = 0.5 + 0.5 * sin(_Time.y * 37.0);
                half flicker = 1.0 - _Flicker * (0.65 * step(0.72, dip) + 0.2 * shimmer * dip);
                // a strong glitch tears the brightness too
                flicker *= 1.0 - 0.35 * _GlitchAmount * step(0.5, Hash(float2(floor(input.positionWS.y * _GlitchBands), floor(_Time.y * 14.0))));

                half alpha = saturate(_Alpha * (0.6 + 0.4 * scan) + rim * _RimIntensity * 0.35) * flicker;
                colour *= flicker;

                colour = lerp(colour, _EdgeColor.rgb, edge);
                alpha = saturate(alpha + edge);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
