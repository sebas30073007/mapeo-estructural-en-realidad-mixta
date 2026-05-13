using UnityEngine;
using UnityEngine.EventSystems;

public class ShowIpKeypadOnClick : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private RosbotIpKeypadController keypadController;

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"🖱️ Click en {gameObject.name}");
        
        if (keypadController == null)
        {
            Debug.LogError($"❌ No RosbotIpKeypadController asignado en {gameObject.name}");
            
            // Intento de búsqueda automática
            keypadController = FindObjectOfType<RosbotIpKeypadController>();
            
            if (keypadController == null)
            {
                Debug.LogError("❌ No se encontró RosbotIpKeypadController en la escena");
                return;
            }
            
            Debug.LogWarning("⚠️ Keypad encontrado automáticamente, pero deberías asignarlo en Inspector");
        }
        
        Debug.Log("✅ Abriendo keypad...");
        keypadController.ShowKeypad();
    }
}