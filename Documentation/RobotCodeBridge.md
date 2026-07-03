# Robot Code Bridge

NateSim can follow live robot-code simulation output through a small UDP JSON stream.
This is meant for the 2026 MECO robot code, which already produces these values through
AdvantageKit:

- `Visualization/RobotRemy/RobotPose`
- `Visualization/RobotRemy/ComponentPoses`
- `FieldSimulation/FuelPositions`
- `FieldSimulation/FuelProjectilePositions`
- `FieldSimulation/SuccessfulScoreCount`

## Unity Setup

1. Add `RobotCodeUdpReceiver` to a scene object.
2. Add `RobotCodeVisualizer` to the same object or the spawned robot.
3. Assign the active robot transform to `Robot Root`.
4. Assign any mechanism transforms in `Component Bindings` using names that match the
   JSON `componentPoses[].name` values.
5. Set `Game Piece Parent` to the field object that should own mirrored fuel pieces.
6. Run the robot code in simulation and publish UDP frames to port `5808`.

The default coordinate mapping assumes WPILib field coordinates in meters:

- WPILib `x` maps to Unity `x`
- WPILib `y` maps to Unity `z`
- WPILib `z` maps to Unity `y`
- The WPILib field origin is centered into Unity by subtracting half the 2026 field
  length and width.

If the imported field model is offset or scaled, tune `Unity Field Origin` and
`Unity Meters Per Wpilib Meter` in the Inspector.

## UDP Frame Format

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

## Java Publisher Sketch

In the robot repo, add a small simulation-only publisher that runs from
`RobotSimulation.visualizationPeriodic()` or `simulationPeriodic()`. Populate it from
`drive.getPhysicsPose()`, `RobotRemyVisualizer` component values, and the MapleSim fuel
pose arrays.

```java
public final class NateSimPublisher implements AutoCloseable {
  private final DatagramSocket socket = new DatagramSocket();
  private final InetAddress host = InetAddress.getByName("127.0.0.1");
  private final int port = 5808;

  public void publish(String json) {
    byte[] bytes = json.getBytes(StandardCharsets.UTF_8);
    socket.send(new DatagramPacket(bytes, bytes.length, host, port));
  }

  @Override
  public void close() {
    socket.close();
  }
}
```

The Unity side intentionally receives plain JSON so the robot project can build the
message with any JSON library or a small formatted string.
