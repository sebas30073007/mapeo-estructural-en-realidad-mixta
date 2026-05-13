using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RosbotLidarModePanelController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RosbotLidarGridReceiver lidarReceiver;
    [SerializeField] private TMP_Dropdown tmpDropdown;
    [SerializeField] private Toggle powerToggle;

    [Header("Behavior")]
    [SerializeField] private bool syncDropdownOnStart = true;
    [SerializeField] private int startupIndex = 0;
    [SerializeField] private bool startupPowerOn = true;

    private void Start()
    {
        startupIndex = Mathf.Clamp(startupIndex, 0, 2);

        if (syncDropdownOnStart && tmpDropdown != null)
        {
            tmpDropdown.SetValueWithoutNotify(startupIndex);
            tmpDropdown.RefreshShownValue();
        }

        if (powerToggle != null)
            powerToggle.SetIsOnWithoutNotify(startupPowerOn);

        ApplyCurrentLidarState();
    }

    public void OnDropdownValueChanged(int index)
    {
        startupIndex = Mathf.Clamp(index, 0, 2);
        if (IsLidarPowerOn())
            ApplySelectedMode();
    }

    public void OnLidarPowerToggleChanged(bool isOn)
    {
        if (lidarReceiver == null)
            return;

        if (isOn)
            ApplySelectedMode();
        else
            lidarReceiver.SetOff();
    }

    public void SelectDetail()
    {
        SetDropdownValue(0);
        if (IsLidarPowerOn())
            ApplySelectedMode();
    }

    public void SelectMedium()
    {
        SetDropdownValue(1);
        if (IsLidarPowerOn())
            ApplySelectedMode();
    }

    public void SelectPanorama()
    {
        SetDropdownValue(2);
        if (IsLidarPowerOn())
            ApplySelectedMode();
    }

    private void ApplyCurrentLidarState()
    {
        if (lidarReceiver == null)
            return;

        if (IsLidarPowerOn())
            ApplySelectedMode();
        else
            lidarReceiver.SetOff();
    }

    private void ApplySelectedMode()
    {
        if (lidarReceiver == null)
            return;

        int index = tmpDropdown != null ? Mathf.Clamp(tmpDropdown.value, 0, 2) : startupIndex;

        switch (index)
        {
            case 0: lidarReceiver.SetDetail(); break;
            case 1: lidarReceiver.SetMedium(); break;
            case 2: lidarReceiver.SetPanorama(); break;
            default: lidarReceiver.SetDetail(); break;
        }
    }

    private bool IsLidarPowerOn()
    {
        return powerToggle == null || powerToggle.isOn;
    }

    private void SetDropdownValue(int index)
    {
        startupIndex = Mathf.Clamp(index, 0, 2);
        if (tmpDropdown == null)
            return;

        tmpDropdown.SetValueWithoutNotify(startupIndex);
        tmpDropdown.RefreshShownValue();
    }
}
