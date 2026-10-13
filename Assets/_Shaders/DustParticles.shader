// TimeTrap/DustParticles
// ---------------------------------------------------------------------------------------------------------------
// Dust motes and spores for a Particle System. Each mote is nearly invisible until the torch beam passes over it.
//
// Vertex shader:
//   - camera-facing: the quad is rebuilt around the particle's centre from the camera's right and up vectors
//   - wobble: the centre is nudged by slow sine waves, different for every particle
//   - torch: how brightly the beam lights this particle is worked out once per particle, from global shader
//     properties that TorchShaderGlobals sets each frame (position, direction, cone angles, range, brightness)
// Fragment shader:
//   - a round soft dot (no texture needed)
//   - soft particles: fades out where the quad gets close to a wall or floor, using the depth texture
//   - fades out right in front of the camera so a mote never fills the screen
//
// The Particle System's renderer must send these vertex streams, in this order:
//   Position, Color, UV, Center, Size       (set up by the Time Trap dust objects; see README_Shaders.md)
// Blending is additive, with no depth write.
Shader "TimeTrap/DustParticles"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.95, 0.85, 1)
        _BeamBoost ("Brightness in the torch beam", Range(0, 20)) = 6
        _AmbientGlow ("Brightness outside the beam", Range(0, 1)) = 0.02
        _Softness ("Dot Softness", Range(0.5, 6)) = 2

        [Header(Wobble)]
        _WobbleAmount ("Wobble Amount (m)", Range(0, 0.3)) = 0.04
        _WobbleSpeed ("Wobble Speed", Range(0, 5)) = 0.8

        [Header(Depth)]
        _SoftDistance ("Soft Particle Distance (m)", Range(0.01, 2)) = 0.4
        _CameraFade ("Camera Fade Start / End (m)", Vector) = (0.25, 0.9, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Dust"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One      // additive
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _CameraFade;
                half _BeamBoost;
                half _AmbientGlow;
                half _Softness;
                half _WobbleAmount;
                half _WobbleSpeed;
                half _SoftDistance;
            CBUFFER_END

            // Globals from TorchShaderGlobals (not per-material, so they stay outside the CBUFFER).
            float3 _TorchPositionWS;
            float3 _TorchDirectionWS;
            float4 _TorchParams;      // x = cos(outer half-angle), y = cos(inner half-angle), z = range, w = brightness 0-1
            half4 _TorchTint;         // white, or gold with the Time Lord reward (unset reads as black: treated as white)

            struct Attributes
            {
                float4 positionOS : POSITION;      // the corner Unity made (not used: the quad is rebuilt)
                half4  color      : COLOR;         // particle colour and alpha (fade in/out over lifetime)
                float4 uvCenterXY : TEXCOORD0;     // xy = quad UV, zw = particle centre x, y
                float4 centerZSize: TEXCOORD1;     // x = particle centre z, y = particle size
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : TEXCOORD1;     // rgb = final colour, a = unused
                float  eyeDepth   : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                // The centre and size arrive in the particle system's simulation space. The system simulates in
                // world space, so the "object" matrix is identity and this is a world position.
                float3 centerWS = TransformObjectToWorld(float3(input.uvCenterXY.zw, input.centerZSize.x));
                float size = input.centerZSize.y;

                // --- wobble: three sine waves, phase taken from the particle's own position ------------------
                float3 phase = centerWS.yzx * 1.7 + centerWS.zxy * 0.9;
                float t = _Time.y * _WobbleSpeed;
                centerWS += float3(sin(t + phase.x), sin(t * 0.8 + phase.y), sin(t * 1.3 + phase.z)) * _WobbleAmount;

                // --- camera-facing quad: corner = centre + right * x + up * y -------------------------------
                float3 cameraRight = UNITY_MATRIX_V[0].xyz;
                float3 cameraUp = UNITY_MATRIX_V[1].xyz;
                float2 corner = (input.uvCenterXY.xy - 0.5) * size;
                float3 positionWS = centerWS + cameraRight * corner.x + cameraUp * corner.y;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uvCenterXY.xy;
                output.eyeDepth = -TransformWorldToView(positionWS).z;

                // --- how much torch light reaches this mote ---------------------------------------------------
                float3 toMote = centerWS - _TorchPositionWS;
                float dist = length(toMote);
                // cosine of the angle between the beam axis and the mote: 1 = dead centre of the beam
                float cosAngle = dot(toMote / max(dist, 0.0001), _TorchDirectionWS);
                half cone = smoothstep(_TorchParams.x, _TorchParams.y, cosAngle);
                half reach = saturate(1.0 - dist / max(_TorchParams.z, 0.0001));
                half inBeam = cone * reach * reach * _TorchParams.w;

                // the glow of its own keeps the dust colour; the part lit by the torch takes the torch's tint
                half3 tint = dot(_TorchTint.rgb, half3(1, 1, 1)) > 0.001 ? _TorchTint.rgb : half3(1, 1, 1);
                half3 light = _AmbientGlow + tint * (inBeam * _BeamBoost);
                output.color = half4(_Color.rgb * input.color.rgb * light * input.color.a, 1.0);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // round soft dot: distance from the quad's centre, 0 in the middle, 1 at the edge
                half r = length(input.uv - 0.5) * 2.0;
                half disc = pow(saturate(1.0 - r), _Softness);

                // soft particles: compare this pixel's depth with the scene behind it
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                half softFade = saturate((sceneEyeDepth - input.eyeDepth) / _SoftDistance);

                // fade out close to the camera
                half cameraFade = smoothstep(_CameraFade.x, _CameraFade.y, input.eyeDepth);

                return half4(input.color.rgb * (disc * softFade * cameraFade), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
