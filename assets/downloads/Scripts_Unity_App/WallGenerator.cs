using UnityEngine;
using System.Collections.Generic;
using System.IO;

[System.Serializable]
public class WallSegment
{
    public float x1, y1, x2, y2;
}

[System.Serializable]
public class WallData
{
    public int version;
    public string unit;
    public MapInfo map_info;
    public List<WallSegment> wall_segments;
}

[System.Serializable]
public class MapInfo
{
    public string image;
    public float resolution;
    public float[] origin;
    public int width, height;
}

public class WallGenerator : MonoBehaviour
{
    [Header("JSON")]
    public bool useRobotMap = true;
    public string jsonFileName = "walls";

    [Header("Wall dimensions")]
    public float wallHeight = 2f;
    public float wallThickness = 0.1f;
    public float minSegmentLength = 0.05f;

    [Header("Material")]
    public Material wallMaterial;

    [Header("References")]
    public MapFrameConverter mapFrame;

    [Header("Debug")]
    public bool showDebugLogs = true;

    private GameObject wallsParent;

    void Start()
    {
        if (!useRobotMap)
        {
            GenerateWallsFromResources();
        }
        else
        {
            if (showDebugLogs)
                Debug.Log("🧱 Esperando paredes del robot...");
        }
    }

    public void GenerateWallsFromResources()
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(jsonFileName);
        if (jsonFile == null)
        {
            Debug.LogError($"❌ No se encontró {jsonFileName}.json en Resources");
            return;
        }

        LoadWallsFromJSON(jsonFile.text);
    }

    public void LoadWallsFromPath(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"❌ No se encontró: {fullPath}");
            return;
        }

        string jsonText = File.ReadAllText(fullPath);
        LoadWallsFromJSON(jsonText);

        if (showDebugLogs)
            Debug.Log($"✅ Paredes cargadas desde: {fullPath}");
    }

    void LoadWallsFromJSON(string jsonText)
    {
        WallData wallData = JsonUtility.FromJson<WallData>(jsonText);

        if (wallData == null)
        {
            Debug.LogError("❌ walls.json inválido");
            return;
        }

        if (wallData.wall_segments == null || wallData.wall_segments.Count == 0)
        {
            Debug.LogWarning("⚠️ walls.json no contiene wall_segments");
            ClearWalls();
            return;
        }

        if (wallData.map_info == null || wallData.map_info.origin == null || wallData.map_info.origin.Length < 2)
        {
            Debug.LogError("❌ walls.json no contiene map_info válido");
            return;
        }

        if (mapFrame == null)
        {
            mapFrame = FindObjectOfType<MapFrameConverter>();
        }

        if (mapFrame == null)
        {
            Debug.LogError("❌ WallGenerator necesita una referencia a MapFrameConverter");
            return;
        }

        // Actualizar frame del mapa desde walls.json
        mapFrame.SetMapInfo(
            wallData.map_info.origin[0],
            wallData.map_info.origin[1],
            wallData.map_info.resolution,
            wallData.map_info.width,
            wallData.map_info.height
        );

        ClearWalls();

        wallsParent = new GameObject("WallsContainer");
        wallsParent.transform.SetParent(transform, false);
        wallsParent.transform.localPosition = Vector3.zero;
        wallsParent.transform.localRotation = Quaternion.identity;
        wallsParent.transform.localScale = Vector3.one;

        int count = 0;

        foreach (WallSegment segment in wallData.wall_segments)
        {
            if (CreateWallSegment(segment, count))
                count++;
        }

        if (showDebugLogs)
            Debug.Log($"✅ Se generaron {count} segmentos de pared");
    }

    bool CreateWallSegment(WallSegment segment, int index)
    {
        if (mapFrame == null)
        {
            Debug.LogError("❌ MapFrameConverter no asignado");
            return false;
        }

        // Convertir del frame map a local Unity
        Vector3 start = mapFrame.MapToLocal(segment.x1, segment.y1, wallHeight * 0.5f);
        Vector3 end   = mapFrame.MapToLocal(segment.x2, segment.y2, wallHeight * 0.5f);

        float length = Vector3.Distance(start, end);
        if (length < minSegmentLength)
            return false;

        Vector3 center = (start + end) * 0.5f;
        Vector3 direction = (end - start).normalized;

        // En Unity plano horizontal = XZ
        float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = $"Wall_{index:D3}";
        wall.transform.SetParent(wallsParent.transform, false);

        wall.transform.localPosition = center;
        wall.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        wall.transform.localScale = new Vector3(wallThickness, wallHeight, length);

        Renderer renderer = wall.GetComponent<Renderer>();
        if (renderer != null)
        {
            if (wallMaterial != null)
            {
                renderer.material = wallMaterial;
            }
            else
            {
                renderer.material.color = new Color(0.8f, 0.2f, 0.2f, 0.8f);
            }
        }

        wall.layer = LayerMask.NameToLayer("Wall");

        if (showDebugLogs)
        {
            Debug.Log(
                $"🧱 Wall_{index:D3} | " +
                $"map=({segment.x1:F2},{segment.y1:F2}) -> ({segment.x2:F2},{segment.y2:F2}) | " +
                $"local=({wall.transform.localPosition.x:F2}, {wall.transform.localPosition.z:F2})"
            );
        }

        return true;
    }

    [ContextMenu("Limpiar Paredes")]
    public void ClearWalls()
    {
        if (wallsParent != null)
        {
            if (Application.isPlaying)
                Destroy(wallsParent);
            else
                DestroyImmediate(wallsParent);
        }

        wallsParent = null;
    }

    [ContextMenu("Regenerar Paredes")]
    public void RegenerateWalls()
    {
        if (useRobotMap)
        {
            string fullPath = Path.Combine(Application.persistentDataPath, "walls.json");
            LoadWallsFromPath(fullPath);
        }
        else
        {
            GenerateWallsFromResources();
        }
    }
}