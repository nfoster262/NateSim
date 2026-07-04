using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Util;

public class RobotCodeVisualizer : MonoBehaviour
{
    [Header("Frame Source")]
    [SerializeField] private RobotCodeUdpReceiver receiver;
    [SerializeField] private LoadMatch loadMatch;
    [SerializeField] private bool autoFindSpawnedRobot = true;

    [Header("Robot Pose")]
    [SerializeField] private Transform robotRoot;
    [SerializeField] private Rigidbody robotRigidbody;
    [SerializeField] private bool followRobotPose = true;
    [SerializeField] private bool makeRobotKinematicWhileFollowing = true;
    [SerializeField] private float poseSmoothing = 18f;

    [Header("Visualizer Mode")]
    [SerializeField] private bool disableUnityRobotControls = true;
    [SerializeField] private bool disableUnityMechanismActions = true;
    [SerializeField] private bool disableUnityFieldGamePieceSystems = true;
    [SerializeField] private bool removeUnitySpawnedGamePieces = true;

    [Header("WPILib Field Mapping")]
    [SerializeField] private float fieldLengthMeters = 16.540988f;
    [SerializeField] private float fieldWidthMeters = 8.052f;
    [SerializeField] private Vector3 unityFieldOrigin = Vector3.zero;
    [SerializeField] private float unityMetersPerWpilibMeter = 1f;
    [SerializeField] private float headingOffsetDegrees = 0f;

    [Header("Mechanism Visuals")]
    [SerializeField] private ComponentBinding[] componentBindings;

    [Header("Game Pieces")]
    [SerializeField] private bool mirrorFuelPoses = true;
    [SerializeField] private bool mirrorProjectilePoses = true;
    [SerializeField] private Transform gamePieceParent;
    [SerializeField] private PieceNames fuelPieceName = PieceNames.Fuel;

    [Header("Status")]
    [SerializeField] private int latestSuccessfulScoreCount;
    [SerializeField] private int latestLaunchEventId;

    private readonly List<GamePiece> mirroredFuel = new List<GamePiece>();
    private readonly List<GamePiece> mirroredProjectiles = new List<GamePiece>();
    private GameObject fuelPrefab;
    private Transform configuredRobotRoot;
    private bool configuredFieldAsVisualizer;

    private void Awake()
    {
        if (!receiver)
        {
            receiver = GetComponent<RobotCodeUdpReceiver>();
        }

        ResolveRobotRoot();

        if (robotRigidbody && makeRobotKinematicWhileFollowing)
        {
            robotRigidbody.isKinematic = true;
        }

        LoadFuelPrefab();
    }

    private void Start()
    {
        ConfigureRobotAsVisualizer();
        ConfigureFieldAsVisualizer();
    }

    private void FixedUpdate()
    {
        if (!receiver || !receiver.TryGetLatestFrame(out RobotCodeFrame frame))
        {
            return;
        }

        ConfigureRobotAsVisualizer();
        ConfigureFieldAsVisualizer();

        latestSuccessfulScoreCount = frame.successfulScoreCount;
        latestLaunchEventId = frame.launchEventId;

        if (followRobotPose && frame.robotPose != null)
        {
            ApplyRobotPose(frame.robotPose);
        }

        ApplyComponentPoses(frame.componentPoses);

        if (mirrorFuelPoses)
        {
            SyncGamePieces(mirroredFuel, frame.fuelPoses);
        }

        if (mirrorProjectilePoses)
        {
            SyncGamePieces(mirroredProjectiles, frame.projectilePoses);
        }
    }

    private void ConfigureFieldAsVisualizer()
    {
        if (configuredFieldAsVisualizer)
        {
            return;
        }

        bool foundFieldObject = false;

        if (disableUnityFieldGamePieceSystems)
        {
            foreach (SpawnGamePiece spawner in FindObjectsByType<SpawnGamePiece>(FindObjectsSortMode.None))
            {
                foundFieldObject = true;
                spawner.enabled = false;
            }
        }

        if (removeUnitySpawnedGamePieces)
        {
            foreach (GamePiece piece in FindObjectsByType<GamePiece>(FindObjectsSortMode.None))
            {
                foundFieldObject = true;
                if (!piece || piece.pieceType != fuelPieceName || IsMirroredPiece(piece))
                {
                    continue;
                }

                Destroy(piece.gameObject);
            }
        }

        if (!loadMatch)
        {
            loadMatch = FindFirstObjectByType<LoadMatch>();
        }

        foundFieldObject |= loadMatch != null && loadMatch.getFieldHolder() != null;
        configuredFieldAsVisualizer = foundFieldObject;
    }

    private void ConfigureRobotAsVisualizer()
    {
        ResolveRobotRoot();

        if (!robotRoot || configuredRobotRoot == robotRoot)
        {
            return;
        }

        configuredRobotRoot = robotRoot;

        if (!robotRigidbody)
        {
            robotRigidbody = robotRoot.GetComponent<Rigidbody>();
        }

        if (robotRigidbody && makeRobotKinematicWhileFollowing)
        {
            robotRigidbody.isKinematic = true;
            robotRigidbody.velocity = Vector3.zero;
            robotRigidbody.angularVelocity = Vector3.zero;
        }

        if (disableUnityRobotControls)
        {
            foreach (PlayerInput playerInput in robotRoot.GetComponentsInChildren<PlayerInput>(true))
            {
                playerInput.DeactivateInput();
                playerInput.enabled = false;
            }

            foreach (SwerveController controller in robotRoot.GetComponentsInChildren<SwerveController>(true))
            {
                controller.enabled = false;
            }

            foreach (ModuleBehaviour module in robotRoot.GetComponentsInChildren<ModuleBehaviour>(true))
            {
                module.enabled = false;
            }

            foreach (DriveMotor driveMotor in robotRoot.GetComponentsInChildren<DriveMotor>(true))
            {
                driveMotor.enabled = false;
            }

            foreach (WheelBehaviour wheel in robotRoot.GetComponentsInChildren<WheelBehaviour>(true))
            {
                wheel.enabled = false;
            }

            foreach (AutoAlign autoAlign in robotRoot.GetComponentsInChildren<AutoAlign>(true))
            {
                autoAlign.enabled = false;
            }

            foreach (AutoAim autoAim in robotRoot.GetComponentsInChildren<AutoAim>(true))
            {
                autoAim.enabled = false;
            }
        }

        if (disableUnityMechanismActions)
        {
            foreach (JointController controller in robotRoot.GetComponentsInChildren<JointController>(true))
            {
                controller.enabled = false;
            }

            foreach (BuildNode node in robotRoot.GetComponentsInChildren<BuildNode>(true))
            {
                node.enabled = false;
            }

            foreach (InterpolateNode node in robotRoot.GetComponentsInChildren<InterpolateNode>(true))
            {
                node.enabled = false;
            }
        }
    }

    private void ResolveRobotRoot()
    {
        if (robotRoot || !autoFindSpawnedRobot)
        {
            return;
        }

        BuildFrame localFrame = GetComponentInParent<BuildFrame>();
        if (localFrame)
        {
            robotRoot = localFrame.transform;
        }

        if (!robotRoot)
        {
            if (!loadMatch)
            {
                loadMatch = FindFirstObjectByType<LoadMatch>();
            }

            GameObject loadedRobot = loadMatch ? loadMatch.GetRobotLoaded() : null;
            if (loadedRobot)
            {
                robotRoot = loadedRobot.transform;
            }
        }

        if (robotRoot && !robotRigidbody)
        {
            robotRigidbody = robotRoot.GetComponent<Rigidbody>();
        }
    }

    private void ApplyRobotPose(Pose2dFrame pose)
    {
        Vector3 targetPosition = WpilibToUnityPosition(pose.x, pose.y, 0f);
        Quaternion targetRotation = WpilibYawToUnityRotation(pose.theta);

        if (robotRigidbody && robotRigidbody.isKinematic)
        {
            robotRigidbody.MovePosition(Vector3.Lerp(robotRigidbody.position, targetPosition,
                SmoothingStep()));
            robotRigidbody.MoveRotation(Quaternion.Slerp(robotRigidbody.rotation, targetRotation,
                SmoothingStep()));
            return;
        }

        robotRoot.SetPositionAndRotation(
            Vector3.Lerp(robotRoot.position, targetPosition, SmoothingStep()),
            Quaternion.Slerp(robotRoot.rotation, targetRotation, SmoothingStep()));
    }

    private void ApplyComponentPoses(Pose3dFrame[] poses)
    {
        if (poses == null || componentBindings == null)
        {
            return;
        }

        foreach (Pose3dFrame pose in poses)
        {
            if (pose == null)
            {
                continue;
            }

            foreach (ComponentBinding binding in componentBindings)
            {
                if (binding.target && binding.name == pose.name)
                {
                    binding.target.localPosition = new Vector3(pose.x, pose.z, pose.y);
                    binding.target.localRotation = Quaternion.Euler(
                        -Mathf.Rad2Deg * pose.pitch,
                        Mathf.Rad2Deg * pose.yaw,
                        -Mathf.Rad2Deg * pose.roll);
                }
            }
        }
    }

    private void SyncGamePieces(List<GamePiece> pieces, Pose3dFrame[] poses)
    {
        if (poses == null)
        {
            TrimPieces(pieces, 0);
            return;
        }

        if (!EnsurePieceCount(pieces, poses.Length))
        {
            return;
        }

        for (int i = 0; i < poses.Length; i++)
        {
            GamePiece piece = pieces[i];
            Pose3dFrame pose = poses[i];
            if (!piece || pose == null)
            {
                continue;
            }

            piece.state = GamePieceState.World;
            piece.transform.SetPositionAndRotation(
                WpilibToUnityPosition(pose.x, pose.y, pose.z),
                WpilibYawToUnityRotation(pose.yaw));

            if (piece.rb)
            {
                piece.rb.isKinematic = true;
                piece.rb.detectCollisions = false;
                piece.rb.velocity = Vector3.zero;
                piece.rb.angularVelocity = Vector3.zero;
            }
        }
    }

    private bool EnsurePieceCount(List<GamePiece> pieces, int count)
    {
        LoadFuelPrefab();
        if (!fuelPrefab)
        {
            return false;
        }

        while (pieces.Count < count)
        {
            Transform parent = ResolveGamePieceParent();
            GamePiece piece = Instantiate(fuelPrefab, parent).GetComponent<GamePiece>();
            piece.pieceType = fuelPieceName;
            piece.state = GamePieceState.World;
            piece.originalParent = parent;
            piece.enabled = false;
            if (piece.rb)
            {
                piece.rb.isKinematic = true;
                piece.rb.detectCollisions = false;
            }
            pieces.Add(piece);
        }

        TrimPieces(pieces, count);
        return true;
    }

    private void TrimPieces(List<GamePiece> pieces, int count)
    {
        for (int i = pieces.Count - 1; i >= count; i--)
        {
            if (pieces[i])
            {
                Destroy(pieces[i].gameObject);
            }
            pieces.RemoveAt(i);
        }
    }

    private bool IsMirroredPiece(GamePiece piece)
    {
        return mirroredFuel.Contains(piece) || mirroredProjectiles.Contains(piece);
    }

    private Transform ResolveGamePieceParent()
    {
        if (gamePieceParent)
        {
            return gamePieceParent;
        }

        if (!loadMatch)
        {
            loadMatch = FindFirstObjectByType<LoadMatch>();
        }

        GameObject fieldHolder = loadMatch ? loadMatch.getFieldHolder() : null;
        if (fieldHolder && fieldHolder.transform.childCount > 0)
        {
            return fieldHolder.transform.GetChild(0);
        }

        return fieldHolder ? fieldHolder.transform : transform;
    }

    private void LoadFuelPrefab()
    {
        if (fuelPrefab)
        {
            return;
        }

        GameObject[] pieces = Resources.LoadAll<GameObject>("Pieces");
        foreach (GameObject piece in pieces)
        {
            GamePiece gamePiece = piece.GetComponent<GamePiece>();
            if (gamePiece && gamePiece.pieceType == fuelPieceName)
            {
                fuelPrefab = piece;
                return;
            }
        }
    }

    private Vector3 WpilibToUnityPosition(float xMeters, float yMeters, float zMeters)
    {
        float unityX = (xMeters - fieldLengthMeters * 0.5f) * unityMetersPerWpilibMeter;
        float unityZ = (yMeters - fieldWidthMeters * 0.5f) * unityMetersPerWpilibMeter;
        float unityY = zMeters * unityMetersPerWpilibMeter;
        return unityFieldOrigin + new Vector3(unityX, unityY, unityZ);
    }

    private Quaternion WpilibYawToUnityRotation(float yawRadians)
    {
        float yawDegrees = 90f - Mathf.Rad2Deg * yawRadians + headingOffsetDegrees;
        return Quaternion.Euler(0f, yawDegrees, 0f);
    }

    private float SmoothingStep()
    {
        return 1f - Mathf.Exp(-poseSmoothing * Time.fixedDeltaTime);
    }
}

[System.Serializable]
public class ComponentBinding
{
    public string name;
    public Transform target;
}
