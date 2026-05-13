---
layout: default
title: "Intensidad de Señal"
parent: "Análisis de Red"
nav_order: 1
---

# Intensidad de Señal

Durante el recorrido del robot, el sistema mide de forma continua la intensidad de la señal WiFi (RSSI, Received Signal Strength Indicator) del punto de acceso al que está conectado. Cada medición queda asociada a una posición precisa dentro del frame de coordenadas `map`, lo que permite construir una representación espacial fiel de la cobertura de red en el entorno explorado.

---

## Medición de RSSI en el robot

El nodo ROS 2 `wifi_sampler`, implementado en `wifi_sampler_realtime.py`, se encarga de obtener el RSSI y enviarlo a Unity junto con la pose del robot.

### Lectura del RSSI

El valor de señal se obtiene ejecutando el comando del sistema `iw wlan0 link`, que reporta el estado actual de la interfaz inalámbrica. El nodo analiza la salida con una expresión regular para extraer el campo `signal`:

```
signal: -62 dBm
```

Si la lectura falla por cualquier motivo (tiempo de espera agotado, interfaz no disponible), se usa el valor de respaldo `-70 dBm`.

### Condición de muestreo

El nodo no toma muestras a intervalos de tiempo fijos, sino en función de la distancia recorrida. Solo se registra una nueva muestra cuando el robot ha avanzado al menos `sample_distance` metros desde la última posición registrada. El valor por defecto es **1.0 m** y es configurable como parámetro ROS 2.

```bash
ros2 run wifi_sampler wifi_sampler_realtime \
  --ros-args -p sample_distance:=1.5 -p unity_ip:=192.168.1.50
```

| Parámetro | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `unity_ip` | string | `100.119.230.103` | Dirección IP del receptor UDP en Unity |
| `unity_port` | int | `5007` | Puerto UDP de destino |
| `sample_distance` | float | `1.0` | Distancia mínima entre muestras en metros |
| `ssid` | string | `""` | SSID forzado; si está vacío se lee del AP asociado |

### Obtención de la posición

La suscripción a `/odom` actúa únicamente como trigger periódico: cada vez que llega un mensaje de odometría, el nodo evalúa si debe tomar una muestra. La posición real utilizada en la muestra **no proviene de la odometría**, sino de la transformación `map -> base_link` consultada mediante `tf2_ros.Buffer.lookup_transform`. Esta transformación incorpora las correcciones del algoritmo SLAM, por lo que las coordenadas reflejan la posición estimada en el mapa global y no la simple integración de encoders.

Si el frame `base_link` no está disponible en el árbol TF, el nodo reintenta automáticamente con `base_footprint`. Si ambos fallan, la muestra se descarta y se emite una advertencia con limitación de frecuencia (máximo una advertencia cada 5 segundos).

```python
candidate_frames = ['base_link', 'base_footprint']

for source_frame in candidate_frames:
    transform = self.tf_buffer.lookup_transform(
        'map',
        source_frame,
        rclpy.time.Time()
    )
```

---

## Estructura del JSON enviado a Unity

Cada muestra se serializa como un objeto JSON y se transmite por UDP al puerto configurado. El formato completo es el siguiente:

```json
{
  "type": "sample",
  "t": 1718200345.892,
  "ssid": "MiRed_5G",
  "bssid": "aa:bb:cc:dd:ee:ff",
  "rssi": -62,
  "row": 0,
  "col": 0,
  "x": 2.4310,
  "y": -1.1050,
  "k": 14,
  "map_origin_x": -3.5,
  "map_origin_y": -2.1
}
```

| Campo | Tipo | Descripción |
|---|---|---|
| `type` | string | Siempre `"sample"` para muestras de señal. El valor `"shutdown"` indica cierre de sesión. |
| `t` | float | Marca de tiempo Unix en segundos, derivada del reloj de ROS 2 con precisión de nanosegundos. |
| `ssid` | string | Nombre de la red WiFi a la que está conectado el robot. |
| `bssid` | string | Dirección MAC del punto de acceso asociado, en formato `aa:bb:cc:dd:ee:ff`. |
| `rssi` | int | Intensidad de señal en dBm. Rango habitual: -30 (excelente) a -90 (sin señal). |
| `row` | int | Fila en la cuadrícula del mapa de ocupación (reservado; actualmente siempre `0`). |
| `col` | int | Columna en la cuadrícula del mapa de ocupación (reservado; actualmente siempre `0`). |
| `x` | float | Coordenada X en el frame `map` de ROS 2, en metros. |
| `y` | float | Coordenada Y en el frame `map` de ROS 2, en metros. |
| `k` | int | Número secuencial de la muestra en la sesión actual. Se usa como clave para evitar duplicados en Unity. |
| `map_origin_x` | float | Coordenada X del origen del mapa, leída desde `current_map.yaml`, en metros. |
| `map_origin_y` | float | Coordenada Y del origen del mapa, leída desde `current_map.yaml`, en metros. |

Las coordenadas `x` e `y` pertenecen al sistema de referencia global del mapa SLAM. Unity las usa directamente para posicionar cada muestra sobre el plano del mapa, aplicando la conversión de ejes correspondiente mediante `MapFrameConverter`.

---

## Modelo de datos en Unity (WifiSample)

En el lado de Unity, cada muestra recibida se deserializa en una instancia de la clase `WifiSample`, marcada con el atributo `[Serializable]` para ser compatible con `JsonUtility.FromJson`. Los campos de la clase se corresponden uno a uno con los del JSON descrito anteriormente.

### Campos de serialización

| Campo | Tipo C# | Descripción |
|---|---|---|
| `type` | string | Tipo de mensaje (`"sample"` o `"shutdown"`). |
| `t` | double | Marca de tiempo Unix de la medición. |
| `ssid` | string | Nombre de la red WiFi. |
| `bssid` | string | Dirección MAC del AP. |
| `rssi` | int | RSSI tal como llegó por UDP; no se modifica tras la deserialización. |
| `row` / `col` | int | Coordenadas en la cuadrícula del mapa (reservadas). |
| `x` / `y` | float | Posición en el frame `map` de ROS 2, en metros. |
| `k` | int | Índice secuencial de la muestra. |
| `map_origin_x` / `map_origin_y` | float | Origen del mapa SLAM en metros. |

### Campos de simulación en tiempo de ejecución

Una vez que la muestra llega por UDP, se llama a `InitializeForRuntime()`, que inicializa tres campos adicionales que no forman parte del JSON pero son esenciales para el funcionamiento del simulador de señal:

| Campo | Tipo C# | Descripción |
|---|---|---|
| `originalRssi` | int | Copia inmutable del RSSI medido por el robot. No se modifica después de la inicialización. |
| `currentRssi` | int | Valor activo utilizado por el heatmap y las etiquetas. Puede ser sobreescrito por el simulador. |
| `hasSimulationOverride` | bool | Indica si `currentRssi` proviene del simulador (`true`) o de la medición real (`false`). |

El método `GetDisplayRssi()` devuelve siempre `currentRssi`, que es el valor empleado para la coloración de esferas, el texto de etiquetas TMP y la interpolación del mapa de calor.

Cuando el simulador calcula una distribución alternativa de señal, invoca `ApplySimulatedRssi(int newRssi)`, que escribe el nuevo valor en `currentRssi` y activa el flag `hasSimulationOverride`. El dato original permanece intacto en `originalRssi`.

Para restaurar el estado de la medición real, el método `ResetToOriginal()` copia `originalRssi` de vuelta a `currentRssi` y desactiva `hasSimulationOverride`:

```csharp
public void ResetToOriginal()
{
    currentRssi = originalRssi;
    hasSimulationOverride = false;
}
```

Este diseño permite alternar entre la medición real y la simulación durante una sesión sin perder los datos originales capturados por el robot.

---

## Archivos de referencia

- [wifi\_sampler\_realtime.py]({{ "/assets/downloads/rosmaster/wifi_sampler_realtime.py" | relative_url }})
- [WifiSample.cs]({{ "/assets/downloads/Scripts_Unity_App/WifiSample.cs" | relative_url }})
