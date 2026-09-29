# Skybound Flight Simulator

Skybound is a Python 3D drone familiarisation simulator built with Ursina. It includes a procedural development city, landing sites, missions, checkpoints, scoring, joystick input, audio, and level progression.

## Architecture

The project is organized as independent systems. `main.py` owns application
startup and coordinates the systems; reusable configuration and gameplay
systems live under `game/`. World generation, input, audio, missions, and
scoring are kept separate so each can be expanded without rewriting the
flight loop.

Current extracted systems include `game/config.py`, `game/roads.py`,
`game/city.py`, `game/traffic.py`, and `game/missions.py`. Landing infrastructure is separated into
`game/landing_sites.py`; airports are treated as reusable drone
landing sites instead of being hard-coded into missions. The city builder
returns collision bounds to gameplay systems instead of coupling building
rendering to mission logic. `TrafficManager` owns non-player aircraft and
level-based activation, leaving room for future air-traffic routes, separation
rules, and drone collision policies.
Mission routes, checkpoint scoring, landing validation, and level progression
are coordinated by `MissionSystem`, keeping gameplay rules separate from the
rendering and flight-control loop.

Buildings are generated at the centres of road blocks rather than on the road
grid. The HUD identifies the regional city grid and indicates when air traffic
will activate in Level 2. The world is divided into named districts such as
Harbor District, Central City, Midtown, Uptown, and Airport Corridor; district
identity and visual building variation are derived from world coordinates.
Each district now has a recognizable landmark and the road network includes
sparse street furniture to improve scale and navigation from the air.
The playable bounds now cover a regional 6000 x 5400 world with a dense core
and a lower-density outer ring. A large collidable terrain foundation keeps
the drone above the ground surface and prevents it from falling below roads.

## Real-city data pipeline

The optional city pipeline uses OSMnx to export real OpenStreetMap roads and
building footprints into a local JSON dataset. Install its dependencies with:

```powershell
python -m pip install -r requirements-city.txt
```

Then export a first New York training area:

```powershell
python tools\export_city.py "Queens, New York, USA" `
  --output assets\cities\nyc_queens.json --distance 5000
```

The generated dataset is ignored by Git because it is derived content. Keep
the required OpenStreetMap attribution in the training build. The next runtime
step is loading this dataset into Ursina and replacing the procedural fallback
roads/buildings.

## Unity/C# migration

The production migration is being built in `unity/` using Unity 6.6 and C#.
The first slice contains `DroneController.cs` and `DroneCamera.cs`. The Python
version remains available as a reference while we migrate flight controls,
missions, city data, Arduino input, and scoring in separate steps.

Run the configuration tests with:

```powershell
python -m unittest discover -s tests
```

## Installation

```powershell
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

## Run

Keyboard mode:

```powershell
python .\main.py
```

Arduino mode:

```powershell
python .\main.py --port COM12
```

Replace `COM12` with the port shown by `python -m serial.tools.list_ports`. Close Arduino Serial Monitor before starting the game.

## Keyboard controls

| Key | Action |
|---|---|
| `W` / `S` | Increase / reduce throttle |
| `UP` / `DOWN` | Pitch up / down |
| `A` / `D` | Roll and steer |
| `Q` / `E` | Yaw left / right |
| `SPACE` | Brake |
| `C` | Chase/cockpit camera |
| `R` | Reset mission |
| `F11` | Toggle fullscreen |
| `ESC` | Quit |

## Arduino joystick

Sketch location:

```text
arduino/flight_joystick/flight_joystick.ino
```

Upload it with Arduino IDE using **Arduino Uno**.

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

## Missions and levels

Level 1 starts at LGA:

```text
LGA -> checkpoints -> JFK landing
```

After all checkpoints are cleared and the drone lands at the destination site, the next level starts automatically. Later levels use longer routes and moving traffic.

Checkpoint points are awarded for clearing the gate, altitude accuracy, speed, and remaining time. A successful destination landing awards an additional bonus.

## Roadmap: training product

The player vehicle is now an original quadcopter drone. The current city
geometry is a procedural development fallback, not a survey-accurate map.
Production scenarios will load permitted real-city road and building data,
such as an OpenStreetMap/GeoJSON export, with a documented local coordinate
origin.

- Import a real city dataset with road, building, and landing-zone metadata
- Replace fallback roads and buildings with imported geometry
- Add drone-style hover, vertical movement, yaw, pitch, and roll physics
- Add first-person drone camera and stabilized chase camera modes
- Add drone delivery, inspection, tracking, and emergency missions
- Add payload weight, battery/energy, signal range, and return-to-base systems
- Add assisted flight and optional manual joystick control
- Add landing pads, rooftops, helipads, and restricted airspace
- Add civilian and emergency drone traffic to the city
- Add drone-specific scoring for precision, battery use, safety, and time

## External drone model

The game checks `assets/models/carbon_drone/` for `carbon_drone.obj`,
`carbon_drone.gltf`, or `carbon_drone.glb`. The external model is loaded only
when requested because large GLB files can take a long time to convert on the
first startup. Run with `--drone-model` after optimizing the asset. Without
that flag, the game starts immediately with the built-in procedural quadcopter.
Add only a model that the project has legal permission to redistribute.

## Audio

Place permitted audio files in `assets/audio/`:

```text
flight_music.ogg
takeoff.wav
checkpoint.wav
landing.wav
```

The game also detects another `.ogg`, `.mp3`, or `.wav` music file in that folder. Only commit audio you have permission to distribute.

## GitHub

Commit the source code, Arduino sketch, requirements file, README, and permitted audio assets. The Python virtual environment is excluded by `.gitignore` and should not be committed.

## Unity/C# migration

The active Unity 6.6 project is the repository root because Unity has created
`ProjectSettings/`, `Packages/`, and `Library/` there. C# migration files are
under `assets/Scripts/` and `assets/Editor/`. After Unity finishes compiling,
choose `Skybound > Create Drone Training Scene` to generate the first playable
drone scene automatically.

## Realistic Unity city (Midtown Manhattan)

The Unity scene is built from real OpenStreetMap data:

1. Export the city (already done for Times Square, ~900 m radius):
   `python tools\export_city.py "Times Square, New York, USA" --output assets\cities\midtown_manhattan.json --distance 900`
2. Open the project root in Unity 6 and run **Skybound > Create Drone Training Scene**.

The builder (`assets/Editor/SkyboundSceneBuilder.cs` + `CityImporter.cs`)
extrudes every OSM building footprint to its real height with procedural
glass/brick/limestone/concrete facades, lays textured roads, adds a
procedural sky, fog and soft shadows, imports the carbon drone model via
glTFast, and attaches the uGUI flight HUD (`assets/Scripts/FlightHud.cs`:
compass, artificial horizon, speed/altitude, battery, minimap, start/pause
menu). Generated meshes and the scene are rebuilt on demand and not committed.

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
