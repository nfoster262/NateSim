using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Util;

public class RobotCodePathPlannerAutoBridge : MonoBehaviour
{
    [Header("PathPlanner Files")]
    [SerializeField] private string pathPlannerRoot =
        @"C:\Users\resto\Documents\GitHub\2026-Rebuilt\src\main\deploy\pathplanner";
    [SerializeField] private string selectedAutoName = "Left Bump Rush";
    [SerializeField] private bool reloadOnStart = true;

    [Header("Robot")]
    [SerializeField] private RobotCodeUdpReceiver receiver;
    [SerializeField] private LoadMatch loadMatch;
    [SerializeField] private Transform robotRoot;
    [SerializeField] private bool autoFindSpawnedRobot = true;

    [Header("Timing")]
    [SerializeField] private bool runOnlyDuringAuto = false;
    [SerializeField] private bool syncTimelineToRobotCodeFrames = true;
    [SerializeField] private bool startTimelineOnRobotMotion = true;
    [SerializeField] private float autoElapsedAtFirstMotionSeconds = 0.5f;
    [SerializeField] private float motionStartDistanceMeters = 0.05f;
    [SerializeField] private float virtualButtonHoldSeconds = 0.15f;
    [SerializeField] private float defaultNamedCommandSeconds = 0.25f;
    [SerializeField] private float defaultPathSeconds = 2f;
    [SerializeField] private float minimumHeldCommandSeconds = 0.1f;

    [Header("Command Mapping")]
    [SerializeField] private bool enableAutoAimModulesDuringAim = true;

    [Header("Status")]
    [SerializeField] private int loadedCommandWindows;
    [SerializeField] private string lastActiveCommand;
    [SerializeField] private bool usingRobotCodeActionState;
    [SerializeField] private bool lastIntakeActive;
    [SerializeField] private bool lastShootActive;
    [SerializeField] private bool lastAimActive;

    private readonly List<AutoCommandWindow> commandWindows = new List<AutoCommandWindow>();
    private readonly HashSet<int> activeWindowIndexes = new HashSet<int>();
    private BuildNode[] buildNodes = Array.Empty<BuildNode>();
    private AutoAim[] autoAimModules = Array.Empty<AutoAim>();
    private float lastMatchTimer = float.PositiveInfinity;
    private bool hasRobotCodeTimelineStart;
    private bool hasInitialRobotPose;
    private Pose2dFrame initialRobotPose;
    private double robotCodeTimelineStart;
    private long lastActionStateFrameSequence = -1;

    private static readonly Regex TypeRegex = new Regex("\"type\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex NameRegex = new Regex("\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex PathNameRegex = new Regex("\"pathName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex WaitRegex = new Regex("\"waitTime\"\\s*:\\s*([-0-9.]+)", RegexOptions.Compiled);
    private static readonly Regex MaxVelocityRegex = new Regex("\"maxVelocity\"\\s*:\\s*([-0-9.]+)", RegexOptions.Compiled);
    private static readonly Regex AnchorRegex = new Regex(
        "\"anchor\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*([-0-9.]+)\\s*,\\s*\"y\"\\s*:\\s*([-0-9.]+)",
        RegexOptions.Compiled);
    private static readonly Regex EventMarkerRegex = new Regex(
        "\"name\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"waypointRelativePos\"\\s*:\\s*([-0-9.]+)",
        RegexOptions.Compiled);

    private void Start()
    {
        if (!receiver)
        {
            receiver = GetComponent<RobotCodeUdpReceiver>();
        }

        ResolveRobotRoot();
        if (reloadOnStart)
        {
            ReloadAuto();
        }
    }

    private void FixedUpdate()
    {
        ResolveRobotRoot();
        if (buildNodes.Length == 0 && robotRoot)
        {
            CacheRobotActions();
        }

        if (TryApplyRobotCodeActionState())
        {
            return;
        }

        if (FMS.MatchTimer > lastMatchTimer + 1f)
        {
            activeWindowIndexes.Clear();
            hasRobotCodeTimelineStart = false;
        }
        lastMatchTimer = FMS.MatchTimer;

        if (runOnlyDuringAuto && FMS.MatchState != MatchState.auto)
        {
            SetAutoAimModules(false);
            activeWindowIndexes.Clear();
            hasRobotCodeTimelineStart = false;
            hasInitialRobotPose = false;
            return;
        }

        float elapsed = ResolveAutoElapsedSeconds();
        if (syncTimelineToRobotCodeFrames && !hasRobotCodeTimelineStart)
        {
            return;
        }

        bool aimActive = false;
        lastActiveCommand = string.Empty;

        for (int i = 0; i < commandWindows.Count; i++)
        {
            AutoCommandWindow window = commandWindows[i];
            bool active = elapsed >= window.startSeconds && elapsed <= window.endSeconds;
            bool wasActive = activeWindowIndexes.Contains(i);

            if (!active)
            {
                if (wasActive)
                {
                    activeWindowIndexes.Remove(i);
                }
                continue;
            }

            activeWindowIndexes.Add(i);
            lastActiveCommand = window.name;
            aimActive |= IsAimCommand(window.name);
            ExecuteNamedCommand(window.name, !wasActive, true);
        }

        SetAutoAimModules(aimActive);
    }

    private bool TryApplyRobotCodeActionState()
    {
        if (!receiver || !receiver.TryGetLatestFrame(out RobotCodeFrame frame, out long frameSequence) ||
            frame == null || !frame.hasGameActionState)
        {
            usingRobotCodeActionState = false;
            return false;
        }

        usingRobotCodeActionState = true;
        bool newFrame = frameSequence != lastActionStateFrameSequence;
        bool intakePressed = newFrame && frame.intakeActive && !lastIntakeActive;
        bool shootPressed = newFrame && frame.shootActive && !lastShootActive;

        if (frame.intakeActive)
        {
            QueueRobotAction(NodeType.Intake, intakePressed);
        }

        if (frame.shootActive)
        {
            QueueRobotAction(NodeType.Transfer, shootPressed);
            QueueRobotAction(NodeType.Outake, shootPressed);
        }

        SetAutoAimModules(frame.aimActive);
        lastActiveCommand = DescribeRobotCodeActions(frame);

        if (newFrame)
        {
            lastActionStateFrameSequence = frameSequence;
            lastIntakeActive = frame.intakeActive;
            lastShootActive = frame.shootActive;
            lastAimActive = frame.aimActive;
        }

        return true;
    }

    private float ResolveAutoElapsedSeconds()
    {
        if (syncTimelineToRobotCodeFrames && receiver &&
            receiver.TryGetLatestFrame(out RobotCodeFrame frame) && frame != null)
        {
            if (!hasRobotCodeTimelineStart)
            {
                if (startTimelineOnRobotMotion && frame.robotPose != null)
                {
                    if (!HasRobotMotionStarted(frame.robotPose))
                    {
                        return 0f;
                    }

                    robotCodeTimelineStart = frame.timestamp - autoElapsedAtFirstMotionSeconds;
                }
                else
                {
                    robotCodeTimelineStart = frame.timestamp;
                }

                hasRobotCodeTimelineStart = true;
            }

            return Mathf.Max(0f, (float)(frame.timestamp - robotCodeTimelineStart));
        }

        FMS fms = FindFirstObjectByType<FMS>();
        return fms ? Mathf.Max(0f, fms.matchTime - FMS.MatchTimer) : 0f;
    }

    private bool HasRobotMotionStarted(Pose2dFrame pose)
    {
        if (!hasInitialRobotPose)
        {
            initialRobotPose = new Pose2dFrame
            {
                x = pose.x,
                y = pose.y,
                theta = pose.theta
            };
            hasInitialRobotPose = true;
            return false;
        }

        float dx = pose.x - initialRobotPose.x;
        float dy = pose.y - initialRobotPose.y;
        return dx * dx + dy * dy >= motionStartDistanceMeters * motionStartDistanceMeters;
    }

    public void ReloadAuto()
    {
        commandWindows.Clear();

        string autoPath = ResolveAutoPath();
        if (string.IsNullOrEmpty(autoPath))
        {
            loadedCommandWindows = 0;
            return;
        }

        string json = File.ReadAllText(autoPath);
        int commandIndex = json.IndexOf("\"command\"", StringComparison.Ordinal);
        int objectStart = json.IndexOf('{', commandIndex);
        if (objectStart < 0)
        {
            loadedCommandWindows = 0;
            return;
        }

        string rootCommand = ExtractObjectAt(json, objectStart);
        ScheduleCommand(rootCommand, 0f, null);
        commandWindows.Sort((a, b) => a.startSeconds.CompareTo(b.startSeconds));
        loadedCommandWindows = commandWindows.Count;
    }

    private void ResolveRobotRoot()
    {
        if (robotRoot || !autoFindSpawnedRobot)
        {
            return;
        }

        if (!loadMatch)
        {
            loadMatch = FindFirstObjectByType<LoadMatch>();
        }

        GameObject loadedRobot = loadMatch ? loadMatch.GetRobotLoaded() : null;
        if (loadedRobot)
        {
            robotRoot = loadedRobot.transform;
            CacheRobotActions();
        }
    }

    private void CacheRobotActions()
    {
        buildNodes = robotRoot ? robotRoot.GetComponentsInChildren<BuildNode>(true) : Array.Empty<BuildNode>();
        autoAimModules = robotRoot ? robotRoot.GetComponentsInChildren<AutoAim>(true) : Array.Empty<AutoAim>();
    }

    private string ResolveAutoPath()
    {
        string autosDir = Path.Combine(pathPlannerRoot, "autos");
        if (!Directory.Exists(autosDir))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(selectedAutoName))
        {
            string configured = Path.Combine(autosDir, selectedAutoName + ".auto");
            if (File.Exists(configured))
            {
                return configured;
            }
        }

        string[] autos = Directory.GetFiles(autosDir, "*.auto");
        return autos.Length > 0 ? autos[0] : null;
    }

    private float ScheduleCommand(string commandJson, float startSeconds, float? forcedEndSeconds)
    {
        string type = ReadFirst(TypeRegex, commandJson);
        switch (type)
        {
            case "named":
            {
                string name = ReadFirst(NameRegex, commandJson);
                if (!string.IsNullOrEmpty(name))
                {
                    float endSeconds = forcedEndSeconds ?? startSeconds + defaultNamedCommandSeconds;
                    commandWindows.Add(new AutoCommandWindow(name, startSeconds,
                        Mathf.Max(startSeconds + minimumHeldCommandSeconds, endSeconds)));
                }
                return forcedEndSeconds.HasValue ? forcedEndSeconds.Value - startSeconds : defaultNamedCommandSeconds;
            }
            case "wait":
                return ReadFloat(WaitRegex, commandJson, 0f);
            case "path":
            {
                string pathName = ReadFirst(PathNameRegex, commandJson);
                float duration = EstimatePathDuration(pathName);
                SchedulePathEventMarkers(pathName, startSeconds, duration);
                return duration;
            }
            case "sequential":
            {
                float cursor = startSeconds;
                foreach (string child in ExtractChildCommands(commandJson))
                {
                    cursor += ScheduleCommand(child, cursor, null);
                }
                return cursor - startSeconds;
            }
            case "deadline":
            case "race":
            case "parallel":
            {
                List<string> children = ExtractChildCommands(commandJson);
                if (children.Count == 0)
                {
                    return 0f;
                }

                float duration = type == "parallel" ? 0f : EstimateDuration(children[0]);
                if (type == "parallel")
                {
                    foreach (string child in children)
                    {
                        duration = Mathf.Max(duration, EstimateDuration(child));
                    }
                }

                float groupEnd = startSeconds + duration;
                for (int i = 0; i < children.Count; i++)
                {
                    ScheduleCommand(children[i], startSeconds, i == 0 ? null : groupEnd);
                }
                return duration;
            }
            default:
                return forcedEndSeconds.HasValue ? forcedEndSeconds.Value - startSeconds : 0f;
        }
    }

    private float EstimateDuration(string commandJson)
    {
        string type = ReadFirst(TypeRegex, commandJson);
        switch (type)
        {
            case "named":
                return defaultNamedCommandSeconds;
            case "wait":
                return ReadFloat(WaitRegex, commandJson, 0f);
            case "path":
                return EstimatePathDuration(ReadFirst(PathNameRegex, commandJson));
            case "sequential":
            {
                float total = 0f;
                foreach (string child in ExtractChildCommands(commandJson))
                {
                    total += EstimateDuration(child);
                }
                return total;
            }
            case "deadline":
            case "race":
            {
                List<string> children = ExtractChildCommands(commandJson);
                return children.Count > 0 ? EstimateDuration(children[0]) : 0f;
            }
            case "parallel":
            {
                float duration = 0f;
                foreach (string child in ExtractChildCommands(commandJson))
                {
                    duration = Mathf.Max(duration, EstimateDuration(child));
                }
                return duration;
            }
            default:
                return 0f;
        }
    }

    private void SchedulePathEventMarkers(string pathName, float startSeconds, float pathDuration)
    {
        string pathJson = ReadPathJson(pathName);
        if (string.IsNullOrEmpty(pathJson))
        {
            return;
        }

        float maxMarkerPosition = 0f;
        List<Tuple<string, float>> markers = new List<Tuple<string, float>>();
        foreach (Match match in EventMarkerRegex.Matches(pathJson))
        {
            float position = ParseFloat(match.Groups[2].Value, 0f);
            markers.Add(new Tuple<string, float>(match.Groups[1].Value, position));
            maxMarkerPosition = Mathf.Max(maxMarkerPosition, position);
        }

        foreach (Tuple<string, float> marker in markers)
        {
            float normalized = maxMarkerPosition > 0f ? marker.Item2 / maxMarkerPosition : 0f;
            float markerStart = startSeconds + pathDuration * Mathf.Clamp01(normalized);
            commandWindows.Add(new AutoCommandWindow(marker.Item1, markerStart,
                Mathf.Max(markerStart + minimumHeldCommandSeconds, startSeconds + pathDuration)));
        }
    }

    private float EstimatePathDuration(string pathName)
    {
        string pathJson = ReadPathJson(pathName);
        if (string.IsNullOrEmpty(pathJson))
        {
            return defaultPathSeconds;
        }

        float maxVelocity = ReadFloat(MaxVelocityRegex, pathJson, 0f);
        if (maxVelocity <= 0f)
        {
            return defaultPathSeconds;
        }

        float length = 0f;
        Vector2 previous = Vector2.zero;
        bool hasPrevious = false;
        foreach (Match match in AnchorRegex.Matches(pathJson))
        {
            Vector2 current = new Vector2(ParseFloat(match.Groups[1].Value, 0f),
                ParseFloat(match.Groups[2].Value, 0f));
            if (hasPrevious)
            {
                length += Vector2.Distance(previous, current);
            }
            previous = current;
            hasPrevious = true;
        }

        return length > 0f ? Mathf.Max(defaultNamedCommandSeconds, length / maxVelocity) : defaultPathSeconds;
    }

    private string ReadPathJson(string pathName)
    {
        if (string.IsNullOrEmpty(pathName))
        {
            return null;
        }

        string path = Path.Combine(pathPlannerRoot, "paths", pathName + ".path");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private void ExecuteNamedCommand(string commandName, bool pressed, bool held)
    {
        if (string.IsNullOrEmpty(commandName) || buildNodes.Length == 0)
        {
            return;
        }

        if (IsIgnoredCommand(commandName) || IsAimCommand(commandName) || IsSpinUpCommand(commandName) ||
            IsMechanismOnlyCommand(commandName))
        {
            return;
        }

        NodeType actionType;
        if (IsShootCommand(commandName))
        {
            QueueRobotAction(NodeType.Transfer, pressed);
            actionType = NodeType.Outake;
        }
        else if (IsCollectionCommand(commandName))
        {
            actionType = NodeType.Intake;
        }
        else
        {
            return;
        }

        QueueRobotAction(actionType, pressed);
    }

    private void QueueRobotAction(NodeType actionType, bool pressed)
    {
        foreach (BuildNode node in buildNodes)
        {
            if (node)
            {
                node.QueueAutoAction(actionType, pressed, virtualButtonHoldSeconds);
            }
        }
    }

    private static string DescribeRobotCodeActions(RobotCodeFrame frame)
    {
        if (frame.intakeActive && frame.shootActive)
        {
            return "RobotCode:Intake+Shoot";
        }

        if (frame.intakeActive)
        {
            return "RobotCode:Intake";
        }

        if (frame.shootActive)
        {
            return "RobotCode:Shoot";
        }

        if (frame.aimActive)
        {
            return "RobotCode:Aim";
        }

        return string.Empty;
    }

    private void SetAutoAimModules(bool enabled)
    {
        if (!enableAutoAimModulesDuringAim)
        {
            return;
        }

        foreach (AutoAim autoAim in autoAimModules)
        {
            if (autoAim)
            {
                autoAim.enabled = enabled;
            }
        }
    }

    private static bool IsShootCommand(string commandName)
    {
        string lower = commandName.ToLowerInvariant();
        return lower.Contains("feed") || lower.Contains("shoot") || lower.Contains("agitate");
    }

    private static bool IsCollectionCommand(string commandName)
    {
        string lower = commandName.ToLowerInvariant();
        return lower.Contains("spinintake") || lower.Contains("collect") || lower.Contains("pickup") ||
               lower.Contains("harvest");
    }

    private static bool IsAimCommand(string commandName)
    {
        return commandName.ToLowerInvariant().Contains("aim");
    }

    private static bool IsSpinUpCommand(string commandName)
    {
        string lower = commandName.ToLowerInvariant();
        return lower.Contains("spinup") || lower.Contains("fender");
    }

    private static bool IsIgnoredCommand(string commandName)
    {
        string lower = commandName.ToLowerInvariant();
        return lower.Contains("idle") || lower.Contains("stow");
    }

    private static bool IsMechanismOnlyCommand(string commandName)
    {
        string lower = commandName.ToLowerInvariant();
        return lower.Contains("deploy") || lower.Contains("rack");
    }

    private static List<string> ExtractChildCommands(string commandJson)
    {
        List<string> children = new List<string>();
        int commandsIndex = commandJson.IndexOf("\"commands\"", StringComparison.Ordinal);
        if (commandsIndex < 0)
        {
            return children;
        }

        int arrayStart = commandJson.IndexOf('[', commandsIndex);
        int arrayEnd = FindMatching(commandJson, arrayStart, '[', ']');
        if (arrayStart < 0 || arrayEnd < 0)
        {
            return children;
        }

        int index = arrayStart + 1;
        while (index < arrayEnd)
        {
            int objectStart = commandJson.IndexOf('{', index);
            if (objectStart < 0 || objectStart >= arrayEnd)
            {
                break;
            }

            string child = ExtractObjectAt(commandJson, objectStart);
            if (string.IsNullOrEmpty(child))
            {
                break;
            }

            children.Add(child);
            index = objectStart + child.Length;
        }

        return children;
    }

    private static string ExtractObjectAt(string text, int objectStart)
    {
        int objectEnd = FindMatching(text, objectStart, '{', '}');
        return objectEnd >= objectStart ? text.Substring(objectStart, objectEnd - objectStart + 1) : null;
    }

    private static int FindMatching(string text, int start, char open, char close)
    {
        if (start < 0 || start >= text.Length || text[start] != open)
        {
            return -1;
        }

        bool inString = false;
        bool escaped = false;
        int depth = 0;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static string ReadFirst(Regex regex, string text)
    {
        Match match = regex.Match(text);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static float ReadFloat(Regex regex, string text, float fallback)
    {
        Match match = regex.Match(text);
        return match.Success ? ParseFloat(match.Groups[1].Value, fallback) : fallback;
    }

    private static float ParseFloat(string value, float fallback)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;
    }

    private readonly struct AutoCommandWindow
    {
        public readonly string name;
        public readonly float startSeconds;
        public readonly float endSeconds;

        public AutoCommandWindow(string name, float startSeconds, float endSeconds)
        {
            this.name = name;
            this.startSeconds = startSeconds;
            this.endSeconds = endSeconds;
        }
    }
}
