---
layout: default
title: "Mapa de Calor"
parent: "Análisis de Red"
nav_order: 2
---

# Mapa de Calor

Las muestras de señal WiFi recibidas por UDP desde el robot se visualizan en tiempo real como un mapa de calor superpuesto al plano del mapa, anclado al espacio físico real mediante las capacidades de realidad mixta del Meta Quest 3. La visualización combina esferas coloreadas por posición, etiquetas numéricas y una textura interpolada que revela la distribución continua de cobertura en el entorno explorado.

---

## Recepción de muestras (UdpWifiReceiver)

El componente `UdpWifiReceiver` gestiona la comunicación de red en un hilo de fondo independiente del hilo principal de Unity, evitando bloqueos en la renderización.

### Arquitectura de recepción

Al iniciarse, el componente abre un `UdpClient` en el **puerto 5007** y lanza un hilo de fondo que ejecuta el bucle de recepción de forma continua. Para evitar que el hilo quede bloqueado indefinidamente cuando no hay datos, el socket tiene un timeout de recepción de 500 ms.

Los mensajes recibidos no se procesan directamente en el hilo de red. En su lugar, cada datagrama se encola en una `ConcurrentQueue<string>`, que actúa como canal seguro entre el hilo de red y el hilo principal de Unity:

```
Hilo de red (ReceiveLoop)
    |
    |-- UdpClient.Receive() --> ConcurrentQueue<string> (inbox)
                                        |
                           Update() del hilo principal de Unity
                                        |
                           Hasta 100 mensajes por frame
                                        |
                           JsonUtility.FromJson<WifiSample>()
                                        |
                           heatmap.AddSample(s)
```

### Procesamiento por frame

En cada llamada a `Update()`, el componente extrae hasta **100 mensajes** de la cola. Este límite evita que un pico de datos cause caídas de framerate al obligar al motor a procesar demasiado trabajo en un único frame.

Para cada mensaje se realiza lo siguiente:

1. Deserializar el JSON en un objeto `WifiSample` con `JsonUtility.FromJson`.
2. Si el campo `type` es `"shutdown"`, se detiene el receptor y finaliza la sesión.
3. Si el campo `type` es `"sample"`, se llama a `InitializeForRuntime()` para inicializar los campos de simulación y luego se pasa la muestra al heatmap mediante `heatmap.AddSample(s)`.

---

## Representación visual

El componente `HeatmapGridRenderer` gestiona tres capas de visualización independientes que pueden activarse o desactivarse por separado: la textura del mapa de calor, las esferas de posición y las etiquetas numéricas.

### Registro de una nueva muestra (AddSample)

Cuando llega una nueva muestra, `AddSample()` la indexa por su campo `k` para garantizar que cada posición se dibuje una sola vez. Si ya existe una entrada con esa clave, la muestra se descarta sin generar ningún objeto adicional.

Para cada muestra nueva se crean dos objetos en la escena de Unity:

- **Esfera coloreada**: se instancia el prefab `pointPrefab` en la posicion local correspondiente a `(x, y)` en el mapa. El color se asigna mediante `WifiColorMap.ColorFromRssiContinuous()` usando el valor `GetDisplayRssi()` de la muestra.
- **Etiqueta TMP**: si `showLabels` esta activo, se instancia el prefab `labelPrefab` ligeramente por encima de la esfera. La etiqueta muestra el valor numérico en el formato `{rssi} dBm`.

La conversion de coordenadas del frame `map` de ROS 2 a coordenadas locales de la escena Unity se delega en el componente `MapFrameConverter` a traves del metodo `MapToLocal()`.

### Modos de visualizacion

Cada capa tiene un toggle independiente controlado por los metodos publicos del componente:

| Metodo | Descripcion |
|---|---|
| `SetHeatmapVisible(bool)` | Activa o desactiva la textura interpolada del mapa de calor. |
| `SetPointsVisible(bool)` | Activa o desactiva las esferas coloreadas en cada posicion de muestra. |
| `SetLabelsVisible(bool)` | Activa o desactiva las etiquetas TMP con los valores en dBm. |

---

## Interpolacion IDW

La textura del mapa de calor no muestra solo los puntos muestreados: interpola el valor de RSSI en todos los pixels de la region explorada usando el metodo IDW (Inverse Distance Weighting, ponderacion por distancia inversa).

### Calculo (ComputeHeatmapGlobal)

El metodo `ComputeHeatmapGlobal()` recalcula la textura completa cada vez que se invoca. El proceso sigue estos pasos:

1. **Bounding box**: se calcula el rectangulo minimo que contiene todas las muestras en coordenadas locales, al que se agrega un margen de **1.0 m** en cada lado. La region resultante se recorta a los limites del plano del mapa.

2. **Iteracion por pixel**: para cada pixel `(px, py)` de la textura de resolucion configurable (`surfaceResolution`, por defecto 128 x 128):
   - Los pixels fuera del bounding box se dejan transparentes.
   - Para los pixels dentro del bounding box se cuenta cuantas muestras caen dentro de un radio de **3.0 m** (`supportRadiusM`).
   - Si hay menos de `minNeighbors` (por defecto 2) vecinos, el pixel se deja transparente.
   - En caso contrario, se aplica IDW con potencia **2** (`idwPower`) sobre todas las muestras.

3. **Formula IDW**:

   ```
   w_i = 1 / (d_i + epsilon)^p
   RSSI_estimado = sum(w_i * rssi_i) / sum(w_i)
   ```

   Donde `d_i` es la distancia euclidea al pixel, `p` es la potencia (2) y `epsilon` (0.05) evita la division por cero cuando el pixel coincide exactamente con una muestra.

4. **Color y opacidad**: el RSSI estimado se convierte en color mediante `WifiColorMap.ColorFromRssiContinuous()`. El brillo se amplifica por `heatmapBrightness` (1.2) y la opacidad se fija a `heatmapOpacity` (0.9).

5. La textura se aplica con `Texture2D.Apply()` y queda visible en el `surfaceRenderer` del plano del mapa.

---

## Escala de colores (WifiColorMap)

La clase estatica `WifiColorMap` define la correspondencia entre valores de RSSI y colores de la textura. El rango cubierto va de **-90 dBm** (ausencia de señal) a **-30 dBm** (señal excelente). Los valores fuera de este rango se recortan al extremo correspondiente antes de calcular el color.

El gradiente se divide en tres segmentos lineales:

| Rango normalizado | RSSI aproximado | Degradado |
|---|---|---|
| 0.00 - 0.33 | -90 a -70 dBm | Rojo a naranja |
| 0.33 - 0.66 | -70 a -50 dBm | Naranja a amarillo |
| 0.66 - 1.00 | -50 a -30 dBm | Amarillo a verde |

La tabla siguiente resume los colores de referencia en los puntos clave del gradiente:

| RSSI (dBm) | Color | Calidad |
|---|---|---|
| -30 | Verde | Excelente |
| -60 | Amarillo | Aceptable |
| -70 | Naranja | Debil |
| -90 | Rojo | Sin señal |

La funcion principal de la clase es `ColorFromRssiContinuous(int rssi, float alpha)`. El parametro `alpha` controla la transparencia del color resultante; el valor por defecto es `0.45f` para las esferas y se sobreescribe a `heatmapOpacity` (0.9) cuando se calcula la textura.

---

## Simulador de señal (AccessPointSignalSimulator)

El componente `AccessPointSignalSimulator` permite proyectar la distribucion teorica de señal de un punto de acceso sobre el mapa sin necesidad de realizar un recorrido fisico con el robot. Esta funcionalidad es util para validar el diseño de infraestructura de red de una instalacion antes de llevar a cabo la exploracion real.

### Funcionamiento general

El simulador trabaja sobre las muestras ya cargadas en el `HeatmapGridRenderer`. En lugar de medir el RSSI con el hardware de red, recalcula el valor esperado en cada posicion de muestra usando un modelo de propagacion que tiene en cuenta la distancia al punto de acceso y la presencia de obstaculos en el entorno.

La posicion del AP puede definirse de dos formas:

- **Posicion inicial capturada**: `CaptureInitialAPPosition()` registra la posicion actual del `Transform` del AP en la escena como referencia de base.
- **Posicion candidata**: `UpdateCandidateAPFromTransform()` actualiza la posicion de prueba sin sobrescribir la posicion inicial. Esto permite mover el AP virtualmente en la escena y evaluar como cambiaria la cobertura.

Al llamar a `RecalculateFromCandidateAP()`, la posicion del AP se convierte a coordenadas locales del heatmap y se pasa a `heatmap.RecalculateSamplesFromAccessPoint()`, que aplica el modelo de propagacion y actualiza el `currentRssi` de cada muestra mediante `ApplySimulatedRssi()`.

Para volver a la medicion real del robot se llama a `ResetToOriginalMeasurements()`, que invoca `heatmap.ResetSamplesToOriginalMeasurements()` y regenera la textura del mapa de calor con los valores originales.

### Estados del AP

| Estado | Descripcion |
|---|---|
| `hasInitialAPPosition = false` | No se ha capturado ninguna posicion de referencia. |
| `hasInitialAPPosition = true` | Se dispone de una posicion inicial para comparacion. |
| `candidateAPWorldPosition` | Posicion actualmente evaluada por el simulador. |

> El componente `AccessPointSignalSimulator` esta actualmente comentado en el codigo fuente pendiente de integracion completa con el modelo de propagacion WiFi. La interfaz publica y la logica de coordenadas estan definidas y listas para su activacion.

---

## Archivos de referencia

- [HeatmapGridRenderer.cs]({{ "/assets/downloads/Scripts_Unity_App/HeatmapGridRenderer.cs" | relative_url }})
- [WifiColorMap.cs]({{ "/assets/downloads/Scripts_Unity_App/WifiColorMap.cs" | relative_url }})
- [UdpWifiReceiver.cs]({{ "/assets/downloads/Scripts_Unity_App/UdpWifiReceiver.cs" | relative_url }})
- [AccessPointSignalSimulator.cs]({{ "/assets/downloads/Scripts_Unity_App/AccessPointSignalSimulator.cs" | relative_url }})
