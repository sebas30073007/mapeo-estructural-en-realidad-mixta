using UnityEngine;

public class OVRControllerTriggerDebug : MonoBehaviour
{
    public Renderer targetRenderer;

    void Update()
    {
        bool leftConnected =
            OVRInput.IsControllerConnected(OVRInput.Controller.LTouch) ||
            OVRInput.IsControllerConnected(OVRInput.Controller.LHand);

        bool rightConnected =
            OVRInput.IsControllerConnected(OVRInput.Controller.RTouch) ||
            OVRInput.IsControllerConnected(OVRInput.Controller.RHand);

        float leftTrigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger);
        float rightTrigger = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger);

        bool pressed = leftTrigger > 0.5f || rightTrigger > 0.5f;

        if (targetRenderer != null)
        {
            if (pressed)
                targetRenderer.material.color = Color.green;
            else if (leftConnected || rightConnected)
                targetRenderer.material.color = Color.yellow;
            else
                targetRenderer.material.color = Color.red;
        }
    }
}