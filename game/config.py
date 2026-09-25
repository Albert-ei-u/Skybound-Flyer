"""Central, data-only configuration for the simulator world."""

RUNWAY_Z = -100
GROUND_Y = 0
PLANE_GROUND_Y = 2.4

# Airport coordinates use the same world coordinate system as the aircraft.
AIRPORTS = {
    'LGA': (-650, 100),
    'JFK': (650, 900),
    'EWR': (-900, 600),
}

CITY_BOUNDS = {
    'min_x': -1400,
    'max_x': 1400,
    'min_z': -600,
    'max_z': 1800,
}

SERIAL_BAUDRATE = 115200
