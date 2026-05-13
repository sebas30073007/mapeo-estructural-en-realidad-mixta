using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using AsyncIO;
using NetMQ;
using NetMQ.Sockets;
using UnityEngine;
using UnityEngine.UI;

public class RosbotVideoStreamReceiver : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RawImage targetImage;

    [Header("Network")]
    [SerializeField] private int videoPort = 5555;
    [SerializeField] private int commandPort = 5002;
    [SerializeField] private string fallbackIp = "192.168.100.20";
    [SerializeField] private string videoTopic = "video_rgb";
    [SerializeField] private string commandTopic = "cmd";

    [Header("Startup")]
    [SerializeField] private string startupCameraMode = "normal";
    [SerializeField] private bool requestModeOnStart = true;

    [Header("Connection")]
    [SerializeField] private float disconnectTimeout = 1.0f;

    private Texture2D videoTexture;
    private Thread recvThread;
    private Thread cmdThread;
    private volatile bool running;

    private readonly ConcurrentQueue<byte[]> frameQueue = new ConcurrentQueue<byte[]>();
    private readonly ConcurrentQueue<string> commandQueue = new ConcurrentQueue<string>();

    private float lastFrameRealtime = -999f;
    private float fpsWindowStart;
    private int fpsFrames;
    private string currentCameraMode = "normal";

    public string CurrentTopic => videoTopic;
    public string ServerIp => ResolveIp();
    public float CurrentFps { get; private set; }
    public int CurrentWidth { get; private set; }
    public int CurrentHeight { get; private set; }
    public bool IsConnected => (Time.unscaledTime - lastFrameRealtime) <= disconnectTimeout;
    public string CurrentCameraMode => currentCameraMode;

    private void Start()
    {
        if (targetImage == null)
        {
            Debug.LogError("[RosbotVideoStreamReceiver] Missing targetImage.");
            enabled = false;
            return;
        }

        fpsWindowStart = Time.unscaledTime;
        videoTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        targetImage.texture = videoTexture;

        currentCameraMode = SanitizeCameraMode(startupCameraMode);

        AsyncIO.ForceDotNet.Force();
        StartThreads();

        if (requestModeOnStart)
            EnqueueCameraModeCommand(currentCameraMode);

        Debug.Log($"[RosbotVideoStreamReceiver] SUB tcp://{ResolveIp()}:{videoPort} topic={videoTopic}");
        Debug.Log($"[RosbotVideoStreamReceiver] PUB tcp://{ResolveIp()}:{commandPort} camera_mode={currentCameraMode}");
    }

    public void Reconnect()
    {
        StopThreads();
        ClearPendingFrames();

        lastFrameRealtime = -999f;
        CurrentFps = 0f;
        CurrentWidth = 0;
        CurrentHeight = 0;
        fpsFrames = 0;
        fpsWindowStart = Time.unscaledTime;

        StartThreads();

        if (requestModeOnStart)
            EnqueueCameraModeCommand(currentCameraMode);

        Debug.Log($"[RosbotVideoStreamReceiver] Reconnected to tcp://{ResolveIp()}:{videoPort}");
    }

    public void SetCameraNormal() => RequestCameraMode("normal");
    public void SetCameraPose() => RequestCameraMode("pose");
    public void SetCameraSegment() => RequestCameraMode("segment");
    public void SetCameraOff() => RequestCameraMode("off");

    public void RequestCameraMode(string mode)
    {
        currentCameraMode = SanitizeCameraMode(mode);
        EnqueueCameraModeCommand(currentCameraMode);
    }

    public void RequestCameraModeFromDropdownIndex(int index)
    {
        switch (index)
        {
            case 0: RequestCameraMode("normal"); break;
            case 1: RequestCameraMode("pose"); break;
            case 2: RequestCameraMode("segment"); break;
            case 3: RequestCameraMode("off"); break;
            default: RequestCameraMode("normal"); break;
        }
    }

    private void Update()
    {
        byte[] latestFrame = null;
        while (frameQueue.TryDequeue(out byte[] frame))
            latestFrame = frame;

        if (latestFrame == null || latestFrame.Length == 0)
            return;

        bool ok = videoTexture.LoadImage(latestFrame);
        if (!ok)
        {
            Debug.LogWarning("[RosbotVideoStreamReceiver] Could not decode JPG frame.");
            return;
        }

        targetImage.texture = videoTexture;
        lastFrameRealtime = Time.unscaledTime;
        CurrentWidth = videoTexture.width;
        CurrentHeight = videoTexture.height;

        fpsFrames++;
        float elapsed = Time.unscaledTime - fpsWindowStart;
        if (elapsed >= 1f)
        {
            CurrentFps = fpsFrames / elapsed;
            fpsFrames = 0;
            fpsWindowStart = Time.unscaledTime;
        }
    }

    private void OnDestroy()
    {
        StopThreads();
    }

    private void OnApplicationQuit()
    {
        StopThreads();
    }

    private string ResolveIp()
    {
        if (RosbotEndpointManager.Instance != null)
            return RosbotEndpointManager.Instance.GetIp();

        return fallbackIp;
    }

    private void StartThreads()
    {
        running = true;

        recvThread = new Thread(ReceiveLoop) { IsBackground = true };
        recvThread.Start();

        cmdThread = new Thread(CommandLoop) { IsBackground = true };
        cmdThread.Start();
    }

    private void StopThreads()
    {
        running = false;

        if (recvThread != null && recvThread.IsAlive)
            recvThread.Join(500);

        if (cmdThread != null && cmdThread.IsAlive)
            cmdThread.Join(500);

        recvThread = null;
        cmdThread = null;
    }

    private void ReceiveLoop()
    {
        try
        {
            using (var sub = new SubscriberSocket())
            {
                sub.Options.ReceiveHighWatermark = 1;
                sub.Connect($"tcp://{ResolveIp()}:{videoPort}");
                sub.Subscribe(videoTopic);

                List<byte[]> msg = null;

                while (running)
                {
                    if (!sub.TryReceiveMultipartBytes(TimeSpan.FromMilliseconds(100), ref msg))
                        continue;

                    if (msg == null || msg.Count < 2)
                        continue;

                    while (frameQueue.Count > 1)
                        frameQueue.TryDequeue(out _);

                    frameQueue.Enqueue(msg[1]);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[RosbotVideoStreamReceiver] ReceiveLoop error: " + e);
        }
    }

    private void CommandLoop()
    {
        try
        {
            using (var pub = new PublisherSocket())
            {
                pub.Options.SendHighWatermark = 10;
                pub.Connect($"tcp://{ResolveIp()}:{commandPort}");

                Thread.Sleep(300);

                while (running)
                {
                    if (!commandQueue.TryDequeue(out string payload))
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    for (int i = 0; i < 2; i++)
                    {
                        pub.SendMoreFrame(commandTopic).SendFrame(payload);
                        Thread.Sleep(30);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[RosbotVideoStreamReceiver] CommandLoop error: " + e);
        }
    }

    private void EnqueueCameraModeCommand(string mode)
    {
        string payload = "{\"type\":\"set_camera_mode\",\"mode\":\"" + mode + "\"}";
        commandQueue.Enqueue(payload);
    }

    private void ClearPendingFrames()
    {
        while (frameQueue.TryDequeue(out _)) { }
    }

    private string SanitizeCameraMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return "normal";

        string m = mode.Trim().ToLowerInvariant();
        if (m == "rgb")
            return "normal";

        if (m == "normal" || m == "pose" || m == "segment" || m == "off")
            return m;

        return "normal";
    }
}
