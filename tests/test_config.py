import unittest

from game.config import AIRPORTS, CITY_BOUNDS, SERIAL_BAUDRATE


class ConfigurationTests(unittest.TestCase):
    def test_airports_have_valid_coordinates(self):
        self.assertEqual({'LGA', 'JFK', 'EWR'}, set(AIRPORTS))
        for x, z in AIRPORTS.values():
            self.assertGreaterEqual(x, CITY_BOUNDS['min_x'])
            self.assertLessEqual(x, CITY_BOUNDS['max_x'])
            self.assertGreaterEqual(z, CITY_BOUNDS['min_z'])
            self.assertLessEqual(z, CITY_BOUNDS['max_z'])

    def test_serial_protocol(self):
        self.assertEqual(SERIAL_BAUDRATE, 115200)

    def test_city_bounds_are_regional(self):
        self.assertGreaterEqual(CITY_BOUNDS['max_x'] - CITY_BOUNDS['min_x'], 5000)
        self.assertGreaterEqual(CITY_BOUNDS['max_z'] - CITY_BOUNDS['min_z'], 5000)


if __name__ == '__main__':
    unittest.main()
