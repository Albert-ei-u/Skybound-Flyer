
"""Skybound: a small, self-contained 3D flight simulator."""

import subprocess
import argparse
from pathlib import Path

try:
    import serial
except ImportError:
    serial = None

try:
    import pygame
except ImportError:
    pygame = None

from ursina import (
    Ursina, Entity, Sky, DirectionalLight, Text, camera, color,
    Vec3, held_keys, application, time, lerp, Audio, Sequence, Func,
)

from game.config import (AIRPORTS, CITY_BOUNDS, GROUND_Y, PLANE_GROUND_Y,
                         RUNWAY_Z)
from game.city import CityBuilder, district_name
from game.landing_sites import LandingSiteBuilder, drone_pad_position
from game.missions import MissionManager as MissionSystem
from game.roads import RoadNetwork
from game.traffic import TrafficManager

CITY_OBSTACLES = []


class GameLoop(Entity):
    """Ursina automatically calls update() on every Entity each frame."""
    def __init__(self, game):
        super().__init__(ignore_paused=True)
        self.game = game

    def update(self):
        self.game.update()


class SoundManager:
    """Uses built-in tones to create music without external downloads."""
    def __init__(self):
        self.pygame_ready = False
        self.music_file = None
        self.pygame_sounds = {}
        self._setup_pygame_audio()
        # Original tropical/ocean-adventure style melody. This is not a
        # recording or reproduction of an existing soundtrack.
        self.music = Sequence(
            Func(self._note, 1.00), .30,
            Func(self._note, 1.19), .30,
            Func(self._note, 1.50), .30,
            Func(self._note, 1.34), .30,
            Func(self._note, 1.19), .30,
            Func(self._note, 1.00), .30,
            Func(self._note, 0.89), .30,
            Func(self._note, 1.00), .60,
            loop=True,
        )
        self.music.start()
        self.bass_music = Sequence(
            Func(self._bass_note, 0.50), 0.60,
            Func(self._bass_note, 0.59), 0.60,
            Func(self._bass_note, 0.67), 0.60,
            Func(self._bass_note, 0.59), 0.60,
            loop=True,
        )
        self.bass_music.start()
        self.flight_music = Sequence(
            Func(self._flight_note, 1.00), .42,
            Func(self._flight_note, 1.26), .42,
            Func(self._flight_note, 1.50), .42,
            Func(self._flight_note, 1.26), .42,
            Func(self._flight_note, 1.12), .42,
            Func(self._flight_note, 0.89), .42,
            Func(self._flight_note, 1.12), .42,
            Func(self._flight_note, 1.34), .84,
            loop=True,
        )
        self.flight_music.start()
        self.flight_music.pause()
        self.engine = Audio('noise', loop=True, autoplay=True, volume=0)
        self.takeoff = Audio('triangle', autoplay=False, volume=0.35)
        self.checkpoint = Audio('triangle', autoplay=False, volume=0.35)
        self.checkpoint_high = Audio('triangle', autoplay=False, volume=0.30)
        self.landing = Audio('sine', autoplay=False, volume=0.4)

    def _setup_pygame_audio(self):
        """Use real beat/music files when present in assets/audio."""
        if pygame is None:
            return
        audio_dir = Path('assets/audio')
        if not audio_dir.exists():
            return
        try:
            pygame.mixer.init()
            self.pygame_ready = True
            music_candidates = [
                audio_dir / 'flight_music.ogg',
                audio_dir / 'flight_music.wav',
                audio_dir / 'flight_music.mp3',
            ]
            # If no standard name exists, use the first music asset supplied
            # in the folder, including names such as DST-TowerDefenseTheme_1.mp3.ogg.
            music_candidates.extend(sorted(audio_dir.glob('*.ogg')))
            music_candidates.extend(sorted(audio_dir.glob('*.mp3')))
            music_candidates.extend(sorted(audio_dir.glob('*.wav')))
            for candidate in music_candidates:
                if candidate.exists() and candidate.name.lower() != 'readme.txt':
                    self.music_file = candidate
                    pygame.mixer.music.load(str(candidate))
                    pygame.mixer.music.set_volume(0.35)
                    pygame.mixer.music.play(-1)
                    print(f'Background music loaded: {candidate.name}')
                    break
            for key, filename in {
                'takeoff': 'takeoff.wav',
                'checkpoint': 'checkpoint.wav',
                'landing': 'landing.wav',
            }.items():
                candidate = audio_dir / filename
                if candidate.exists():
                    self.pygame_sounds[key] = pygame.mixer.Sound(str(candidate))
        except pygame.error as error:
            print(f'Pygame audio unavailable: {error}')
            self.pygame_ready = False

    def _note(self, pitch):
        Audio('triangle', autoplay=True, auto_destroy=True, volume=0.09, pitch=pitch)

    def _bass_note(self, pitch):
        Audio('sine', autoplay=True, auto_destroy=True, volume=0.06, pitch=pitch)

    def _flight_note(self, pitch):
        Audio('triangle', autoplay=True, auto_destroy=True, volume=0.075, pitch=pitch)

    def begin_flight_music(self):
        if self.music_file and self.pygame_ready:
            pygame.mixer.music.unpause()
        self.music.pause()
        self.bass_music.pause()
        self.flight_music.start()

    def reset_music(self):
        if self.music_file and self.pygame_ready:
            pygame.mixer.music.pause()
        self.flight_music.pause()
        self.music.start()
        self.bass_music.start()
        if self.music_file and self.pygame_ready:
            self.music.pause()
            self.bass_music.pause()

    def update_engine(self, speed, throttle):
        self.engine.volume = min(0.055, 0.008 + throttle * 0.035 + speed / 5000)

    def play_takeoff(self):
        self.takeoff.play()
        if 'takeoff' in self.pygame_sounds:
            self.pygame_sounds['takeoff'].play()
        self.begin_flight_music()
        # Windows includes a speech engine, so this needs no voice asset.
        speech_script = (
            "Add-Type -AssemblyName System.Speech; "
            "$v = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
            "$v.Speak('We are ready to take off'); $v.Dispose()"
        )
        try:
            subprocess.Popen(
                ['powershell.exe', '-NoProfile', '-WindowStyle', 'Hidden',
                 '-Command', speech_script],
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                creationflags=subprocess.CREATE_NO_WINDOW,
            )
        except (OSError, AttributeError):
            # The takeoff sound still plays if Windows speech is unavailable.
            pass

    def play_checkpoint(self):
        # Bright two-note confirmation when a gate awards points.
        self.checkpoint.play()
        self.checkpoint_high.pitch = 1.8
        self.checkpoint_high.play()
        if 'checkpoint' in self.pygame_sounds:
            self.pygame_sounds['checkpoint'].play()

    def play_landing(self):
        self.landing.play()
        if 'landing' in self.pygame_sounds:
            self.pygame_sounds['landing'].play()


class ArduinoJoystick:
    """Reads the CSV stream sent by arduino/flight_joystick.ino."""
    def __init__(self, port='COM3', baudrate=115200):
        self.port = port
        self.connection = None
        self.last_camera = 0
        self.last_reset = 0
        if serial is not None:
            try:
                self.connection = serial.Serial(port, baudrate, timeout=0)
                print(f'Joystick connected on {port}')
            except (serial.SerialException, OSError) as error:
                print(f'Could not open joystick on {port}: {error}')
                print('Keyboard controls remain active.')

    @property
    def status(self):
        if serial is None:
            return 'JOYSTICK: INSTALL PYSERIAL'
        return f'JOYSTICK: CONNECTED {self.port}' if self.connection else f'JOYSTICK: NOT FOUND {self.port}'

    def poll(self):
        if self.connection is None:
            return None
        latest = None
        while self.connection.in_waiting:
            line = self.connection.readline().decode('ascii', errors='ignore').strip()
            values = line.split(',')
            if len(values) >= 7:
                try:
                    joy1_x, joy1_y, joy2_x, joy2_y, brake, camera_button, reset_button = map(int, values[:7])
                    def axis(value, invert=False):
                        result = (512 - value if invert else value - 512) / 512.0
                        return 0.0 if abs(result) < 0.10 else max(-1.0, min(1.0, result))
                    latest = {
                        'roll': axis(joy1_x),
                        'pitch': axis(joy1_y, invert=True),
                        'yaw': axis(joy2_x),
                        'throttle': axis(joy2_y, invert=True),
                        'brake': brake == 1,
                        'camera': camera_button == 1 and self.last_camera == 0,
                        'reset': reset_button == 1 and self.last_reset == 0,
                    }
                    self.last_camera = camera_button
                    self.last_reset = reset_button
                except ValueError:
                    pass
        return latest


class World:
    def __init__(self):
        self.traffic = TrafficManager()
        Sky()
        sun = DirectionalLight()
        sun.look_at(Vec3(1, -2, -1))
        sun.color = color.rgba(255, 245, 220, 255)

        Entity(
            model='cube', scale=(10000, 1, 10000), position=(0, -0.5, 900),
            color=color.rgb(58, 115, 62), texture='white_cube',
            texture_scale=(300, 300), collider='box',
        )
        self.landing_sites = LandingSiteBuilder(AIRPORTS)
        self.landing_sites.build()

        self._build_roads()

        CITY_OBSTACLES.extend(CityBuilder(AIRPORTS).build())

    def set_level(self, level):
        self.traffic.set_level(level)

    def update(self, dt):
        self.traffic.update(dt)

    def _build_airport(self, name, x, z):
        # Compatibility wrapper for older callers.
        LandingSiteBuilder({name: (x, z)}).build()

    def _build_roads(self):
        RoadNetwork().build()


class Checkpoint:
    def __init__(self, position, number, active=False):
        self.position = Vec3(*position)
        self.number = number
        self.root = Entity(position=self.position, enabled=active)
        self.root.color = color.azure
        # A simple square gate that the aircraft flies through.
        Entity(parent=self.root, model='cube', scale=(2, 22, 2),
               position=(-18, 0, 0), color=color.azure)
        Entity(parent=self.root, model='cube', scale=(2, 22, 2),
               position=(18, 0, 0), color=color.azure)
        Entity(parent=self.root, model='cube', scale=(38, 2, 2),
               position=(0, 20, 0), color=color.azure)

    def set_active(self, active):
        self.root.enabled = active


class MissionManager:
    """Controls routes, levels, scoring, completion, and failure."""
    LEVELS = [
        ('LGA TO JFK', 360, (('LGA', 'JFK'), [(-420, 80, 340), (180, 120, 650), (650, 18, 900)])),
        ('JFK TO EWR', 420, (('JFK', 'EWR'), [(350, 110, 1120), (-250, 140, 980), (-900, 18, 600)])),
        ('EWR TO LGA', 420, (('EWR', 'LGA'), [(-760, 120, 350), (-500, 85, 140), (-650, 18, 100)])),
    ]

    def __init__(self, plane):
        self.plane = plane
        self.level = 0
        self.checkpoint_index = 0
        self.score = 0
        self.time_left = 0
        self.state = 'FLYING'
        self.last_award = 0
        self.landing_airport = None
        self.message = 'Reach the blue gates'
        self.gates = []
        self._load_level()

    def _load_level(self):
        title, duration, route = self.LEVELS[self.level]
        airports, points = route
        self.time_left = duration
        self.checkpoint_index = 0
        self.state = 'FLYING'
        self.last_award = 0
        self.landing_airport = None
        self.message = f'{airports[0]} -> {airports[1]} | gate 1 of {len(points)}'
        for gate in self.gates:
            gate.root.disable()
        self.gates = [Checkpoint(point, i + 1, i == 0) for i, point in enumerate(points)]

    def reset(self):
        self.level = 0
        self.score = 0
        self._load_level()

    def update(self, dt):
        if self.state not in ('FLYING', 'LANDING'):
            return
        self.time_left -= dt
        position = self.plane.root.position
        if (position.x < CITY_BOUNDS['min_x'] or
                position.x > CITY_BOUNDS['max_x'] or
                position.y > 340):
            self.state = 'FAILED'
            self.message = 'OUT OF BOUNDS - press R to restart'
            return
        if (position.z < CITY_BOUNDS['min_z'] or
                position.z > CITY_BOUNDS['max_z']):
            self.state = 'FAILED'
            self.message = 'LEFT THE CITY MAP - press R to restart'
            return
        near_airport_runway = any(
            abs(position.x - airport_x) < 28 and abs(position.z - airport_z) < 140
            for airport_x, airport_z in AIRPORTS.values()
        )
        if not near_airport_runway:
            for x, z, half_x, half_z, height in CITY_OBSTACLES:
                if (abs(position.x - x) < half_x + 3 and
                        abs(position.z - z) < half_z + 7 and
                        position.y < height + 5):
                    self.state = 'FAILED'
                    self.message = 'CRASHED INTO A BUILDING - press R to restart'
                    return

        if self.state == 'LANDING':
            airport_x, airport_z = AIRPORTS[self.landing_airport]
            pad_x, pad_z = drone_pad_position(AIRPORTS, self.landing_airport)
            near_runway = (abs(position.x - airport_x) < 32 and
                           abs(position.z - airport_z) < 125)
            near_drone_pad = (abs(position.x - pad_x) < 16 and
                              abs(position.z - pad_z) < 16)
            stable_landing = position.y <= 8 and self.plane.speed < 45
            if (near_runway or near_drone_pad) and stable_landing:
                self.score += 300
                if self.level == len(self.LEVELS) - 1:
                    self.state = 'WON'
                    self.message = f'LANDED AT {self.landing_airport} +300 | ALL MISSIONS COMPLETE'
                else:
                    self.level += 1
                    self._load_level()
                    self.message = f'LANDED AT {self.landing_airport} +300 | NEXT: {self.LEVELS[self.level][0]}'
            return

        # Give clear feedback if the pilot lands before completing the route.
        if position.y <= 8 and self.plane.speed < 45:
            for airport_name, (airport_x, airport_z) in AIRPORTS.items():
                if (abs(position.x - airport_x) < 32 and
                        abs(position.z - airport_z) < 125):
                    expected_start = self.LEVELS[self.level][2][0][0]
                    if airport_name == expected_start:
                        self.message = (f'AT {airport_name} DEPARTURE | '
                                        f'FLY GATE {self.checkpoint_index + 1} FIRST')
                    else:
                        destination = self.LEVELS[self.level][2][0][1]
                        self.message = (f'LANDED AT {airport_name} | '
                                        f'REACH CHECKPOINTS, THEN LAND AT {destination}')
                    break

        gate = self.gates[self.checkpoint_index]
        if (position - gate.position).length() < 22:
            altitude_bonus = max(0, 50 - int(abs(position.y - gate.position.y) * 2))
            speed_bonus = min(50, int(self.plane.speed / 2))
            time_bonus = min(100, int(self.time_left / 5))
            award = 100 + altitude_bonus + speed_bonus + time_bonus
            self.score += award
            self.last_award = award
            gate.set_active(False)
            self.checkpoint_index += 1
            if self.checkpoint_index >= len(self.gates):
                self.score += 500
                self.landing_airport = self.LEVELS[self.level][2][0][1]
                self.state = 'LANDING'
                self.message = (f'CHECKPOINTS COMPLETE +{award} +500 | '
                                f'LAND AT {self.landing_airport} (slow below 45)')
            else:
                self.gates[self.checkpoint_index].set_active(True)
                self.message = (f'GATE CLEARED +{award} '
                                f'(alt {altitude_bonus} speed {speed_bonus} time {time_bonus}) | '
                                f'next gate {self.checkpoint_index + 1}/{len(self.gates)}')

    def objective(self):
        if self.state != 'FLYING':
            return self.message
        route = self.LEVELS[self.level][2][0]
        return f'{self.LEVELS[self.level][0]} ({route[0]} -> {route[1]}) | {self.message}'

    def map_text(self):
        p = self.plane.root.position
        gate = self.gates[self.checkpoint_index] if self.state == 'FLYING' else None
        gate_text = f'{gate.position.x:4.0f},{gate.position.z:4.0f}' if gate else '--'
        route = self.LEVELS[self.level][2][0] if self.state in ('FLYING', 'LANDING') else ('-', '-')
        return (f'RADAR\nPLANE {p.x:4.0f},{p.z:4.0f}\n'
                f'LGA {AIRPORTS["LGA"][0]:4.0f},{AIRPORTS["LGA"][1]:4.0f}\n'
                f'JFK {AIRPORTS["JFK"][0]:4.0f},{AIRPORTS["JFK"][1]:4.0f}\n'
                f'EWR {AIRPORTS["EWR"][0]:4.0f},{AIRPORTS["EWR"][1]:4.0f}\n'
                f'ROUTE {route[0]} -> {route[1]}\n'
                f'NEXT  {gate_text}\n'
                f'GATE  {self.checkpoint_index + 1 if gate else "-"}')


class Radar:
    """A compact world map shown in the corner of the flight display."""
    def __init__(self):
        self.center = Vec3(0.72, 0.24, 0)
        self.size = Vec3(0.32, 0.30, 1)
        self.background = Entity(parent=camera.ui, model='quad',
                                 position=self.center + Vec3(0, 0, 0.1),
                                 scale=self.size, color=color.rgba(8, 22, 35, 220))
        # Map grid lines give it a radar/map feel.
        for offset in (-0.25, 0, 0.25):
            Entity(parent=camera.ui, model='quad',
                   position=self.center + Vec3(offset * self.size.x, 0, 0),
                   scale=(0.002, self.size.y, 1), color=color.rgba(80, 150, 170, 130))
            Entity(parent=camera.ui, model='quad',
                   position=self.center + Vec3(0, offset * self.size.y, 0),
                   scale=(self.size.x, 0.002, 1), color=color.rgba(80, 150, 170, 130))
        self.player_dot = Entity(parent=camera.ui, model='quad', scale=0.018,
                                 color=color.lime)
        self.target_dot = Entity(parent=camera.ui, model='quad', scale=0.014,
                                 color=color.yellow)
        self.airport_dots = {}
        self.labels = {}
        for name, (x, z) in AIRPORTS.items():
            self.airport_dots[name] = Entity(parent=camera.ui, model='quad',
                                             scale=0.012, color=color.azure)
            self.labels[name] = Text(parent=camera.ui, text=name, scale=0.55,
                                     color=color.white)
        self.title = Text(parent=camera.ui, text='CITY MAP / RADAR',
                          position=self.center + Vec3(-0.14, 0.17, 0),
                          scale=0.65, color=color.azure)

    def _to_ui(self, x, z):
        # Map the complete regional city bounds into the radar panel.
        nx = ((x - CITY_BOUNDS['min_x']) /
              (CITY_BOUNDS['max_x'] - CITY_BOUNDS['min_x']))
        nz = ((z - CITY_BOUNDS['min_z']) /
              (CITY_BOUNDS['max_z'] - CITY_BOUNDS['min_z']))
        return self.center + Vec3((nx - 0.5) * self.size.x,
                                  (nz - 0.5) * self.size.y, -0.1)

    def update(self, plane, mission):
        self.player_dot.position = self._to_ui(plane.root.x, plane.root.z)
        for name, (x, z) in AIRPORTS.items():
            point = self._to_ui(x, z)
            self.airport_dots[name].position = point
            self.labels[name].position = point + Vec3(0.012, 0.012, -0.1)
        if mission.state == 'FLYING':
            gate = mission.gates[mission.checkpoint_index]
            self.target_dot.enabled = True
            self.target_dot.position = self._to_ui(gate.position.x, gate.position.z)
        elif mission.state == 'LANDING':
            airport_x, airport_z = drone_pad_position(AIRPORTS, mission.landing_airport)
            self.target_dot.enabled = True
            self.target_dot.position = self._to_ui(airport_x, airport_z)
        else:
            self.target_dot.enabled = False


class Drone:
    def __init__(self):
        start_x, start_z = AIRPORTS['LGA']
        self.root = Entity(position=(start_x, PLANE_GROUND_Y, start_z))
        self.speed = 0.0
        self.throttle = 0.0
        self._build_model()

    def _part(self, scale, position=(0, 0, 0), tint=color.white, rotation=(0, 0, 0)):
        return Entity(parent=self.root, model='cube', scale=scale,
                      position=position, color=tint, rotation=rotation)

    def _build_model(self):
        frame = color.rgb(35, 105, 220)
        dark = color.rgb(25, 35, 48)
        propeller = color.rgb(185, 195, 210)
        self._part((4.5, 1.2, 4.5), tint=color.rgb(242, 242, 245))
        self._part((17, 0.35, 0.55), position=(0, 0.45, 0), tint=frame)
        self._part((0.55, 0.35, 17), position=(0, 0.48, 0), tint=frame)
        self._part((2.2, 1.0, 2.0), position=(0, 0.9, 1.0), tint=dark)
        self._part((1.4, 0.45, 1.2), position=(0, 0.25, 2.6), tint=color.azure)
        for side_x in (-1, 1):
            for side_z in (-1, 1):
                position = (side_x * 6.5, 0.75, side_z * 6.5)
                self._part((1.4, 0.7, 1.4), position=position, tint=dark)
                self._part((5.0, 0.08, 0.35), position=position, tint=propeller)
                self._part((0.35, 0.08, 5.0), position=position, tint=propeller)
                self._part((0.25, 2.2, 0.25),
                           position=(position[0], -1.1, position[2]), tint=dark)

    def reset(self):
        start_x, start_z = AIRPORTS['LGA']
        self.root.position = (start_x, PLANE_GROUND_Y, start_z)
        self.root.rotation = Vec3(0, 0, 0)
        self.speed = 0.0
        self.throttle = 0.0

    def fly(self, dt, joystick=None):
        throttle_input = float(held_keys['w']) - float(held_keys['s'])
        pitch_input = float(held_keys['up arrow']) - float(held_keys['down arrow'])
        roll_input = float(held_keys['d']) - float(held_keys['a'])
        yaw_input = float(held_keys['e']) - float(held_keys['q'])
        brake_input = bool(held_keys['space'])
        if joystick:
            # The throttle potentiometer is absolute; move the aircraft's
            # throttle smoothly toward its physical position.
            throttle_input = joystick['throttle']
            pitch_input = joystick['pitch']
            roll_input = joystick['roll']
            yaw_input = joystick['yaw']
            brake_input = joystick['brake']
        self.throttle = max(0.0, min(1.0, self.throttle + throttle_input * dt * 0.5))
        self.speed += throttle_input * 28 * dt
        if brake_input:
            self.speed -= 40 * dt
        elif throttle_input == 0:
            # Air resistance means the aircraft can eventually stall if the
            # pilot stops managing throttle while airborne.
            self.speed -= (4 + self.speed * 0.012) * dt
        self.speed = max(0.0, min(125.0, self.speed))

        pitch = pitch_input
        roll = roll_input
        yaw = yaw_input
        self.root.rotation_x += pitch * 34 * dt
        self.root.rotation_z -= roll * 55 * dt
        # A/D banks and also provides gentle steering, so turning is easy
        # even for players who do not use the Q/E yaw controls.
        self.root.rotation_y += (yaw + roll * 0.65) * 28 * dt
        self.root.rotation_z *= max(0.0, 1 - dt * 0.7)
        self.root.rotation_x = max(-35, min(35, self.root.rotation_x))

        lift = max(0.0, self.speed - 38) * 0.28
        vertical = lift + pitch * 5 - 12 * dt
        if self.root.y <= PLANE_GROUND_Y and self.speed < 38:
            self.root.y = PLANE_GROUND_Y
            vertical = 0
        self.root.y = max(PLANE_GROUND_Y, min(350, self.root.y + vertical * dt))
        if self.root.y <= PLANE_GROUND_Y + 0.05 and self.speed < 5:
            # A parked drone must sit level on the landing surface.  This
            # prevents joystick drift from leaving the chase camera inverted.
            self.root.rotation_x = lerp(self.root.rotation_x, 0, min(1, dt * 8))
            self.root.rotation_z = lerp(self.root.rotation_z, 0, min(1, dt * 8))
        self.root.position += self.root.forward * self.speed * dt


class FlightGame:
    def __init__(self, joystick_port='COM11'):
        self.app = Ursina()
        application.title = 'Skybound - 3D Flight Simulator'
        application.fullscreen = False
        camera.fov = 85
        self.world = World()
        self.plane = Drone()
        self.missions = MissionSystem(self.plane, CITY_OBSTACLES)
        self.world.set_level(self.missions.level)
        self.sounds = SoundManager()
        self.joystick = ArduinoJoystick(port=joystick_port)
        self.was_airborne = False
        self.camera_mode = 'CHASE'
        self.c_pressed = False
        self.r_pressed = False

        self.help = Text(
            text='SKYBOUND\nW/S throttle   UP/DOWN pitch   A/D steer/roll   Q/E yaw\n'
                 'Follow RADAR gates/airports   SPACE brake   R reset\n'
                 'Left joystick reset   Right button camera   F11 fullscreen   ESC quit',
            position=(-0.86, 0.43), scale=0.9, background=True,
        )
        self.hud = Text(position=(-0.86, 0.22), scale=1.05, color=color.azure)
        self.radar = Radar()
        self.mission_hud = Text(position=(-0.86, -0.40), scale=0.95,
                                color=color.yellow, background=True)
        self.status_overlay = Text(position=(0, 0.05), origin=(0, 0),
                                   scale=1.45, align='center',
                                   color=color.red, background=True,
                                   enabled=False)
        self.score_popup = Text(position=(0, 0.24), origin=(0, 0),
                                scale=1.25, align='center',
                                color=color.lime, background=True,
                                enabled=False)
        self.popup_timer = 0.0
        self.frames = 0
        self.update_camera(1.0)
        self.loop = GameLoop(self)

    def update_camera(self, blend):
        if self.camera_mode == 'COCKPIT':
            wanted = self.plane.root.position + self.plane.root.forward * 3
            camera.position = lerp(camera.position, wanted, blend)
            camera.look_at(camera.position + self.plane.root.forward * 30)
        else:
            # Chase view uses world-up, not the drone's banked up-vector.
            # Otherwise a roll rotates the horizon and can make the player
            # appear to be looking underneath the terrain.
            wanted = (self.plane.root.position + Vec3(0, 8, 0)
                      - self.plane.root.forward * 26)
            camera.position = lerp(camera.position, wanted, blend)
            camera.look_at(self.plane.root.position + Vec3(0, 1.5, 0))
            camera.rotation_z = 0

    def update(self):
        dt = min(time.dt, 0.05)
        self.frames += 1
        joystick = self.joystick.poll()
        self.world.update(dt)
        r_down = bool(held_keys['r'])
        joystick_reset = bool(joystick and joystick.get('reset', False))
        if (r_down and not self.r_pressed) or joystick_reset:
            self.plane.reset()
            self.missions.reset()
            self.sounds.reset_music()
            self.was_airborne = False
            self.camera_mode = 'CHASE'
            self.update_camera(1.0)
        elif self.missions.state == 'FLYING':
            self.plane.fly(dt, joystick)
            self.sounds.update_engine(self.plane.speed, self.plane.throttle)
            airborne = self.plane.root.y > PLANE_GROUND_Y + 5
            if airborne and not self.was_airborne:
                self.sounds.play_takeoff()
            self.was_airborne = airborne
            old_score = self.missions.score
            self.missions.update(dt)
            self.world.set_level(self.missions.level)
            score_gain = self.missions.score - old_score
            if score_gain > 0:
                self.sounds.play_checkpoint()
                self.score_popup.text = f'+{score_gain} POINTS\nCHECKPOINT CLEARED'
                self.popup_timer = 2.5
        elif self.missions.state == 'LANDING':
            self.sounds.update_engine(self.plane.speed, self.plane.throttle)
            old_score = self.missions.score
            self.missions.update(dt)
            self.world.set_level(self.missions.level)
            score_gain = self.missions.score - old_score
            if score_gain > 0:
                self.sounds.play_landing()
                if self.missions.state == 'WON':
                    self.score_popup.text = '+300 LANDING BONUS\nMISSION COMPLETED'
                else:
                    self.score_popup.text = (f'+300 LANDING BONUS\n'
                                             f'NEXT LEVEL {self.missions.level + 1}: '
                                             f'{self.missions.LEVELS[self.missions.level][0]}')
                self.popup_timer = 4.0
        self.r_pressed = r_down
        joystick_camera = bool(joystick and joystick['camera'])
        self.popup_timer = max(0.0, self.popup_timer - dt)
        self.score_popup.enabled = self.popup_timer > 0
        c_down = bool(held_keys['c']) or joystick_camera
        if c_down and not self.c_pressed:
            self.camera_mode = 'COCKPIT' if self.camera_mode == 'CHASE' else 'CHASE'
        self.c_pressed = c_down
        self.update_camera(min(1.0, dt * 6))
        self.hud.text = (f'ALT {max(0, self.plane.root.y - PLANE_GROUND_Y):06.1f} m\n'
                         f'SPD {self.plane.speed:06.1f}\n'
                         f'THR {self.plane.throttle * 100:05.1f}%\n'
                         f'LEVEL {self.missions.level + 1}\n'
                         f'CITY REGIONAL GRID\n'
                         f'DISTRICT {district_name(self.plane.root.x, self.plane.root.z)}\n'
                         f'AIR TRAFFIC {"ACTIVE" if self.missions.level >= 1 else "LEVEL 2"}\n'
                         f'VEHICLE DRONE\n'
                         f'VIEW {self.camera_mode}\n'
                         f'SCORE {self.missions.score}\n'
                         f'TIME {max(0, self.missions.time_left):05.0f}\n'
                         f'{self.joystick.status}')
        self.radar.update(self.plane, self.missions)
        self.mission_hud.text = self.missions.objective()
        if self.missions.state == 'FAILED':
            self.status_overlay.enabled = True
            self.status_overlay.text = ('MISSION FAILED\n'
                                        f'{self.missions.message}\n'
                                        'PRESS R TO RESTART')
        elif self.missions.state == 'WON':
            self.status_overlay.enabled = True
            self.status_overlay.color = color.lime
            self.status_overlay.text = ('MISSION COMPLETE\n'
                                        f'SCORE {self.missions.score}\n'
                                        'PRESS R TO PLAY AGAIN')
        else:
            self.status_overlay.enabled = False
            self.status_overlay.color = color.red
        if held_keys['escape']:
            application.quit()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', default='COM11', help='Arduino serial port, e.g. COM11')
    args = parser.parse_args()
    game = FlightGame(joystick_port=args.port)
    game.app.run()
