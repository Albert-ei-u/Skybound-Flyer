# Skybound Unity runtime

The repository root is the active Unity 6.6 project created by Unity Hub. The
Python/Ursina prototype remains available at the repository root as a
reference while systems are migrated and verified one at a time. The C#
scripts in this folder are the migration source copies; Unity uses the copies
under the root `assets/` folder.

## Create/open the Unity project

Open the repository root in Unity Hub. It already contains the generated
`ProjectSettings` and `Packages` folders. Use the **3D (URP)** template only
if creating a fresh Unity project.

## First scene setup

After Unity finishes importing the project, use:

```text
Skybound → Create Drone Training Scene
```

The editor tool creates `Scenes/DroneTraining.unity`, imports the supplied
drone model, adds the Rigidbody and controller, creates a collidable training
ground, and wires the stabilized camera automatically.

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
