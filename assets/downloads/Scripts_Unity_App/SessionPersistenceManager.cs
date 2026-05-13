using System.IO;
using System.Linq;
using UnityEngine;

public class SessionPersistenceManager : MonoBehaviour
{
    [Header("References")]
    public HeatmapGridRenderer heatmap;
    public WallGenerator wallGenerator;
    public MapFrameConverter mapFrame;

    [Header("File names")]
    public string wallsFileName = "walls.json";
    public string samplesFileName = "wifi_samples.json";

    [Header("Debug")]
    public bool showDebugLogs = true;

    string PersistentPath => Application.persistentDataPath;
    string SessionsRoot => Path.Combine(PersistentPath, "SavedSessions");

    void Awake()
    {
        if (heatmap == null) heatmap = FindObjectOfType<HeatmapGridRenderer>();
        if (wallGenerator == null) wallGenerator = FindObjectOfType<WallGenerator>();
        if (mapFrame == null) mapFrame = FindObjectOfType<MapFrameConverter>();
    }

    public void ClearFullSession()
    {
        if (heatmap != null)
            heatmap.ClearSession();

        if (wallGenerator != null)
            wallGenerator.ClearWalls();

        if (showDebugLogs)
            Debug.Log("🧹 Sesión completa limpiada: muestras + heatmap + paredes");
    }

    public void SaveCurrentSession()
    {
        string sessionFolder = CreateSessionFolder();

        SaveWalls(sessionFolder);
        SaveSamples(sessionFolder);

        if (showDebugLogs)
            Debug.Log($"📁 Sesión guardada en: {sessionFolder}");
    }

    public void LoadLatestSession()
    {
        if (!Directory.Exists(SessionsRoot))
        {
            Debug.LogWarning("⚠️ No existe la carpeta SavedSessions");
            return;
        }

        var dirs = new DirectoryInfo(SessionsRoot)
            .GetDirectories()
            .OrderByDescending(d => d.Name)
            .ToArray();

        if (dirs.Length == 0)
        {
            Debug.LogWarning("⚠️ No hay sesiones guardadas");
            return;
        }

        string latestSessionPath = dirs[0].FullName;
        LoadSessionFromPath(latestSessionPath);
    }

    public void LoadSessionFromPath(string sessionFolder)
    {
        string wallsPath = Path.Combine(sessionFolder, wallsFileName);
        string samplesPath = Path.Combine(sessionFolder, samplesFileName);

        if (!File.Exists(wallsPath))
        {
            Debug.LogError($"❌ No existe walls.json en: {sessionFolder}");
            return;
        }

        if (!File.Exists(samplesPath))
        {
            Debug.LogError($"❌ No existe wifi_samples.json en: {sessionFolder}");
            return;
        }

        // 1. limpiar visual actual
        ClearFullSession();

        // 2. cargar paredes (esto también actualiza MapFrameConverter si WallGenerator limpio lo hace)
        if (wallGenerator != null)
        {
            wallGenerator.LoadWallsFromPath(wallsPath);
        }

        // 3. cargar muestras
        string samplesJson = File.ReadAllText(samplesPath);
        WifiSampleList sampleList = JsonUtility.FromJson<WifiSampleList>(samplesJson);

        if (sampleList == null || sampleList.samples == null)
        {
            Debug.LogError("❌ wifi_samples.json inválido");
            return;
        }

        if (heatmap != null)
        {
            heatmap.LoadSamplesFromList(sampleList.samples);
        }

        if (showDebugLogs)
            Debug.Log($"📂 Sesión cargada desde: {sessionFolder}");
    }

    void SaveWalls(string sessionFolder)
    {
        string sourceWallsPath = Path.Combine(PersistentPath, wallsFileName);

        if (!File.Exists(sourceWallsPath))
        {
            Debug.LogWarning($"⚠️ No existe {sourceWallsPath}, no se guardaron paredes");
            return;
        }

        string targetWallsPath = Path.Combine(sessionFolder, wallsFileName);
        File.Copy(sourceWallsPath, targetWallsPath, true);

        if (showDebugLogs)
            Debug.Log($"🧱 walls.json guardado en: {targetWallsPath}");
    }

    void SaveSamples(string sessionFolder)
    {
        if (heatmap == null)
        {
            Debug.LogWarning("⚠️ HeatmapGridRenderer no asignado, no se guardaron muestras");
            return;
        }

        WifiSampleList wrapper = new WifiSampleList();
        wrapper.samples = heatmap.GetSamples();

        string json = JsonUtility.ToJson(wrapper, true);
        string targetSamplesPath = Path.Combine(sessionFolder, samplesFileName);

        File.WriteAllText(targetSamplesPath, json);

        if (showDebugLogs)
            Debug.Log($"📡 wifi_samples.json guardado en: {targetSamplesPath}");
    }

    string CreateSessionFolder()
    {
        if (!Directory.Exists(SessionsRoot))
            Directory.CreateDirectory(SessionsRoot);

        string folderName = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string sessionFolder = Path.Combine(SessionsRoot, folderName);

        if (!Directory.Exists(sessionFolder))
            Directory.CreateDirectory(sessionFolder);

        return sessionFolder;
    }
}