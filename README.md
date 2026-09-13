# Time Trap

A first-person survival-horror game built in Unity (URP) for COMP307.

## Premise

A time-displaced operative is sent into a secret government research facility on the day
its zombie outbreak began. The player must restore power, retrieve two objectives, and
escape through a time portal before a countdown expires — succeeding where history failed.

Full design details (stages, world layout, characters, win/loss conditions) are in
`Deliverable1_GameConceptOverview.docx`.

## Engine / Setup

- Unity 6 (6000.5.9f1), Universal Render Pipeline (URP)
- Main gameplay scene: `Assets/Scenes/MainGame`
- Facility build (work in progress, kept separate to avoid scene conflicts): `Assets/Scenes/FacilityBuild`
- Git LFS is used for large binary assets (models, textures, audio) — make sure Git LFS is
  installed locally before cloning, or large files will pull down as broken placeholder pointers.

## Current Progress

### Done
- Project set up on URP with organized folder structure (`_Models`, `_Prefabs`, `_Scripts`,
  `_Shaders`, `ThirdParty`)
- First-person movement, sprint, and camera look (Easy Peasy First Person Controller)
- Flashlight: modeled prop (not just a bare light), correctly scaled, gripped in the right
  hand, toggle-ready, correctly tracks camera pitch/yaw
- Player arm rig: full humanoid rig imported via Mixamo, arms isolated from the body in
  Blender so only the arms render in first-person view; Idle animation wired through an
  Animator Controller; speed-based arm sway responding to movement state
- Player hierarchy packaged as a Prefab (`_Prefabs/Player.prefab`) to avoid scene-merge
  conflicts while multiple people work on the project
- One custom shader written: `DissolveURP.shader` (HLSL, URP) — noise-based dissolve
  with a glowing edge, intended for the zombie Dead state. Written and ready to test/wire
  up against an actual zombie object.
- Facility build started in its own isolated scene (`FacilityBuild`); first room pack
  imported and being test-fitted for scale against existing geometry

### In Progress
- Facility/map layout (corridors, Generator Room, Armory, Research Lab wing, side rooms)
- Zombie AI (5-state FSM: Patrol / Investigate / Chase / Search / Dead) and NavMesh setup
- Walk and Run animations for the player arms (currently Idle + sway only)

### Not Started Yet
- Weapon/Armory system (deliberately deferred to the Final Deliverable — see design notes)
- Win/loss condition logic (countdown timer, objective tracking, checkpoint respawn)
- Additional shaders (flashlight is currently a built-in Spot Light + URP Lit material,
  not yet a hand-authored lighting shader)

## Known Issues / Notes for Whoever Picks This Up

- There are disabled leftover objects (`arms2`, an old `Flashlight` duplicate) still sitting
  in `MainGame` as a fallback from an earlier working version — safe to ignore, will be
  removed once the current player setup is fully confirmed stable.
- `DissolveURP.shader` has not yet been tested against real zombie geometry — needs a
  material created from it, a noise texture assigned, and the `DissolveOnDeath` script
  wired to whatever triggers zombie death.

## Third-Party Assets (confirm licenses before final submission)

- **Easy Peasy First Person Controller** — free, Unity Asset Store, open-source
- **Player character rig + Idle/Walk/Run animations** — Mixamo (Adobe), free for use in any project
- **Flashlight model** — Sketchfab, downloaded as GLB — *[fill in exact listing/creator/license here]*
- **Facility room pack**
