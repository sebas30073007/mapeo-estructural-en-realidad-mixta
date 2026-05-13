using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.UI;

public class RosbotVideoUdpReceiver : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RawImage targetImage;
    
    [Header("Network")]
    [SerializeField] private int videoPort = 5555;
    [SerializeField] private int commandPort = 5556; // UDP para comandos
    [SerializeField] private string fallbackIp = "192.168.100.20";
    
    [Header("Startup")]
    [SerializeField] private string startupCameraMode = "normal";
    [SerializeField] private bool requestModeOnStart = true;
    
    [Header("Connection")]
    [SerializeField] private float disconnectTimeout = 2.0f;
    
    [Header("UDP Settings")]
    [SerializeField] private int maxPacketSize = 65507; // Tamaño máximo UDP
    [SerializeField] private int receiveBufferSize = 512000; // 500KB buffer
    
    private Texture2D videoTexture;
    private UdpClient videoClient;
    private UdpClient commandClient;
    private Thread recvThread;
    private volatile bool running;
    
    private readonly ConcurrentQueue<byte[]> frameQueue = new ConcurrentQueue<byte[]>();
    
    private float lastFrameRealtime = -999f;
    private float fpsWindowStart;
    private int fpsFrames;
    private string currentCameraMode = "normal";
    
    // Propiedades públicas
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
            Debug.LogError("[RosbotVideoUdpReceiver] Missing targetImage.");
            enabled = false;
            return;
        }
        
        fpsWindowStart = Time.unscaledTime;
        videoTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        targetImage.texture = videoTexture;
        
        currentCameraMode = SanitizeCameraMode(startupCameraMode);
        
        StartReceiver();
        
        if (requestModeOnStart)
            SendCameraModeCommand(currentCameraMode);
        
        Debug.Log($"[RosbotVideoUdpReceiver] UDP listening on port {videoPort}");
        Debug.Log($"[RosbotVideoUdpReceiver] Sending commands to {ResolveIp()}:{commandPort}");
    }
    
    public void Reconnect()
    {
        StopReceiver();
        ClearPendingFrames();
        
        lastFrameRealtime = -999f;
        CurrentFps = 0f;
        CurrentWidth = 0;
        CurrentHeight = 0;
        fpsFrames = 0;
        fpsWindowStart = Time.unscaledTime;
        
        StartReceiver();
        
        if (requestModeOnStart)
            SendCameraModeCommand(currentCameraMode);
        
        Debug.Log($"[RosbotVideoUdpReceiver] Reconnected");
    }
    
    // Métodos de control de cámara
    public void SetCameraNormal() => RequestCameraMode("normal");
    public void SetCameraPose() => RequestCameraMode("pose");
    public void SetCameraSegment() => RequestCameraMode("segment");
    public void SetCameraOff() => RequestCameraMode("off");
    
    public void RequestCameraMode(string mode)
    {
        currentCameraMode = SanitizeCameraMode(mode);
        SendCameraModeCommand(currentCameraMode);
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
        // Procesar frames pendientes
        byte[] latestFrame = null;
        while (frameQueue.TryDequeue(out byte[] frame))
            latestFrame = frame;
        
        if (latestFrame == null || latestFrame.Length == 0)
            return;
        
        // Decodificar JPEG
        bool ok = videoTexture.LoadImage(latestFrame);
        if (!ok)
        {
            Debug.LogWarning("[RosbotVideoUdpReceiver] Could not decode JPG frame.");
            return;
        }
        
        targetImage.texture = videoTexture;
        lastFrameRealtime = Time.unscaledTime;
        
        CurrentWidth = videoTexture.width;
        CurrentHeight = videoTexture.height;
        
        // Calcular FPS
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
        try
        {
            // Cliente UDP para recibir video
            videoClient = new UdpClient(videoPort);
            videoClient.Client.ReceiveBufferSize = receiveBufferSize;
            videoClient.Client.ReceiveTimeout = 500; // ms
            
            // Cliente UDP para enviar comandos
            commandClient = new UdpClient();
            
            running = true;
            recvThread = new Thread(ReceiveLoop) { IsBackground = true };
            recvThread.Start();
            
            Debug.Log($"[RosbotVideoUdpReceiver] UDP receiver started on port {videoPort}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[RosbotVideoUdpReceiver] Failed to start: {e.Message}");
        }
    }
    
    private void StopReceiver()
    {
        running = false;
        
        try
        {
            videoClient?.Close();
            commandClient?.Close();
        }
        catch { }
        
        if (recvThread != null && recvThread.IsAlive)
            recvThread.Join(500);
        
        videoClient = null;
        commandClient = null;
        recvThread = null;
    }
    
    private void ReceiveLoop()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, videoPort);
        
        while (running)
        {
            try
            {
                byte[] data = videoClient.Receive(ref remoteEP);
                
                if (data == null || data.Length == 0)
                    continue;
                
                // Mantener solo el frame más reciente
                while (frameQueue.Count > 1)
                    frameQueue.TryDequeue(out _);
                
                frameQueue.Enqueue(data);
            }
            catch (SocketException)
            {
                // Timeout normal, continuar
            }
            catch (Exception e)
            {
                if (running)
                    Debug.LogWarning($"[RosbotVideoUdpReceiver] Receive error: {e.Message}");
            }
        }
    }
    
    private void SendCameraModeCommand(string mode)
    {
        try
        {
            string json = $"{{\"type\":\"set_camera_mode\",\"mode\":\"{mode}\"}}";
            byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
            
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(ResolveIp()), commandPort);
            
            // Enviar comando 2 veces para redundancia
            for (int i = 0; i < 2; i++)
            {
                commandClient.Send(data, data.Length, endpoint);
                Thread.Sleep(30);
            }
            
            Debug.Log($"[RosbotVideoUdpReceiver] Sent camera mode: {mode}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[RosbotVideoUdpReceiver] Failed to send command: {e.Message}");
        }
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
        if (m == "rgb") return "normal";
        if (m == "normal" || m == "pose" || m == "segment" || m == "off")
            return m;
        
        return "normal";
    }
}