using System.Net;
using System.Net.Sockets;
using TMPro;
using UnityEngine;

public class RosbotTelemetryStatusPanel : MonoBehaviour
{
    [Header("Receivers")]
    [SerializeField] private MJPEGReceiver videoReceiver;
    [SerializeField] private UdpLidarReceiver lidarReceiver;

    [Header("Texts")]
    [SerializeField] private TMP_Text robotIpValueText;
    [SerializeField] private TMP_Text myIpValueText;

    [Header("Connection Indicator")]
    [SerializeField] private Renderer connectionCylinder;
    [SerializeField] private Color connectedColor = Color.green;
    [SerializeField] private Color disconnectedColor = Color.red;

    [Header("Refresh")]
    [SerializeField] private float refreshHz = 5f;
    [SerializeField] private float localIpRefreshSeconds = 5f;
    [SerializeField] private string fallbackText = "--";

    private float nextRefreshTime;
    private float nextIpRefreshTime;
    private string cachedMyIp = "Unknown";
    private Material cylinderMaterial;

    private void Awake()
    {
        cachedMyIp = GetLocalIPv4();

        if (connectionCylinder != null)
        {
            cylinderMaterial = connectionCylinder.material;
            cylinderMaterial.color = disconnectedColor;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextIpRefreshTime)
        {
            nextIpRefreshTime = Time.unscaledTime + Mathf.Max(1f, localIpRefreshSeconds);
            cachedMyIp = GetLocalIPv4();
        }

        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + 1f / Mathf.Max(1f, refreshHz);
        RefreshPanel();
    }

    private void RefreshPanel()
    {
        string endpointIp = RosbotEndpointManager.Instance != null
            ? RosbotEndpointManager.Instance.GetIp()
            : fallbackText;

        SetValue(robotIpValueText, endpointIp);
        SetValue(myIpValueText, cachedMyIp);

        bool isConnected = (videoReceiver != null && videoReceiver.IsConnected)
                        || (lidarReceiver != null && lidarReceiver.IsConnected);

        if (cylinderMaterial != null)
            cylinderMaterial.color = isConnected ? connectedColor : disconnectedColor;
    }

    private void SetValue(TMP_Text field, string value)
    {
        if (field != null)
            field.text = string.IsNullOrWhiteSpace(value) ? fallbackText : value;
    }

    private string GetLocalIPv4()
    {
        try
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    return ip.ToString();
            }
        }
        catch { }
        return "Unknown";
    }
}