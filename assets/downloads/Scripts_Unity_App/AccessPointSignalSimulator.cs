// using UnityEngine;

// public class AccessPointSignalSimulator : MonoBehaviour
// {
//     [Header("References")]
//     public HeatmapGridRenderer heatmap;
//     public Transform accessPointTransform;

//     [Header("AP positions")]
//     public bool hasInitialAPPosition = false;
//     public Vector3 initialAPWorldPosition;
//     public Vector3 candidateAPWorldPosition;

//     [Header("Debug")]
//     public bool recalculateOnStart = false;

//     void Start()
//     {
//         if (accessPointTransform != null)
//         {
//             candidateAPWorldPosition = accessPointTransform.position;
//         }

//         if (recalculateOnStart)
//         {
//             RecalculateFromCandidateAP();
//         }
//     }

//     public void CaptureInitialAPPosition()
//     {
//         if (accessPointTransform == null)
//         {
//             Debug.LogWarning("AccessPointSignalSimulator: accessPointTransform no asignado.");
//             return;
//         }

//         initialAPWorldPosition = accessPointTransform.position;
//         candidateAPWorldPosition = initialAPWorldPosition;
//         hasInitialAPPosition = true;

//         Debug.Log("[AP] Initial AP captured.");
//         Debug.Log($"[AP] initialAPWorldPosition = {initialAPWorldPosition}");
//     }

//     public void UpdateCandidateAPFromTransform()
//     {
//         if (accessPointTransform == null)
//         {
//             Debug.LogWarning("AccessPointSignalSimulator: accessPointTransform no asignado.");
//             return;
//         }

//         candidateAPWorldPosition = accessPointTransform.position;
//         Debug.Log($"[AP] Candidate AP updated from transform: {candidateAPWorldPosition}");
//     }

//     public void RecalculateFromCandidateAP()
//     {
//         if (heatmap == null)
//         {
//             Debug.LogWarning("AccessPointSignalSimulator: heatmap no asignado.");
//             return;
//         }

//         if (accessPointTransform == null)
//         {
//             Debug.LogWarning("AccessPointSignalSimulator: accessPointTransform no asignado.");
//             return;
//         }

//         // Tomar posición actual del AP visible como candidata
//         UpdateCandidateAPFromTransform();

//         // Convertir a coordenadas locales del heatmap
//         Vector3 apLocalToHeatmap = heatmap.transform.InverseTransformPoint(candidateAPWorldPosition);

//         Debug.Log($"[AP] Candidate AP world position = {candidateAPWorldPosition}");
//         Debug.Log($"[AP] Candidate AP local to heatmap = {apLocalToHeatmap}");

//         // NUEVO: usar el modelo con obstáculos/distancia
//         heatmap.RecalculateSamplesFromAccessPoint(apLocalToHeatmap);
//     }

//     public void ResetToOriginalMeasurements()
//     {
//         if (heatmap == null)
//         {
//             Debug.LogWarning("AccessPointSignalSimulator: heatmap no asignado.");
//             return;
//         }

//         heatmap.ResetSamplesToOriginalMeasurements();
//         Debug.Log("[AP] Measurements restored to original values.");
//     }

//     public void ResetCandidateAPToInitial()
//     {
//         if (!hasInitialAPPosition)
//         {
//             Debug.LogWarning("[AP] No hay AP inicial capturado todavía.");
//             return;
//         }

//         candidateAPWorldPosition = initialAPWorldPosition;

//         if (accessPointTransform != null)
//         {
//             accessPointTransform.position = initialAPWorldPosition;
//         }

//         Debug.Log($"[AP] Candidate AP reset to initial position: {initialAPWorldPosition}");
//     }

//     public void PrintAPState()
//     {
//         Debug.Log($"hasInitialAPPosition = {hasInitialAPPosition}");
//         Debug.Log($"initialAPWorldPosition = {initialAPWorldPosition}");
//         Debug.Log($"candidateAPWorldPosition = {candidateAPWorldPosition}");

//         if (accessPointTransform != null)
//             Debug.Log($"accessPointTransform.position = {accessPointTransform.position}");
//     }
// }