"""Road-network rendering for the regional city."""

from ursina import Entity, color


class RoadNetwork:
    """Builds a deterministic avenue and cross-street grid."""

    def __init__(self, min_x=-2880, max_x=2880, min_z=-1620, max_z=3420,
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
            # Keep each visual layer at a distinct height.  At large world
            # coordinates tiny vertical gaps prevent depth-buffer flicker.
            Entity(model='cube', scale=(18, .08, world_depth),
                   position=(x, .05, center_z), color=road_color)
            Entity(model='cube', scale=(2, .04, world_depth),
                   position=(x, .19, center_z), color=lane_color)
            for side in (-1, 1):
                Entity(model='cube', scale=(3, .06, world_depth),
                       position=(x + side * 12, .12, center_z),
                       color=sidewalk_color)

        for z in range(self.min_z, self.max_z + 1, self.spacing):
            Entity(model='cube', scale=(world_width, .08, 18),
                   position=(center_x, .06, z), color=road_color)
            # Cross-street markings are 0.04 units higher than avenue
            # markings, eliminating coplanar overlap at intersections.
            Entity(model='cube', scale=(world_width, .04, 2),
                   position=(center_x, .23, z), color=lane_color)
            for side in (-1, 1):
                Entity(model='cube', scale=(world_width, .06, 3),
                       position=(center_x, .13, z + side * 12),
                       color=sidewalk_color)

        self._build_streetlights()

    def _build_streetlights(self):
        """Add sparse lighting landmarks without filling every block."""
        lamp_color = color.rgb(245, 220, 130)
        for x in range(self.min_x + self.spacing // 2,
                       self.max_x, self.spacing * 2):
            for z in range(self.min_z + self.spacing // 2,
                           self.max_z, self.spacing * 2):
                Entity(model='cube', scale=(.35, 7, .35),
                       position=(x, 3.5, z), color=color.rgb(55, 58, 64))
                Entity(model='cube', scale=(1.2, .25, 1.2),
                       position=(x, 7.1, z), color=lamp_color)
