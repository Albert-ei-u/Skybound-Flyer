"""Export a real OpenStreetMap city area to a simulator-friendly JSON file.

This is an offline content-pipeline step, not something the game runs every
frame. The output keeps local metric coordinates so the simulator does not
depend on latitude/longitude during gameplay.

Example:
    python tools/export_city.py "Queens, New York, USA" \
        --output assets/cities/nyc_queens.json --distance 5000
"""

import argparse
import json
from pathlib import Path


def _local_linestring(geometry, origin_x, origin_y):
    """Convert a projected LineString/MultiLineString to local x/z arrays."""
    if geometry.geom_type == 'MultiLineString':
        return [_local_linestring(part, origin_x, origin_y)
                for part in geometry.geoms]
    return [[round(float(x - origin_x), 2), round(float(y - origin_y), 2)]
            for x, y in geometry.coords]


def _local_polygon(geometry, origin_x, origin_y):
    """Convert the exterior of a projected Polygon to local coordinates."""
    if geometry.geom_type == 'MultiPolygon':
        geometry = max(geometry.geoms, key=lambda polygon: polygon.area)
    return [[round(float(x - origin_x), 2), round(float(y - origin_y), 2)]
            for x, y in geometry.exterior.coords]


def export_city(place, output, distance):
    try:
        import osmnx as ox
    except ImportError as exc:
        raise SystemExit(
            'Install city-pipeline dependencies first: '
            'python -m pip install -r requirements-city.txt'
        ) from exc

    boundary_gdf = ox.geocode_to_gdf(place)
    boundary = boundary_gdf.geometry.iloc[0]
    centroid = boundary.centroid
    roads_graph = ox.graph_from_point(
        (centroid.y, centroid.x), dist=distance, network_type='drive',
        simplify=True,
    )
    roads_graph = ox.project_graph(roads_graph)
    _, edges = ox.graph_to_gdfs(roads_graph)

    # Query OSM features in WGS84 first, then project both datasets to metres.
    buildings = ox.features_from_polygon(
        boundary,
        tags={'building': True},
    )
    buildings = ox.project_gdf(buildings)
    projected_boundary = ox.project_gdf(boundary_gdf).geometry.iloc[0]

    origin_x = float(projected_boundary.centroid.x)
    origin_y = float(projected_boundary.centroid.y)
    road_features = []
    for _, edge in edges.iterrows():
        geometry = edge.geometry
        if geometry is None:
            continue
        road_features.append({
            'name': str(edge.get('name', 'Unnamed road')),
            'highway': str(edge.get('highway', 'road')),
            'coordinates': _local_linestring(geometry, origin_x, origin_y),
        })

    building_features = []
    for _, building in buildings.iterrows():
        geometry = building.geometry
        if geometry is None or geometry.is_empty:
            continue
        height = building.get('height')
        levels = building.get('building:levels')
        try:
            height = float(height)
        except (TypeError, ValueError):
            try:
                height = max(6.0, float(levels) * 3.2)
            except (TypeError, ValueError):
                height = 10.0
        building_features.append({
            'building': str(building.get('building', 'yes')),
            'height': round(height, 2),
            'coordinates': _local_polygon(geometry, origin_x, origin_y),
        })

    payload = {
        'schema': 'skybound.city.v1',
        'source': 'OpenStreetMap via OSMnx',
        'attribution': '© OpenStreetMap contributors',
        'place': place,
        'units': 'meters',
        'origin_projected_meters': [origin_x, origin_y],
        'roads': road_features,
        'buildings': building_features,
    }
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, indent=2), encoding='utf-8')
    print(f'Exported {len(road_features)} roads and '
          f'{len(building_features)} buildings to {output}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('place', help='City or area understood by OpenStreetMap')
    parser.add_argument('--output', required=True, help='Destination JSON path')
    parser.add_argument('--distance', type=int, default=5000,
                        help='Radius around the place centroid in metres')
    args = parser.parse_args()
    export_city(args.place, args.output, args.distance)
