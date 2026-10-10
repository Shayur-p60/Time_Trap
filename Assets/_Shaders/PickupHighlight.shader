// TimeTrap/PickupHighlight
// ---------------------------------------------------------------------------------------------------------------
// The glowing outline on pickups and objective items. The PickupHighlight script draws each item's mesh a second
// time with this shader, on top of the item's own material (which is not changed).
//
// Two passes:
//   Outline   "inverted hull": every vertex is pushed outward along its normal and only BACK faces are drawn. The
//             item itself hides the middle of this shell, so only a rim shows around it. The push is done in clip
//             space and scaled by distance, so the outline is the same number of pixels wide however far away it is.
//   Glow      the item's front faces again, adding a faint fresnel glow (brighter toward the silhouette).
// Both pulse, and both are additive (they only add light).
//
// Visibility is worked out in the vertex shader, once per vertex, with no script running per frame:
//   - the item is within about 6 m of the camera, OR
//   - the torch beam points at it (the global torch properties TorchShaderGlobals sets for the dust shader)
// _Fade (per item) takes it to nothing when the item is collected.
//
// URP draws one pass per LightMode tag, so the two passes use two different tags.
Shader "TimeTrap/PickupHighlight"
{
    Properties
    {
        [Header(Outline)]
        [HDR] _OutlineColor ("Outline Colour", Color) = (0.25, 1.9, 2.6, 1)
        _OutlineWidth ("Outline Width (pixels)", Range(0, 12)) = 4

        [Header(Glow on the object)]
        [HDR] _GlowColor ("Glow Colour", Color) = (0.2, 0.9, 1.2, 1)
        _GlowPower ("Fresnel Power (higher = thinner)", Range(0.5, 8)) = 2.5
        _GlowStrength ("Glow Strength", Range(0, 2)) = 0.6

        [Header(Pulse)]
        _PulseSpeed ("Pulse Speed", Range(0, 10)) = 3
        _PulseMin ("Brightness at the low point", Range(0, 1)) = 0.45

        [Header(Visibility)]
        _NearDistance ("Always visible within (m)", Float) = 6
        _NearFade ("Fade-out band beyond that (m)", Float) = 1.5
        _TorchReach ("Torch lights it up to (m)", Float) = 18
        _Fade ("Fade (set by script: 1 shown, 0 collected)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        // Shared by both passes: the material buffer, the torch globals, the visibility test and the pulse.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _OutlineColor;
            half4 _GlowColor;
            half _OutlineWidth;
            half _GlowPower;
            half _GlowStrength;
            half _PulseSpeed;
            half _PulseMin;
            float _NearDistance;
            float _NearFade;
            float _TorchReach;
            half _Fade;
        CBUFFER_END

        // Globals from TorchShaderGlobals (part A).
        float3 _TorchPositionWS;
        float3 _TorchDirectionWS;
        float4 _TorchParams;      // x = cos(outer half-angle), y = cos(inner half-angle), z = range, w = brightness 0-1

        // 0 = hidden, 1 = fully shown. Uses the object's pivot, so the whole item switches together.
        half Visibility()
        {
            float3 pivotWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));

            // near the player
            float cameraDistance = distance(pivotWS, _WorldSpaceCameraPos);
            half nearby = 1.0 - smoothstep(_NearDistance, _NearDistance + _NearFade, cameraDistance);

            // in the torch beam: inside the cone, within reach, and the torch is on
            float3 toItem = pivotWS - _TorchPositionWS;
            float torchDistance = length(toItem);
            float cosAngle = dot(toItem / max(torchDistance, 0.0001), _TorchDirectionWS);
            half inCone = smoothstep(_TorchParams.x, _TorchParams.y, cosAngle);
            half inReach = step(torchDistance, min(_TorchParams.z, _TorchReach));
            half inBeam = inCone * inReach * _TorchParams.w;

            return max(nearby, inBeam) * _Fade;
        }

        // Brightness that rises and falls between _PulseMin and 1.
        half Pulse()
        {
            return lerp(_PulseMin, 1.0, 0.5 + 0.5 * sin(_Time.y * _PulseSpeed));
        }
        ENDHLSL

        // ------------------------------------------------------------------------------------------------------
        // Pass 1: the outline (inverted hull)
        // ------------------------------------------------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front         // back faces only: the shell is seen from the inside
            ZWrite Off
            ZTest LEqual       // the item itself is already in the depth buffer and hides the shell's middle
            Blend One One      // additive

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  colour     : TEXCOORD0;
            };

            Varyings OutlineVert(Attributes input)
            {
                Varyings output;
                half visible = Visibility();

                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // the normal's direction on screen
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalOnScreen = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
                normalOnScreen = normalize(normalOnScreen + float2(0.00001, 0.0));

                // Push the vertex outward by _OutlineWidth pixels.
                //   pixels -> clip units:   2 / screen size   (the screen is 2 clip units across)
                //   x positionCS.w:         cancels the perspective divide, so the width does not shrink with distance
                positionCS.xy += normalOnScreen * (_OutlineWidth * 2.0 / _ScreenParams.xy) * positionCS.w;

                // hidden items: collapse every vertex to one point, so no pixels are drawn at all
                output.positionCS = visible > 0.001 ? positionCS : float4(0.0, 0.0, 0.0, 1.0);
                output.colour = _OutlineColor.rgb * (visible * Pulse());
                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                return half4(input.colour, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------------------------
        // Pass 2: fresnel glow on the item's own surface
        // ------------------------------------------------------------------------------------------------------
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1      // drawn exactly on top of the item: nudge it toward the camera to avoid flicker
            Blend One One

            HLSLPROGRAM
            #pragma vertex GlowVert
            #pragma fragment GlowFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                half3  viewWS     : TEXCOORD1;
                half   strength   : TEXCOORD2;
            };

            Varyings GlowVert(Attributes input)
            {
                Varyings output;
                half visible = Visibility();
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                output.positionCS = visible > 0.001 ? TransformWorldToHClip(positionWS) : float4(0.0, 0.0, 0.0, 1.0);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                output.strength = visible * Pulse() * _GlowStrength;
                return output;
            }

            half4 GlowFrag(Varyings input) : SV_Target
            {
                // fresnel: 0 where the surface faces the camera, 1 where it turns away
                half fresnel = pow(1.0 - saturate(dot(normalize(input.normalWS), normalize(input.viewWS))), _GlowPower);
                return half4(_GlowColor.rgb * (fresnel * input.strength), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
