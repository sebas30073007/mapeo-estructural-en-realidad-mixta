using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class MJPEGReceiver : MonoBehaviour
{
    [Header("Stream")]
    public string streamUrl = "http://100.90.163.4:8080/stream";

    [Header("UI")]
    public RawImage cameraRawImage;

    Texture2D camTex;
    Thread streamThread;
    volatile bool running = false;

    // Cola thread-safe para pasar frames al hilo principal
    readonly Queue<byte[]> frameQueue = new Queue<byte[]>();
    readonly object queueLock = new object();

    private float lastFrameTime = -999f;
    private const float disconnectTimeout = 3f;

    // Agrega esta propiedad pública
    public bool IsConnected => (Time.unscaledTime - lastFrameTime) <= disconnectTimeout;


    void Start()
    {
        camTex = new Texture2D(320, 240, TextureFormat.RGB24, false);
        if (cameraRawImage != null)
            cameraRawImage.texture = camTex;

        running = true;
        streamThread = new Thread(StreamLoop) { IsBackground = true };
        streamThread.Start();
    }

    void StreamLoop()
    {
        while (running)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(streamUrl);
                request.Timeout = 5000;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                {
                    byte[] buffer = new byte[65536];
                    byte[] accumulated = new byte[0];

                    // Markers JPEG
                    byte[] SOI = { 0xFF, 0xD8 };
                    byte[] EOI = { 0xFF, 0xD9 };

                    while (running)
                    {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

                        // Acumular datos
                        byte[] newAcc = new byte[accumulated.Length + bytesRead];
                        Array.Copy(accumulated, 0, newAcc, 0, accumulated.Length);
                        Array.Copy(buffer, 0, newAcc, accumulated.Length, bytesRead);
                        accumulated = newAcc;

                        // Extraer frames JPEG completos
                        while (true)
                        {
                            int soiIdx = FindBytes(accumulated, SOI, 0);
                            if (soiIdx < 0) { accumulated = new byte[0]; break; }

                            int eoiIdx = FindBytes(accumulated, EOI, soiIdx + 2);
                            if (eoiIdx < 0) break;

                            int frameLen = eoiIdx + 2 - soiIdx;
                            byte[] frame = new byte[frameLen];
                            Array.Copy(accumulated, soiIdx, frame, 0, frameLen);

                            // Encolar frame para el hilo principal
                            lock (queueLock)
                            {
                                // Mantener solo el frame más reciente
                                frameQueue.Clear();
                                frameQueue.Enqueue(frame);
                            }

                            int remaining = accumulated.Length - (eoiIdx + 2);
                            byte[] newBuf = new byte[remaining];
                            Array.Copy(accumulated, eoiIdx + 2, newBuf, 0, remaining);
                            accumulated = newBuf;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (running)
                {
                    Debug.LogWarning($"MJPEG stream error: {e.Message}. Reintentando...");
                    Thread.Sleep(2000);
                }
            }
        }
    }

    void Update()
    {
        byte[] frame = null;
        lock (queueLock)
        {
            if (frameQueue.Count > 0)
                frame = frameQueue.Dequeue();
        }

        if (frame != null && camTex.LoadImage(frame))
        {
            if (cameraRawImage != null)
                cameraRawImage.texture = camTex;
                lastFrameTime = Time.unscaledTime;
        }
    }

    int FindBytes(byte[] haystack, byte[] needle, int startIdx)
    {
        for (int i = startIdx; i <= haystack.Length - needle.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { found = false; break; }
            if (found) return i;
        }
        return -1;
    }

    void OnDestroy()
    {
        running = false;
        streamThread?.Join(500);
    }
}