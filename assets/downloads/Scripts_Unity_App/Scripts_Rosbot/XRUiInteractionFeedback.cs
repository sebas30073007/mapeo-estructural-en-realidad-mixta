using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class XRUiInteractionFeedback : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip hoverClip;
    [SerializeField] private AudioClip clickClip;
    [SerializeField, Range(0f, 1f)] private float hoverVolume = 0.6f;
    [SerializeField, Range(0f, 1f)] private float clickVolume = 0.8f;

    [Header("Haptics")]
    [SerializeField] private bool enableHaptics = true;
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;
    [SerializeField, Range(0f, 1f)] private float hoverAmplitude = 0.08f;
    [SerializeField, Range(0f, 1f)] private float hoverFrequency = 0.08f;
    [SerializeField] private float hoverDuration = 0.03f;
    [SerializeField, Range(0f, 1f)] private float clickAmplitude = 0.18f;
    [SerializeField, Range(0f, 1f)] private float clickFrequency = 0.18f;
    [SerializeField] private float clickDuration = 0.05f;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (audioSource != null && hoverClip != null)
            audioSource.PlayOneShot(hoverClip, hoverVolume);

        if (enableHaptics)
            StartCoroutine(HapticPulse(hoverFrequency, hoverAmplitude, hoverDuration));
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (audioSource != null && clickClip != null)
            audioSource.PlayOneShot(clickClip, clickVolume);

        if (enableHaptics)
            StartCoroutine(HapticPulse(clickFrequency, clickAmplitude, clickDuration));
    }

    private IEnumerator HapticPulse(float frequency, float amplitude, float duration)
    {
        if (!OVRInput.IsControllerConnected(controller))
            yield break;

        OVRInput.SetControllerVibration(frequency, amplitude, controller);
        yield return new WaitForSeconds(duration);
        OVRInput.SetControllerVibration(0f, 0f, controller);
    }

    private void OnDisable()
    {
        OVRInput.SetControllerVibration(0f, 0f, controller);
    }
}
