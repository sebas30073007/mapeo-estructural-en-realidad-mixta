using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using AsyncIO;
using NetMQ;
using NetMQ.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class RosbotLidarGridPayload
{
    public float ts;
    public string mode;
    public int grid_size;
    public float cell_size_m;
    public float radius_m;
    public int hits;
    public int[] occupancy;
}

public class RosbotLidarGridReceiver : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RawImage targetImage;
    [SerializeField] private TMP_Text modeLabel;

    [Header("Network")]
    [SerializeField] private int sensorPort = 5001;
    [SerializeField] private int commandPort = 5002;
    [SerializeField] private string fallbackIp = "192.168.100.20";
    [SerializeField] private string lidarTopic = "lidar_grid";
    [SerializeField] private string commandTopic = "cmd";

    [Header("Startup")]
    [SerializeField] private string startupMode = "detail";
    [SerializeField] private bool requestModeOnStart = true;

    [Header("Connection")]
    [SerializeField] private float disconnectTimeout = 2.0f;

    [Header("Visual")]
    [SerializeField] private Color32 emptyColor = new Color32(245, 245, 245, 255);
    [SerializeField] private Color32 occupiedColor = new Color32(10, 10, 10, 255);
    [SerializeField] private Color32 gridLineColor = new Color32(210, 210, 210, 255);
    [SerializeField] private Color32 robotColor = new Color32(64, 220, 120, 255);
    [SerializeField] private Color32 forwardColor = new Color32(80, 160, 255, 255);
    [SerializeField, Range(0, 10)] private int robotMarkerRadius = 0;
    [SerializeField] private bool drawGridLines = false;
    [SerializeField, Min(1)] private int pointSize = 3;
    [SerializeField] private bool roundPoints = true;
    [SerializeField, Range(0f, 1f)] private float pointAlpha = 1f;
    [SerializeField, Min(1)] private int detailPointSize = 3;
    [SerializeField, Min(1)] private int mediumPointSize = 5;
    [SerializeField, Min(1)] private int panoramaPointSize = 7;
    [SerializeField] private bool autoPointSizeByMode = true;

    [Header("Texture")]
    [SerializeField] private bool useBilinearFilter = false;

    private Thread recvThread;
    private Thread cmdThread;
    private volatile bool running;

    private readonly ConcurrentQueue<RosbotLidarGridPayload> gridQueue = new ConcurrentQueue<RosbotLidarGridPayload>();
    private readonly ConcurrentQueue<string> commandQueue = new ConcurrentQueue<string>();

    private Texture2D gridTexture;
    private Color32[] pixelBuffer;
    private float lastGridRealtime = -999f;
    private string requestedMode = "detail";

    public bool IsConnected => (Time.unscaledTime - lastGridRealtime) <= disconnectTimeout;
    public string CurrentMode { get; private set; } = "detail";
    public int CurrentGridSize { get; private set; }
    public float CurrentCellSizeM { get; private set; }
    public float CurrentRadiusM { get; private set; }
    public int CurrentHits { get; private set; }
    public string ServerIp => ResolveIp();

    private void Start()
    {
        if (targetImage == null)
        {
            Debug.LogError("[RosbotLidarGridReceiver] Missing targetImage.");
            enabled = false;
            return;
        }

        requestedMode = SanitizeMode(startupMode);
        CurrentMode = requestedMode;

        AsyncIO.ForceDotNet.Force();
        CreateTexture(40);
        StartThreads();

        if (requestModeOnStart)
        {
            RequestMode(requestedMode);
            RequestMode(requestedMode);
        }

        UpdateModeLabel();
        Debug.Log($"[RosbotLidarGridReceiver] SUB tcp://{ResolveIp()}:{sensorPort} topic={lidarTopic}");
    }

    public void Reconnect()
    {
        StopThreads();
        lastGridRealtime = -999f;

        while (gridQueue.TryDequeue(out _)) { }
        while (commandQueue.TryDequeue(out _)) { }

        StartThreads();

        if (requestModeOnStart)
            RequestMode(requestedMode);
    }

    public void SetDetail() => RequestMode("detail");
    public void SetMedium() => RequestMode("medium");
    public void SetPanorama() => RequestMode("panorama");
    public void SetOff() => RequestMode("off");

    public void RequestMode(string mode)
    {
        string clean = SanitizeMode(mode);
        requestedMode = clean;
        CurrentMode = clean;
        UpdateModeLabel();

        string payload = "{\"type\":\"set_lidar_mode\",\"mode\":\"" + clean + "\"}";
        commandQueue.Enqueue(payload);
    }

    private void Update()
    {
        RosbotLidarGridPayload latest = null;
        while (gridQueue.TryDequeue(out RosbotLidarGridPayload grid))
            latest = grid;

        if (latest == null)
            return;

        RenderGrid(latest);
        lastGridRealtime = Time.unscaledTime;
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
                sub.Options.ReceiveHighWatermark = 10;
                sub.Connect($"tcp://{ResolveIp()}:{sensorPort}");
                sub.Subscribe(lidarTopic);

                List<byte[]> msg = null;

                while (running)
                {
                    if (!sub.TryReceiveMultipartBytes(TimeSpan.FromMilliseconds(100), ref msg))
                        continue;

                    if (msg == null || msg.Count < 2)
                        continue;

                    string topic = System.Text.Encoding.UTF8.GetString(msg[0]);
                    if (topic != lidarTopic)
                        continue;

                    string json = System.Text.Encoding.UTF8.GetString(msg[1]);

                    try
                    {
                        var payload = JsonUtility.FromJson<RosbotLidarGridPayload>(json);
                        if (payload == null || payload.occupancy == null)
                            continue;

                        while (gridQueue.Count > 1)
                            gridQueue.TryDequeue(out _);

                        gridQueue.Enqueue(payload);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[RosbotLidarGridReceiver] Parse error: " + e.Message);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[RosbotLidarGridReceiver] ReceiveLoop error: " + e);
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
            Debug.LogError("[RosbotLidarGridReceiver] CommandLoop error: " + e);
        }
    }

    private void CreateTexture(int gridSize)
    {
        CurrentGridSize = gridSize;
        gridTexture = new Texture2D(gridSize, gridSize, TextureFormat.RGBA32, false)
        {
            filterMode = useBilinearFilter ? FilterMode.Bilinear : FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        pixelBuffer = new Color32[gridSize * gridSize];
        ClearTexture();
        targetImage.texture = gridTexture;
    }

    private void ClearTexture()
    {
        if (pixelBuffer == null || gridTexture == null)
            return;

        for (int i = 0; i < pixelBuffer.Length; i++)
            pixelBuffer[i] = emptyColor;

        gridTexture.SetPixels32(pixelBuffer);
        gridTexture.Apply(false);
    }

    private void RenderGrid(RosbotLidarGridPayload grid)
    {
        if (grid.grid_size <= 0 || grid.occupancy == null)
            return;

        if (gridTexture == null || gridTexture.width != grid.grid_size || gridTexture.height != grid.grid_size)
            CreateTexture(grid.grid_size);

        CurrentMode = string.IsNullOrWhiteSpace(grid.mode) ? requestedMode : SanitizeMode(grid.mode);
        CurrentCellSizeM = grid.cell_size_m;
        CurrentRadiusM = grid.radius_m;
        CurrentHits = grid.hits;

        int size = grid.grid_size;

        for (int i = 0; i < pixelBuffer.Length; i++)
            pixelBuffer[i] = emptyColor;

        if (drawGridLines)
            DrawGridLines(size);

        int maxCount = Mathf.Min(grid.occupancy.Length, size * size);
        Color32 lidarPointColor = occupiedColor;
        lidarPointColor.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(pointAlpha) * 255f);
        int effectivePointSize = GetPointSizeForCurrentMode();

        for (int idx = 0; idx < maxCount; idx++)
        {
            if (grid.occupancy[idx] == 0)
                continue;

            int row = idx / size;
            int col = idx % size;
            int texY = (size - 1) - row;
            PaintPoint(col, texY, effectivePointSize, lidarPointColor, roundPoints);
        }

        PaintRobotMarkerCentered(size);

        gridTexture.SetPixels32(pixelBuffer);
        gridTexture.Apply(false);
        targetImage.texture = gridTexture;
        UpdateModeLabel();
    }

    private void DrawGridLines(int size)
    {
        int step = 10;
        if (size >= 400) step = 20;
        if (size >= 600) step = 40;

        for (int i = 0; i < size; i += step)
        {
            for (int x = 0; x < size; x++)
                pixelBuffer[i * size + x] = gridLineColor;

            for (int y = 0; y < size; y++)
                pixelBuffer[y * size + i] = gridLineColor;
        }

        for (int x = 0; x < size; x++)
        {
            pixelBuffer[x] = gridLineColor;
            pixelBuffer[(size - 1) * size + x] = gridLineColor;
        }

        for (int y = 0; y < size; y++)
        {
            pixelBuffer[y * size] = gridLineColor;
            pixelBuffer[y * size + (size - 1)] = gridLineColor;
        }
    }

    private int GetPointSizeForCurrentMode()
    {
        if (!autoPointSizeByMode)
            return Mathf.Max(1, pointSize);

        switch (CurrentMode)
        {
            case "detail": return Mathf.Max(1, detailPointSize);
            case "medium": return Mathf.Max(1, mediumPointSize);
            case "panorama": return Mathf.Max(1, panoramaPointSize);
            default: return Mathf.Max(1, pointSize);
        }
    }

    private void PaintRobotMarkerCentered(int size)
    {
        int x0 = (size - 1) / 2;
        int x1 = size / 2;
        int y0 = (size - 1) / 2;
        int y1 = size / 2;

        PaintDisc(x0, y0, robotMarkerRadius, robotColor);
        PaintDisc(x1, y0, robotMarkerRadius, robotColor);
        PaintDisc(x0, y1, robotMarkerRadius, robotColor);
        PaintDisc(x1, y1, robotMarkerRadius, robotColor);

        PaintDisc(x0, y1 + 1, 0, forwardColor);
        PaintDisc(x1, y1 + 1, 0, forwardColor);
        PaintDisc(x0, y1 + 2, 0, forwardColor);
        PaintDisc(x1, y1 + 2, 0, forwardColor);
    }

    private void PaintPoint(int cx, int cy, int sizePx, Color32 color, bool round)
    {
        int texSize = gridTexture.width;
        int r = Mathf.Max(0, sizePx - 1);

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (round && (dx * dx + dy * dy > r * r))
                    continue;

                int x = cx + dx;
                int y = cy + dy;
                if (x < 0 || x >= texSize || y < 0 || y >= texSize)
                    continue;

                pixelBuffer[y * texSize + x] = color;
            }
        }
    }

    private void PaintDisc(int cx, int cy, int radius, Color32 color)
    {
        int size = gridTexture.width;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                int x = cx + dx;
                int y = cy + dy;
                if (x < 0 || x >= size || y < 0 || y >= size)
                    continue;

                pixelBuffer[y * size + x] = color;
            }
        }
    }

    private void UpdateModeLabel()
    {
        if (modeLabel == null)
            return;

        string pretty = PrettyMode(CurrentMode);

        if (CurrentRadiusM > 0f && CurrentCellSizeM > 0f)
            modeLabel.text = $"{pretty} | {CurrentCellSizeM:0.000} m | R={CurrentRadiusM:0.0} m | Grid {CurrentGridSize}x{CurrentGridSize}";
        else
            modeLabel.text = pretty;
    }

    private string SanitizeMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return "detail";

        string m = mode.Trim().ToLowerInvariant();
        if (m == "detail" || m == "medium" || m == "panorama" || m == "off")
            return m;

        return "detail";
    }

    private string PrettyMode(string mode)
    {
        switch (SanitizeMode(mode))
        {
            case "detail": return "Detail";
            case "medium": return "Medium";
            case "panorama": return "Panorama";
            case "off": return "Off";
            default: return mode;
        }
    }
}
