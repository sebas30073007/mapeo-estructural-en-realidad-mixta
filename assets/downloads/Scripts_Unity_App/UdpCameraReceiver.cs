using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class UdpCameraReceiver : MonoBehaviour
{
    [Header("Red")]
    public int listenPort = 5004;

    [Header("Visualización")]
    public RawImage cameraRawImage;

    // UDP
    UdpClient udpClient;
    Thread receiveThread;
    volatile bool running = false;

    // Buffer de imagen
    byte[] latestJpegBytes = null;
    bool newFrameAvailable = false;
    readonly object frameLock = new object();

    // Textura
    Texture2D cameraTex;

    void Start()
    {
        cameraTex = new Texture2D(320, 240, TextureFormat.RGB24, false);

        if (cameraRawImage != null)
            cameraRawImage.texture = cameraTex;

        udpClient = new UdpClient(listenPort);
        // Buffer grande para recibir JPEGs
        udpClient.Client.ReceiveBufferSize = 65535;

        running = true;
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();

        Debug.Log($"📷 Cámara UDP escuchando en puerto {listenPort}");
    }

    void ReceiveLoop()
    {
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, listenPort);
        while (running)
        {
            try
            {
                byte[] data = udpClient.Receive(ref endpoint);
                Debug.Log($"📷 Frame recibido: {data.Length} bytes"); // ← agrega esto
                lock (frameLock)
                {
                    latestJpegBytes  = data;
                    newFrameAvailable = true;
                }
            }
            catch (SocketException) { break; }
            catch (Exception e)
            {
                if (running) Debug.LogWarning($"Cámara UDP error: {e.Message}");
            }
        }
    }

    void Update()
    {
        byte[] jpegBytes;
        bool hasNew;

        lock (frameLock)
        {
            hasNew    = newFrameAvailable;
            jpegBytes = latestJpegBytes;
            newFrameAvailable = false;
        }

        if (!hasNew || jpegBytes == null) return;

        // LoadImage detecta JPEG automáticamente y redimensiona la textura
        if (cameraTex.LoadImage(jpegBytes))
        {
            if (cameraRawImage != null)
                cameraRawImage.texture = cameraTex;
        }
        else
        {
            Debug.LogWarning("Error cargando frame JPEG");
        }
    }

    void OnDestroy()
    {
        running = false;
        udpClient?.Close();
        receiveThread?.Join(500);
    }
}