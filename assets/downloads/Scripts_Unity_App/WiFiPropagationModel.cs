using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class WiFiPropagationModel : MonoBehaviour
{
    [Header("Referencias")]
    public HeatmapGridRenderer heatmap;
    public string wallSegmentsFile = "unity_walls_contours.json";

    [Header("Parámetros del Modelo")]
    public float d0 = 1.0f; // Distancia de referencia (metros)

    [Header("Ray Casting")]
    public LayerMask wallLayerMask;
    public float rayHeight = 0.2f;

    [Header("Calibración")]
    public int maxCalibrationIterations = 100;

    // Estado interno
    private Vector3 initialAPPosition;
    private bool hasInitialAP = false;
    private bool isCalibrated = false;

    // Parámetros calibrados
    private float calibratedP0 = -40f;
    private float calibratedN = 2.0f;
    private float calibratedLwall = 4.0f;

    private List<WallSegment> wallSegments;

    // =========================================================
    // INICIALIZACIÓN
    // =========================================================
    void Start()
    {
        LoadWallSegments();
    }

    void LoadWallSegments()
    {
        string path = Path.Combine(Application.streamingAssetsPath, wallSegmentsFile);
        
        if (!File.Exists(path))
        {
            Debug.LogWarning($"⚠️ No se encontró {wallSegmentsFile} en StreamingAssets");
            Debug.LogWarning("Se usará ray casting con colliders físicos en su lugar");
            return;
        }

        string json = File.ReadAllText(path);
        WallData data = JsonUtility.FromJson<WallData>(json);  // ← Usar WallData (no WallsData)
        
        wallSegments = data.wall_segments;
        Debug.Log($"✅ Cargados {wallSegments.Count} segmentos de pared desde JSON");
    }

    // =========================================================
    // BOTÓN: Set Initial AP
    // =========================================================
    public void SetInitialAP(Vector3 apPosition)
    {
        var samples = heatmap.GetSamples();
        
        if (samples == null || samples.Count == 0)
        {
            Debug.LogError("❌ No hay muestras para calibrar. Primero recibe datos WiFi.");
            return;
        }

        initialAPPosition = apPosition;
        hasInitialAP = true;

        Debug.Log($"📍 AP inicial establecido en: {apPosition}");
        Debug.Log($"📊 Calibrando con {samples.Count} muestras...");

        // Calibrar parámetros automáticamente
        CalibrateParameters();
    }

    // =========================================================
    // CALIBRACIÓN AUTOMÁTICA (Grid Search)
    // =========================================================
    private void CalibrateParameters()
    {
        float bestP0 = -40f;
        float bestN = 2.0f;
        float bestLwall = 4.0f;
        float bestError = float.MaxValue;

        // Rangos de búsqueda
        float[] p0Range = { -50f, -40f, -35f, -30f, -25f };
        float[] nRange = { 1.5f, 2.0f, 2.5f, 3.0f, 3.5f };
        float[] lwallRange = { 2f, 4f, 6f, 8f, 10f };

        Debug.Log("🔧 Iniciando calibración por grid search...");

        int totalCombinations = p0Range.Length * nRange.Length * lwallRange.Length;
        int currentCombination = 0;

        foreach (float p0 in p0Range)
        {
            foreach (float n in nRange)
            {
                foreach (float lwall in lwallRange)
                {
                    currentCombination++;
                    float error = CalculateModelError(p0, n, lwall);

                    if (error < bestError)
                    {
                        bestError = error;
                        bestP0 = p0;
                        bestN = n;
                        bestLwall = lwall;
                        
                        Debug.Log($"  Mejor configuración actualizada: P0={p0:F1}, n={n:F2}, Lwall={lwall:F1}, MSE={error:F4}");
                    }
                }
            }
        }

        // Guardar parámetros calibrados
        calibratedP0 = bestP0;
        calibratedN = bestN;
        calibratedLwall = bestLwall;
        isCalibrated = true;

        Debug.Log($"✅ Calibración completada:");
        Debug.Log($"   P0 = {calibratedP0:F2} dBm");
        Debug.Log($"   n = {calibratedN:F3}");
        Debug.Log($"   Lwall = {calibratedLwall:F2} dBm");
        Debug.Log($"   Error MSE = {bestError:F4}");
    }

    // =========================================================
    // CALCULAR ERROR DEL MODELO
    // =========================================================
    private float CalculateModelError(float P0, float n, float Lwall)
    {
        var samples = heatmap.GetSamples();
        float sumSquaredError = 0f;

        foreach (var sample in samples)
        {
            Vector3 sampleLocal = new Vector3(sample.x, 0f, sample.y);

            // Distancia
            float dx = sample.x - initialAPPosition.x;
            float dz = sample.y - initialAPPosition.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            dist = Mathf.Max(dist, d0);

            // Contar paredes
            int wallCount = CountWallsBetween(initialAPPosition, sampleLocal);

            // Predicción
            float rssiPred = P0 
                - 10f * n * Mathf.Log10(dist / d0)
                - wallCount * Lwall;

            // Error cuadrático
            float error = rssiPred - sample.originalRssi;
            sumSquaredError += error * error;
        }

        // Error cuadrático medio
        return sumSquaredError / samples.Count;
    }

    // =========================================================
    // RAY CASTING: Contar Paredes
    // =========================================================
    private int CountWallsBetween(Vector3 apLocalPosition, Vector3 sampleLocalPosition)
    {
        // Convertir a coordenadas del mundo
        Vector3 apWorld = heatmap.transform.TransformPoint(apLocalPosition.x, rayHeight, apLocalPosition.z);
        Vector3 sampleWorld = heatmap.transform.TransformPoint(sampleLocalPosition.x, rayHeight, sampleLocalPosition.z);
        
        Vector3 direction = sampleWorld - apWorld;
        float distance = direction.magnitude;
        
        if (distance <= 0.001f)
            return 0;
        
        direction.Normalize();

        // Usar Physics.RaycastAll con colliders físicos
        RaycastHit[] hits = Physics.RaycastAll(apWorld, direction, distance, wallLayerMask, QueryTriggerInteraction.Ignore);
        
        return hits.Length;
    }

    // =========================================================
    // BOTÓN: Compute Heatmap (con parámetros calibrados)
    // =========================================================
    public void ComputeHeatmapWithCalibration()
    {
        if (!hasInitialAP || !isCalibrated)
        {
            Debug.LogError("❌ Primero establece el AP inicial para calibrar");
            return;
        }

        Debug.Log("🎨 Generando heatmap con parámetros calibrados...");
        Debug.Log($"   Usando: P0={calibratedP0:F2}, n={calibratedN:F3}, Lwall={calibratedLwall:F2}");
        
        heatmap.ComputeHeatmapGlobal();
    }

    // =========================================================
    // BOTÓN: Recalculate Heatmap (con nueva posición del AP)
    // =========================================================
    public void RecalculateWithNewAP(Vector3 newAPPosition)
    {
        if (!hasInitialAP || !isCalibrated)
        {
            Debug.LogError("❌ Primero establece el AP inicial y calibra");
            return;
        }

        Debug.Log($"🔄 Recalculando con AP en: {newAPPosition}");

        var samples = heatmap.GetSamples();
        int totalWalls = 0;

        foreach (var sample in samples)
        {
            Vector3 sampleLocal = new Vector3(sample.x, 0f, sample.y);

            // Distancia a nueva posición
            float dx = sample.x - newAPPosition.x;
            float dz = sample.y - newAPPosition.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            dist = Mathf.Max(dist, d0);

            // Contar paredes con nueva posición
            int wallCount = CountWallsBetween(newAPPosition, sampleLocal);
            totalWalls += wallCount;

            // Aplicar fórmula calibrada: RSSI = P0 - 10n·log10(d) - k·Lwall
            float pathLoss = 10f * calibratedN * Mathf.Log10(dist / d0);
            float wallLoss = wallCount * calibratedLwall;
            float estimated = calibratedP0 - pathLoss - wallLoss;

            int estimatedRssi = Mathf.RoundToInt(estimated);
            sample.ApplySimulatedRssi(estimatedRssi);
        }

        Debug.Log($"✅ Recálculo completado");
        Debug.Log($"📊 Promedio de paredes por muestra: {(float)totalWalls / samples.Count:F2}");

        // Actualizar visualización
        heatmap.RefreshVisualsFromCurrentSamples();
        heatmap.ComputeHeatmapGlobal();
    }

    // =========================================================
    // MÉTODOS PÚBLICOS DE UTILIDAD
    // =========================================================
    public void GetCalibratedParameters(out float P0, out float n, out float Lwall)
    {
        P0 = calibratedP0;
        n = calibratedN;
        Lwall = calibratedLwall;
    }

    public bool IsCalibrated()
    {
        return isCalibrated;
    }

    public Vector3 GetInitialAPPosition()
    {
        return initialAPPosition;
    }
}