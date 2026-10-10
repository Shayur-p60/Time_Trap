// TimeTrap/TimeDistortionFullscreen
// ---------------------------------------------------------------------------------------------------------------
// A full-screen post-processing effect. It is drawn by a URP "Full Screen Pass Renderer Feature" on PC_Renderer,
// after URP's own post-processing (so it sits on top of the TT_PostFX volume without touching it) and before the UI.
// The feature hands this shader the finished frame as _BlitTexture; the fragment shader reads it back, moves the
// read position around, and recolours the result.
//
// One shader, five states. Each state is one 0-1 number that the ScreenFX script sets as a GLOBAL shader property:
//   _Glitch   caught / respawning   time-rewind glitch: RGB split, sliced rows, scanlines, brief desaturation
//   _Pulse    low time              red vignette that beats (faster as time runs out) + slight barrel distortion
//   _Frost    Zombie Freeze         cold blue tint, frosted noise creeping in from the screen edges
//   _Flash    Flash Pulse           quick white bloom-out
//   _Danger   zombie chasing near   subtle edge darkening and desaturation
// When all five are 0, ScreenFX switches the renderer feature off, so the effect costs nothing while idle.
Shader "TimeTrap/TimeDistortionFullscreen"
{
    Properties
    {
        _NoiseTex ("Noise (frost)", 2D) = "gray" {}

        [Header(Glitch)]
        _GlitchSplit ("RGB split (fraction of screen width)", Range(0, 0.05)) = 0.012
        _GlitchSlices ("Rows the screen is cut into", Range(4, 80)) = 26
        _GlitchSliceShift ("Row shift (fraction of screen width)", Range(0, 0.2)) = 0.07
        _GlitchScanlines ("Scanline strength", Range(0, 1)) = 0.35

        [Header(Low time pulse)]
        _PulseColor ("Vignette colour", Color) = (0.85, 0.04, 0.02, 1)
        _PulseBarrel ("Barrel distortion", Range(0, 0.4)) = 0.1

        [Header(Frost)]
        _FrostColor ("Frost colour", Color) = (0.62, 0.86, 1, 1)
        _FrostReach ("How far frost creeps in", Range(0, 1)) = 0.2

        [Header(Danger)]
        _DangerDarken ("Edge darkening", Range(0, 1)) = 0.5
        _DangerDesaturate ("Desaturation", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "TimeDistortion"

            // a full-screen pass: no depth, no culling, and it replaces the pixel outright
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert          // Blit.hlsl: draws one triangle that covers the screen
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _PulseColor;
                half4 _FrostColor;
                half _GlitchSplit;
                half _GlitchSlices;
                half _GlitchSliceShift;
                half _GlitchScanlines;
                half _PulseBarrel;
                half _FrostReach;
                half _DangerDarken;
                half _DangerDesaturate;
            CBUFFER_END

            // Globals from ScreenFX (not per-material, so they stay outside the CBUFFER).
            half _Glitch;
            half _Pulse;
            half _Frost;
            half _Flash;
            half _Danger;
            float _PulsePhase;        // radians; ScreenFX advances it faster as time runs out
            float4 _ScreenFxClock;    // x = game time (stops while paused), y = real time (never stops)

            // A repeatable random number 0-1 from a 2D position.
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            half3 SampleScreen(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 fromCentre = uv - 0.5;
                // 0 in the middle of the screen, 1 in the corners
                half edge = length(fromCentre) * 1.414;

                // --- low time: the heartbeat ------------------------------------------------------------------
                // a sine wave raised to a power is flat most of the time with a short sharp peak: a beat
                half beat = pow(saturate(0.5 + 0.5 * sin(_PulsePhase)), 4.0);
                half pulse = _Pulse * (0.35 + 0.65 * beat);

                // barrel distortion: read closer to the centre the further out the pixel is, so the middle bulges
                uv = 0.5 + fromCentre * (1.0 - _PulseBarrel * pulse * dot(fromCentre, fromCentre) * 4.0);

                // --- glitch: cut the screen into rows and slide some of them sideways -------------------------
                // the rows are re-rolled 24 times a second, on real time so it still plays over an end screen
                float tick = floor(_ScreenFxClock.y * 24.0);
                float row = floor(uv.y * _GlitchSlices);
                float moved = step(0.55, Hash(float2(tick, row * 1.7)));          // about half the rows move
                float shift = (Hash(float2(row, tick)) - 0.5) * moved;
                uv.x += shift * _GlitchSliceShift * _Glitch;

                // --- RGB split (chromatic aberration): read red, green and blue from three different places ---
                float split = _GlitchSplit * _Glitch;
                half3 colour;
                colour.r = SampleScreen(uv + float2(split, 0.0)).r;
                colour.g = SampleScreen(uv).g;
                colour.b = SampleScreen(uv - float2(split, 0.0)).b;

                // scanlines: every other pixel row is dimmed, flickering
                half scanline = 0.5 + 0.5 * sin(uv.y * _ScreenParams.y * PI);
                half flicker = 0.6 + 0.4 * Hash(float2(tick, 3.0));
                colour *= 1.0 - _Glitch * _GlitchScanlines * scanline * flicker;

                // --- desaturation: glitch (brief) and danger (while a zombie is close) ---------------------------
                half grey = dot(colour, half3(0.299, 0.587, 0.114));
                colour = lerp(colour, grey.xxx, saturate(_Glitch * 0.8 + _Danger * _DangerDesaturate));

                // --- danger: darken toward the edges ------------------------------------------------------------
                half vignette = smoothstep(0.4, 1.0, edge);
                colour *= 1.0 - vignette * _Danger * _DangerDarken;

                // --- low time: red vignette, strongest on the beat ----------------------------------------------
                half redVignette = smoothstep(0.3, 1.0, edge);
                colour = lerp(colour, _PulseColor.rgb * (grey * 0.6 + 0.3), redVignette * pulse);

                // --- frost: blue tint everywhere, icy noise creeping in from the edges ----------------------------
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 frostUV = float2(uv.x * aspect, uv.y);
                // distance to the nearest screen edge: 0 in the middle, 1 on any edge
                half border = max(abs(fromCentre.x), abs(fromCentre.y)) * 2.0;
                // two noise lookups at different sizes and angles, multiplied, so the texture's repeat does not show
                half blotches = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, frostUV * 0.9).r;
                half crystalA = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, frostUV * 2.3 + _ScreenFxClock.x * 0.004).g;
                half crystalB = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(frostUV.y, -frostUV.x) * 3.7 + 0.31).r;
                half crystals = saturate(crystalA * crystalB * 2.2);
                // the frost line: it starts at the edge and creeps inward as _Frost rises, ragged with the noise
                half frostLine = 1.0 - _FrostReach * _Frost;
                half frostMask = saturate((border + (blotches - 0.5) * 0.35 - frostLine) * 5.0) * _Frost;
                colour = lerp(colour, grey * _FrostColor.rgb * 1.25, 0.4 * _Frost);
                colour = lerp(colour, _FrostColor.rgb * (0.25 + 0.75 * crystals), frostMask * (0.55 + 0.45 * crystals));

                // --- flash: bright areas bloom out first, then the whole screen goes white -------------------------
                colour = colour * (1.0 + _Flash * 8.0) + _Flash * _Flash;

                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
