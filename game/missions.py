"""Mission routes, checkpoints, scoring, and landing validation."""

from ursina import Entity, Vec3, color

from game.config import AIRPORTS, CITY_BOUNDS
from game.landing_sites import drone_pad_position


class Checkpoint:
    def __init__(self, position, number, active=False):
        self.position = Vec3(*position)
        self.number = number
        self.root = Entity(position=self.position, enabled=active)
        Entity(parent=self.root, model='cube', scale=(2, 22, 2),
               position=(-18, 0, 0), color=color.azure)
        Entity(parent=self.root, model='cube', scale=(2, 22, 2),
               position=(18, 0, 0), color=color.azure)
        Entity(parent=self.root, model='cube', scale=(38, 2, 2),
               position=(0, 20, 0), color=color.azure)

    def set_active(self, active):
        self.root.enabled = active


class MissionManager:
    """Owns the route state machine independently from flight controls."""

    LEVELS = [
        ('LGA TO JFK', 360, (('LGA', 'JFK'), [
            (-420, 80, 340), (180, 120, 650), (650, 18, 900),
        ])),
        ('JFK TO EWR', 420, (('JFK', 'EWR'), [
            (350, 110, 1120), (-250, 140, 980), (-900, 18, 600),
        ])),
        ('EWR TO LGA', 420, (('EWR', 'LGA'), [
            (-760, 120, 350), (-500, 85, 140), (-650, 18, 100),
        ])),
    ]

    def __init__(self, plane, city_obstacles):
        self.plane = plane
        self.city_obstacles = city_obstacles
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
        self.gates = [Checkpoint(point, i + 1, i == 0)
                       for i, point in enumerate(points)]

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
                position.x > CITY_BOUNDS['max_x'] or position.y > 340):
            self.state = 'FAILED'
            self.message = 'OUT OF BOUNDS - press R to restart'
            return
        if (position.z < CITY_BOUNDS['min_z'] or
                position.z > CITY_BOUNDS['max_z']):
            self.state = 'FAILED'
            self.message = 'LEFT THE CITY MAP - press R to restart'
            return

        near_airport_runway = any(
            abs(position.x - airport_x) < 28 and
            abs(position.z - airport_z) < 140
            for airport_x, airport_z in AIRPORTS.values()
        )
        if not near_airport_runway:
            for x, z, half_x, half_z, height in self.city_obstacles:
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
                    self.message = (f'LANDED AT {self.landing_airport} +300 | '
                                    'ALL MISSIONS COMPLETE')
                else:
                    completed_airport = self.landing_airport
                    self.level += 1
                    self._load_level()
                    self.message = (f'LANDED AT {completed_airport} +300 | '
                                    f'NEXT: {self.LEVELS[self.level][0]}')
            return

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
