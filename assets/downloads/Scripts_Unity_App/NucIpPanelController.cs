using UnityEngine;
using TMPro;

public class NucIpPanelController : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField ipInputField;
    public TMP_Text feedbackText;

    private void Start()
    {
        if (ipInputField != null && NucIpManager.Instance != null)
        {
            ipInputField.text = NucIpManager.Instance.GetIp();
        }
    }

    public void ApplyIp()
    {
        if (ipInputField == null)
        {
            Debug.LogWarning("[NucIpPanelController] No hay ipInputField asignado.");
            return;
        }

        string newIp = ipInputField.text.Trim();

        if (string.IsNullOrWhiteSpace(newIp))
        {
            SetFeedback("IP vacía");
            return;
        }

        if (NucIpManager.Instance == null)
        {
            Debug.LogError("[NucIpPanelController] No existe NucIpManager en escena.");
            SetFeedback("No NucIpManager");
            return;
        }

        NucIpManager.Instance.SetIp(newIp);
        SetFeedback("IP aplicada: " + newIp);
    }

    public void ResetInputToSavedIp()
    {
        if (ipInputField != null && NucIpManager.Instance != null)
        {
            ipInputField.text = NucIpManager.Instance.GetIp();
            SetFeedback("IP restaurada");
        }
    }

    private void SetFeedback(string msg)
    {
        Debug.Log("[NucIpPanelController] " + msg);

        if (feedbackText != null)
            feedbackText.text = msg;
    }
}