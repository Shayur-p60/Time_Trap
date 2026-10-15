# Time Trap

**A first-person survival-horror game made in Unity 6 (URP).**

COMP307 Group Project 2026 · **Dead Reckoning Games**

You're sent back in time through a portal to the night a genetics facility's zombie outbreak began. Before the portal collapses, you have to restore power, take the pathogen sample, download the research data, and get back out through the portal at Gate B.

---

## Team

| Name | Student number |
|---|---|
| Tharish Dayanand | 223004453 |
| Mfundo Shwabede | 223059561 |
| Simbonge Ndlovu | 223059617 |
| Yakhani Dlamini | 223063732 |
| Shayur Pragdheen | 224014462 |

---

## Running the game (build)

Builds are provided for **macOS (Apple silicon, M1 or later)** and **Windows (64-bit)**.
Download the zip for your system from the [v1.0 release](https://github.com/Shayur-p60/Time_Trap/releases/tag/v1.0) (under **Assets**) and unzip it. Keep everything in the unzipped folder together.

### macOS

1. Unzip `TimeTrap-macOS.zip` and find `Time Trap.app`.
2. **The first time:** the app isn't signed, so macOS blocks it. Right-click the app, choose **Open**, then **Open** again.
   On macOS Sequoia (15) or later that option may not appear. Try to open the app once, then go to
   **System Settings → Privacy & Security**, scroll down and click **Open Anyway**.
3. If macOS says the app is "damaged", open Terminal and run:
   `xattr -dr com.apple.quarantine "/path/to/Time Trap.app"`
   (type the command up to the quote, then drag the app into the Terminal window).
4. The game starts fullscreen at your screen's resolution. Press **Esc** in-game for the pause menu.

Log if something goes wrong: `~/Library/Logs/Dead Reckoning Games/Time Trap/Player.log`

### Windows

1. Unzip `TimeTrap-Windows.zip`. Run `Time Trap.exe` from inside the unzipped folder. It needs the files next to it
   (`UnityPlayer.dll`, the `Time Trap_Data` folder), so don't move the .exe on its own.
2. **The first time:** the app isn't signed, so Windows SmartScreen may show "Windows protected your PC".
   Click **More info → Run anyway**.
3. The game starts fullscreen at your screen's resolution. Press **Esc** in-game for the pause menu.

Log if something goes wrong: `%USERPROFILE%\AppData\LocalLow\Dead Reckoning Games\Time Trap\Player.log`

## Running from source

Works on Windows or macOS.

1. Install **Unity 6000.5.9f1** through Unity Hub. Use this exact version.
   (To build for the other platform, add its Build Support module in Unity Hub → Installs → ⚙ → Add modules.)
2. Clone the repository:

```
   git clone https://github.com/Shayur-p60/Time_Trap.git
```

3. In Unity Hub: **Add → Add project from disk** and pick the `Time_Trap` folder. The first import takes a while.
4. Open **`Assets/Scenes/MainGame.unity`** and press **Play**.
5. **To build:** File → Build Profiles → macOS or Windows → Build. `MainGame` is the only scene in the build.

**Dev keys** (work in the editor and in development builds only; on a Mac, hold **Fn**):
F5 completes the next objective · F6 sets the timer to 30 s · F7 simulates a zombie catch · F8 pauses the timer.

---

## Controls

| Action | Key |
|---|---|
| Move | W A S D / arrow keys |
| Look | Mouse |
| Sprint | Left Shift |
| Crouch | Left Ctrl |
| Interact / hold to download | E |
| Torch on/off | F |
| Switch torch / pistol | Q |
| Fire | Left mouse |
| Reload | R |
| Pause | Esc |
| Skip the intro cutscene | Hold Space |
| Shader before/after (plain URP vs our shaders) | F9 |

---

## How to play

- **Goal:** restore power at the generator, take the pathogen sample and download the research data, in any order. Then reach the **time portal at Gate B** before the timer runs out.
- **Win:** escape through the portal → *Containment Successful*.
- **Lose:**
  - *Portal Failure*: the timer runs out.
  - *Infected*: you run out of lives. Each zombie catch costs a life and 10 seconds (5 on Easy, 15 on Hard), and sends you back to your last checkpoint.
  - *Lost in the Dark*: you fall to your death with no lives left.
- **Difficulty:**

| | Easy | Normal | Hard |
|---|---|---|---|
| Time limit | 6:00 | 5:00 | 4:00 |
| Lives | 5 | 3 | 2 |

- **Tips:**
  - Zombies see your torch from twice as far away, so switch it off to sneak.
  - Sprinting is loud.
  - Headshots do triple damage.
  - Restoring power unlocks the Armory, which holds the pistol, but it also makes the zombies more aggressive.

---

## Features

**Story and presentation**
- An intro cutscene built with Unity Timeline: a typewriter intro, then a dialogue scene with **Dr Birkin**, a holographic AI guide with a recorded voice, blinking and voice-driven mouth movement.
- An original portal chamber, a walk into the portal, and an arrival portal that collapses behind you.
- In-game radio messages from Dr Birkin.

**Movement and mechanics**
- First-person controller with sprint, crouch, stamina (with exhaustion and panting) and head bob, with footsteps synced to the stride.
- A torch with a draining battery, and a pistol with limited ammo, reloading and headshots.
- Doors and power-locked areas, a generator, a hold-to-download terminal, chests, checkpoints, and a fall-death pit.
- Power-ups: Flash Pulse (stun), Speed Boost, Zombie Freeze, and a Compass that points to the nearest objective.
- Pickups are placed randomly each run.

**Zombie AI**
- A finite state machine: wander, investigate, alert (the scream), chase, attack (with a wind-up before each catch), search, door opening, staggered, stunned, and dead. Zombie Freeze holds them all in place.
- **Sight** depends on light: 5 m in the dark, 11 m when your torch is on, 14 m once power is back, with line-of-sight checks.
- **Hearing** uses NavMesh path distance, so sound travels around walls, not through them.
- A spawner and director: population caps per stage, runners in Stage 3, and a slow, tough **Bloater brute**.
- 3D zombie audio with occlusion muffling.

**Extensions**
- Main menu with a live fly-through, settings (audio, controls, display), three difficulty levels and a pause menu.
- **17 achievements (1000 TP)** with an Xbox-style unlock pop-up, an achievements screen and saved progress. Unlocking them all rewards you with a golden torch beam.
- End screens with run stats, personal bests and tips.
- An audio mixer with separate groups for music, sound effects, zombies, ambience and voice, plus ducking and a muffled pause effect.

---

## Custom shaders (hand-written HLSL, `Assets/_Shaders/`)

| Shader | What it does / techniques |
|---|---|
| `ZombieSurface` | Half-Lambert diffuse, Blinn-Phong specular, fresnel rim, spherical-harmonics ambient; noise dissolve on death with a cyan edge; hit flash, freeze and stun effects |
| `FlashlightBeam` | A 12-step ray-marched volumetric torch beam that stops at the scene depth |
| `DustParticles` | Soft particles lit by the torch through global shader values |
| `TimePortal` | Vertex ripple, polar-coordinate swirl, refraction using the opaque texture, an activation parameter |
| `TimeDistortionFullscreen` | A full-screen pass: glitch, pulse, frost, flash and danger effects |
| `BioreactorFluid` | Sum-of-sines vertex waves, depth-based colour, foam |
| `PickupHighlight` | Inverted-hull outline that reacts to distance and the torch |
| `Hologram` | Fresnel rim, world-space scanlines, flicker, vertex glitch bands, bottom-up reveal, depth pre-pass |
| `TankFluid` | Animated emissive fluid inside the culture tanks |
| `TankGlass` | Transparent fresnel glass |

Press **F9** in-game to swap them all for Unity's standard URP Lit shader and compare. Full write-ups are in `Assets/_Shaders/README_Shaders.md`.

---

## Original models (made by our team in Blender)

- **Dr Birkin**, the AI guide character
- **The portal chamber** (intro cutscene room, culture tanks, portal ring)
- **The control panel / generator console**
- **The elevator**

All third-party models, animations, sounds and music are credited in **`Assets/_Audio/CREDITS.md`** and on the in-game Credits screen.

---

## Project structure

```
Assets/
  Scenes/MainGame.unity     the game (only scene in the build)
  _Scripts/                 game code (AI, Game, Audio, Shaders, UI, Cutscene, ...)
  _Shaders/                 the hand-written HLSL shaders + README_Shaders.md
  _Prefabs/                 zombies, pickups, player
  _Models/                  characters and environment models
  _Audio/                   sounds, music, voice, mixer, CREDITS.md
  _Archive/                 old scenes and unused assets, kept for reference
VoiceOriginals/             original voice recordings + normalise script
```

---

## Changes since Deliverable 1

- **Briefing:** the hub control room briefing became an intro cutscene with a holographic Dr Birkin and a portal chamber.
- **Objectives:** all three can now be completed in any order.
- **Not implemented:** the chest rewards "reveal part of the map" and "extra time", the player's own voiced lines, and returning to the hub after a win.
