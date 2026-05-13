using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class MapBoundsAutoCollider : MonoBehaviour
{
    public Transform contentRoot; // asigna MapRoot o un hijo que contenga el mapa
    public float padding = 0.05f;

    BoxCollider col;

    void Awake()
    {
        col = GetComponent<BoxCollider>();
        if (contentRoot == null) contentRoot = transform;
    }

    public void Recompute()
    {
        var renderers = contentRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        bool firstFound = false;
        Bounds b = new Bounds();

        foreach (var r in renderers)
        {
            // EXCLUSIÓN CRÍTICA: Ignoramos el objeto que se llame "VisualBounds" 
            // o cualquier objeto que no sea parte de los datos reales.
            if (r.gameObject.name == "VisualBounds") continue;

            if (!firstFound)
            {
                b = r.bounds;
                firstFound = true;
            }
            else
            {
                b.Encapsulate(r.bounds);
            }
        }

        if (!firstFound) return; // No se encontraron datos válidos

        // El resto del proceso de conversión a local y asignación al colisionador sigue igual...
        Vector3 centerLocal = transform.InverseTransformPoint(b.center);
        Vector3 sizeWorld = b.size;
        Vector3 ls = transform.lossyScale;
        Vector3 sizeLocal = new Vector3(
            sizeWorld.x / Mathf.Max(ls.x, 1e-6f),
            sizeWorld.y / Mathf.Max(ls.y, 1e-6f),
            sizeWorld.z / Mathf.Max(ls.z, 1e-6f)
        );

        sizeLocal += Vector3.one * padding;
        col.center = centerLocal;
        col.size = sizeLocal;
    }
}