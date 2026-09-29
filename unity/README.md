# Skybound Unity runtime

This folder is the C# migration target for Unity 6.6. The Python/Ursina
prototype remains at the repository root while systems are migrated and
verified one at a time.

## Create/open the Unity project

Open Unity Hub and add this `unity` folder as a project. If Unity asks for a
template, use **3D (URP)**. The project version is recorded in
`ProjectSettings/ProjectVersion.txt`.

## First scene setup

1. Create a scene named `Scenes/DroneTraining.unity`.
2. Create a `Drone` GameObject with a `Rigidbody`.
3. Add the supplied optimized model from:
   `../assets/models/carbon_drone/drone/skybound_drone.glb`.
4. Add `DroneController.cs` to the drone root.
5. Create a camera and add `DroneCamera.cs`; assign the drone as `target`.
6. Add a plane with a collider so the drone has a landing surface.

The first C# controls are:

| Control | Action |
|---|---|
| W / S | Increase / decrease lift |
| Up / Down | Pitch |
| A / D | Roll |
| Q / E | Yaw |
| R | Reset drone |

The C# scripts intentionally use explicit fields and small methods so each
part can be explained while we migrate the simulator.
