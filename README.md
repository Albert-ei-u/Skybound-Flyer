# Skybound Flight Simulator

Skybound is a Python 3D flight simulator built with Ursina. It includes a procedural city, airports, missions, checkpoints, scoring, landing objectives, joystick input, audio, and level progression.

## Architecture

The project is organized as independent systems. `main.py` owns application
startup and coordinates the systems; reusable configuration and gameplay
systems live under `game/`. World generation, input, audio, missions, and
scoring are kept separate so each can be expanded without rewriting the
flight loop.

Current extracted systems include `game/config.py`, `game/roads.py`, and
`game/city.py`. Landing infrastructure is separated into
`game/landing_sites.py`; airports are treated as reusable aircraft and drone
landing sites instead of being hard-coded into missions. The city builder
returns collision bounds to gameplay systems instead of coupling building
rendering to mission logic.

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

After all checkpoints are cleared and the aircraft lands at the destination airport, the next level starts automatically. Later levels use longer routes and moving aircraft traffic.

Checkpoint points are awarded for clearing the gate, altitude accuracy, speed, and remaining time. A successful destination landing awards an additional bonus.

## Roadmap: drone evolution

The current aircraft is a temporary flight prototype. The planned vehicle
will become an original drone system as development continues:

- Replace the aircraft model with a detailed drone model
- Add drone-style hover, vertical movement, yaw, pitch, and roll physics
- Add first-person drone camera and stabilized chase camera modes
- Add drone delivery, inspection, tracking, and emergency missions
- Add payload weight, battery/energy, signal range, and return-to-base systems
- Add assisted flight and optional manual joystick control
- Add landing pads, rooftops, helipads, and restricted airspace
- Add civilian and emergency drone traffic to the city
- Add drone-specific scoring for precision, battery use, safety, and time

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
