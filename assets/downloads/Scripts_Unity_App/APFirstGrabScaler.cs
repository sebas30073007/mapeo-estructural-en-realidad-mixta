using UnityEngine;

public class APFirstGrabScaler : MonoBehaviour
{
    public ControlsPanelUI controlsPanelUI;
    public MonoBehaviour apHandGrabInteractable;

    private bool wasGrabbedLastFrame = false;

    void Update()
    {
        if (controlsPanelUI == null || apHandGrabInteractable == null)
            return;

        // Detectar si el interactable está actualmente seleccionado/grabbed
        // NOTA: esto depende del tipo exacto del componente.
        // Aquí usamos una aproximación por reflection para no depender del tipo concreto.
        bool isGrabbedNow = GetIsGrabbed(apHandGrabInteractable);

        // primer grab
        if (isGrabbedNow && !wasGrabbedLastFrame)
        {
            controlsPanelUI.ScaleAPToMapNow();
        }

        wasGrabbedLastFrame = isGrabbedNow;
    }

    bool GetIsGrabbed(MonoBehaviour interactable)
    {
        var type = interactable.GetType();

        // probar propiedad "State"
        var stateProp = type.GetProperty("State");
        if (stateProp != null)
        {
            object state = stateProp.GetValue(interactable);
            if (state != null && state.ToString().ToLower().Contains("select"))
                return true;
        }

        // probar propiedad "IsSelected"
        var isSelectedProp = type.GetProperty("IsSelected");
        if (isSelectedProp != null)
        {
            object value = isSelectedProp.GetValue(interactable);
            if (value is bool b) return b;
        }

        return false;
    }
}