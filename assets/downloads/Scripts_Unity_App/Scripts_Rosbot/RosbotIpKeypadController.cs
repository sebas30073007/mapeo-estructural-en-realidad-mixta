using TMPro;
using UnityEngine;

public class RosbotIpKeypadController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField targetInputField;
    private TMP_Text currentTargetTextField;
    private TMP_InputField currentTargetInputField;
    [SerializeField] private TMP_Text displayLabel;
    [SerializeField] private RosbotEndpointPanelController_v2 endpointPanelController;

    [Header("Keypad Root")]
    [SerializeField] private GameObject keypadRoot;

    [Header("Placement")]
    [SerializeField] private Transform followTarget;
    [SerializeField] private float showDistance = 0.55f;
    [SerializeField] private float verticalOffset = -0.05f;
    [SerializeField] private bool faceUser = true;

    [Header("Behavior")]
    [SerializeField] private string currentValue = "";
    [SerializeField] private int maxLength = 15;
    [SerializeField] private bool loadInitialValueFromSavedIp = true;
    [SerializeField] private string emptyDisplayText = "_";

    private void Start()
    {
        InitializeValue();
        SyncToInputField();
        RefreshDisplay();
        ForceLeftToRight();

        if (keypadRoot != null)
        {
            keypadRoot.SetActive(false);
            Debug.Log($"✅ {keypadRoot.name} inicialmente oculto");
        }
    }

    public void Press0() => Append("0");
    public void Press1() => Append("1");
    public void Press2() => Append("2");
    public void Press3() => Append("3");
    public void Press4() => Append("4");
    public void Press5() => Append("5");
    public void Press6() => Append("6");
    public void Press7() => Append("7");
    public void Press8() => Append("8");
    public void Press9() => Append("9");
    public void PressDot() => Append(".");

    public void PressDelete()
    {
        if (string.IsNullOrEmpty(currentValue))
            return;

        currentValue = currentValue.Substring(0, currentValue.Length - 1);
        RefreshDisplay();
        SyncToInputField();
    }

    public void PressClear()
    {
        currentValue = string.Empty;
        RefreshDisplay();
        SyncToInputField();
    }

    public void PressApply()
    {
        Debug.Log("✅ Apply presionado");
        
        // Actualizar el TMP_Text visual
        if (currentTargetTextField != null)
        {
            currentTargetTextField.text = currentValue;
        }
        
        // Actualizar el TMP_InputField (si existe)
        if (currentTargetInputField != null)
        {
            currentTargetInputField.text = currentValue;
        }
        
        // Aplicar IP y reconectar
        if (endpointPanelController != null)
        {
            endpointPanelController.ApplyIp();
        }
        
        HideKeypad();
    }
    
    public void ShowKeypad()
    {
        Debug.Log("🔓 ShowKeypad() llamado");
        
        // ✅ Usar la referencia asignada en Inspector
        if (keypadRoot == null)
        {
            Debug.LogError("❌ keypadRoot no asignado en Inspector");
            return;
        }
        
        keypadRoot.SetActive(true);
        
        RefreshFromBestSource();
        RefreshDisplay();
        SyncToInputField();
        ForceLeftToRight();
        PositionNearUser();
    }

    public void ShowKeypadForField(TMP_Text textField, TMP_InputField inputField = null)
    {
        Debug.Log($"🔓 ShowKeypad para campo: {textField.name}");
        
        currentTargetTextField = textField;
        currentTargetInputField = inputField; // ✅ Guardar referencia
        
        if (keypadRoot == null)
        {
            Debug.LogError("❌ keypadRoot no asignado");
            return;
        }
        
        // ✅ Informar al EndpointPanelController qué campo actualizar
        if (endpointPanelController != null && inputField != null)
        {
            endpointPanelController.SetTargetInputField(inputField);
        }
        
        // Cargar valor actual
        if (currentTargetTextField != null)
        {
            currentValue = currentTargetTextField.text;
        }
        
        keypadRoot.SetActive(true);
        RefreshDisplay();
        ForceLeftToRight();
        PositionNearUser();
    }


    public void HideKeypad()
    {
        Debug.Log("🔒 HideKeypad() llamado");
        
        if (keypadRoot != null)
        {
            Debug.Log($"✅ Desactivando: {keypadRoot.name}");
            keypadRoot.SetActive(false);
        }
    }

    public void SetValue(string newValue)
    {
        currentValue = string.IsNullOrEmpty(newValue) ? string.Empty : newValue.Trim();

        if (currentValue.Length > maxLength)
            currentValue = currentValue.Substring(0, maxLength);

        RefreshDisplay();
        SyncToInputField();
    }

    private void Append(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        if (currentValue.Length >= maxLength)
            return;

        if (value == ".")
        {
            if (currentValue.Length == 0)
                return;

            if (currentValue.EndsWith("."))
                return;
        }

        currentValue += value;
        RefreshDisplay();
        SyncToInputField();
    }

    private void InitializeValue()
    {
        if (loadInitialValueFromSavedIp && RosbotEndpointManager.Instance != null)
        {
            currentValue = RosbotEndpointManager.Instance.GetIp();
            return;
        }

        if (targetInputField != null && !string.IsNullOrEmpty(targetInputField.text))
            currentValue = targetInputField.text.Trim();
    }

    private void RefreshFromBestSource()
    {
        if (targetInputField != null && !string.IsNullOrEmpty(targetInputField.text))
        {
            currentValue = targetInputField.text.Trim();
            return;
        }

        if (RosbotEndpointManager.Instance != null)
            currentValue = RosbotEndpointManager.Instance.GetIp();
    }

    private void SyncToInputField()
    {
        if (targetInputField == null)
            return;

        targetInputField.text = currentValue;
        targetInputField.caretPosition = targetInputField.text.Length;
        ForceLeftToRight();
    }

    private void RefreshDisplay()
    {
        if (displayLabel == null)
            return;

        displayLabel.isRightToLeftText = false;
        displayLabel.alignment = TextAlignmentOptions.Left;
        displayLabel.text = string.IsNullOrEmpty(currentValue) ? emptyDisplayText : currentValue;
    }

    private void ForceLeftToRight()
    {
        if (targetInputField != null)
        {
            targetInputField.isRichTextEditingAllowed = false;

            if (targetInputField.textComponent != null)
            {
                targetInputField.textComponent.isRightToLeftText = false;
                targetInputField.textComponent.alignment = TextAlignmentOptions.Left;
            }

            if (targetInputField.placeholder is TMP_Text placeholderText)
            {
                placeholderText.isRightToLeftText = false;
                placeholderText.alignment = TextAlignmentOptions.Left;
            }
        }

        if (displayLabel != null)
        {
            displayLabel.isRightToLeftText = false;
            displayLabel.alignment = TextAlignmentOptions.Left;
        }
    }

    private void PositionNearUser()
    {
        if (keypadRoot == null || followTarget == null)
            return;

        Transform keypadTransform = keypadRoot.transform;

        Vector3 forwardFlat = followTarget.forward;
        forwardFlat.y = 0f;
        if (forwardFlat.sqrMagnitude < 0.0001f)
            forwardFlat = Vector3.forward;

        forwardFlat.Normalize();
        keypadTransform.position = followTarget.position + forwardFlat * showDistance + Vector3.up * verticalOffset;

        if (!faceUser)
            return;

        Vector3 lookDirection = keypadTransform.position - followTarget.position;
        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude > 0.0001f)
            keypadTransform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
    }
}
