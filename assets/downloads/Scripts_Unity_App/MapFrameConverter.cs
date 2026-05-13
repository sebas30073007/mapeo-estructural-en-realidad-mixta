using UnityEngine;

public class MapFrameConverter : MonoBehaviour
{
    [Header("Map frame data")]
    [SerializeField] private float mapOriginX = 0f;
    [SerializeField] private float mapOriginY = 0f;
    [SerializeField] private float mapResolution = 0.05f;
    [SerializeField] private int mapWidth = 0;
    [SerializeField] private int mapHeight = 0;

    public float MapOriginX => mapOriginX;
    public float MapOriginY => mapOriginY;
    public float MapResolution => mapResolution;
    public int MapWidth => mapWidth;
    public int MapHeight => mapHeight;

    public float MapWidthMeters => mapWidth * mapResolution;
    public float MapHeightMeters => mapHeight * mapResolution;

    public void SetMapInfo(float originX, float originY, float resolution, int width, int height)
    {
        mapOriginX = originX;
        mapOriginY = originY;
        mapResolution = resolution;
        mapWidth = width;
        mapHeight = height;

        Debug.Log(
            $"[MapFrameConverter] origin=({mapOriginX:F2}, {mapOriginY:F2}) " +
            $"resolution={mapResolution:F3} size=({MapWidthMeters:F2}m, {MapHeightMeters:F2}m)"
        );
    }

    /// <summary>
    /// Convierte coordenadas globales del frame 'map' a coordenadas locales de Unity.
    /// map_x -> Unity X
    /// map_y -> Unity Z
    /// Unity Y = altura visual
    /// </summary>
    public Vector3 MapToLocal(float mapX, float mapY, float yHeight = 0f)
    {
        float localX = mapX - mapOriginX;
        float localZ = mapY - mapOriginY;
        return new Vector3(localX, yHeight, localZ);
    }

    /// <summary>
    /// Igual que MapToLocal, pero solo en 2D (X,Z).
    /// </summary>
    public Vector2 MapToLocal2D(float mapX, float mapY)
    {
        float localX = mapX - mapOriginX;
        float localZ = mapY - mapOriginY;
        return new Vector2(localX, localZ);
    }

    /// <summary>
    /// Convierte coordenadas locales de Unity de vuelta al frame 'map'.
    /// Unity X -> map_x
    /// Unity Z -> map_y
    /// </summary>
    public Vector2 LocalToMap(float localX, float localZ)
    {
        float mapX = localX + mapOriginX;
        float mapY = localZ + mapOriginY;
        return new Vector2(mapX, mapY);
    }

    /// <summary>
    /// Devuelve el centro del mapa en coordenadas locales.
    /// Si el origen local es la esquina inferior izquierda del mapa, el centro es (width/2, height/2).
    /// </summary>
    public Vector3 GetLocalMapCenter(float yHeight = 0f)
    {
        return new Vector3(MapWidthMeters * 0.5f, yHeight, MapHeightMeters * 0.5f);
    }

    /// <summary>
    /// Comprueba si un punto del frame 'map' cae dentro de los límites del mapa actual.
    /// </summary>
    public bool IsInsideMap(float mapX, float mapY)
    {
        float localX = mapX - mapOriginX;
        float localZ = mapY - mapOriginY;

        return localX >= 0f && localX <= MapWidthMeters &&
               localZ >= 0f && localZ <= MapHeightMeters;
    }

    /// <summary>
    /// Comprueba si un punto local de Unity cae dentro de los límites del mapa actual.
    /// </summary>
    public bool IsInsideLocalMap(float localX, float localZ)
    {
        return localX >= 0f && localX <= MapWidthMeters &&
               localZ >= 0f && localZ <= MapHeightMeters;
    }
}