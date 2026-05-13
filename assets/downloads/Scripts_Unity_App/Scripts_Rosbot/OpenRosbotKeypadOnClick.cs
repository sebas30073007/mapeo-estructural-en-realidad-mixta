using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class OpenRosbotKeypadOnClick : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private RosbotIpKeypadController keypadController;
    [SerializeField] private TMP_Text targetTextField;      // El texto visual
    [SerializeField] private TMP_InputField targetInputField; // ✅ NUEVO: El input field asociado

    public void OnPointerClick(PointerEventData eventData)
    {
        if (keypadController != null && targetTextField != null)
        {
            Debug.Log($"🖱️ Click en {gameObject.name}");
            keypadController.ShowKeypadForField(targetTextField, targetInputField);
        }
    }
}