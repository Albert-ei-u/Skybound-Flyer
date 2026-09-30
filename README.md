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
| ENTER | Start / resume |
| W | Fly forward (hold Shift to boost) |
| S | Slow down / brake (reverse when stopped) |
| Q / E | Turn left / right |
| A / D | Roll left / right (slide sideways) |
| ↑ / ↓ | Climb / descend and land |
| R | Reset drone to the start point |
| H | Show / hide the in-game guide |
| M | Big map with mission markers |
| J | Jobs (mission select) |
| ESC | Pause menu |

Click inside the Game view first so Unity receives keyboard input.

### Cities, traffic and missions

Skybound > Create Drone Training Scene builds one scene per exported city
(New York, London with left-hand traffic, Kigali). On the start/pause menu
press 1/2/3 to switch city. Each city has cars driving in their lanes and
pedestrians on the sidewalks, following the real OSM road network.

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
starts over. Press M for the big map.

## Arduino joystick

Sketch location:

```text
arduino/flight_joystick/flight_joystick.ino
```

Upload it with Arduino IDE using **Arduino Uno**, then close the Serial Monitor
(only one program can use the port). Press Play in Unity: the drone finds the
Arduino automatically and logs "Arduino joystick connected on COMx" in the
Console. To force a port, set **Joystick Port** on the Drone's
`DroneController` component. The keyboard keeps working alongside the sticks.

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
Joystick 1 VRy -> A1    Pitch / UP-DOWN
Joystick 2 VRx -> A2    Yaw / Q-E
Joystick 2 VRy -> A3    Throttle / W-S
Left SW        -> D2    Reset
Right SW       -> D3    Camera
```

Both joystick modules need `5V` and `GND`. The sketch uses `115200` baud.

## GitHub

Commit the source code, Arduino sketch, README, and permitted assets. Only
commit models and audio you have permission to distribute.
