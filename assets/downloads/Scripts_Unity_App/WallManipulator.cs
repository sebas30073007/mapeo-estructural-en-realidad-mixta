using UnityEngine;

public class WallManipulator : MonoBehaviour
{
    [Header("Referencias OVR")]
    public Transform rightHandAnchor;
    public Transform leftHandAnchor;
    
    [Header("Configuración")]
    public float rayDistance = 10f;
    public LayerMask interactableLayer = -1;
    
    [Header("Manipulación")]
    public float rotationSpeed = 100f;
    public float scaleSpeed = 1f;
    public float minScale = 0.1f;
    public float maxScale = 5f;
    
    [Header("Visual Feedback")]
    public LineRenderer rightRay;
    public LineRenderer leftRay;
    
    private bool isGrabbed = false;
    private Transform activeHand;
    private Vector3 grabOffset;
    private float initialScale;

    void Start()
    {
        // Auto-encontrar hand anchors si no están asignados
        if (rightHandAnchor == null || leftHandAnchor == null)
        {
            GameObject cameraRig = GameObject.Find("OVRCameraRig");
            if (cameraRig != null)
            {
                rightHandAnchor = cameraRig.transform.Find("TrackingSpace/RightHandAnchor");
                leftHandAnchor = cameraRig.transform.Find("TrackingSpace/LeftHandAnchor");
                Debug.Log("✅ Hand Anchors encontrados automáticamente");
            }
        }
        
        SetupRayVisuals();
    }

    void SetupRayVisuals()
    {
        // Crear LineRenderers para visualizar raycast
        if (rightHandAnchor != null && rightRay == null)
        {
            GameObject rayObj = new GameObject("RightRay");
            rayObj.transform.parent = rightHandAnchor;
            rightRay = rayObj.AddComponent<LineRenderer>();
            ConfigureLineRenderer(rightRay, Color.green);
        }
        
        if (leftHandAnchor != null && leftRay == null)
        {
            GameObject rayObj = new GameObject("LeftRay");
            rayObj.transform.parent = leftHandAnchor;
            leftRay = rayObj.AddComponent<LineRenderer>();
            ConfigureLineRenderer(leftRay, Color.blue);
        }
    }

    void ConfigureLineRenderer(LineRenderer lr, Color color)
    {
        lr.startWidth = 0.01f;
        lr.endWidth = 0.005f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = color;
        lr.endColor = new Color(color.r, color.g, color.b, 0.3f);
        lr.positionCount = 2;
    }

    void Update()
    {
        UpdateRayVisuals();
        HandleGrabInput();
        
        if (isGrabbed)
        {
            HandleManipulation();
        }
    }

    void UpdateRayVisuals()
    {
        if (rightRay != null && rightHandAnchor != null)
        {
            rightRay.SetPosition(0, rightHandAnchor.position);
            rightRay.SetPosition(1, rightHandAnchor.position + rightHandAnchor.forward * rayDistance);
        }
        
        if (leftRay != null && leftHandAnchor != null)
        {
            leftRay.SetPosition(0, leftHandAnchor.position);
            leftRay.SetPosition(1, leftHandAnchor.position + leftHandAnchor.forward * rayDistance);
        }
    }

    void HandleGrabInput()
    {
        // Mano derecha - Grip
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch))
        {
            TryGrab(rightHandAnchor);
        }
        
        // Mano izquierda - Grip
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch))
        {
            TryGrab(leftHandAnchor);
        }
        
        // Soltar
        if (isGrabbed && OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger))
        {
            Release();
        }
    }

    void TryGrab(Transform hand)
    {
        if (hand == null) return;
        
        Ray ray = new Ray(hand.position, hand.forward);
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, rayDistance, interactableLayer))
        {
            if (hit.transform.IsChildOf(transform))
            {
                isGrabbed = true;
                activeHand = hand;
                grabOffset = transform.position - hand.position;
                initialScale = transform.localScale.x;
                
                // Haptic feedback
                OVRInput.SetControllerVibration(0.5f, 0.5f, 
                    hand == rightHandAnchor ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch);
                
                Debug.Log("✅ Paredes agarradas");
            }
        }
    }

    void Release()
    {
        isGrabbed = false;
        activeHand = null;
        Debug.Log("🔓 Paredes soltadas");
    }

    void HandleManipulation()
    {
        if (activeHand == null) return;
        
        // Mover
        transform.position = activeHand.position + grabOffset;
        
        // Rotar con thumbstick
        Vector2 thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        if (thumbstick.magnitude > 0.1f)
        {
            transform.Rotate(Vector3.up, thumbstick.x * rotationSpeed * Time.deltaTime, Space.World);
        }
        
        // Escalar con trigger
        float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger);
        if (trigger > 0.1f)
        {
            float newScale = Mathf.Clamp(
                transform.localScale.x + (scaleSpeed * trigger * Time.deltaTime),
                minScale,
                maxScale
            );
            transform.localScale = Vector3.one * newScale;
        }
    }
}