# Time Trap – custom shaders (part A)

Four hand-written HLSL shaders for URP 17.5 (Unity 6, Forward+). No Shader Graph. Each one includes URP's
`Core.hlsl` (and `Lighting.hlsl` where it needs lights) for the matrices and light data, and does its own maths.

| Shader | File | Used on | Driver script |
|---|---|---|---|
| `TimeTrap/ZombieSurface` | `ZombieSurface.shader` | all zombie bodies (4 materials) | `ZombieShaderFX` |
| `TimeTrap/FlashlightBeam` | `FlashlightBeam.shader` | `FlashlightBeamPivot/BeamCone` | `FlashlightBeamCone`, `TorchShaderGlobals` |
| `TimeTrap/DustParticles` | `DustParticles.shader` | `Camera/DustMotes`, `Map/ShaderFX/Spores_*` | `TorchShaderGlobals` |
| `TimeTrap/TimePortal` | `TimePortal.shader` | `EscapePortal_GateB/Visual/Disc` | `EscapePortal` |

Materials are in `Assets/_Materials/Shaders/`. Scripts are in `Assets/_Scripts/Shaders/`.
`Textures/TT_Noise.png` is a generated, tileable noise texture (R and G hold two different noise patterns); the
dissolve, the beam and the portal all read it.

**Things that apply to all four**

- **SRP Batcher:** every per-material property is declared once inside `CBUFFER_START(UnityPerMaterial)`, and the
  buffer is identical in every pass of a shader (in `ZombieSurface` it sits in an `HLSLINCLUDE` block shared by all
  four passes). Values set from scripts as globals (`_TorchPositionWS` and friends) are deliberately outside it.
- **Forward+:** in URP 17 the keyword is `_CLUSTER_LIGHT_LOOP` (older versions called it `_FORWARD_PLUS`).
  `ZombieSurface` declares it and loops over lights with `LIGHT_LOOP_BEGIN` / `LIGHT_LOOP_END`.
- **Before/after:** press **F9** in Play mode (`ShaderDevToggle`, editor and development builds only). Zombies swap to
  plain URP Lit with the same textures, the portal swaps to its old placeholder material, and the beam and dust are
  hidden (they have no URP Lit equivalent). F9 again switches back. The runner's red tint is not carried over to the
  plain version.

---

## 1. TimeTrap/ZombieSurface

**What it does.** The skin and clothes of every zombie. Lit by every light in the scene, including the torch, with
a wet, sickly look. Four effects are layered on top, each driven by one 0–1 property: dissolve on death, a flash
when hit, an icy look while frozen, and a flicker while stunned.

**Techniques**

- *Half-Lambert diffuse*: `N·L` is remapped from −1..1 to 0..1 and squared, so light wraps round to the unlit side.
- *Blinn-Phong specular*: highlight from the half vector between light and view; the exponent comes from smoothness.
  A specular mask texture keeps it to the wet parts.
- *Rim light (fresnel)*: `1 − N·V`, raised to a power. It is multiplied by each light's colour, so the silhouette
  only glows when the torch is on the zombie.
- *Ambient from spherical harmonics*: `SampleSH(normal)`.
- *Normal mapping*: the tangent-space normal is rotated into world space with the tangent, bitangent and normal.
- *Forward+ light loop*: main light first, then every additional light in the pixel's cluster. Shadows are received
  from the main light and from additional lights (the torch).
- *Dissolve*: a noise value per pixel is compared with a threshold; pixels below it are removed with `clip()`.
  Pixels just above it glow.
- *Alpha cut-out*: the hair and torn clothes are cut out with a second `clip()` on the albedo alpha.
- *Four passes*: `UniversalForward`, `ShadowCaster`, `DepthOnly`, `DepthNormals`. The last three repeat both clips,
  so the shadow and the depth dissolve with the body.

**The dissolve idea: erased from time, not burnt.** The glowing edge uses the portal's colours: a bright cyan-white
core right at the cut, fading to deep blue further in (`_EdgeColor` and `_EdgeColorOuter`). A dying zombie looks as
if it is being pulled out of time by the same energy that powers the escape portal, instead of burning away.

**Module concepts shown:** lighting models (half-Lambert, Blinn-Phong, fresnel, SH ambient), surface properties
(albedo, normal map, smoothness, specular mask, tint), texture/UV mapping (four textures, tiled noise), depth (depth
and shadow passes with matching clips), emission.

**Key lines for a slide**

```hlsl
// Half-Lambert: wrap the light round to the far side
half NdotL = dot(s.normal, light.direction);
half halfLambert = NdotL * 0.5 + 0.5;
half3 diffuse = s.albedo * (halfLambert * halfLambert);

// Blinn-Phong: highlight where the normal lines up with the half vector
half3 halfVector = SafeNormalize(light.direction + s.view);
half3 specular = s.specColor * pow(saturate(dot(s.normal, halfVector)), s.specPower) * saturate(NdotL * 4.0);

// Rim: fresnel, lit by the light
half3 rim = _RimColor.rgb * (s.fresnel * _RimStrength);
return (diffuse + specular + rim) * light.color * attenuation;
```

```hlsl
// Every point and spot light that reaches this pixel (Forward+ cluster): the torch, room lights, the portal glow
LIGHT_LOOP_BEGIN(lightCount)
    Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
    colour += ShadeLight(light, s);
LIGHT_LOOP_END
```

```hlsl
// Dissolve: cut away everything whose noise value is below the threshold
half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv * _NoiseScale).r;
half threshold = lerp(-_EdgeWidth, 1.001, _Dissolve);
half above = noise - threshold;
clip(above);
// ...and glow where the pixel is only just above it: deep blue -> cyan-white
half edge = 1.0 - smoothstep(0.0, _EdgeWidth, above);
half3 edgeColour = lerp(_EdgeColorOuter.rgb, _EdgeColor.rgb, edge * edge);
```

**Driver: `ZombieShaderFX`** (on `Zombie.prefab`)

| Event | Property | Timing |
|---|---|---|
| `ZombieAI.OnDeathStarted` | `_Dissolve` 0→1 | starts 3.5 s after death (3 s death animation + 0.5 s lying), takes 2.5 s; gone at about 6 s |
| `ZombieHealth.OnDamaged` | `_HitFlash` 1→0 | 0.15 s |
| `ZombieAI.IsFrozen` | `_Freeze` | fades over 0.35 s |
| `ZombieAI` state `Stunned` | `_Stun` | fades over 0.2 s |
| `ZombieSpawner` tint | `_Tint` | set once at spawn (runners) |

It turns `sinkOnDeath` off and sets `ZombieAI`'s linger and sink time so the zombie is removed exactly when the
dissolve finishes.

**SRP Batcher and the MaterialPropertyBlock.** The shader is SRP Batcher compatible, but a renderer that has a
MaterialPropertyBlock is not batched by the SRP Batcher: Unity draws it with its own draw call so that it can have
its own values. That is the price of every zombie sharing four materials while still dissolving, flashing and
freezing on its own. It is acceptable here because at most 10 zombies are alive at once (the spawner's Stage 3
cap), each with 1–3 skinned renderers, so it costs a few dozen draw calls at worst. The alternative, one material
instance per zombie, would keep the batcher but create and destroy materials on every spawn.

---

## 2. TimeTrap/FlashlightBeam

**What it does.** The faint shaft of light in the air in front of the torch. It is meant to be felt more than seen.

**Geometry.** `FlashlightBeamCone` builds a cone mesh in code from the spot light: apex at the light, axis along +Z,
the light's outer angle and range. It rebuilds if the angle or range changes. The object is a child of
`FlashlightBeamPivot`, so it follows the light, including when the pivot moves it to `WeaponLightMount` for the
pistol.

**Techniques**

- *Ray-marched volume, not a shaded surface.* The player looks along the beam from just behind it, so shading the
  cone's surface would show almost nothing. Instead, for each pixel the shader walks along the view ray in
  **12 steps** and adds up how much lit dust the ray passes through.
- *Falloff along the length*: inverse-square style thinning, reaching zero at the light's range, with a short
  fade-in at the lens.
- *Falloff toward the edge*: full inside the light's inner angle, zero at the outer angle, the same shape as the
  real spot light.
- *Soft depth*: the ray is cut off at the scene depth read from `_CameraDepthTexture`. Where the beam meets a wall
  the amount of dust in front of it shrinks smoothly to zero, so there is never a hard edge.
- *Scrolling noise*: one noise lookup per step, drifting slowly, for dust in the air.
- *Fog*: light scattered far from the camera is faded by `exp(-k·distance)` so the beam does not lift the black
  level of dark, foggy areas.
- *Jitter*: each pixel's ray starts at a slightly different offset, which turns banding into fine grain. Steps are
  bunched up near the camera where the beam changes fastest.
- *Blending*: additive (`Blend One One`), `ZWrite Off`, `ZTest Always`, `Cull Front` so it still draws when the
  camera is inside the cone.

**Behaviour.** `TorchShaderGlobals.Level` is 0 when the torch is off, follows the low-battery flicker (it reads
the light's intensity, which `FlashlightController` dips), and dims below 30 % battery. `FlashlightBeamCone`
copies it into `_Intensity`.

**Module concepts shown:** blending (additive transparency), depth (depth texture, manual depth test, soft
intersection), texture/UV mapping (scrolling noise), procedural geometry, volumetric lighting.

**Key lines for a slide**

```hlsl
// soft depth: how far can the view ray go before it hits the scene?
float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
float sceneDistance = sceneEyeDepth / max(dot(rayDirWS, cameraForward), 0.0001);
float rayLength = min(surfaceDistance, sceneDistance);
```

```hlsl
// walk the ray, adding up the lit dust
for (int i = 0; i < STEPS; i++)
{
    float u = (i + jitter) / STEPS;
    float t = rayLength * u * u;                 // more samples near the camera
    dust += DustAt(originOS + rayDirOS * t) * (2.0 * u) * exp(-_ViewFalloff * t);
}
```

```hlsl
// inside DustAt: edge and length falloff of the cone
half edge = 1.0 - smoothstep(innerRadius, coneRadius, radius);
half reach = saturate(1.0 - along / _Range);
half lengthFade = reach * reach / (1.0 + _LengthFalloff * along * along);
```

---

## 3. TimeTrap/DustParticles

**What it does.** Dust motes around the player and spores in three dark rooms. A mote is almost invisible until the
torch beam passes over it, then it sparkles.

**Techniques**

- *Camera-facing billboard built in the vertex shader*: the quad is rebuilt around the particle's centre from the
  camera's right and up vectors.
- *Vertex wobble*: the centre is nudged by three sine waves whose phase depends on the particle's position.
- *Brightness from the torch*: worked out once per particle in the vertex shader from three global properties set
  by `TorchShaderGlobals` (`_TorchPositionWS`, `_TorchDirectionWS`, `_TorchParams`). It uses the angle from the
  beam axis and the distance from the torch.
- *Soft particles*: fades out where the quad gets close to geometry, using the depth texture.
- *Camera fade*: fades out right in front of the camera.
- *Procedural round dot*: no texture.
- *Blending*: additive, no depth write.

**Vertex streams.** The Particle System Renderer must send, in this order: `Position, Color, UV, Center, Size`.
The centre arrives in `TEXCOORD0.zw` + `TEXCOORD1.x`, the size in `TEXCOORD1.y`.

**Placement**

- `Camera/DustMotes`: 6 m sphere around the camera, world-space simulation, slow drift, up to 700 motes.
- `Map/ShaderFX/Spores_Shelter`, `Spores_BirkinLab`, `Spores_Bioreactors`: static box emitters using the `Spores`
  material (same shader, sickly green, a little self-glow so they show faintly without the torch).

**Module concepts shown:** vertex animation (billboard and wobble), blending, depth (soft particles), global
shader properties shared between scripts and shaders.

**Key lines for a slide**

```hlsl
// camera-facing quad: corner = centre + right * x + up * y
float3 cameraRight = UNITY_MATRIX_V[0].xyz;
float3 cameraUp    = UNITY_MATRIX_V[1].xyz;
float2 corner = (input.uvCenterXY.xy - 0.5) * size;
float3 positionWS = centerWS + cameraRight * corner.x + cameraUp * corner.y;
```

```hlsl
// how much torch light reaches this mote
float cosAngle = dot(toMote / max(dist, 0.0001), _TorchDirectionWS);
half cone = smoothstep(_TorchParams.x, _TorchParams.y, cosAngle);   // outer -> inner angle
half reach = saturate(1.0 - dist / max(_TorchParams.z, 0.0001));
half inBeam = cone * reach * reach * _TorchParams.w;
```

```hlsl
// soft particles
half softFade = saturate((sceneEyeDepth - input.eyeDepth) / _SoftDistance);
```

---

## 4. TimeTrap/TimePortal

**What it does.** The escape portal at Gate B: a swirling cyan vortex with a bright core and rim, which bends the
view of whatever is behind it. It grows in from nothing when Stage 3 starts.

**Geometry.** `Assets/_Models/Generated/PortalDisc.asset`: a flat disc, radius 1, 24 rings × 64 segments, so there
are enough vertices to ripple.

**Techniques**

- *Vertex animation*: a sine wave travelling outward from the centre (height depends on radius and time), pinned to
  zero at the rim; the whole disc also breathes in and out. The normal is recomputed from the wave's slope.
- *Polar UVs with a swirl*: the pixel's position is converted to (angle, radius); the angle is twisted more toward
  the centre and spun over time.
- *Two scrolling noise layers* sampled through those polar UVs and multiplied together: the vortex.
- *Edge ring, fresnel and core*: a band just inside the rim, a fresnel glow on the tilted ripples, and a core that
  goes from cyan to white.
- *Screen-space refraction*: `_CameraOpaqueTexture` is sampled at a screen position pushed around by the noise, so
  the scene behind distorts. This is a post-processing style effect done in a material.
- *Blending*: alpha blend, no depth write, both sides drawn.

**Activation.** `EscapePortal` drives `_Activate` from 0 to 1 over 2.5 s when Stage 3 starts. The disc scales up,
flashes white half-way, and the cyan point light comes up with it. The light and the trigger volume are unchanged.

**Module concepts shown:** vertex animation, texture/UV mapping (polar coordinates, scrolling UVs), blending,
post-processing (reading the rendered scene), fresnel, emission.

**Key lines for a slide**

```hlsl
// vertex: ripple travelling outward, pinned at the rim
float wavePhase = radius * _RippleFreq - _Time.y * _RippleSpeed;
p.z += sin(wavePhase) * amplitude;
```

```hlsl
// fragment: polar coordinates with a swirl
float2 centred = input.uv * 2.0 - 1.0;
float radius = length(centred);
float angle = atan2(centred.y, centred.x);
angle += (1.0 - radius) * _Twist + _Time.y * _SpinSpeed;
float2 polarUV = float2(angle / TWO_PI, radius);
```

```hlsl
// refraction: read the scene behind, at a screen position pushed by the noise
float2 push = (float2(noiseA, noiseB) - 0.5) * (_Refraction * open);
half3 scene = SampleSceneColor(screenUV + push);
```

---

## URP settings these shaders rely on

`Assets/Settings/PC_RPAsset.asset` already had both switched on; nothing was changed.

- **Depth Texture**: beam, dust and spores.
- **Opaque Texture**: portal refraction.

## Performance

Measured in the editor on an Apple M3 (Metal) at 1920×1080, Stage 2 lighting, two zombies, beam and dust in view.
Each figure is the average time for one camera render over 180–240 renders, including waiting for the GPU.

| Setup | ms per render | Cost |
|---|---|---|
| Everything custom (beam at 16 steps) | 10.63 | |
| Beam hidden | 9.98 | beam, 16 steps: about 0.65 ms |
| Beam at 12 steps | 10.36 | beam, 12 steps: about 0.39 ms |
| Dust hidden | 10.30 | dust: about 0.33 ms |
| F9 plain URP Lit (no beam, no dust) | 9.56 | all custom shaders together: about 1.1 ms (16 steps) |

The beam is the most expensive of the four because it runs a loop for every pixel the cone covers, which is most
of the screen. 12 steps looked the same as 16 in the corridor test, so it ships at 12 (`#define STEPS` in
`FlashlightBeam.shader`). The portal was not measured separately.

## Screenshots

`Screenshots/ShaderReview/`, each in Stage 1 and Stage 2 lighting: zombie in the torch, hit flash, frozen,
mid-dissolve, beam in a corridor, beam at a close wall, dust in the beam, spores in the Shelter, portal activating,
portal open, plus the F9 before/after pair. Files ending `_beam_hidden` and `_refraction_off` are comparison shots.
