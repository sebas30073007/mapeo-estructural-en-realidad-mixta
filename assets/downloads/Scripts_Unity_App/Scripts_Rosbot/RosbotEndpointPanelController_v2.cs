using TMPro;
using UnityEngine;

public class RosbotEndpointPanelController_v2 : MonoBehaviour
{
    [Header("UI")]
    private TMP_InputField currentIpInputField;
    [SerializeField] private TMP_Text feedbackText;

    [Header("Behavior")]
    [SerializeField] private bool syncInputOnStart = true;

    private void Start()
    {
        if (syncInputOnStart)
            ResetInputToSavedIp();
    }

        public void SetTargetInputField(TMP_InputField field)
    {
        currentIpInputField = field;
        Debug.Log($"[RosbotEndpointPanelController_v2] Campo objetivo cambiado a: {field.name}");
    }
    
    private bool ValidateReferences()
    {
        if (RosbotEndpointManager.Instance == null)
        {
            SetFeedback("Missing RosbotEndpointManager.");
            Debug.LogError("[RosbotEndpointPanelController_v2] RosbotEndpointManager not found in scene.");
            return false;
        }
        
        if (currentIpInputField == null)
        {
            SetFeedback("No IP input assigned.");
            Debug.LogWarning("[RosbotEndpointPanelController_v2] No currentIpInputField assigned.");
            return false;
        }
        
        return true;
    }

    public void ApplyIp()
    {
        if (!ValidateReferences())
            return;
        
        string newIp = currentIpInputField.text.Trim();
        
        if (!RosbotEndpointManager.Instance.TrySetIp(newIp))
        {
            SetFeedback("Invalid IP address.");
            return;
        }
        
        string appliedIp = RosbotEndpointManager.Instance.GetIp();
        currentIpInputField.text = appliedIp;
        
        SetFeedback($"IP applied: {appliedIp}");
    }

    public void ResetInputToSavedIp()
    {
        if (!ValidateReferences())
            return;
        
        currentIpInputField.text = RosbotEndpointManager.Instance.GetIp();
        SetFeedback("Saved IP restored.");
    }

    public void ResetEndpointToDefault()
    {
        if (RosbotEndpointManager.Instance == null)
        {
            SetFeedback("Missing RosbotEndpointManager.");
            return;
        }

        RosbotEndpointManager.Instance.ResetToDefault();
        ResetInputToSavedIp();
    }

    private void SetFeedback(string message)
    {
        Debug.Log($"[RosbotEndpointPanelController_v2] {message}");

        if (feedbackText != null)
            feedbackText.text = message;
    }
}
