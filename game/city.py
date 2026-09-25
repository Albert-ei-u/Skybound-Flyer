"""Procedural districts, buildings, and landmarks."""

from ursina import Entity, color


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
        for x in range(-1200, 1201, 140):
            for z in range(-300, 1701, 150):
                clear_approach = any(
                    abs(x - airport_x) < 95 and abs(z - airport_z) < 300
                    for airport_x, airport_z in self.airports.values()
                )
                if clear_approach or abs(x) < 35:
                    continue
                district = (abs(x) // 140 + max(0, z) // 150) % 4
                height = 18 + ((abs(x) * 3 + z * 7) % (35 + district * 12))
                width = 30 + (district % 2) * 8
                depth = 32 + ((district + 1) % 2) * 8
                CityBuilding(x, z, width, depth, height, district)
                obstacles.append((x, z, width / 2, depth / 2, height))

        Entity(model='cube', scale=(26, 145, 26), position=(0, 72.5, 420),
               color=color.rgb(90, 95, 112))
        Entity(model='cube', scale=(5, 35, 5), position=(0, 162, 420),
               color=color.rgb(75, 80, 95))
        obstacles.append((0, 420, 13, 13, 145))
        return obstacles
