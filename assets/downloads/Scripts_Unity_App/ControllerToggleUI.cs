using UnityEngine;

public class ControllerToggleUI : MonoBehaviour
{
    [Header("Settings")]
    public GameObject panelToToggle; // Arrastra aquí el Panel_Root
    
    [Tooltip("Botón X en el mando izquierdo")]
    public OVRInput.RawButton toggleButton = OVRInput.RawButton.X;

    [Header("Initial State")]
    public bool startActive = false;

    void Start()
    {
        if (panelToToggle != null)
        {
            panelToToggle.SetActive(startActive);
        }
    }

    void Update()
    {
        // Importante: OVRInput solo funciona si el visor detecta que lo tienes puesto (focus)
        if (OVRInput.GetDown(toggleButton))
        {
            TogglePanel();
        }
    }

    public void TogglePanel()
    {
        if (panelToToggle != null)
        {
            bool currentState = panelToToggle.activeSelf;
            panelToToggle.SetActive(!currentState);
        }
    }
}