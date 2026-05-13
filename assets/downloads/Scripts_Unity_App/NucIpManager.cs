using UnityEngine;

public class NucIpManager : MonoBehaviour
{
    public static NucIpManager Instance { get; private set; }

    [Header("Default")]
    public string defaultIp = "192.168.100.20";

    [Header("Persistence")]
    public string playerPrefsKey = "NUC_IP";

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

        LoadIp();
    }

    public void LoadIp()
    {
        CurrentIp = PlayerPrefs.GetString(playerPrefsKey, defaultIp);
        Debug.Log("[NucIpManager] Loaded IP: " + CurrentIp);
    }

    public void SetIp(string newIp)
    {
        if (string.IsNullOrWhiteSpace(newIp))
        {
            Debug.LogWarning("[NucIpManager] IP vacía, no se aplicó.");
            return;
        }

        newIp = newIp.Trim();

        CurrentIp = newIp;
        PlayerPrefs.SetString(playerPrefsKey, CurrentIp);
        PlayerPrefs.Save();

        Debug.Log("[NucIpManager] Saved IP: " + CurrentIp);
    }

    public string GetIp()
    {
        return string.IsNullOrWhiteSpace(CurrentIp) ? defaultIp : CurrentIp;
    }
}