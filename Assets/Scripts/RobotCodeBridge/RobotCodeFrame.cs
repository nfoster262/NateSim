using System;

[Serializable]
public class RobotCodeFrame
{
    public double timestamp;
    public Pose2dFrame robotPose;
    public Pose3dFrame[] componentPoses;
    public Pose3dFrame[] fuelPoses;
    public Pose3dFrame[] projectilePoses;
    public int successfulScoreCount;
    public int launchEventId;
}

[Serializable]
public class Pose2dFrame
{
    public float x;
    public float y;
    public float theta;
}

[Serializable]
public class Pose3dFrame
{
    public string name;
    public float x;
    public float y;
    public float z;
    public float roll;
    public float pitch;
    public float yaw;
}
