using System;

[Serializable]
public class WifiSample
{
    public string type;
    public double t;
    public string ssid;
    public string bssid;

    // Valor recibido originalmente
    public int rssi;

    public int row;
    public int col;
    public float x;
    public float y;
    public int k;

    // Nuevos campos para conservar medición base y simulación
    public int originalRssi;
    public int currentRssi;
    public bool hasSimulationOverride;
    public float map_origin_x;
    public float map_origin_y;

    // Inicializa la muestra al llegar desde UDP
    public void InitializeForRuntime()
    {
        originalRssi = rssi;
        currentRssi = rssi;
        hasSimulationOverride = false;
    }

    // Valor que debe usar el heatmap/render
    public int GetDisplayRssi()
    {
        return currentRssi;
    }

    // Restaurar valor real medido
    public void ResetToOriginal()
    {
        currentRssi = originalRssi;
        hasSimulationOverride = false;
    }

    // Aplicar valor recalculado/simulado
    public void ApplySimulatedRssi(int newRssi)
    {
        currentRssi = newRssi;
        hasSimulationOverride = true;
    }
}