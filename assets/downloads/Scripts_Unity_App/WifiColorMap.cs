using UnityEngine;

public static class WifiColorMap
{
    // Map RSSI to 0..1
    public static float Normalize(int rssi)
    {
        // Clamp to typical range
        // -30 (best) -> 1, -90 (worst) -> 0
        float t = Mathf.InverseLerp(-90f, -30f, rssi);
        return Mathf.Clamp01(t);
    }

    // Continuous gradient: red -> orange -> yellow -> green
    public static Color ColorFromRssiContinuous(int rssi, float alpha = 0.45f)
    {
        float t = Normalize(rssi);

        Color c;
        if (t < 0.33f)
        {
            // red -> orange
            float u = t / 0.33f;
            c = Color.Lerp(new Color(0.9f, 0.1f, 0.1f), new Color(1.0f, 0.5f, 0.1f), u);
        }
        else if (t < 0.66f)
        {
            // orange -> yellow
            float u = (t - 0.33f) / 0.33f;
            c = Color.Lerp(new Color(1.0f, 0.5f, 0.1f), new Color(0.95f, 0.95f, 0.1f), u);
        }
        else
        {
            // yellow -> green
            float u = (t - 0.66f) / 0.34f;
            c = Color.Lerp(new Color(0.95f, 0.95f, 0.1f), new Color(0.1f, 0.9f, 0.1f), u);
        }

        c.a = alpha;
        return c;
    }
}