using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class TMPInputFieldFocusOnClick : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TMP_InputField inputField;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (inputField == null)
            return;

        inputField.Select();
        inputField.ActivateInputField();
        Debug.Log("[TMPInputFieldFocusOnClick] Input selected.");
    }
}
