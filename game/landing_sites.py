"""Landing-site construction and shared landing-zone definitions."""

from ursina import Entity, color


def drone_pad_position(sites, name):
    """Return the center of the raised drone pad for a named site."""
    x, z = sites[name]
    return x + 62, z - 35


class LandingSiteBuilder:
    """Builds airports as reusable aircraft and drone landing sites."""

    def __init__(self, sites):
        self.sites = sites

    def build(self):
        for name, (x, z) in self.sites.items():
            self._build_site(name, x, z)

    def _build_site(self, name, x, z):
        # Main runway for aircraft and larger drone missions.
        Entity(model='cube', scale=(42, 0.2, 260), position=(x, 0.05, z),
               color=color.rgb(48, 50, 56))
        for mark_z in range(z - 115, z + 116, 24):
            Entity(model='cube', scale=(1.4, 0.08, 10),
                   position=(x, 0.18, mark_z), color=color.white)

        # Terminal and a raised drone landing pad beside the runway.
        Entity(model='cube', scale=(70, 7, 35), position=(x + 62, 3.5, z + 20),
               color=color.rgb(180, 190, 205))
        Entity(model='cube', scale=(28, 0.35, 28),
               position=(x + 62, 7.2, z - 35), color=color.rgb(42, 48, 58))
        Entity(model='cube', scale=(20, 0.12, 2),
               position=(x + 62, 7.45, z - 35), color=color.azure)
        Entity(model='cube', scale=(2, 0.12, 20),
               position=(x + 62, 7.46, z - 35), color=color.azure)

        # A visible landing beacon helps the pilot identify the drone zone.
        Entity(model='cube', scale=(.5, 5, .5),
               position=(x + 62, 10, z - 35), color=color.lime)
