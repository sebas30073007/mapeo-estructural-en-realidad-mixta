using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ControlsPanelUI : MonoBehaviour
{
    [Header("Core References")]
    public HeatmapGridRenderer heatmap;
    public GameObject apMarker; // ← Arrastra el APMarker existente de la escena
    public Transform cameraTransform;
    public SessionPersistenceManager sessionManager;

    [Header("Heatmap Controls")]
    public Button btnComputeHeatmap;
    public Toggle tglShowHeatmap;

    [Header("Samples Controls")]
    public Toggle tglShowPoints;
    public Toggle tglShowLabels;
    public Button btnClearSession;

    [Header("AP What-if Controls")]
    public Toggle tglShowAP;
    public Button btnAPPlacement;
    public TMP_Text txtAPPlacement;

    [Header("AP Configuration")]
    public float apSpawnDistance = 1f;

    [Header("AP Scaling")]
    public Vector3 apPreviewScale = new Vector3(1f, 2f, 1f);
    private bool apScaledToMap = false;

    [Header("Map Manipulation")]
    public GameObject visualBounds;
    public MonoBehaviour handGrabInteractable;
    public MonoBehaviour rayGrabInteractable;
    public Button btnToggleMapEdit;

    [Header("Map Frame")]
    public MapFrameConverter mapFrame;

    [Header("AP Manipulation")] 
    public MonoBehaviour apHandGrabInteractable;
    public MonoBehaviour apRayGrabInteractable;

    [Header("WiFi Propagation Model")]
    public WiFiPropagationModel propagationModel;
    public Button btnSetInitialAP;
    public Button btnRecalculateHeatmap;

    [Header("Action Buttons")]
    public Button btnResetMeasurements;
    public Button btnResetAP;

    // Estados
    private bool isMapEditMode = false;
    private bool isAPPlacementMode = false;
    private bool apExists = false;
    private Vector3 apOriginalPosition; // Para guardar posición original

    void Start()
    {
        // Auto-encontrar cámara
        if (cameraTransform == null)
        {
            GameObject cameraRig = GameObject.Find("OVRCameraRig");
            if (cameraRig != null)
            {
                cameraTransform = cameraRig.transform.Find("TrackingSpace/CenterEyeAnchor");
            }
        }

        // --- Heatmap ---
        if (btnComputeHeatmap != null)
            btnComputeHeatmap.onClick.AddListener(() => heatmap.ComputeHeatmapGlobal());
        if (tglShowHeatmap != null)
            tglShowHeatmap.onValueChanged.AddListener(on => heatmap.SetHeatmapVisible(on));

        // --- Samples ---
        if (tglShowPoints != null)
            tglShowPoints.onValueChanged.AddListener(on => heatmap.SetPointsVisible(on));
        if (tglShowLabels != null)
            tglShowLabels.onValueChanged.AddListener(on => heatmap.SetLabelsVisible(on));
        if (btnClearSession != null)
            btnClearSession.onClick.AddListener(() => sessionManager.ClearFullSession());

        // --- AP ---
        if (tglShowAP != null)
            tglShowAP.onValueChanged.AddListener(OnShowAP);
        if (btnAPPlacement != null)
            btnAPPlacement.onClick.AddListener(ToggleAPPlacement);

        // --- Map ---
        if (btnToggleMapEdit != null)
            btnToggleMapEdit.onClick.AddListener(ToggleMapManipulation);

        // --- WiFi Propagation Model ---
        if (btnSetInitialAP != null)
            btnSetInitialAP.onClick.AddListener(OnSetInitialAP);

        if (btnRecalculateHeatmap != null)
            btnRecalculateHeatmap.onClick.AddListener(OnRecalculateHeatmap);

        // Reset
        if (btnResetMeasurements != null)
            btnResetMeasurements.onClick.AddListener(() => heatmap.ResetSamplesToOriginalMeasurements());
        
        if (btnResetAP != null)
            btnResetAP.onClick.AddListener(OnResetAP);
        
        // btnClearSession ya debería estar conectado, verifica:
        if (btnClearSession != null)
            btnClearSession.onClick.AddListener(() => sessionManager.ClearFullSession());


        // Estados iniciales
        apExists = false;
        if (apMarker != null)
        {
            apOriginalPosition = apMarker.transform.localPosition;
            apMarker.SetActive(false); // Ocultar al inicio
            SetAPInteractable(false);

            // Si el toggle está marcado, desmarcarlo
            if (tglShowAP != null && tglShowAP.isOn)
            {
                tglShowAP.SetIsOnWithoutNotify(false);
            }
        }
        
        SetMapEditMode(false);
        UpdateAPButtonText();
    }

    // ==================== AP MANAGEMENT ====================
    
    void ToggleAPPlacement()
    {
        if (!apExists)
        {
            // Primera vez: posicionar AP frente al usuario
            CreateAP();
        }
        else
        {
            // Toggle modo placement
            isAPPlacementMode = !isAPPlacementMode;
            SetAPInteractable(isAPPlacementMode);
        }
        
        UpdateAPButtonText();
    }

    void CreateAP()
    {
        if (apMarker == null)
        {
            Debug.LogError("❌ No hay APMarker asignado en el Inspector");
            return;
        }

        if (cameraTransform == null)
        {
            Debug.LogError("❌ No se encontró CenterEyeAnchor");
            return;
        }

        if (apExists)
        {
            Debug.LogWarning("⚠️ El AP ya fue posicionado previamente");
            isAPPlacementMode = true;
            SetAPInteractable(true);
            UpdateAPButtonText();
            return;
        }

        Vector3 worldSpawnPos = cameraTransform.position + cameraTransform.forward * apSpawnDistance;

        Transform mapRoot = apMarker.transform.parent;
        apMarker.transform.SetParent(null, true);

        apMarker.transform.position = worldSpawnPos;

        if (mapRoot != null)
        {
            apMarker.transform.SetParent(mapRoot, true);

            // IMPORTANTE: al inicio solo preview scale
            apMarker.transform.localScale = apPreviewScale;
        }

        apMarker.SetActive(true);
        apExists = true;
        isAPPlacementMode = true;
        apScaledToMap = false;

        SetAPInteractable(true);

        if (tglShowAP != null)
        {
            tglShowAP.isOn = true;
        }

        Debug.Log($"✅ AP reposicionado en {worldSpawnPos}");
        Debug.Log($"📍 Posición local en MapRoot: {apMarker.transform.localPosition}");
        Debug.Log($"📏 Escala preview: {apMarker.transform.localScale}");
    }
        
    // Ajustar escala del AP
    void AdjustAPScale(Transform mapRoot)
    {
        // Tamaño deseado en metros del mundo real
        float targetSizeMeters = 0.15f; // 15 cm
        
        // Obtener la escala total del MapRoot (lossyScale incluye toda la jerarquía)
        float mapScale = mapRoot.lossyScale.x; // Asumiendo escala uniforme
        
        if (mapScale < 0.0001f)
        {
            mapScale = 0.005f; // Valor por defecto
        }
        
        // Calcular la escala local necesaria para que el AP mida 20cm en el mundo
        // Si MapRoot tiene escala 0.005:
        // Para que el AP sea 0.2m en mundo real, necesita escala local = 0.2 / 0.005 = 40
        float localScaleX = targetSizeMeters / mapScale;
        float localScaleY = (targetSizeMeters * 0.25f) / mapScale; // Altura mitad (forma de libro)
        float localScaleZ = (targetSizeMeters * 0.7f) / mapScale;
        
        apMarker.transform.localScale = new Vector3(localScaleX, localScaleY, localScaleZ);
        Debug.Log($"📏 AdjustAPScale -> mapScale={mapScale}, localScale={apMarker.transform.localScale}");
    }


    void SetAPInteractable(bool interactable)
    {
        if (apMarker == null) return;

        // SOLUCIÓN DIRECTA: Deshabilitar los componentes específicos (igual que el mapa)
        if (apHandGrabInteractable != null)
        {
            apHandGrabInteractable.enabled = interactable;
            Debug.Log($"🔒 AP HandGrabInteractable.enabled = {interactable}");
        }

        if (apRayGrabInteractable != null)
        {
            apRayGrabInteractable.enabled = interactable;
            Debug.Log($"🔒 AP RayGrabInteractable.enabled = {interactable}");
        }

        // También buscar otros componentes por si acaso
        var components = apMarker.GetComponents<MonoBehaviour>();
        foreach (var component in components)
        {
            if (component == null) continue;
            
            string typeName = component.GetType().Name;
            
            // Deshabilitar componentes adicionales de ISDK
            if (typeName == "Grabbable" || 
                typeName.Contains("Transformer"))
            {
                component.enabled = interactable;
                Debug.Log($"🔒 {typeName}.enabled = {interactable}");
            }
        }

        // Feedback visual
        /*Renderer renderer = apMarker.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            Color color = interactable ?
                new Color(0.3f, 0.7f, 1f, 1f) : 
                new Color(0.2f, 0.4f, 0.6f, 0.8f);
            
            if (renderer.material.HasProperty("_Color"))
            {
                renderer.material.color = color;
            }
        }*/

        Debug.Log($"🔒 AP interactuable: {interactable}");
    }

    void OnShowAP(bool visible)
    {
        if (apMarker == null)
        {
            Debug.LogWarning("⚠️ No hay APMarker asignado");
            return;
        }

        // Solo cambiar visibilidad, NO crear ni reposicionar
        apMarker.SetActive(visible);
        
        // Si se está mostrando y NO existe formalmente, avisar
        if (visible && !apExists)
        {
            Debug.LogWarning("⚠️ Mostrando AP pero no ha sido posicionado con 'AP Placement'");
        }
        
        Debug.Log($"👁️ AP visibilidad: {visible}");
    }

    void UpdateAPButtonText()
    {
        if (txtAPPlacement == null) return;

        if (!apExists)
        {
            txtAPPlacement.text = "AP Placement";
        }
        else
        {
            txtAPPlacement.text = isAPPlacementMode ? "Lock AP" : "AP Placement";
        }
    }

    // ==================== MAP MANIPULATION ====================
    
    void ToggleMapManipulation()
    {
        isMapEditMode = !isMapEditMode;
        SetMapEditMode(isMapEditMode);
    }

    void SetMapEditMode(bool active)
    {
        if (visualBounds != null)
            visualBounds.SetActive(active);

        if (handGrabInteractable != null)
            handGrabInteractable.enabled = active;
        if (rayGrabInteractable != null)
            rayGrabInteractable.enabled = active;

        if (btnToggleMapEdit != null)
        {
            var txt = btnToggleMapEdit.GetComponentInChildren<TMP_Text>();
            if (txt != null)
                txt.text = active ? "Lock Map" : "Move Map";
        }

        if (active)
        {
            UpdateMapInteractivity();
        }
    }

    private void UpdateMapInteractivity()
    {
        var autoCol = heatmap.GetComponentInParent<MapBoundsAutoCollider>();
        if (autoCol != null)
        {
            autoCol.Recompute();

            var col = autoCol.GetComponent<BoxCollider>();
            if (visualBounds != null && col != null)
            {
                visualBounds.transform.localPosition = col.center;
                visualBounds.transform.localScale = col.size;
            }
        }
    }

    [ContextMenu("Debug AP Components")]
    void DebugAPComponents()
    {
        if (apMarker == null)
        {
            Debug.Log("❌ No hay AP asignado");
            return;
        }

        Debug.Log("=== COMPONENTES DEL AP ===");
        var components = apMarker.GetComponents<Component>();
        foreach (var comp in components)
        {
            Debug.Log($"- {comp.GetType().Name} (enabled: {(comp as MonoBehaviour)?.enabled})");
        }
    }

    // ==================== WIFI PROPAGATION MODEL ====================

    private float GetMapScaleCompensation()
    {
        if (apMarker == null) return 1f;
        
        Transform mapRoot = apMarker.transform.parent;
        if (mapRoot == null) return 1f;
        
        // Obtener la escala total acumulada del MapRoot
        float mapScale = mapRoot.lossyScale.x; // Asumiendo escala uniforme
        
        if (mapScale < 0.0001f)
        {
            return 1f;
        }
        
        // La compensación es el inverso de la escala
        float compensation = 1f / mapScale;
        
        return compensation;
    }

    void OnSetInitialAP()
    {
        if (apMarker == null)
        {
            return;
        }
        
        if (propagationModel == null)
        {
            return;
        }

        if (mapFrame == null)
        {
            Debug.LogError("❌ No hay MapFrameConverter asignado");
            return;
        }

        Vector3 apLocalPosition = apMarker.transform.localPosition;
        Vector2 apMap2D = mapFrame.LocalToMap(apLocalPosition.x, apLocalPosition.z);
        Vector3 apMapCoords = new Vector3(apMap2D.x, 0f, apMap2D.y);

        propagationModel.SetInitialAP(apMapCoords);
        Debug.Log($"📍 AP inicial en coords map: {apMapCoords}");
    }

    void OnRecalculateHeatmap()
    {
        if (apMarker == null)
        {
            return;
        }
        
        if (propagationModel == null)
        {
            return;
        }
        
        if (mapFrame == null)
        {
            Debug.LogError("❌ No hay MapFrameConverter asignado");
            return;
        }

        Vector3 apLocalPosition = apMarker.transform.localPosition;
        Vector2 apMap2D = mapFrame.LocalToMap(apLocalPosition.x, apLocalPosition.z);
        Vector3 apMapCoords = new Vector3(apMap2D.x, 0f, apMap2D.y);

        propagationModel.RecalculateWithNewAP(apMapCoords);
        Debug.Log($"🔄 AP candidato en coords map: {apMapCoords}");
    }

    void OnResetAP()
    {
        if (apMarker == null || !apExists) return;
        
        // Restaurar a posición original
        apMarker.transform.localPosition = apOriginalPosition;
        
        // Resetear muestras a valores originales
        heatmap.ResetSamplesToOriginalMeasurements();
        
        Debug.Log("🔄 AP y mediciones reseteadas");
    }

    public void ScaleAPToMapNow()
    {
        if (apMarker == null || apScaledToMap)
            return;

        Transform mapRoot = apMarker.transform.parent;
        if (mapRoot != null)
        {
            AdjustAPScale(mapRoot);
            apScaledToMap = true;
            Debug.Log("✅ AP escalado al tamaño real de maqueta en el primer grab.");
        }
    }

}