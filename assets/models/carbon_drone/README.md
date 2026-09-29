# Carbon drone model

Place the legally obtained Sketchfab model files in this folder. The game
currently detects the supplied nested `drone/fly.glb` automatically, or accepts one of
these preferred names:

The optimized runtime copy is `drone/skybound_drone.glb`; the original source
file remains in `drone/source/fly.glb`.

- `carbon_drone.obj` plus its texture files, or
- `carbon_drone.gltf` plus its referenced assets, or
- `carbon_drone.glb`

The game loads the first supported file it finds. Keep the model's textures in
the same folder or preserve the relative paths created by the exporter.

Do not commit a paid or restricted asset to the repository unless its license
allows redistribution. The current Sketchfab page is a Store listing, so check
the purchase license before adding it to GitHub.
