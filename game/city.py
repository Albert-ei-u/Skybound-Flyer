"""Procedural districts, buildings, and landmarks."""

from ursina import Entity, color


DISTRICTS = (
    ('Harbor District', -1170, -210, -300, 500),
    ('Old Town', -1170, -210, 500, 1610),
    ('Central City', -210, 510, -300, 700),
    ('Midtown', -210, 510, 700, 1610),
    ('Uptown', 510, 1170, -300, 800),
    ('Airport Corridor', 510, 1170, 800, 1610),
)

LANDMARKS = (
    ('Harbor District', -630, 330, 'tower'),
    ('Old Town', -990, 1050, 'clock'),
    ('Central City', 270, 330, 'tower'),
    ('Midtown', 270, 1050, 'tower'),
    ('Uptown', 990, 330, 'plaza'),
    ('Airport Corridor', 990, 1230, 'terminal'),
)


def district_name(x, z):
    """Return the named district containing a world coordinate."""
    for name, min_x, max_x, min_z, max_z in DISTRICTS:
        if min_x <= x <= max_x and min_z <= z <= max_z:
            return name
    return 'Regional Outskirts'


class CityBuilding:
    """A detailed procedural building assembled from facade pieces."""
    def __init__(self, x, z, width, depth, height, style):
        body_colors = [
            color.rgb(105, 112, 128), color.rgb(150, 112, 82),
            color.rgb(72, 88, 108), color.rgb(178, 178, 165),
        ]
        body = body_colors[style % len(body_colors)]
        self.root = Entity(position=(x, 0, z))
        Entity(parent=self.root, model='cube', scale=(width, height, depth),
               position=(0, height / 2, 0), color=body)
        Entity(parent=self.root, model='cube', scale=(width + 1, .8, depth + 1),
               position=(0, height + .4, 0), color=color.rgb(45, 48, 58))
        if style % 3 == 0:
            Entity(parent=self.root, model='cube', scale=(width * .32, 4, depth * .32),
                   position=(0, height + 2.4, 0), color=color.rgb(65, 68, 76))

        columns = max(2, min(5, int(width // 5)))
        rows = max(2, min(8, int(height // 8)))
        for row in range(rows):
            y = 5 + row * max(6, (height - 10) / rows)
            for column in range(columns):
                window_x = -width / 2 + 3 + column * (width - 6) / max(1, columns - 1)
                for face in (-1, 1):
                    Entity(parent=self.root, model='cube', scale=(1.7, 2.2, .12),
                           position=(window_x, y, face * (depth / 2 + .08)),
                           color=(color.rgb(235, 195, 92)
                                  if (row + column + style) % 4
                                  else color.rgb(100, 190, 220)))
        for row in range(0, rows, 2):
            y = 6 + row * max(6, (height - 10) / rows)
            Entity(parent=self.root, model='cube', scale=(.12, 2.2, 2.0),
                   position=(-width / 2 - .08, y, 0), color=color.rgb(210, 185, 90))


class CityBuilder:
    """Builds city districts and returns collision bounds for gameplay."""
    def __init__(self, airports):
        self.airports = airports

    def build(self):
        obstacles = []
        # Roads use a 180-unit grid.  Buildings are placed at the centres of
        # the blocks between roads, so facades cannot spill into the lanes.
        for x in range(-1170, 1171, 180):
            for z in range(-210, 1611, 180):
                clear_approach = any(
                    abs(x - airport_x) < 95 and abs(z - airport_z) < 300
                    for airport_x, airport_z in self.airports.values()
                )
                near_landmark = any(abs(x - landmark[1]) < 75 and
                                    abs(z - landmark[2]) < 75
                                    for landmark in LANDMARKS)
                if clear_approach or near_landmark or abs(x) < 35:
                    continue
                district_index = next(
                    (index for index, district in enumerate(DISTRICTS)
                     if district[1] <= x <= district[2]
                     and district[3] <= z <= district[4]),
                    0,
                )
                style = (district_index + abs(x) // 180 + max(0, z) // 180) % 4
                height = 18 + ((abs(x) * 3 + z * 7) % (35 + style * 12))
                width = 30 + (style % 2) * 8
                depth = 32 + ((style + 1) % 2) * 8
                CityBuilding(x, z, width, depth, height, style)
                obstacles.append((x, z, width / 2, depth / 2, height))

        # A lower-density outer ring keeps the city visually continuous while
        # avoiding the object count of a fully detailed downtown everywhere.
        for x in range(-2790, 2791, 360):
            for z in range(-1530, 3331, 360):
                in_core = -1350 <= x <= 1350 and -390 <= z <= 1790
                clear_approach = any(
                    abs(x - airport_x) < 125 and abs(z - airport_z) < 360
                    for airport_x, airport_z in self.airports.values()
                )
                if in_core or clear_approach:
                    continue
                style = (abs(x) // 360 + abs(z) // 360) % 4
                height = 14 + ((abs(x) * 5 + z * 3) % 28)
                width = 34 + (style % 2) * 10
                depth = 34 + ((style + 1) % 2) * 10
                CityBuilding(x, z, width, depth, height, style)
                obstacles.append((x, z, width / 2, depth / 2, height))

        self._build_landmarks(obstacles)
        return obstacles

    def _build_landmarks(self, obstacles):
        """Create recognizable district centres and their collision bounds."""
        for name, x, z, kind in LANDMARKS:
            if kind == 'tower':
                Entity(model='cube', scale=(26, 145, 26),
                       position=(x, 72.5, z), color=color.rgb(90, 95, 112))
                Entity(model='cube', scale=(5, 35, 5),
                       position=(x, 162, z), color=color.rgb(75, 80, 95))
                height = 145
            elif kind == 'clock':
                Entity(model='cube', scale=(42, 55, 42),
                       position=(x, 27.5, z), color=color.rgb(128, 91, 66))
                Entity(model='cube', scale=(8, 45, 8),
                       position=(x, 77.5, z), color=color.rgb(90, 64, 52))
                height = 100
            elif kind == 'plaza':
                Entity(model='cube', scale=(78, 0.5, 78),
                       position=(x, 0.3, z), color=color.rgb(150, 150, 140))
                for offset in (-24, 24):
                    Entity(model='cube', scale=(4, 32, 4),
                           position=(x + offset, 16, z + offset),
                           color=color.rgb(70, 110, 82))
                height = 32
            else:
                Entity(model='cube', scale=(80, 10, 48),
                       position=(x, 5, z), color=color.rgb(180, 190, 205))
                Entity(model='cube', scale=(48, 18, 8),
                       position=(x, 19, z), color=color.rgb(65, 75, 92))
                height = 28
            obstacles.append((x, z, 42, 42, height))
