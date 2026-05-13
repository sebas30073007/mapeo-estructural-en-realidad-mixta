using UnityEngine;
using System.Collections.Generic;
using System.IO;

[System.Serializable]
public class WallSegment
{
    public float x1, y1, x2, y2;
}

[System.Serializable]
public class Persona
{
    public float x;
    public float y;
    public float radio_m;
    public float arco_deg;
}

[System.Serializable]
public class WallData
{
    public int version;
    public string unit;
    public MapInfo map_info;
    public List<WallSegment> wall_segments;
    public List<Persona> personas;
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
    [Header("Configuración JSON")]
    public string jsonFileName = "walls";
    public bool useRobotMap = false;
    
    [Header("Dimensiones de Paredes")]
    public float wallHeight = 2f;
    public float wallThickness = 0.1f;
    
    [Header("Escala y Posición")]
    public float scaleMultiplier = 1f;
    public Vector3 offsetPosition = Vector3.zero;
    
    [Header("Material")]
    public Material wallMaterial;

    [Header("Personas")]
    public float personHeight = 1.8f;
    public Material personMaterial;

    private GameObject wallsParent;
    private GameObject personasParent;

    void Start()
    {
        if (!useRobotMap)
        {
            // Modo prueba: cargar desde Resources
            GenerateWalls();
        }
        else
        {
            Debug.Log("🧱 Esperando paredes del robot...");
        }
    }

    public void GenerateWalls()
    {
        // Método original - carga desde Resources
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
        
        Debug.Log($"✅ Paredes cargadas desde: {fullPath}");
    }

    void LoadWallsFromJSON(string jsonText)
    {
        WallData wallData = JsonUtility.FromJson<WallData>(jsonText);
        
        // Limpiar paredes anteriores
        if (wallsParent != null) DestroyImmediate(wallsParent);
        
        wallsParent = new GameObject("WallsContainer");
        wallsParent.transform.parent = transform;
        wallsParent.transform.localPosition = offsetPosition;
        
        // Generar paredes
        int count = 0;
        foreach (WallSegment segment in wallData.wall_segments)
        {
            CreateWallSegment(segment, count);
            count++;
        }
        
        Debug.Log($"✅ Se generaron {count} segmentos de pared");

        // Generar cilindros de personas
        if (personasParent != null) DestroyImmediate(personasParent);

        personasParent = new GameObject("PersonasContainer");
        personasParent.transform.parent = transform;
        personasParent.transform.localPosition = offsetPosition;

        int personCount = 0;
        if (wallData.personas != null)
        {
            foreach (Persona persona in wallData.personas)
            {
                CreatePersonaCylinder(persona, personCount);
                personCount++;
            }
        }

        Debug.Log($"🧍 Se generaron {personCount} personas");
    }

    void CreateWallSegment(WallSegment segment, int index)
    {
        // Convertir coordenadas 2D a 3D
        // X del JSON -> X de Unity
        // Y del JSON -> Z de Unity (plano horizontal)
        Vector3 start = new Vector3(
            segment.x1 * scaleMultiplier, 
            wallHeight / 2f,  // Altura media
            segment.y1 * scaleMultiplier
        );
        
        Vector3 end = new Vector3(
            segment.x2 * scaleMultiplier, 
            wallHeight / 2f,  // Altura media
            segment.y2 * scaleMultiplier
        );

        // Calcular posición central
        Vector3 center = (start + end) / 2f;
        
        // Calcular longitud del segmento
        float length = Vector3.Distance(start, end);
        
        // Calcular dirección y ángulo en el plano XZ
        Vector3 direction = (end - start).normalized;
        float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        // Crear cubo
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = $"Wall_{index:D3}";
        wall.transform.parent = wallsParent.transform;
        
        // Posicionar
        wall.transform.position = center;
        
        // Escalar: largo a lo largo del segmento, altura vertical, grosor perpendicular
        wall.transform.localScale = new Vector3(wallThickness, wallHeight, length);
        
        // Rotar para alinear con el segmento
        wall.transform.rotation = Quaternion.Euler(0, angle, 0);

        // Aplicar material
        Renderer renderer = wall.GetComponent<Renderer>();
        if (wallMaterial != null)
        {
            renderer.material = wallMaterial;
        }
        else
        {
            renderer.material.color = new Color(0.8f, 0.2f, 0.2f, 0.8f); // Rojo semi-transparente
        }
        
        // Configurar collider
        wall.layer = LayerMask.NameToLayer("Wall");
    }

    void CreatePersonaCylinder(Persona persona, int index)
    {
        // El diámetro del cilindro usa el radio detectado por RANSAC (* 2)
        // X del JSON -> X de Unity,  Y del JSON -> Z de Unity (plano horizontal)
        float diameter = persona.radio_m * 2f * scaleMultiplier;

        Vector3 position = new Vector3(
            persona.x * scaleMultiplier,
            personHeight / 2f,   // base en el suelo, tope en personHeight
            persona.y * scaleMultiplier
        );

        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = $"Persona_{index:D3}";
        cylinder.transform.parent = personasParent.transform;
        cylinder.transform.position = position;

        // Unity Cylinder nativo tiene radio 0.5 en escala 1 → multiplicar por 2 el diámetro
        cylinder.transform.localScale = new Vector3(diameter, personHeight / 2f, diameter);

        // Aplicar material o color por defecto
        Renderer renderer = cylinder.GetComponent<Renderer>();
        if (personMaterial != null)
        {
            renderer.material = personMaterial;
        }
        else
        {
            renderer.material.color = new Color(0.2f, 0.6f, 1.0f, 0.9f); // Azul
        }

        Debug.Log($"🧍 Persona_{index:D3} en x={persona.x:F3}m  y={persona.y:F3}m  " +
                  $"radio={persona.radio_m:F3}m  arco={persona.arco_deg:F1}°");
    }

    [ContextMenu("Regenerar Paredes")]
    public void RegenerateWalls()
    {
        GenerateWalls();
    }
}