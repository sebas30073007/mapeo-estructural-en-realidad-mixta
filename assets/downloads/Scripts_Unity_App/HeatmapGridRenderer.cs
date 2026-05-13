using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class HeatmapGridRenderer : MonoBehaviour
{
    [Header("References")]
    public MapFrameConverter mapFrame;

    [Header("Point markers (spheres)")]
    public GameObject pointPrefab;
    public float pointY = 0.08f;
    [Tooltip("Multiplicador del tamaño de las esferas")]
    public float pointSize = 1.0f;

    [Header("RSSI Labels (TextMeshPro)")]
    public TMP_Text labelPrefab;
    public bool showLabels = true;
    public float labelY = 0.18f;
    [Tooltip("Multiplicador del tamaño de labels respecto al tamaño de esfera")]
    public float labelScaleFactor = 0.6f;

    [Header("Heatmap surface")]
    public Renderer surfaceRenderer;
    public int surfaceResolution = 128;

    [Header("Heatmap intensity")]
    [Range(0f, 1f)]
    public float heatmapOpacity = 0.9f;
    public float heatmapBrightness = 1.2f;

    [Header("Interpolation (Global IDW)")]
    [Tooltip("Mayor valor = más influencia de muestras cercanas")]
    public float idwPower = 2f;
    [Tooltip("Evita división por cero")]
    public float idwEpsilon = 0.05f;

    [Header("Local support")]
    public int minNeighbors = 2;
    public float supportRadiusM = 3.0f;

    [Header("Texture alignment")]
    public bool flipU = true;
    public bool flipV = true;

    // Internals
    private Texture2D heatTex;
    private Color[] pixels;

    private readonly List<WifiSample> samples = new();
    private readonly Dictionary<int, GameObject> pointByKey = new();
    private readonly Dictionary<int, TMP_Text> labelByKey = new();
    private readonly Dictionary<int, int> rssiByKey = new();

    private bool heatmapVisible = true;
    private bool pointsVisible = true;
    private bool labelsVisible = true;

    void Awake()
    {
        heatTex = new Texture2D(surfaceResolution, surfaceResolution, TextureFormat.RGBA32, false);
        heatTex.filterMode = FilterMode.Bilinear;
        heatTex.wrapMode = TextureWrapMode.Clamp;
        pixels = new Color[surfaceResolution * surfaceResolution];

        ClearHeatmapTexture();

        if (surfaceRenderer != null)
        {
            surfaceRenderer.material.mainTexture = heatTex;
            surfaceRenderer.enabled = heatmapVisible;
        }
    }

    float GetDynamicPointSize()
    {
        return 0.15f * pointSize;
    }

    Vector3 SampleMapToLocal(WifiSample s, float yValue)
    {
        if (mapFrame == null)
        {
            Debug.LogError("❌ HeatmapGridRenderer necesita MapFrameConverter");
            return new Vector3(0f, yValue, 0f);
        }

        return mapFrame.MapToLocal(s.x, s.y, yValue);
    }

    public void AddSample(WifiSample s)
    {
        if (mapFrame == null)
        {
            Debug.LogError("❌ HeatmapGridRenderer necesita MapFrameConverter");
            return;
        }

        int key = s.k;
        if (rssiByKey.ContainsKey(key))
            return;

        samples.Add(s);
        rssiByKey[key] = s.GetDisplayRssi();

        float sphereSize = GetDynamicPointSize();
        Vector3 pointLocal = SampleMapToLocal(s, pointY);
        Vector3 labelLocal = SampleMapToLocal(s, labelY);

        // Crear esfera
        if (pointPrefab != null && !pointByKey.ContainsKey(key))
        {
            GameObject go = Instantiate(pointPrefab, transform);
            go.transform.localScale = Vector3.one * sphereSize;
            go.transform.localPosition = pointLocal;
            go.name = $"Point_k{key}";
            pointByKey[key] = go;

            Debug.Log($"📍 Muestra k={key} local=({pointLocal.x:F2}, {pointLocal.z:F2}) map=({s.x:F2}, {s.y:F2}) size={sphereSize:F2}");
        }

        if (pointByKey.TryGetValue(key, out var pointGo) && pointGo != null)
        {
            pointGo.SetActive(pointsVisible);
            var rend = pointGo.GetComponent<Renderer>();
            if (rend != null)
                rend.material.color = WifiColorMap.ColorFromRssiContinuous(s.GetDisplayRssi(), 1f);
        }

        // Crear label
        if (labelPrefab != null && showLabels)
        {
            if (!labelByKey.ContainsKey(key))
            {
                TMP_Text txt = Instantiate(labelPrefab, transform);
                txt.transform.localScale = Vector3.one * sphereSize * labelScaleFactor;
                txt.transform.localPosition = labelLocal;
                txt.name = $"Label_k{key}";
                txt.alignment = TextAlignmentOptions.Center;
                txt.enableWordWrapping = false;
                labelByKey[key] = txt;
            }

            var label = labelByKey[key];
            label.gameObject.SetActive(labelsVisible);
            label.text = $"{s.GetDisplayRssi()} dBm";
            label.color = Color.black;
        }
    }

    public void ComputeHeatmapGlobal()
    {
        if (samples.Count == 0)
        {
            Debug.LogWarning("⚠️ No hay muestras para computar heatmap");
            return;
        }

        if (mapFrame == null)
        {
            Debug.LogError("❌ HeatmapGridRenderer necesita MapFrameConverter");
            return;
        }

        float planeWidthM = mapFrame.MapWidthMeters;
        float planeHeightM = mapFrame.MapHeightMeters;

        if (planeWidthM <= 0f || planeHeightM <= 0f)
        {
            Debug.LogWarning("⚠️ El tamaño del mapa aún no es válido");
            return;
        }

        float minSx = float.PositiveInfinity, maxSx = float.NegativeInfinity;
        float minSz = float.PositiveInfinity, maxSz = float.NegativeInfinity;

        for (int i = 0; i < samples.Count; i++)
        {
            Vector2 local = mapFrame.MapToLocal2D(samples[i].x, samples[i].y);
            minSx = Mathf.Min(minSx, local.x);
            maxSx = Mathf.Max(maxSx, local.x);
            minSz = Mathf.Min(minSz, local.y);
            maxSz = Mathf.Max(maxSz, local.y);
        }

        Debug.Log($"📊 Bounding box muestras: X=[{minSx:F2},{maxSx:F2}] Z=[{minSz:F2},{maxSz:F2}]");

        float marginM = 1.0f;
        minSx -= marginM; maxSx += marginM;
        minSz -= marginM; maxSz += marginM;

        float planeMinX = 0f;
        float planeMaxX = planeWidthM;
        float planeMinZ = 0f;
        float planeMaxZ = planeHeightM;

        minSx = Mathf.Clamp(minSx, planeMinX, planeMaxX);
        maxSx = Mathf.Clamp(maxSx, planeMinX, planeMaxX);
        minSz = Mathf.Clamp(minSz, planeMinZ, planeMaxZ);
        maxSz = Mathf.Clamp(maxSz, planeMinZ, planeMaxZ);

        Debug.Log($"📊 Bounding box clipped: X=[{minSx:F2},{maxSx:F2}] Z=[{minSz:F2},{maxSz:F2}]");

        float supportRadius2 = supportRadiusM * supportRadiusM;
        int N = surfaceResolution;
        int painted = 0;

        for (int py = 0; py < N; py++)
        {
            for (int px = 0; px < N; px++)
            {
                float u = px / (float)(N - 1);
                float v = py / (float)(N - 1);

                if (flipU) u = 1f - u;
                if (flipV) v = 1f - v;

                float x = Mathf.Lerp(planeMinX, planeMaxX, u);
                float z = Mathf.Lerp(planeMinZ, planeMaxZ, v);

                // pintar solo en la región recorrida
                if (x < minSx || x > maxSx || z < minSz || z > maxSz)
                {
                    pixels[py * N + px] = new Color(0, 0, 0, 0);
                    continue;
                }

                int neighbors = 0;
                float wSum = 0f;
                float rssiSum = 0f;

                for (int i = 0; i < samples.Count; i++)
                {
                    Vector2 local = mapFrame.MapToLocal2D(samples[i].x, samples[i].y);

                    float dx = x - local.x;
                    float dz = z - local.y;
                    float d2 = dx * dx + dz * dz;

                    if (d2 <= supportRadius2)
                        neighbors++;

                    float d = Mathf.Sqrt(d2);
                    float w = 1f / Mathf.Pow(d + idwEpsilon, idwPower);
                    wSum += w;
                    rssiSum += w * samples[i].GetDisplayRssi();
                }

                if (neighbors < minNeighbors || wSum <= 0f)
                {
                    pixels[py * N + px] = new Color(0, 0, 0, 0);
                    continue;
                }

                int rssiEst = Mathf.RoundToInt(rssiSum / wSum);
                Color c = WifiColorMap.ColorFromRssiContinuous(rssiEst, 1f);

                c.r = Mathf.Clamp01(c.r * heatmapBrightness);
                c.g = Mathf.Clamp01(c.g * heatmapBrightness);
                c.b = Mathf.Clamp01(c.b * heatmapBrightness);
                c.a = heatmapOpacity;

                pixels[py * N + px] = c;
                painted++;
            }
        }

        Debug.Log($"✅ Heatmap computado: {painted} píxeles pintados de {N * N}");

        heatTex.SetPixels(pixels);
        heatTex.Apply();

        if (surfaceRenderer != null)
        {
            surfaceRenderer.enabled = heatmapVisible;
            Debug.Log($"✅ Textura aplicada al renderer: {surfaceRenderer.gameObject.name}");
        }
        else
        {
            Debug.LogError("❌ surfaceRenderer es null — asígnalo en el Inspector");
        }
    }

    public void UpdateMapDimensionsAndRefresh(string wallsJsonPath)
    {
        if (mapFrame == null || surfaceRenderer == null)
        {
            Debug.LogWarning("⚠️ Falta MapFrameConverter o surfaceRenderer");
            return;
        }

        float width = mapFrame.MapWidthMeters;
        float height = mapFrame.MapHeightMeters;

        Transform planeTf = surfaceRenderer.transform;
        planeTf.localScale = new Vector3(width / 10f, 1f, height / 10f);

        // Como el mapa local va de (0,0) a (width,height), el plane debe quedar centrado en esa caja
        //planeTf.localPosition = new Vector3(width * 0.5f, planeTf.localPosition.y, height * 0.5f);
        planeTf.localPosition = mapFrame.GetLocalMapCenter(planeTf.localPosition.y);
        Debug.Log($"🟦 HeatmapPlane → scale={planeTf.localScale}, pos={planeTf.localPosition}, parent={planeTf.parent?.name}");

        RefreshSamplePositions();
    }

    void RefreshSamplePositions()
    {
        if (mapFrame == null) return;

        float sphereSize = GetDynamicPointSize();

        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            int key = s.k;

            Vector3 pointLocal = SampleMapToLocal(s, pointY);
            Vector3 labelLocal = SampleMapToLocal(s, labelY);

            if (pointByKey.TryGetValue(key, out var pointGo) && pointGo != null)
            {
                pointGo.transform.localScale = Vector3.one * sphereSize;
                pointGo.transform.localPosition = pointLocal;
            }

            if (labelByKey.TryGetValue(key, out var label) && label != null)
            {
                label.transform.localScale = Vector3.one * sphereSize * labelScaleFactor;
                label.transform.localPosition = labelLocal;
            }
        }

        Debug.Log($"🔄 {samples.Count} muestras reposicionadas");
    }

    public void RefreshVisualsFromCurrentSamples()
    {
        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            int key = s.k;
            int displayRssi = s.GetDisplayRssi();

            if (pointByKey.TryGetValue(key, out var pointGo) && pointGo != null)
            {
                var rend = pointGo.GetComponent<Renderer>();
                if (rend != null)
                    rend.material.color = WifiColorMap.ColorFromRssiContinuous(displayRssi, 1f);
            }

            if (labelByKey.TryGetValue(key, out var label) && label != null)
                label.text = $"{displayRssi} dBm";
        }
    }

    public void ResetSamplesToOriginalMeasurements()
    {
        for (int i = 0; i < samples.Count; i++)
            samples[i].ResetToOriginal();

        rssiByKey.Clear();
        for (int i = 0; i < samples.Count; i++)
            rssiByKey[samples[i].k] = samples[i].GetDisplayRssi();

        RefreshVisualsFromCurrentSamples();
        ComputeHeatmapGlobal();
    }

    public void ClearSession()
    {
        samples.Clear();
        rssiByKey.Clear();

        foreach (var kv in pointByKey) Destroy(kv.Value);
        foreach (var kv in labelByKey) Destroy(kv.Value);

        pointByKey.Clear();
        labelByKey.Clear();

        ClearHeatmapTexture();
        Debug.Log("🧹 Sesión limpiada");
    }

    public void SetHeatmapVisible(bool on)
    {
        heatmapVisible = on;
        if (surfaceRenderer != null) surfaceRenderer.enabled = on;
    }

    public void SetPointsVisible(bool on)
    {
        pointsVisible = on;
        foreach (var kv in pointByKey)
            if (kv.Value != null) kv.Value.SetActive(on);
    }

    public void SetLabelsVisible(bool on)
    {
        labelsVisible = on;
        foreach (var kv in labelByKey)
            if (kv.Value != null) kv.Value.gameObject.SetActive(on);
    }

    void ClearHeatmapTexture()
    {
        int N = surfaceResolution;
        for (int i = 0; i < N * N; i++)
            pixels[i] = new Color(0, 0, 0, 0);

        heatTex.SetPixels(pixels);
        heatTex.Apply();

        if (surfaceRenderer != null)
            surfaceRenderer.enabled = heatmapVisible;
    }

    public List<WifiSample> GetSamples() => samples;

    public List<WifiSample> GetSamplesCopy()
    {
        return new List<WifiSample>(samples);
    }

    public void LoadSamplesFromList(List<WifiSample> loadedSamples)
    {
        ClearSession();

        for (int i = 0; i < loadedSamples.Count; i++)
        {
            AddSample(loadedSamples[i]);
        }

        //ComputeHeatmapGlobal();
    }
}