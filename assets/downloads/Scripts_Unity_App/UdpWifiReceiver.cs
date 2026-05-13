using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using UnityEngine;

public class UdpWifiReceiver : MonoBehaviour
{
    public int listenPort = 5007;
    public HeatmapGridRenderer heatmap;

    private UdpClient client;
    private Thread thread;
    private volatile bool running;

    private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();

    void Start()
    {
        running = true;
        client = new UdpClient(listenPort);
        client.Client.ReceiveTimeout = 500; // ms, avoids blocking forever

        thread = new Thread(ReceiveLoop);
        thread.IsBackground = true;
        thread.Start();

        Debug.Log($"UDP receiver listening on 127.0.0.1:{listenPort}");
    }

    void ReceiveLoop()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, listenPort);

        while (running)
        {
            try
            {
                byte[] data = client.Receive(ref ep);
                string msg = Encoding.UTF8.GetString(data);
                inbox.Enqueue(msg);
            }
            catch (SocketException)
            {
                // timeout, continue
            }
            catch
            {
                // ignore malformed packets
            }
        }
    }

    void Update()
    {
        int maxPerFrame = 100;
        int processed = 0;

        while (processed < maxPerFrame && inbox.TryDequeue(out var msg))
        {
            processed++;

            try
            {
                var s = JsonUtility.FromJson<WifiSample>(msg);
                if (s == null) continue;

                if (s.type == "shutdown")
                {
                    Debug.Log("Shutdown received.");
                    StopReceiver();
                    return;
                }

                if (s.type == "sample")
                {
                    s.InitializeForRuntime();
                    heatmap.AddSample(s);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Parse error: " + e.Message);
            }
        }
    }

    void StopReceiver()
    {
        running = false;

        try { client?.Close(); } catch { }
        client = null;

        try
        {
            if (thread != null && thread.IsAlive)
                thread.Join(500);
        }
        catch { }

        thread = null;
    }

    void OnDisable() => StopReceiver();
    void OnDestroy() => StopReceiver();
}