using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class TMPInputFocusHelper : MonoBehaviour, IPointerClickHandler
{
    public TMP_InputField inputField;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (inputField == null) return;

        inputField.Select();
        inputField.ActivateInputField();

        Debug.Log("[TMPInputFocusHelper] Input seleccionado.");
    }
}