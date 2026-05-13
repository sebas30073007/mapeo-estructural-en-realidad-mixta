using UnityEngine;
using TMPro;

public class IpKeypadController : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField targetInputField;   // tu TextField real
    public TMP_Text displayLabel;             // tu Title
    public NucIpPanelController ipPanelController;

    [Header("Keypad Root")]
    public GameObject keypadRoot;             // panel/objeto raíz del keypad

    [Header("Placement")]
    public Transform followTarget;            // normalmente Main Camera / CenterEyeAnchor
    public float showDistance = 0.55f;
    public float verticalOffset = -0.05f;
    public bool faceUser = true;

    [Header("Behavior")]
    public string currentValue = "";
    public int maxLength = 15; // 255.255.255.255
    public bool loadInitialValueFromSavedIp = true;
    public string emptyDisplayText = "_";

    private void Start()
    {
        if (loadInitialValueFromSavedIp && NucIpManager.Instance != null)
        {
            currentValue = NucIpManager.Instance.GetIp();
        }
        else if (targetInputField != null && !string.IsNullOrEmpty(targetInputField.text))
        {
            currentValue = targetInputField.text;
        }

        SyncToInputField();
        RefreshDisplay();
        ForceLeftToRight();

        if (keypadRoot != null)
            keypadRoot.SetActive(false);
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
        currentValue = "";
        RefreshDisplay();
        SyncToInputField();
    }

    public void PressApply()
    {
        SyncToInputField();

        if (ipPanelController != null)
        {
            ipPanelController.ApplyIp();
        }
        else
        {
            Debug.LogWarning("[IpKeypadController] No hay NucIpPanelController asignado.");
        }

        HideKeypad();
    }

    public void ShowKeypad()
    {
        if (keypadRoot == null)
        {
            Debug.LogWarning("[IpKeypadController] No hay keypadRoot asignado.");
            return;
        }

        // Refresca desde IP guardada o del input
        if (targetInputField != null && !string.IsNullOrEmpty(targetInputField.text))
            currentValue = targetInputField.text;
        else if (NucIpManager.Instance != null)
            currentValue = NucIpManager.Instance.GetIp();

        RefreshDisplay();
        SyncToInputField();
        ForceLeftToRight();

        PositionNearUser();

        keypadRoot.SetActive(true);
    }

    public void HideKeypad()
    {
        if (keypadRoot != null)
            keypadRoot.SetActive(false);
    }

    public void Append(string value)
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

    public void SetValue(string newValue)
    {
        currentValue = newValue ?? "";

        if (currentValue.Length > maxLength)
            currentValue = currentValue.Substring(0, maxLength);

        RefreshDisplay();
        SyncToInputField();
    }

    private void SyncToInputField()
    {
        if (targetInputField != null)
        {
            targetInputField.text = currentValue;
            targetInputField.caretPosition = targetInputField.text.Length;
        }

        ForceLeftToRight();
    }

    private void RefreshDisplay()
    {
        if (displayLabel != null)
        {
            displayLabel.isRightToLeftText = false;
            displayLabel.alignment = TextAlignmentOptions.Left;

            displayLabel.text = string.IsNullOrEmpty(currentValue) ? emptyDisplayText : currentValue;
        }
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

            if (targetInputField.placeholder is TMP_Text ph)
            {
                ph.isRightToLeftText = false;
                ph.alignment = TextAlignmentOptions.Left;
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

        Transform t = keypadRoot.transform;

        Vector3 forwardFlat = followTarget.forward;
        forwardFlat.y = 0f;
        if (forwardFlat.sqrMagnitude < 0.0001f)
            forwardFlat = followTarget.forward;

        forwardFlat.Normalize();

        t.position = followTarget.position + forwardFlat * showDistance + Vector3.up * verticalOffset;

        if (faceUser)
        {
            Vector3 lookDir = t.position - followTarget.position;
            lookDir.y = 0f;

            if (lookDir.sqrMagnitude > 0.0001f)
                t.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
        }
    }
}