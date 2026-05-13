using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using System;
using TMPro;

[Serializable]
public class MapResponse
{
    public string status;
    public string message;
    public string map_name;
    public string pgm;
    public string yaml;
    public string walls;
    public int pgm_size;
    public int yaml_size;
    public int walls_size;
}

public class SLAMMapDownloader : MonoBehaviour
{
    [Header("Configuración del robot")]
    [SerializeField] private string fallbackIp = "100.90.163.4";
    public int robotPort = 5008;

    [Header("Referencias UI")]
    public Button btnUpdateSLAM;
    public TMP_Text statusText;

    [Header("Referencias componentes")]
    public PGMViewer pgmViewer;
    public WallGenerator wallGenerator;
    public HeatmapGridRenderer heatmap;
    public MapFrameConverter mapFrame;
    public MapBoundsAutoCollider mapBoundsAutoCollider;

    [Header("Opciones")]
    [Tooltip("Si se activa, se limpian las muestras antes de actualizar el mapa.")]
    public bool clearSamplesBeforeUpdate = false;

    [Header("Debug")]
    public bool showDebugLogs = true;

    private string RobotIP
    {
        get
        {
            if (RosbotEndpointManager.Instance != null)
            {
                string ip = RosbotEndpointManager.Instance.GetIp();
                Debug.Log($"[SLAMMapDownloader] IP desde EndpointManager: {ip}");
                return ip;
            }

            Debug.LogWarning("[SLAMMapDownloader] RosbotEndpointManager no encontrado, usando fallback");
            return fallbackIp;
        }
    }

    private string ServerURL => $"http://{RobotIP}:{robotPort}";
    private string PersistentPath => Application.persistentDataPath;

    void Start()
    {
        if (btnUpdateSLAM != null)
            btnUpdateSLAM.onClick.AddListener(OnUpdateSLAMClicked);

        UpdateStatus("Listo para descargar mapa");

        if (showDebugLogs)
        {
            Debug.Log($"📁 Persistent Path: {PersistentPath}");
            Debug.Log($"🌐 Server URL: {ServerURL}");
        }

        // Autoencontrar referencias si no están asignadas
        if (wallGenerator == null)
            wallGenerator = FindObjectOfType<WallGenerator>();

        if (heatmap == null)
            heatmap = FindObjectOfType<HeatmapGridRenderer>();

        if (mapFrame == null)
            mapFrame = FindObjectOfType<MapFrameConverter>();

        if (mapBoundsAutoCollider == null)
            mapBoundsAutoCollider = FindObjectOfType<MapBoundsAutoCollider>();
    }

    public void OnUpdateSLAMClicked()
    {
        StartCoroutine(DownloadMapFromRobot());
    }

    IEnumerator DownloadMapFromRobot()
    {
        UpdateStatus("🔄 Conectando al robot...");
        yield return StartCoroutine(CheckServerHealth());

        UpdateStatus("🗺️ Generando mapa SLAM...");

        UnityWebRequest request = UnityWebRequest.Get($"{ServerURL}/generate_map");
        request.timeout = 60;

        if (showDebugLogs)
            Debug.Log($"📤 GET {request.url}");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorMsg = $"❌ Error al descargar mapa: {request.error}";
            UpdateStatus(errorMsg);
            Debug.LogError(errorMsg);
            Debug.LogError($"Response Code: {request.responseCode}");
            yield break;
        }

        string jsonResponse = request.downloadHandler.text;

        if (showDebugLogs)
            Debug.Log($"📥 Respuesta recibida: {jsonResponse.Substring(0, Mathf.Min(200, jsonResponse.Length))}...");

        MapResponse response = null;
        try
        {
            response = JsonUtility.FromJson<MapResponse>(jsonResponse);
        }
        catch (Exception e)
        {
            UpdateStatus($"❌ Error parseando respuesta: {e.Message}");
            Debug.LogError($"Error parseando JSON del servidor: {e}");
            yield break;
        }

        if (response == null || response.status != "success")
        {
            UpdateStatus($"❌ Error del servidor: {response?.message}");
            yield break;
        }

        UpdateStatus("💾 Guardando archivos...");

        string pgmPath = Path.Combine(PersistentPath, "current_map.pgm");
        string yamlPath = Path.Combine(PersistentPath, "current_map.yaml");
        string wallsPath = Path.Combine(PersistentPath, "walls.json");

        try
        {
            // Guardar PGM
            byte[] pgmBytes = Convert.FromBase64String(response.pgm);
            File.WriteAllBytes(pgmPath, pgmBytes);

            // Guardar YAML
            File.WriteAllText(yamlPath, response.yaml);

            // Guardar walls
            File.WriteAllText(wallsPath, response.walls);

            if (showDebugLogs)
            {
                Debug.Log($"✅ Archivos guardados:");
                Debug.Log($"   PGM: {pgmPath} ({pgmBytes.Length} bytes)");
                Debug.Log($"   YAML: {yamlPath} ({response.yaml.Length} chars)");
                Debug.Log($"   Walls: {wallsPath} ({response.walls.Length} chars)");
            }
        }
        catch (Exception e)
        {
            UpdateStatus($"❌ Error guardando archivos: {e.Message}");
            Debug.LogError($"Error guardando archivos: {e}");
            yield break;
        }

        // Parsear walls.json para extraer map_info
        WallData wallData = null;
        try
        {
            wallData = JsonUtility.FromJson<WallData>(response.walls);
        }
        catch (Exception e)
        {
            UpdateStatus($"❌ Error parseando walls.json: {e.Message}");
            Debug.LogError($"Error parseando walls.json: {e}");
            yield break;
        }

        if (wallData == null || wallData.map_info == null || wallData.map_info.origin == null || wallData.map_info.origin.Length < 2)
        {
            UpdateStatus("❌ walls.json inválido: falta map_info/origin");
            Debug.LogError("walls.json inválido: falta map_info/origin");
            yield break;
        }

        // 1) Actualizar converter del mapa
        if (mapFrame != null)
        {
            mapFrame.SetMapInfo(
                wallData.map_info.origin[0],
                wallData.map_info.origin[1],
                wallData.map_info.resolution,
                wallData.map_info.width,
                wallData.map_info.height
            );
        }
        else
        {
            Debug.LogWarning("⚠️ No se encontró MapFrameConverter en escena");
        }

        // 2) Actualizar PGM viewer
        if (pgmViewer != null)
        {
            UpdateStatus("🎨 Actualizando PGM...");
            pgmViewer.LoadPGMFromPath(pgmPath);
        }

        // 3) Generar paredes
        if (wallGenerator != null)
        {
            UpdateStatus("🧱 Generando paredes...");
            wallGenerator.LoadWallsFromPath(wallsPath);
        }
        else
        {
            Debug.LogWarning("⚠️ No se encontró WallGenerator en escena");
        }

        // 4) Limpiar muestras opcionalmente
        if (heatmap != null && clearSamplesBeforeUpdate)
        {
            heatmap.ClearSession();
            Debug.Log("🧹 Muestras limpiadas antes de refrescar mapa");
        }

        // 5) Refrescar heatmap con nueva geometría
        if (heatmap != null)
        {
            heatmap.UpdateMapDimensionsAndRefresh(wallsPath);
        }
        else
        {
            Debug.LogWarning("⚠️ No se encontró HeatmapGridRenderer en escena");
        }

        // 6) Recomputar collider global si existe
        if (mapBoundsAutoCollider != null)
        {
            mapBoundsAutoCollider.Recompute();
        }

        UpdateStatus("✅ Mapa, paredes y heatmap actualizados");
    }

    IEnumerator CheckServerHealth()
    {
        string url = $"{ServerURL}/health";

        if (showDebugLogs)
            Debug.Log($"🏥 Health Check: {url}");

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = 5;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorMsg = $"❌ Robot no responde: {request.error}";
            UpdateStatus(errorMsg);
            Debug.LogError(errorMsg);
            Debug.LogError($"Response Code: {request.responseCode}");
        }
        else
        {
            if (showDebugLogs)
                Debug.Log($"✅ Servidor conectado: {request.downloadHandler.text}");
        }
    }

    void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        if (showDebugLogs)
            Debug.Log($"[SLAMDownloader] {message}");
    }
}