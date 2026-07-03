using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class RobotCodeUdpReceiver : MonoBehaviour
{
    [SerializeField] private int port = 5808;
    [SerializeField] private bool listenOnStart = true;

    private readonly object frameLock = new object();
    private UdpClient udpClient;
    private Thread receiveThread;
    private RobotCodeFrame latestFrame;
    private string latestJson;
    private bool hasUnparsedFrame;
    private bool running;
    private double lastReceiveRealtime;
    private readonly Stopwatch stopwatch = new Stopwatch();

    public bool HasFrame
    {
        get
        {
            lock (frameLock)
            {
                return latestFrame != null;
            }
        }
    }

    public double SecondsSinceLastFrame => stopwatch.Elapsed.TotalSeconds - lastReceiveRealtime;

    private void Start()
    {
        if (listenOnStart)
        {
            StartListening();
        }
    }

    private void OnDisable()
    {
        StopListening();
    }

    public void StartListening()
    {
        if (running)
        {
            return;
        }

        running = true;
        stopwatch.Restart();
        udpClient = new UdpClient(port);
        receiveThread = new Thread(ReceiveLoop)
        {
            IsBackground = true,
            Name = "Robot Code UDP Receiver"
        };
        receiveThread.Start();
    }

    public void StopListening()
    {
        running = false;
        stopwatch.Stop();
        udpClient?.Close();
        udpClient = null;

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(100);
        }

        receiveThread = null;
    }

    public bool TryGetLatestFrame(out RobotCodeFrame frame)
    {
        lock (frameLock)
        {
            if (hasUnparsedFrame)
            {
                try
                {
                    latestFrame = JsonUtility.FromJson<RobotCodeFrame>(latestJson);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to parse robot code frame: {ex.Message}");
                }

                hasUnparsedFrame = false;
            }

            frame = latestFrame;
            return frame != null;
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);

        while (running)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEndpoint);

                lock (frameLock)
                {
                    latestJson = Encoding.UTF8.GetString(data);
                    hasUnparsedFrame = true;
                    lastReceiveRealtime = stopwatch.Elapsed.TotalSeconds;
                }
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
