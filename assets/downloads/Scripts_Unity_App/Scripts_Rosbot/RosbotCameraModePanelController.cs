using UnityEngine;
using UnityEngine.UI;

public class RosbotCameraModePanelController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RosbotVideoUdpReceiver videoReceiver;
    // ❌ ELIMINAR: [SerializeField] private TMP_Dropdown tmpDropdown;
    [SerializeField] private Toggle powerToggle;
    
    [Header("Behavior")]
    [SerializeField] private bool startupPowerOn = true;
    private int currentModeIndex = 0; // 0=Normal, 1=Pose, 2=Segment
    
    private void Start()
    {
        if (powerToggle != null)
            powerToggle.SetIsOnWithoutNotify(startupPowerOn);
        
        ApplyCurrentCameraState();
    }
    
    // Llamado por el Toggle.OnValueChanged
    public void OnCameraPowerToggleChanged(bool isOn)
    {
        if (videoReceiver == null)
        {
            Debug.LogWarning("[RosbotCameraModePanelController] videoReceiver no asignado");
            return;
        }
        
        if (isOn)
            ApplyModeByIndex(currentModeIndex);
        else
            videoReceiver.SetCameraOff();
    }
    
    // ✅ Métodos públicos llamados por los botones del dropdown
    public void SelectNormal()
    {
        currentModeIndex = 0;
        if (IsCameraPowerOn())
            videoReceiver.SetCameraNormal();
    }
    
    public void SelectPose()
    {
        currentModeIndex = 1;
        if (IsCameraPowerOn())
            videoReceiver.SetCameraPose();
    }
    
    public void SelectSegment()
    {
        currentModeIndex = 2;
        if (IsCameraPowerOn())
            videoReceiver.SetCameraSegment();
    }
    
    // Aplica el estado inicial al arrancar
    private void ApplyCurrentCameraState()
    {
        if (videoReceiver == null)
        {
            Debug.LogWarning("[RosbotCameraModePanelController] videoReceiver no asignado en Start");
            return;
        }
        
        if (IsCameraPowerOn())
            ApplyModeByIndex(currentModeIndex);
        else
            videoReceiver.SetCameraOff();
    }
    
    // Aplica un modo específico por índice
    private void ApplyModeByIndex(int index)
    {
        if (videoReceiver == null)
            return;
        
        switch (index)
        {
            case 0: 
                videoReceiver.SetCameraNormal(); 
                Debug.Log("[CameraPanel] Modo: Normal");
                break;
            case 1: 
                videoReceiver.SetCameraPose(); 
                Debug.Log("[CameraPanel] Modo: Pose");
                break;
            case 2: 
                videoReceiver.SetCameraSegment(); 
                Debug.Log("[CameraPanel] Modo: Segment");
                break;
            default: 
                videoReceiver.SetCameraNormal(); 
                break;
        }
    }
    
    // Verifica si el toggle de power está encendido
    private bool IsCameraPowerOn()
    {
        return powerToggle == null || powerToggle.isOn;
    }
}