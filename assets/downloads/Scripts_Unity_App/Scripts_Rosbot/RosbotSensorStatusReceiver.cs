using System;
using System.Collections.Generic;
using System.Threading;
using AsyncIO;
using NetMQ;
using NetMQ.Sockets;
using UnityEngine;

[Serializable]
public class RosbotStatusPayload
{
    public float ts;
    public float uptime_s;

    public bool camera_ok;
    public bool lidar_ok;
    public bool cmd_link_ok;

    public string active_camera_mode;
    public string active_lidar_mode;
    public string camera_mode;
    public string lidar_mode;
    public string video_mode;
}

public class RosbotSensorStatusReceiver : MonoBehaviour
{
    [Header("Network")]
    [SerializeField] private int sensorPort = 5001;
    [SerializeField] private string fallbackIp = "192.168.100.20";
    [SerializeField] private string statusTopic = "stat";
    [SerializeField] private string modeAckTopic = "mode_ack";

    [Header("Connection")]
    [SerializeField] private float disconnectTimeout = 2.0f;

    private Thread recvThread;
    private volatile bool running;
    private float lastSensorRealtime = -999f;
    private volatile bool gotPacket;

    public bool IsConnected => (Time.unscaledTime - lastSensorRealtime) <= disconnectTimeout;
    public bool CameraOk { get; private set; }
    public bool LidarOk { get; private set; }
    public bool CmdLinkOk { get; private set; }
    public string CurrentCameraMode { get; private set; } = "normal";
    public string CurrentLidarMode { get; private set; } = "detail";
    public float UptimeSeconds { get; private set; }
    public string LastTopic { get; private set; } = "--";

    private void Start()
    {
        AsyncIO.ForceDotNet.Force();
        StartReceiver();
        Debug.Log($"[RosbotSensorStatusReceiver] SUB tcp://{ResolveIp()}:{sensorPort} topics={statusTopic},{modeAckTopic}");
    }

    public void Reconnect()
    {
        StopReceiver();
        lastSensorRealtime = -999f;
        StartReceiver();
    }

    private void Update()
    {
        if (!gotPacket)
            return;

        gotPacket = false;
        lastSensorRealtime = Time.unscaledTime;
    }

    private void OnDestroy()
    {
        StopReceiver();
    }

    private void OnApplicationQuit()
    {
        StopReceiver();
    }

    private string ResolveIp()
    {
        if (RosbotEndpointManager.Instance != null)
            return RosbotEndpointManager.Instance.GetIp();

        return fallbackIp;
    }

    private void StartReceiver()
    {
        running = true;
        recvThread = new Thread(ReceiveLoop) { IsBackground = true };
        recvThread.Start();
    }

    private void StopReceiver()
    {
        running = false;

        if (recvThread != null && recvThread.IsAlive)
            recvThread.Join(500);

        recvThread = null;
    }

    private void ReceiveLoop()
    {
        try
        {
            using (var sub = new SubscriberSocket())
            {
                sub.Options.ReceiveHighWatermark = 20;
                sub.Connect($"tcp://{ResolveIp()}:{sensorPort}");
                sub.Subscribe(statusTopic);
                sub.Subscribe(modeAckTopic);

                List<byte[]> msg = null;

                while (running)
                {
                    if (!sub.TryReceiveMultipartBytes(TimeSpan.FromMilliseconds(100), ref msg))
                        continue;

                    if (msg == null || msg.Count < 2)
                        continue;

                    string topic = System.Text.Encoding.UTF8.GetString(msg[0]);
                    string json = System.Text.Encoding.UTF8.GetString(msg[1]);
                    LastTopic = topic;

                    try
                    {
                        RosbotStatusPayload payload = JsonUtility.FromJson<RosbotStatusPayload>(json);
                        if (payload == null)
                            continue;

                        CameraOk = payload.camera_ok;
                        LidarOk = payload.lidar_ok;
                        CmdLinkOk = payload.cmd_link_ok;
                        UptimeSeconds = payload.uptime_s;

                        string cameraMode = FirstNonEmpty(payload.active_camera_mode, payload.camera_mode, payload.video_mode);
                        if (!string.IsNullOrWhiteSpace(cameraMode))
                            CurrentCameraMode = NormalizeCameraMode(cameraMode);

                        string lidarMode = FirstNonEmpty(payload.active_lidar_mode, payload.lidar_mode);
                        if (!string.IsNullOrWhiteSpace(lidarMode))
                            CurrentLidarMode = NormalizeLidarMode(lidarMode);

                        gotPacket = true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[RosbotSensorStatusReceiver] Parse error: " + e.Message);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[RosbotSensorStatusReceiver] ReceiveLoop error: " + e);
        }
    }

    private string FirstNonEmpty(params string[] values)
    {
        if (values == null)
            return null;

        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
                return values[i];
        }

        return null;
    }

    private string NormalizeCameraMode(string mode)
    {
        string m = mode.Trim().ToLowerInvariant();
        return m == "rgb" ? "normal" : m;
    }

    private string NormalizeLidarMode(string mode)
    {
        return mode.Trim().ToLowerInvariant();
    }
}
