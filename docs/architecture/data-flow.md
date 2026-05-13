---
layout: default
title: "Flujo de Datos"
parent: "Arquitectura del Sistema"
nav_order: 4
---

# Flujo de Datos

Toda la comunicación entre el robot y los lentes Meta Quest se realiza sobre la red virtual de **Tailscale**. La implementación final utiliza cuatro canales distintos con protocolos elegidos según la naturaleza del dato.

## Mapa de canales

| Puerto | Protocolo | Dirección | Contenido | Frecuencia |
|---|---|---|---|---|
| **5002** | UDP | Meta Quest → Robot | Comandos de movimiento (vx, vy, wz) | Continuo mientras hay entrada |
| **5007** | UDP | Robot → Meta Quest | Muestras de señal WiFi (RSSI + posición) | Cada 1 m recorrido |
| **5008** | HTTP | Meta Quest → Robot | Solicitud de mapa SLAM (`/generate_map`) | Bajo demanda |
| **5555** | ZMQ | Robot → Meta Quest | Stream de video (frames JPEG) | Continuo (~30 fps) |

## Elección de protocolos

**UDP** para teleop y WiFi: la latencia mínima es prioritaria. Un paquete perdido simplemente se descarta; el siguiente refleja el estado actual del joystick o de la señal.

**HTTP** para el mapa SLAM: la transferencia de mapa es una operación de petición–respuesta con payload grande (PGM en base64 + JSON de paredes). HTTP sobre TCP garantiza la entrega completa sin implementar confirmación manual.

**ZMQ** para video: ZeroMQ desacopla el productor del consumidor mediante un patrón publish/subscribe, lo que facilita el manejo de backpressure cuando los frames llegan más rápido de lo que Unity los procesa.

---

## Puerto 5002 — Comandos de movimiento

El script `UdpTeleopSender` en Unity captura la entrada del joystick del operador y envía un comando JSON por UDP al robot cada ~100 ms:

```json
{ "vx": 0.1200, "vy": 0.0000, "wz": -0.3000 }
```

- **`vx`** — velocidad lineal adelante/atrás en m/s (máximo ±0.15 m/s).
- **`vy`** — velocidad lateral en m/s, disponible gracias a la tracción omnidireccional del ROSbot.
- **`wz`** — velocidad angular sobre el eje vertical en rad/s (máximo ±0.5 rad/s).

El robot recibe el datagrama, parsea el JSON y publica en `/cmd_vel` como `geometry_msgs/Twist`. Si no llega ningún comando durante varios ciclos, el robot se detiene por seguridad.

## Puerto 5007 — Muestras de señal WiFi

El nodo ROS 2 `wifi_sampler` corre en el robot, escucha `/odom` como trigger y envía una muestra al puerto 5007 de Unity cada vez que el robot avanza 1 m (configurable):

```json
{
  "type": "sample",
  "t": 1718200345.892,
  "ssid": "MiRed_5G",
  "bssid": "aa:bb:cc:dd:ee:ff",
  "rssi": -62,
  "x": 2.4310,
  "y": -1.1050,
  "k": 14,
  "map_origin_x": -3.5,
  "map_origin_y": -2.1
}
```

Las coordenadas `x`, `y` corresponden al frame `map` de ROS 2, obtenidas mediante lookup TF2 `map → base_link`. Unity usa `map_origin_x/y` para alinear la muestra con el mapa descargado.

## Puerto 5008 — Solicitud de mapa SLAM

El servidor `slam_server.py` corre en el robot como proceso Flask. Unity hace una petición HTTP GET cuando el operador pulsa el botón de actualizar mapa:

**`GET http://<robot_ip>:5008/health`** — verifica disponibilidad antes de pedir el mapa.

**`GET http://<robot_ip>:5008/generate_map`** — desencadena la secuencia completa de generación:

1. Ejecuta `ros2 run nav2_map_server map_saver_cli` → guarda `current_map.pgm` + `current_map.yaml`.
2. Ejecuta `extract_walls.py` sobre esos archivos → genera `walls.json` con segmentos de pared y esquinas.
3. Devuelve un único JSON con los tres archivos empaquetados.

```json
{
  "status": "success",
  "pgm": "<base64...>",
  "yaml": "image: current_map.pgm\nresolution: 0.05\n...",
  "walls": "{\"version\":3,\"wall_segments\":[...],\"personas\":[...]}",
  "pgm_size": 153600,
  "yaml_size": 142,
  "walls_size": 8740
}
```

## Puerto 5555 — Stream de video (ZMQ)

El script `RosbotVideoStreamReceiver` en Unity se suscribe al topic ZMQ `"video_rgb"` en el puerto 5555 del robot. La recepción ocurre en un hilo separado que deposita los frames en una `ConcurrentQueue<byte[]>`; el hilo principal de Unity decodifica el JPEG y actualiza la textura en cada `Update()`.

