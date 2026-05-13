using UnityEngine;
using System.Collections;
using TMPro;

public class WelcomeFlowController : MonoBehaviour
{
    [Header("Settings")]
    public CanvasGroup welcomePanelGroup; // Arrastra el WelcomePanel aquí
    public GameObject mainControlsPanel;  // Tu Panel_Root principal
    public float displayTime = 3.0f;      // Tiempo que se queda el mensaje
    public float fadeDuration = 1.5f;     // Duración de la animación

    void Start()
    {
        // Al inicio, nos aseguramos que el panel de bienvenida esté visible
        // y el panel de controles esté apagado
        if (welcomePanelGroup != null)
        {
            welcomePanelGroup.alpha = 1.0f;
            welcomePanelGroup.gameObject.SetActive(true);
        }

        if (mainControlsPanel != null)
        {
            mainControlsPanel.SetActive(false);
        }

        // Iniciamos la secuencia
        StartCoroutine(WelcomeSequence());
    }

    IEnumerator WelcomeSequence()
    {
        // 1. Esperamos el tiempo definido
        yield return new WaitForSeconds(displayTime);

        // 2. Animación de Fade Out
        float elapsedTime = 0;
        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            welcomePanelGroup.alpha = Mathf.Lerp(1.0f, 0.0f, elapsedTime / fadeDuration);
            yield return null;
        }

        // 3. Limpieza final
        welcomePanelGroup.gameObject.SetActive(false);

        // 4. Mostramos el panel de controles principal automáticamente
        if (mainControlsPanel != null)
        {
            mainControlsPanel.SetActive(true);
        }
    }
}