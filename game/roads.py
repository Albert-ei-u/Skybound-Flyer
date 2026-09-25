"""Road-network rendering for the regional city."""

from ursina import Entity, color


class RoadNetwork:
    """Builds a deterministic avenue and cross-street grid."""

    def __init__(self, min_x=-1260, max_x=1260, min_z=-300, max_z=1700,
                 spacing=180):
        self.min_x = min_x
        self.max_x = max_x
        self.min_z = min_z
        self.max_z = max_z
        self.spacing = spacing

    def build(self):
        road_color = color.rgb(38, 40, 45)
        sidewalk_color = color.rgb(105, 105, 103)
        lane_color = color.rgb(215, 190, 80)
        world_width = self.max_x - self.min_x + 100
        world_depth = self.max_z - self.min_z + 100
        center_x = (self.min_x + self.max_x) / 2
        center_z = (self.min_z + self.max_z) / 2

        for x in range(self.min_x, self.max_x + 1, self.spacing):
            Entity(model='cube', scale=(18, .08, world_depth),
                   position=(x, .06, center_z), color=road_color)
            Entity(model='cube', scale=(2, .1, world_depth),
                   position=(x, .13, center_z), color=lane_color)
            for side in (-1, 1):
                Entity(model='cube', scale=(3, .1, world_depth),
                       position=(x + side * 12, .10, center_z),
                       color=sidewalk_color)

        for z in range(self.min_z, self.max_z + 1, self.spacing):
            Entity(model='cube', scale=(world_width, .08, 18),
                   position=(center_x, .07, z), color=road_color)
            Entity(model='cube', scale=(world_width, .1, 2),
                   position=(center_x, .14, z), color=lane_color)
            for side in (-1, 1):
                Entity(model='cube', scale=(world_width, .1, 3),
                       position=(center_x, .11, z + side * 12),
                       color=sidewalk_color)
