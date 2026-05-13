using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class UdpLidarReceiver : MonoBehaviour
{
    [Header("Red")]
    public int listenPort = 5003;

    [Header("Visualización")]
    public RawImage lidarRawImage;
    public int textureSize = 512;
    public float maxRangeM = 8f;
    public Color backgroundColor = Color.black;
    public Color pointColor = Color.green;
    public Color robotColor = Color.red;
    public int pointRadius = 2;

    // UDP
    UdpClient udpClient;
    Thread receiveThread;
    volatile bool running = false;

    // Datos del último frame
    float[] latestRanges;
    float angleMin;
    float angleIncrement;
    bool newDataAvailable = false;
    readonly object dataLock = new object();

    // Textura
    Texture2D lidarTex;
    Color[] pixels;

    private float lastPacketTime = -999f;
    private const float disconnectTimeout = 3f;
    public bool IsConnected => (Time.unscaledTime - lastPacketTime) <= disconnectTimeout;


    void Start()
    {
        // Crear textura
        lidarTex = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        lidarTex.filterMode = FilterMode.Bilinear;
        pixels = new Color[textureSize * textureSize];
        ClearTexture();

        if (lidarRawImage != null)
            lidarRawImage.texture = lidarTex;

        // Iniciar hilo UDP
        udpClient = new UdpClient(listenPort);
        running = true;
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();

        Debug.Log($"📡 LiDAR UDP escuchando en puerto {listenPort}");
    }

    void ReceiveLoop()
    {
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, listenPort);
        while (running)
        {
            try
            {
                byte[] data = udpClient.Receive(ref endpoint);
                string json = Encoding.UTF8.GetString(data);
                ParseLidarJson(json);
            }
            catch (SocketException) { break; }
            catch (Exception e)
            {
                if (running) Debug.LogWarning($"LiDAR UDP error: {e.Message}");
            }
        }
    }

    void ParseLidarJson(string json)
    {
        try
        {
            LidarFrame frame = JsonUtility.FromJson<LidarFrame>(json);
            lock (dataLock)
            {
                latestRanges    = frame.ranges;
                angleMin        = frame.angle_min;
                angleIncrement  = frame.angle_increment;
                newDataAvailable = true;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Error parseando LiDAR JSON: {e.Message}");
        }
    }

    void Update()
    {
        bool hasNew;
        float[] ranges;
        float aMin, aInc;

        lock (dataLock)
        {
            hasNew = newDataAvailable;
            ranges = latestRanges;
            aMin   = angleMin;
            aInc   = angleIncrement;
            newDataAvailable = false;
        }

        if (!hasNew || ranges == null) return;

        DrawLidar(ranges, aMin, aInc);
        lastPacketTime = Time.unscaledTime;
    }

    void DrawLidar(float[] ranges, float aMin, float aInc)
    {
        // Limpiar fondo
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = backgroundColor;

        int cx = textureSize / 2;
        int cy = textureSize / 2;
        float scale = (textureSize / 2f) / maxRangeM;

        // Dibujar puntos
        for (int i = 0; i < ranges.Length; i++)
        {
            float r = ranges[i];
            if (float.IsNaN(r) || float.IsInfinity(r) || r <= 0.05f || r > maxRangeM)
                continue;

            float angle = aMin + i * aInc;

            // ROS: X adelante, Y izquierda → textura: X derecha, Y arriba
            int px = cx + Mathf.RoundToInt( r * Mathf.Sin(angle) * scale);
            int py = cy + Mathf.RoundToInt( r * Mathf.Cos(angle) * scale);

            DrawCircle(px, py, pointRadius, pointColor);
        }

        // Dibujar robot en el centro
        DrawCircle(cx, cy, 4, robotColor);

        lidarTex.SetPixels(pixels);
        lidarTex.Apply();
    }

    void DrawCircle(int cx, int cy, int radius, Color color)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int px = cx + dx;
            int py = cy + dy;
            if (px < 0 || px >= textureSize || py < 0 || py >= textureSize) continue;
            pixels[py * textureSize + px] = color;
        }
    }

    void ClearTexture()
    {
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = backgroundColor;
        lidarTex.SetPixels(pixels);
        lidarTex.Apply();
    }

    void OnDestroy()
    {
        running = false;
        udpClient?.Close();
        receiveThread?.Join(500);
    }

    // Clases para deserializar JSON
    [Serializable]
    class LidarFrame
    {
        public float angle_min;
        public float angle_increment;
        public float[] ranges;
    }
}