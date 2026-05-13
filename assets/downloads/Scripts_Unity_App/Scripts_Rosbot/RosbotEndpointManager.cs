using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class RosbotEndpointManager : MonoBehaviour
{
    public static RosbotEndpointManager Instance { get; private set; }

    [Header("Default Endpoint")]
    [SerializeField] private string defaultIp = "100.90.163.4";

    [Header("Persistence")]
    [SerializeField] private string playerPrefsKey = "ROSBOT_ENDPOINT_IP";

    [Header("Validation")]
    [SerializeField] private bool requireIPv4 = true;

    public string CurrentIp { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadSavedIp();
    }

    public void LoadSavedIp()
    {
        string savedIp = PlayerPrefs.GetString(playerPrefsKey, defaultIp);
        CurrentIp = SanitizeIp(savedIp, defaultIp);
        Debug.Log($"[RosbotEndpointManager] Loaded IP: {CurrentIp}");
    }

    public bool TrySetIp(string newIp)
    {
        string sanitized = SanitizeIp(newIp, string.Empty);
        if (!IsValidIp(sanitized))
        {
            Debug.LogWarning($"[RosbotEndpointManager] Invalid IP ignored: '{newIp}'");
            return false;
        }

        CurrentIp = sanitized;
        PlayerPrefs.SetString(playerPrefsKey, CurrentIp);
        PlayerPrefs.Save();

        Debug.Log($"[RosbotEndpointManager] Saved IP: {CurrentIp}");
        return true;
    }

    public void ResetToDefault()
    {
        CurrentIp = defaultIp;
        PlayerPrefs.SetString(playerPrefsKey, CurrentIp);
        PlayerPrefs.Save();
        Debug.Log($"[RosbotEndpointManager] Reset to default IP: {CurrentIp}");
    }

    public string GetIp()
    {
        if (!IsValidIp(CurrentIp))
            return defaultIp;

        return CurrentIp;
    }

    public bool IsValidIp(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        if (!IPAddress.TryParse(candidate.Trim(), out IPAddress parsed))
            return false;

        if (!requireIPv4)
            return true;

        return parsed.AddressFamily == AddressFamily.InterNetwork;
    }

    private string SanitizeIp(string value, string fallback)
    {
        string sanitized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
