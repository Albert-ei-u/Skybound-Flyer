"""Moving air-traffic entities and their level visibility rules.

Traffic is intentionally independent from the player aircraft and mission
logic.  This gives later levels a clean place to add routes, separation
rules, and collision detection without growing ``main.py``.
"""

import math

from ursina import Entity, color


class TrafficPlane:
    """A lightweight aircraft that follows a deterministic city route."""

    def __init__(self, start_x, start_z, phase):
        self.phase = phase
        self.root = Entity(position=(start_x, 95, start_z), rotation=(0, 90, 0))
        Entity(parent=self.root, model='cube', scale=(2, 2, 10), color=color.white)
        Entity(
            parent=self.root,
            model='cube',
            scale=(12, .3, 2),
            color=color.rgb(220, 220, 230),
        )
        Entity(
            parent=self.root,
            model='cube',
            scale=(.3, 3, 2),
            position=(0, 1.5, -3.5),
            color=color.red,
        )

    def update(self, dt):
        self.phase += dt * .18
        self.root.x += math.sin(self.phase) * dt * 18
        self.root.z += dt * 12
        if self.root.z > 1400:
            self.root.z = -350


class TrafficManager:
    """Owns non-player aircraft and controls when levels activate them."""

    def __init__(self):
        self.aircraft = [
            TrafficPlane(-520, -250, 0.0),
            TrafficPlane(420, 120, 2.0),
            TrafficPlane(180, 500, 4.0),
        ]
        self.set_level(0)

    def set_level(self, level):
        """Show traffic from level 2 onward; keep level 1 approachable."""
        for aircraft in self.aircraft:
            aircraft.root.enabled = level >= 1

    def update(self, dt):
        for aircraft in self.aircraft:
            if aircraft.root.enabled:
                aircraft.update(dt)
