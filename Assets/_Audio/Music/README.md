# Music

Drop the music files in this folder, then assign each one to its slot on `Map/Gameplay/MusicManager` in `MainGame.unity`.
Every slot may stay empty: an empty slot is silent and logs nothing.

| File name | MusicManager slot | Notes |
| --- | --- | --- |
| `Music_Menu_Loop` | Menu Loop | Seamless loop. Plays while the main menu is up. |
| `Music_Cutscene_Loop` | Cutscene Loop | Seamless loop. Plays through the intro cutscene, from the Play click to the arrival in the facility. |
| `Music_Ambient_Loop` | Ambient Loop | Seamless loop. Cross-fades in over 2 s when Play is pressed (after the intro: at the arrival). |
| `Music_Chase_Loop` | Chase Layer | Seamless loop, same tempo and key as the ambient loop: it plays on top of it while a zombie is chasing the player, and all through Stage 3. |
| `Music_Win_Stinger` | Win Stinger | One-shot. Plays on the win screen while the loops fade out. |
| `Music_Loss_Stinger` | Loss Stinger | One-shot. Plays on any loss screen while the loops fade out. |
| `Luke_s Song` | Credits Track | Plays while the Credits screen is open, from its start each time, looping; the menu loop fades out under it and returns on Back. |

Any format Unity imports will do (`.ogg` loops most cleanly; `.mp3` can leave a small gap at the loop point).
For the four loops, set **Load Type: Streaming** in the import settings. Each slot has its own volume, and
everything plays through the **Music** group of `TimeTrapMixer`, so the Music slider in Settings controls it.
