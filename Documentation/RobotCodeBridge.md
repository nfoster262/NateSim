# Robot Code Bridge

NateSim can act as a 3D visualizer for the 2026 MECO robot code. In this mode,
the robot is not driven by Unity, a keyboard, or a game controller inside NateSim.
All control comes from the WPILib robot simulator, Driver Station, and Elastic.
NateSim only receives robot pose, mechanism pose, ball pose, projectile pose, and
score information over UDP and renders it on the field.

This is similar in spirit to opening a 3D field view in AdvantageScope, except the
visualizer is the NateSim Unity scene.

## What You Need

- Unity Hub
- Unity Editor `2023.2.22f1`
- This NateSim repository
- The `2026-Rebuilt` robot-code repository with the NateSim publisher changes
- WPILib 2026 tools
- Elastic, if you want to use it as your robot-code dashboard

The exact Unity version matters. This project was last saved with:

```text
Unity 2023.2.22f1
```

You can confirm this in `ProjectSettings/ProjectVersion.txt`.

## Install Unity

1. Install Unity Hub from Unity's website.
2. Open Unity Hub.
3. Go to `Installs`.
4. Install Unity Editor `2023.2.22f1`.
5. Include the normal Windows build support modules if Unity asks. NateSim can run
   in the editor, so extra platform modules are not important for basic testing.

If Unity Hub warns that the project was made with a different version, use
`2023.2.22f1` when possible. Opening with a newer version may upgrade project files.

## Open NateSim

1. Open Unity Hub.
2. Click `Add` or `Add project from disk`.
3. Select the NateSim folder:

```text
C:\Users\resto\Documents\GitHub\NateSim
```

4. Open the project.
5. Wait for Unity to import assets. The first import can take a while.

If Unity asks to enter Safe Mode because of compile errors, do not start changing
random assets. Open the Console first and fix the C# compile error.

## Open The Field Scene

1. In Unity, find the `Project` window.
2. Open this scene:

```text
Assets/Scenes/FieldScene.unity
```

3. You should see the field in the `Scene` view.

The scene is the place where we add the visualizer receiver object.

## Add The Bridge Object

Yes, you need to add this to the Unity `Hierarchy`. It is not enough for the
scripts to exist in the `Project` window. Unity only runs components that are
attached to GameObjects in the open scene.

1. In the `Hierarchy` window, right-click empty space.
2. Choose `Create Empty`.
3. Name it:

```text
Robot Code Bridge
```

4. Select `Robot Code Bridge`.
5. In the `Inspector`, click `Add Component`.
6. Add:

```text
RobotCodeUdpReceiver
```

7. Click `Add Component` again.
8. Add:

```text
RobotCodeVisualizer
```

After this step, your scene hierarchy should include something like this:

```text
FieldScene
└── Robot Code Bridge
    ├── RobotCodeUdpReceiver
    └── RobotCodeVisualizer
```

`Robot Code Bridge` can sit at the root of the scene. It does not need to be a
child of the robot. With `Auto Find Spawned Robot` enabled, the visualizer will
find the robot that NateSim spawns for the match.

## Configure The Receiver

`RobotCodeUdpReceiver` is the Unity component that listens for messages from the
robot-code simulator. Think of it as the radio antenna for NateSim. It does not
move the robot by itself, and it does not know anything about swerve, shooting, or
scoring. Its only job is to wait for JSON packets from the robot code and keep the
newest packet available for `RobotCodeVisualizer`.

Select the `Robot Code Bridge` GameObject in the `Hierarchy`. In the `Inspector`,
find the `RobotCodeUdpReceiver` component.

Set it like this:

- `Port`: `5808`
- `Listen On Start`: checked

What those settings mean:

- `Port` is the local UDP port Unity listens on. The robot code sends to
  `127.0.0.1:5808`, so this must also be `5808`.
- `Listen On Start` tells Unity to start listening automatically when you press
  Play. Leave this checked unless you are debugging the receiver manually.

You do not need to type in an IP address in Unity. Unity is listening on your
computer. The robot code is responsible for sending to `127.0.0.1`, which means
"this same computer."

When Play Mode starts, the receiver opens UDP port `5808` in the background. UDP
does not create a visible connection, so there is no "connected" light or login
step. If the robot code is not running yet, the receiver simply waits. When the
robot code starts publishing, the newest packet is picked up by the visualizer on
the next physics update.

The receiver should be on an active GameObject in the scene. The easiest setup is:

```text
FieldScene
└── Robot Code Bridge
    ├── RobotCodeUdpReceiver
    └── RobotCodeVisualizer
```

If the receiver is missing, disabled, or using the wrong port, NateSim will still
enter Play Mode, but the robot will not follow the WPILib sim.

Quick receiver checklist:

- `Robot Code Bridge` exists in the `Hierarchy`.
- `RobotCodeUdpReceiver` is attached to it.
- The checkbox beside the component name is enabled.
- `Port` is `5808`.
- `Listen On Start` is checked.
- Unity is in Play Mode.
- The robot code is running and publishing to `127.0.0.1:5808`.

## Configure The Visualizer

On `RobotCodeVisualizer`, use these defaults:

- `Auto Find Spawned Robot`: checked
- `Follow Robot Pose`: checked
- `Make Robot Kinematic While Following`: checked
- `Disable Unity Robot Controls`: checked
- `Disable Unity Mechanism Actions`: checked
- `Disable Unity Field Game Piece Systems`: checked
- `Remove Unity Spawned Game Pieces`: checked
- `Mirror Fuel Poses`: checked
- `Mirror Projectile Poses`: checked
- `Fuel Piece Name`: `Fuel`

These settings are important. They make NateSim passive:

- Unity `PlayerInput` is disabled.
- NateSim's local swerve controller is disabled.
- NateSim's local wheel/module physics drive scripts are disabled.
- NateSim's local mechanism and game-piece action scripts are disabled.
- NateSim's local field spawners are disabled.
- NateSim's pre-spawned local fuel pieces are removed.
- The robot transform follows the pose sent by WPILib sim.

Leave `Robot Root` empty at first. With `Auto Find Spawned Robot` enabled, the
visualizer will ask `LoadMatch` which robot was spawned and use that robot.

## Optional: Game Piece Parent

`Game Piece Parent` controls where mirrored fuel objects are created in the Unity
hierarchy. If you leave it empty, the visualizer will use a nearby parent transform.
That is usually fine for early testing.

If you want a cleaner hierarchy later, set `Game Piece Parent` to the field holder
object after entering Play Mode and seeing where the field is spawned.

## Optional: Mechanism Bindings

`Component Bindings` lets you connect named robot-code mechanism poses to Unity
transforms.

The robot code currently publishes these names:

- `flywheel`
- `hood`
- `intakeRack`
- `intakeKickerBar`
- `hopper`
- `climber`

For each binding:

1. Increase the `Component Bindings` array size.
2. Set `Name` to one of the names above.
3. Drag the matching Unity transform into `Target`.

You can skip this for the first test. The robot body, fuel, and projectiles can
still work without mechanism bindings.

## Run NateSim

1. Save the scene.
2. Click the Play button at the top of Unity.
3. NateSim should enter Play Mode.
4. The bridge is now listening on UDP port `5808`.

At this point, NateSim will not move the robot by itself. That is expected.

## Run The Robot Code Simulator

Open a terminal in the robot-code repo:

```text
C:\Users\resto\Documents\GitHub\2026-Rebuilt
```

Run:

```powershell
$env:JAVA_HOME='C:\Users\Public\wpilib\2026\jdk'
.\gradlew.bat simulateJava
```

Then:

1. Open the WPILib simulation Driver Station.
2. Enable the robot.
3. Use your normal robot-code controls, commands, and Elastic dashboard.

The robot code should publish frames to:

```text
127.0.0.1:5808
```

NateSim should receive those frames and update the 3D view.

## Normal Startup Order

Use this order while testing:

1. Open NateSim in Unity.
2. Open `Assets/Scenes/FieldScene.unity`.
3. Press Play in Unity.
4. Start `simulateJava` in `2026-Rebuilt`.
5. Enable the robot in the WPILib sim Driver Station.
6. Drive, run commands, and shoot from the robot-code simulator.
7. Watch NateSim as the visualizer.

If you start robot code before Unity, that is usually okay because UDP does not
need a connection. Unity will just begin showing the newest frames once it starts
listening.

## Verify It Is Working

You know the bridge is working when:

- The robot in NateSim moves when the WPILib sim robot moves.
- Pressing controller buttons while focused on Unity does not drive the NateSim robot.
- Fuel pieces appear or move based on robot-code simulation state.
- Projectiles appear when the robot-code shooter simulation launches fuel.
- Elastic and the WPILib sim remain the places where robot behavior is controlled.

## Troubleshooting

If the robot does not move:

- Make sure Unity is in Play Mode.
- Make sure `RobotCodeUdpReceiver` is on an active GameObject.
- Make sure `Listen On Start` is checked.
- Make sure the receiver port is `5808`.
- Make sure the robot code is running in simulation mode.
- Make sure the robot-code NateSim publisher is present in `2026-Rebuilt`.

If Unity controller input still drives the robot:

- Select `Robot Code Bridge`.
- Confirm `Disable Unity Robot Controls` is checked.
- Confirm `Auto Find Spawned Robot` is checked, or manually assign `Robot Root`.
- Enter Play Mode again so the visualizer can disable the spawned robot's input scripts.

If the robot appears offset or rotated:

- Tune `Unity Field Origin`.
- Tune `Unity Meters Per Wpilib Meter`.
- Tune `Heading Offset Degrees`.

The default coordinate mapping assumes WPILib field coordinates in meters:

- WPILib `x` maps to Unity `x`
- WPILib `y` maps to Unity `z`
- WPILib `z` maps to Unity `y`
- The WPILib field origin is centered into Unity by subtracting half the 2026 field
  length and width.

If balls do not appear:

- Confirm `Mirror Fuel Poses` is checked.
- Confirm `Mirror Projectile Poses` is checked.
- Confirm `Assets/Resources/Pieces/Fuel.prefab` exists.
- Confirm the robot code is publishing `fuelPoses` and `projectilePoses`.

If autonomous becomes very laggy:

- Look for hundreds or thousands of yellow fuel balls piling up in the Unity Game
  view.
- Confirm `Disable Unity Field Game Piece Systems` is checked.
- Confirm `Remove Unity Spawned Game Pieces` is checked.
- Stop Play Mode and start it again after changing those settings.
- NateSim should only show the fuel/projectile objects mirrored from robot code,
  not its own local field-spawned fuel plus the robot-code fuel at the same time.

## UDP Frame Format

The robot code publishes JSON frames like this:

```json
{
  "timestamp": 12.34,
  "robotPose": { "x": 8.2, "y": 4.0, "theta": 1.57 },
  "componentPoses": [
    { "name": "hood", "x": 0.0, "y": 0.0, "z": 0.0, "roll": 0.0, "pitch": 0.2, "yaw": 0.0 }
  ],
  "fuelPoses": [
    { "x": 7.0, "y": 3.0, "z": 0.04, "roll": 0.0, "pitch": 0.0, "yaw": 0.0 }
  ],
  "projectilePoses": [
    { "x": 7.4, "y": 3.2, "z": 1.1, "roll": 0.0, "pitch": 0.0, "yaw": 0.0 }
  ],
  "successfulScoreCount": 1,
  "launchEventId": 5
}
```

All linear units are meters and all angles are radians.
