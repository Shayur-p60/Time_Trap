# Time Trap – custom shaders (parts A and B, and the intro cutscene)

Ten hand-written HLSL shaders for URP 17.5 (Unity 6, Forward+). No Shader Graph. Each one includes URP's
`Core.hlsl` (and `Lighting.hlsl` where it needs lights) for the matrices and light data, and does its own maths.

| Shader | File | Used on | Driver script |
|---|---|---|---|
| `TimeTrap/ZombieSurface` | `ZombieSurface.shader` | all zombie bodies (13 materials) | `ZombieShaderFX` |
| `TimeTrap/FlashlightBeam` | `FlashlightBeam.shader` | `FlashlightBeamPivot/BeamCone` | `FlashlightBeamCone`, `TorchShaderGlobals` |
| `TimeTrap/DustParticles` | `DustParticles.shader` | `Camera/DustMotes`, `Map/ShaderFX/Spores_*` | `TorchShaderGlobals` |
| `TimeTrap/TimePortal` | `TimePortal.shader` | `EscapePortal_GateB/Visual/Disc` | `EscapePortal` |
| `TimeTrap/TimeDistortionFullscreen` | `TimeDistortionFullscreen.shader` | Full Screen Pass feature on `PC_Renderer` | `ScreenFX` |
| `TimeTrap/BioreactorFluid` | `BioreactorFluid.shader` | `Map/ShaderFX/BioreactorFluid` (pit pool, tank strips) | none |
| `TimeTrap/PickupHighlight` | `PickupHighlight.shader` | every pickup and objective item, added at runtime | `PickupHighlight`, `PickupHighlightConfig` |
| `TimeTrap/Hologram` | `Hologram.shader` | Dr Birkin in the intro cutscene (`IntroCutscene/Birkin`, one material per body part) | `BirkinHologramFX` |
| `TimeTrap/TankFluid` | `TankFluid.shader` | the liquid in the eight specimen tanks of the portal chamber (`M_Tank_Fluid`) | none |
| `TimeTrap/TankGlass` | `TankGlass.shader` | the glass of those tanks (`M_Tank_Glass`) | none |

Materials are in `Assets/_Materials/Shaders/`. Scripts are in `Assets/_Scripts/Shaders/`.
`Textures/TT_Noise.png` is a generated, tileable noise texture (R and G hold two different noise patterns); the
dissolve, the beam, the portal, the frost and the fluid all read it.

**Things that apply to all ten**

- **SRP Batcher:** every per-material property is declared once inside `CBUFFER_START(UnityPerMaterial)`, and the
  buffer is identical in every pass of a shader (in `ZombieSurface` it sits in an `HLSLINCLUDE` block shared by all
  four passes). Values set from scripts as globals (`_TorchPositionWS` and friends) are deliberately outside it.
- **Forward+:** in URP 17 the keyword is `_CLUSTER_LIGHT_LOOP` (older versions called it `_FORWARD_PLUS`).
  `ZombieSurface`, `BioreactorFluid` and `TankGlass` declare it and loop over lights with `LIGHT_LOOP_BEGIN` /
  `LIGHT_LOOP_END`.
- **Before/after:** press **F9** in Play mode (`ShaderDevToggle`; works in the editor and in builds, release builds included). Zombies swap to
  plain URP Lit with the same textures, the portal swaps to its old placeholder material, and the beam and dust are
  hidden (they have no URP Lit equivalent). The pit pool becomes a flat URP Lit surface, the tank strips and the
  pickup outlines are hidden, and the full-screen effect is switched off. F9 again switches back. A role tint
  (`_Tint`, no role uses one for now beyond the regulars' slight variations) is not carried over to the plain version.
  In the intro cutscene F9 shows Dr Birkin as the plain URP Lit model he was imported as (each hologram material has
  its original listed under `ShaderDevToggle`'s Overrides), and the tank fluid and glass are hidden.

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
its own values. That is the price of every zombie sharing the same materials while still dissolving, flashing and
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

## 5. TimeTrap/TimeDistortionFullscreen

**What it does.** One full-screen post-processing shader with five states: a time-rewind glitch when the player is
caught or respawns, a red heartbeat vignette as time runs out, frost during Zombie Freeze, a white bloom-out for
Flash Pulse, and a subtle darkening when a zombie is chasing close by.

**How it is drawn.** A URP **Full Screen Pass Renderer Feature** on `PC_Renderer` (named `TimeDistortionFullscreen`),
injected *after* URP's post-processing, so it sits on top of the `TT_PostFX` volume without touching it, and before
the UI, so the HUD and menus are never distorted. The feature gives the shader the finished frame as `_BlitTexture`.

**Techniques**

- *Reading the frame back and moving the read position*: every effect is a change to *where* the frame is sampled
  (distortion) or to the colour that comes back.
- *RGB split (chromatic aberration)*: red, green and blue are read from three slightly different places.
- *Row slicing*: the screen is cut into rows and a pseudo-random half of them slide sideways; a hash function
  re-rolls them 24 times a second.
- *Scanlines*: a sine wave down the screen dims alternate pixel rows.
- *Desaturation*: blend toward the pixel's grey value (its luminance).
- *Vignette*: a mask that is 0 in the middle of the screen and 1 at the corners.
- *Heartbeat*: a sine wave raised to a power gives a short sharp beat; the script advances its phase faster as
  time runs out.
- *Barrel distortion*: pixels are read from closer to the centre the further out they are.
- *Frost*: two noise lookups at different sizes and angles are multiplied (so the texture's repeat does not show)
  and creep in from the screen's border.
- *Bloom-out*: the frame is multiplied up so bright areas clip first, then the whole screen goes white.

**States and their drivers (`ScreenFX`, on `Map/Gameplay/ScreenFX`)**

| Global property | Trigger | Timing |
|---|---|---|
| `_Glitch` | `PlayerEvents.OnPlayerKilled`, `CheckpointManager.OnPlayerRespawned` | 1 → 0 over 0.6 s, on real time |
| `_Pulse` (+ `_PulsePhase`) | `ScreenFX.SetLowTime(0–1)`, called by `GameManager` over the last 60 s | follows the value; 1.1 → 3.2 beats per second |
| `_Frost` | `PowerUps.OnZombieFreeze` … `OnZombieFreezeEnded` | fades over 0.4 s, on for the whole freeze |
| `_Flash` | `PowerUps.OnFlashPulse` | 1 → 0 over 0.2 s, on real time |
| `_Danger` | nearest zombie in Chase or Attack within 8 m | 0 at 8 m, rising as it closes in |

- **Idle cost is zero.** While all five are 0, `ScreenFX` switches the renderer feature off and URP skips the pass.
  The feature is saved switched off in `PC_Renderer`; `ScreenFX` switches it off again when it is disabled, so a
  Play session leaves the asset unchanged.
- **Pause** (`timeScale` 0): the glitch and flash run on unscaled time, so they always finish. Pulse, frost and
  danger keep their values and the animation clock stops, so the picture is frozen with the game.
- **End screens**: everything fades to 0 in 0.3 s (unscaled) once the run is won or lost.
- **Restart**: `GameManager.ResetAllStatics()` calls `ScreenFX.ResetForNewPlaySession()`.
- **F9** turns the whole effect off.

**Module concepts shown:** post-processing (a custom full-screen pass), texture/UV mapping (distorting screen UVs,
noise), colour operations (desaturation, tint, vignette), global shader properties driven by gameplay.

**Key lines for a slide**

```hlsl
// RGB split: read red, green and blue from three different places
float split = _GlitchSplit * _Glitch;
colour.r = SampleScreen(uv + float2(split, 0.0)).r;
colour.g = SampleScreen(uv).g;
colour.b = SampleScreen(uv - float2(split, 0.0)).b;
```

```hlsl
// glitch: cut the screen into rows and slide about half of them sideways
float tick = floor(_ScreenFxClock.y * 24.0);
float row = floor(uv.y * _GlitchSlices);
float moved = step(0.55, Hash(float2(tick, row * 1.7)));
uv.x += (Hash(float2(row, tick)) - 0.5) * moved * _GlitchSliceShift * _Glitch;
```

```hlsl
// low time: a heartbeat, a barrel distortion and a red vignette
half beat = pow(saturate(0.5 + 0.5 * sin(_PulsePhase)), 4.0);
half pulse = _Pulse * (0.35 + 0.65 * beat);
uv = 0.5 + fromCentre * (1.0 - _PulseBarrel * pulse * dot(fromCentre, fromCentre) * 4.0);
colour = lerp(colour, _PulseColor.rgb * (grey * 0.6 + 0.3), redVignette * pulse);
```

---

## 6. TimeTrap/BioreactorFluid

**What it does.** The toxic green liquid in the Bioreactors room: a pool on the pit floor, and the liquid seen
through the sight-glass strip on every culture tank.

**Geometry.** The room came with no liquid meshes (the tanks are opaque), so two kinds were added under
`Map/ShaderFX/BioreactorFluid`. Neither has a collider, and the pit death zone is untouched.

- `PitPool`: a flat 38 × 36 m grid (`PitFluidPlane.asset`, 76 × 72 cells so the waves have vertices to move) at
  y = −15.3, about 0.8 m above the lowest floor. The tank platforms stay dry.
- 84 `TankFluid_*` strips: a copy of each tank's sight-glass sub-mesh, pushed 4 mm outward so it sits just in front
  of the original. The UC pack's renderers and materials are not changed.

Two materials: `BioreactorFluid_Pit` (waves, depth colour, foam) and `BioreactorFluid_Tank` (no waves, opaque,
more bubbles).

**Techniques**

- *Sum-of-sines waves (vertex)*: three sine waves with different directions, wavelengths and heights are added.
- *Normals from derivatives (vertex)*: the slope of each wave is its cosine; the normal of a surface `y = h(x, z)`
  is `(-dh/dx, 1, -dh/dz)`.
- *Depth-based colour*: scene depth minus the surface's depth gives the thickness of liquid; colour and opacity
  follow Beer's law (`1 - exp(-thickness × density)`), so thicker reads darker.
- *Foam line (depth intersection)*: where the thickness is nearly zero the surface is touching a wall or a machine.
- *Blinn-Phong specular* from the main light and all additional lights through the Forward+ light loop.
- *Fresnel reflection*: at grazing angles the reflection replaces the liquid's own colour.
- *Bubbles*: two noise layers multiplied; they scroll upward on vertical faces (tanks) and drift on the pool.
- *Emissive glow*, dimmer where the liquid is thick, so the pool and its depth read in Stage 1 darkness.
- *Blending*: transparent, alpha blended, no depth write (it must not be in the depth texture it reads).

**F9:** the pool becomes a flat URP Lit surface (`BioreactorFluid_Plain`); the tank strips are hidden, showing the
original sight glass.

**Module concepts shown:** vertex animation, lighting models (Blinn-Phong, fresnel), depth (depth texture, soft
intersection), blending, texture/UV mapping (scrolling noise), emission.

**Key lines for a slide**

```hlsl
// vertex: one sine wave adds its height and its slope
float phase = dot(direction, xz) * k + _Time.y * _WaveSpeed * k;
height += amplitude * sin(phase);
slope  += direction * (amplitude * k * cos(phase));    // derivative of sin is cos
...
// normal of the surface y = h(x, z)
half3 waveNormalWS = normalize(half3(-slope.x, 1.0, -slope.y));
```

```hlsl
// depth colour: how much liquid is between the surface and what is behind it?
float thickness = max(sceneEyeDepth - input.eyeDepth, 0.0);
half depth01 = 1.0 - exp(-thickness * _DepthDensity);          // Beer's law
half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);
```

```hlsl
// foam: where the liquid is thinner than the foam width, it is touching something
half foamEdge = thickness + (rippleB - 0.5) * _FoamWidth * 0.9;
half foam = (1.0 - smoothstep(0.0, _FoamWidth, foamEdge)) * upness * _FoamAmount;
```

---

## 7. TimeTrap/PickupHighlight

**What it does.** A pulsing cyan outline, with a faint glow on the item itself, on everything the player can take
or use: pistol, ammo, batteries, power-ups, chests, the vaccine case and the data processor.

**Techniques**

- *Inverted hull outline*: the mesh is drawn a second time, every vertex pushed outward along its normal, with only
  back faces drawn (`Cull Front`). The item hides the middle of this shell, so only a rim is left.
- *Screen-space-constant width*: the push is done in clip space and multiplied by `positionCS.w`, which cancels
  the perspective divide. The outline is 4 pixels wide at any distance.
- *Fresnel glow*: a second pass on the item's front faces, brighter toward the silhouette.
- *Two passes in URP*: URP draws one pass per LightMode tag, so the outline uses `SRPDefaultUnlit` and the glow
  uses `UniversalForward`.
- *Visibility in the vertex shader*: shown when the item's pivot is within 6 m of the camera (fading out to 7.5 m),
  or inside the torch cone and within 18 m, using the global torch properties from part A. Hidden items collapse
  to a single point, so no pixels are drawn.
- *Blending*: additive, no depth write.

**Script side.** `PickupHighlight` is added at runtime by `Pickup` (which covers `Chest` and every spawned pickup),
`PathogenSample` and `DataDownload`. It makes a child object per mesh that draws the mesh again with the highlight
material; the item's own materials are untouched. Because the shader decides visibility, no script runs per frame.
On collection the shells are detached and `_Fade` goes 1 → 0 over 0.4 s, so the outline fades out even though the
item has already been destroyed. `PickupHighlightConfig` (on `Map/Gameplay`) holds the material, since pickups
created by `PickupSpawner` cannot carry a reference of their own.

**Module concepts shown:** vertex manipulation (extrusion along normals, clip-space maths), culling, blending,
fresnel, global shader properties shared between shaders.

**Key lines for a slide**

```hlsl
// outline: push the vertex outward by _OutlineWidth pixels, whatever the distance
float2 normalOnScreen = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
normalOnScreen = normalize(normalOnScreen + float2(0.00001, 0.0));
positionCS.xy += normalOnScreen * (_OutlineWidth * 2.0 / _ScreenParams.xy) * positionCS.w;
```

```hlsl
Cull Front         // back faces only: the shell is seen from the inside
ZTest LEqual       // the item itself hides the shell's middle
Blend One One      // additive
```

```hlsl
// visible when near the player, or when the torch beam points at it
half nearby = 1.0 - smoothstep(_NearDistance, _NearDistance + _NearFade, cameraDistance);
half inCone = smoothstep(_TorchParams.x, _TorchParams.y, cosAngle);
half inBeam = inCone * step(torchDistance, min(_TorchParams.z, _TorchReach)) * _TorchParams.w;
return max(nearby, inBeam) * _Fade;
```

---

## 8. TimeTrap/Hologram

**What it does.** Dr Birkin, the AI guide of the intro cutscene, is a man made of light: cyan, see-through, edged
with a glow, crossed by scanlines that climb his body, flickering, and now and then torn sideways by a glitch. He
materialises from the feet up when he first appears.

**Where it is used.** Every renderer of `IntroCutscene/Birkin`. There is one hologram material per original material
(`Assets/_Materials/Cutscene/Hologram/Holo_*`), because each keeps its own albedo texture. The hair cards use
`_Cutoff` and `Cull Off`; the lens of the glasses has `_ZWrite` off so it does not hide the eyes.

**Techniques**

- *Vertex glitch*: the body is cut into horizontal bands in world space (`floor(positionWS.y * _GlitchBands)`). A
  hash of the band and of the time, cut into steps of 1/14 s, decides which bands are displaced, and by how much;
  the displaced bands move along the camera's right vector. Because the time is stepped, an offset holds for a few
  frames and then jumps, which is what makes it read as a glitch and not a wobble.
- *Colour with detail*: the output is `_Color`, multiplied by the albedo's luminance (blended by `_DetailStrength`),
  so the face, the coat's folds and the moustache still read although everything is one colour.
- *Fresnel rim*: `pow(1 - dot(normal, view), _RimPower) * _RimIntensity`, added to the colour and to the alpha.
- *Scanlines*: a sine of the world-space height, scrolled with time so the lines travel upward; `_ScanDensity` is
  lines per metre, so they keep their spacing whatever the camera does.
- *Flicker*: a hash of the stepped time gives random dips in brightness; `_Flicker` scales them.
- *Reveal*: each vertex carries its height up the figure (0 at the feet, 1 at the head). Pixels above the reveal line
  are thrown away with `clip`, and the line itself is drawn in a bright `_EdgeColor`. `_Reveal` 0 → 1 materialises
  him from the bottom up.
- *Transparency with a depth pre-pass*: see below.

**The depth pre-pass.** A see-through character drawn in one pass shows everything inside itself: the back of the
coat through the front, the teeth through the cheeks, an arm through the chest. So the shader has two passes:

1. `HologramDepth` writes depth only (`ColorMask 0`, `ZWrite On`).
2. `Hologram` draws the colour, alpha blended, with `ZTest LEqual` and no depth write.

Doing both passes renderer by renderer is not enough, because Birkin is eighteen separate skinned meshes (head,
teeth, eyes, hair, hands, coat and so on): the head's colour would be drawn before the hair's depth. So the first
pass has its own LightMode tag, which URP never draws by itself, and a **Render Objects** renderer feature on
`PC_Renderer` (named `Hologram Depth`, event Before Rendering Transparents, pass name `HologramDepth`) draws that
pass for every hologram renderer before any transparent object is drawn. By the time the colour pass runs, the
nearest surface of the whole figure is in the depth buffer, and only that surface is shaded. Both passes move the
vertices with the same glitch function, so their depths match exactly.

**Driver.** `BirkinHologramFX` (on Birkin) sets `_Reveal`, `_GlitchAmount`, `_Flicker`, `_RevealMinY` and
`_RevealHeight` through a `MaterialPropertyBlock`, the same way `ZombieShaderFX` does, so the materials themselves
never change. `Appear` and `Disappear` run the reveal with a big glitch; the cutscene director fires a small glitch
between dialogue lines; a faint one fires by itself every few seconds. The script also switches the Render Objects
feature on while Birkin exists and off again when the cutscene is over, so the feature costs nothing during play.

**Module concepts shown:** vertex displacement, fresnel, procedural patterns in world space, alpha blending and
draw order (the depth pre-pass), `clip`, multi-pass shaders, driving a shader from a script.

**Key lines for a slide**

```hlsl
// vertex: random horizontal bands jump sideways for a few frames
float tick = floor(_Time.y * 14.0);
float band = floor(positionWS.y * _GlitchBands);
float on = step(1.0 - 0.55 * _GlitchAmount, Hash(float2(band, tick)));
float shift = (Hash(float2(tick, band) + 7.3) - 0.5) * 2.0 * _GlitchShift * _GlitchAmount;
positionWS += UNITY_MATRIX_V[0].xyz * (on * shift);
```

```hlsl
// fragment: reveal from the feet up, with a glowing line
half below = _Reveal * (1.0 + _RevealEdgeWidth) - input.height01;
clip(below);
half edge = (1.0 - smoothstep(0.0, _RevealEdgeWidth, below)) * step(_Reveal, 0.999);
```

```hlsl
// fragment: scanlines scrolling upward, and the fresnel rim
half scan = 0.5 + 0.5 * sin((input.positionWS.y * _ScanDensity - _Time.y * _ScanSpeed) * TWO_PI);
half rim = pow(1.0 - saturate(dot(normalWS, viewWS)), _RimPower);
```

```hlsl
// pass 1: depth only, drawn for the whole figure before any colour
Tags { "LightMode" = "HologramDepth" }
ZWrite [_ZWrite]
ColorMask 0
```

---

## 9. TimeTrap/TankFluid and 10. TimeTrap/TankGlass

Two small shaders for the eight specimen tanks of the portal chamber. The bodies floating in the tanks are the
game's own zombie bodies with their usual `ZombieSurface` materials.

**TankFluid** (`M_Tank_Fluid`): a see-through teal column that glows and never looks still.

- two noise layers read through world-space coordinates and scrolled upward at different speeds: slow currents,
  different in every tank
- fresnel: the edge of the column, where the camera looks through more liquid, is denser and brighter
- brighter toward the bottom, where the tank's light is
- alpha blended, no depth write, queue `Transparent-10` so it is drawn before the glass around it

**TankGlass** (`M_Tank_Glass`): clear when looked at straight on, more visible toward its edges, slightly
reflective.

- fresnel drives both the opacity and the brightness
- there is nothing to reflect in a dark hall, so two soft vertical streaks are faked from the direction of the
  reflected view ray; they slide round the cylinder as the camera moves
- Blinn-Phong highlights from the additional lights (the tank lights and the red lamps), through the Forward+ light
  loop

```hlsl
// TankFluid: currents rising through world-space noise
float up = input.positionWS.y * _NoiseScale - _Time.y * _RiseSpeed;
half currents = lerp(1.0, noiseA * noiseB * 4.0, _NoiseStrength);
```

```hlsl
// TankGlass: a faked reflection, placed by where the reflected ray points
half3 reflected = reflect(-viewWS, normalWS);
half streak = pow(saturate(dot(normalize(reflected.xz), half2(0.83, 0.56))), _StreakPower);
```

---

## URP settings these shaders rely on

`Assets/Settings/PC_RPAsset.asset` already had both textures switched on; nothing was changed there.

- **Depth Texture**: beam, dust and spores, fluid (depth colour and foam).
- **Opaque Texture**: portal refraction.
- **`PC_Renderer`**: one renderer feature was added, a Full Screen Pass named `TimeDistortionFullscreen` (injection
  point After Rendering Post Processing, Fetch Color Buffer on, material `TimeDistortionFullscreen`). It is saved
  switched off; `ScreenFX` switches it on only while an effect is showing.
- **`PC_Renderer`**, for the hologram: a Render Objects feature named `Hologram Depth` (event Before Rendering
  Transparents, transparent queue, shader pass `HologramDepth`). Also saved switched off; `BirkinHologramFX`
  switches it on only while Dr Birkin exists.

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

**Part B**, same machine and method, 270 renders per figure. These views are heavier (13–18 ms a render in Stage 2),
and the figures moved by about ±0.4 ms between runs, so treat them as rough.

| Setup | ms per render | Cost |
|---|---|---|
| Bioreactors bridge, neither full-screen pass nor fluid | 16.81 | |
| Full-screen pass only (an effect showing) | 17.64 | about 0.8 ms |
| Fluid only (pool and 84 tank strips) | 17.78 | about 1.0 ms |
| Both | 17.76 | about 1.0 ms together (within the noise of either alone) |
| Chest at 3 m, with / without the 40 outline shells | 12.92 / 13.23 | too small to measure |

The full-screen pass costs nothing while no effect is showing, because the renderer feature is switched off.

**Intro cutscene.** The hologram, the tank shaders and the `Hologram Depth` feature only exist while `IntroCutscene` is
active. After the arrival that root is inactive and the feature is off, so none of it is drawn or updated during
play. At the player's start in Stage 1 the render time was the same with the root inactive and with it switched on
500 m below (25.7 and 30.6 ms against 26.6 and 31.1 ms over two alternating batches of 80 renders, inside the noise of
that measurement).

## Screenshots

`Screenshots/ShaderReview/`, each in Stage 1 and Stage 2 lighting: zombie in the torch, hit flash, frozen,
mid-dissolve, beam in a corridor, beam at a close wall, dust in the beam, spores in the Shelter, portal activating,
portal open, plus the F9 before/after pair. Files ending `_beam_hidden` and `_refraction_off` are comparison shots.

`Screenshots/ShaderReviewB/` (part B): each full-screen state (pulse at 0.5 and 1.0, frost, flash, glitch, danger),
the paused and loss-screen checks, the pit pool and tanks in Stage 1 and Stage 2, a close-up of the waves and foam,
a pickup outlined at 4 m and not at 10 m, the outline fading after collection, a chest, and the F9 pair. The glitch,
flash and collection fade were slowed down to capture them; files ending `_zoomed` use a narrow field of view.
