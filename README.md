# Skybound Flight Simulator

Skybound is a Unity 6 / C# drone familiarisation simulator. It builds real
cities from OpenStreetMap data and includes traffic, pedestrians, missions,
checkpoints, scoring, and level progression.

## Project layout

The Unity project is the repository root (`ProjectSettings/`, `Packages/`,
`Library/`). C# scripts live under `assets/Scripts/` and `assets/Editor/`.
After Unity finishes compiling, choose `Skybound > Create Drone Training Scene`
to generate the playable drone scene automatically.

## Realistic Unity city

The scene builder (`assets/Editor/SkyboundSceneBuilder.cs` + `CityImporter.cs`)
reads exported city JSON files from `assets/cities/` (for example
`midtown_manhattan.json`), extrudes every OSM building footprint to its real
height with procedural glass/brick/limestone/concrete facades, lays textured
roads, adds a procedural sky, fog and soft shadows, imports the carbon drone
model via glTFast, and attaches the uGUI flight HUD
(`assets/Scripts/FlightHud.cs`: compass, artificial horizon, speed/altitude,
battery, minimap, start/pause menu). Generated meshes and the scene are
rebuilt on demand and not committed. Keep the required OpenStreetMap
attribution in the training build.

## How to play

| Key | Action |
| --- | --- |
| ← ↑ → ↓ + ENTER | Move through any menu and choose |
| W | Fly forward (hold Shift to boost) |
| S | Slow down / brake (reverse when stopped) |
| Q / E | Turn left / right |
| A / D | Roll left / right (slide sideways) |
| ↑ / ↓ | Climb / descend and land |
| R | Reset drone to the start point |
| H | Show / hide the in-game guide |
| M | Big map with mission markers |
| J | Jobs (mission select) |
| ESC | Pause menu / back |

Click inside the Game view first so Unity receives keyboard input.

### Cities, traffic and missions

Skybound > Create Drone Training Scene builds one scene per city file in
`assets/cities/` (New York, London with left-hand traffic, Kigali, plus any
you download). Switch city from **JOBS > CITY**. Each city has cars driving
in their lanes and pedestrians on the sidewalks, following the real OSM road
network. People swing their arms and legs as they walk, some stand in
groups chatting, and anyone you buzz below head height runs away.

**More real cities.** Skybound > Download Real City… fetches buildings and
streets for any place on Earth from OpenStreetMap (presets: Paris, Tokyo,
Dubai, Sydney, San Francisco, Nairobi, Hong Kong, Rio, Cape Town, Singapore,
Berlin, Toronto, or type coordinates). It saves `assets/cities/<name>.json`
and offers to rebuild the scenes. Needs internet.

**Endless world.** Fly out of the city in any direction: terrain keeps
generating around you (countryside, forests, mountains with snow, beaches)
and an endless ocean lies a few kilometres out on one side of every city.
Splashing into the sea counts as a crash. The status line (top-left) tells
you where you are.

**Hangar.** JOBS > HANGAR (or Pause > HANGAR) swaps between five drones
with different handling: Carbon X4 (balanced), Skybound S1 (light, climbs
fast), Racer FPV (very fast), Phantom Pro (steady, easy to land) and Hex
Lifter (six rotors, slow and stable). The choice is saved.

**Music.** Free roam plays the original theme. Every job level has its own
track that gets busier as the levels go up, plus effects (motor hum,
checkpoint ding, mission passed, wasted, splash). To use your own music for
a level, drop a file named `m<mission>_l<level>` (e.g. `m3_l2.ogg`) in
`assets/audio/levels/` and rebuild the scenes.

**Real 3D people (optional).** Put humanoid FBX models in
`assets/models/people/` (for example Mixamo characters downloaded with a
"Walking" animation, *In Place* ticked, format *FBX for Unity*) and rebuild
the scenes: they replace the built-in pedestrians and walk with that
animation.

**Jobs (GTA-style mission select).** Press ENTER on the title screen or J in
flight to open the job list. Missions unlock in order:

| Mission | Type | Levels |
| --- | --- | --- |
| 1. First Flight | Checkpoint race (low rings) | 3 |
| 2. Express Delivery | Pick up and deliver a parcel | 3 |
| 3. Skyline Race | High, fast rings between towers | 3 |
| 4. Medical Emergency | Several drop-offs against the clock | 3 |

Only Mission 1 / Level 1 is open at first. Finishing a level unlocks the
next level; finishing a mission's last level unlocks the next mission.
Completed levels can be replayed. During a job, follow the purple GPS route
on the radar (bottom-left, calculated along the real roads), the yellow
blip and the yellow arrow under the compass. Hover inside yellow markers to
pick up / drop off; fly through blue rings. Crashing shows WASTED and the
job restarts. Progress and cash are saved; RESET PROGRESS on the job screen
starts over. Press M for the big map. Crashing in free roam also shows
WASTED and respawns you at the drone base.

## Arduino joystick

Sketch location:

```text
arduino/flight_joystick/flight_joystick.ino
```

1. Plug the Arduino Uno in with its USB cable.
2. In the Arduino IDE open the sketch, choose **Tools > Board > Arduino Uno**
   and **Tools > Port > COMx** (the port you were given; it is the one that
   appears when you plug the board in).
3. Click **Upload**. To check the wiring, open **Tools > Serial Monitor** at
   **115200 baud**: lines like `512,509,515,520,0,0,0` should change as you
   move the sticks. **Close the Serial Monitor afterwards** - only one
   program can use the port, and Unity cannot connect while it is open.
4. In Unity press Play. The game finds the Arduino by itself; the title and
   pause screens show "Joystick connected on COMx". If it does not, open the
   pause menu (ESC) and press **JOYSTICK PORT** until it shows your port: it
   is saved and reconnects at once. (Player Settings > Api Compatibility
   Level must stay on **.NET Framework** for serial ports.)

The keyboard keeps working alongside the sticks.

| Stick | Action |
| --- | --- |
| Left stick left/right | Roll (slide sideways) |
| Left stick up/down | Climb / descend |
| Right stick left/right | Turn |
| Right stick up | Fly forward (further = faster) |
| Right stick down | Brake / reverse |
| Left stick button | Reset drone |

### Wiring

```text
Joystick 1 VRx -> A0    Roll / A-D
Joystick 1 VRy -> A1    Climb / UP-DOWN
Joystick 2 VRx -> A2    Yaw / Q-E
Joystick 2 VRy -> A3    Throttle / W-S
Left SW        -> D2    Reset
Right SW       -> D3    Camera
```

Both joystick modules need `5V` and `GND`. The sketch uses `115200` baud.

## GitHub

Commit the source code, Arduino sketch, README, and permitted assets. Only
commit models and audio you have permission to distribute.
