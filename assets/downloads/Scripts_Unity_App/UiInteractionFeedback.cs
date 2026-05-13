using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class UiInteractionFeedback : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip hoverClip;
    public AudioClip clickClip;
    [Range(0f, 1f)] public float hoverVolume = 0.6f;
    [Range(0f, 1f)] public float clickVolume = 0.8f;

    [Header("Haptics")]
    public bool enableHaptics = true;
    public OVRInput.Controller controller = OVRInput.Controller.RTouch;

    [Range(0f, 1f)] public float hoverAmplitude = 0.08f;
    [Range(0f, 1f)] public float hoverFrequency = 0.08f;
    public float hoverDuration = 0.03f;

    [Range(0f, 1f)] public float clickAmplitude = 0.18f;
    [Range(0f, 1f)] public float clickFrequency = 0.18f;
    public float clickDuration = 0.05f;

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