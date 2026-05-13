---
layout: default
title: "Stack ROS"
parent: "Ficha Técnica"
nav_order: 2
---

# Stack ROS

El robot Husarion ROSbot 2R ejecuta Ubuntu 24.04 LTS con ROS 2 Jazzy Jalisco. Esta sección documenta los nodos activos durante una sesión de mapeo, los topics que circulan entre ellos y su relación con el hardware embarcado.

---

## Entorno de ejecución

| Elemento | Valor |
|---|---|
| Sistema operativo | Ubuntu 24.04 LTS (Noble Numbat) |
| Distribución ROS | ROS 2 Jazzy Jalisco |
| Workspace del robot | `/home/iberomsc02/ros2_ws_02/` |
| Directorio de mapas | `/home/iberomsc02/maps/` |
| Puerto del servidor de mapas | `5008` |

El script `start_slam_server.sh` inicializa el entorno y lanza el servidor Flask en un solo paso:

```bash
# Cargar entorno de ROS2 Jazzy
source /opt/ros/jazzy/setup.bash

# Cargar tu workspace
source /home/iberomsc02/ros2_ws_02/install/setup.bash

# Ejecutar servidor Flask
exec python3 /home/iberomsc02/slam_server.py
```

---

## Nodos y procesos activos

Durante una sesión de mapeo completa se ejecutan simultáneamente los siguientes procesos:

| Proceso | Tipo | Función |
|---|---|---|
| `slam_toolbox` (Husarion) | Nodo ROS 2 | Construye y actualiza el mapa de ocupación continuo |
| `rplidar_ros` | Nodo ROS 2 | Publica escaneos LiDAR en `/scan` |
| `astra_camera` | Nodo ROS 2 | Publica video e imagen de profundidad |
| `wifi_sampler` | Nodo ROS 2 (Python) | Muestrea RSSI por posición, envía las muestras por UDP |
| `slam_server.py` | Proceso Flask | Sirve el mapa bajo demanda en el puerto 5008 |

Todos los nodos ROS 2 corren dentro del workspace `/home/iberomsc02/ros2_ws_02/`. El proceso Flask es independiente del grafo ROS pero invoca comandos ROS 2 mediante `subprocess`.

---

## Topics ROS 2 relevantes

| Topic | Tipo de mensaje | Rol en el sistema |
|---|---|---|
| `/scan` | `sensor_msgs/LaserScan` | Publicado por `rplidar_ros`, consumido por `slam_toolbox` |
| `/odom` | `nav_msgs/Odometry` | Usado por `wifi_sampler` únicamente como disparador periódico |
| `/map` | `nav_msgs/OccupancyGrid` | Publicado por `slam_toolbox`, leído por `map_saver_cli` al guardar |
| TF: `map -> base_link` | Transformación | Provista por `slam_toolbox`, consultada por `wifi_sampler` |

---

## TF2 para localización precisa

El nodo `wifi_sampler` necesita saber en todo momento en qué punto del mapa se encuentra el robot para asociar cada medición de RSSI a una coordenada espacial. Existen dos fuentes posibles para esta información: la odometría directa (`/odom`) y el árbol de transformaciones TF2.

La odometría acumula error de integración con el tiempo: pequeñas imprecisiones en la medición de velocidades de rueda se suman y provocan una deriva progresiva de la posición estimada. El árbol TF2, en cambio, incorpora las correcciones que `slam_toolbox` aplica al mapa en cada iteración del algoritmo de SLAM. La transformación `map -> base_link` refleja por tanto la posición corregida del robot en el frame global del mapa, y no la posición acumulada desde el arranque.

Por este motivo, `wifi_sampler` se suscribe a `/odom` solo como mecanismo de disparo periódico: cada vez que llega un mensaje de odometría, el nodo consulta el árbol TF2 para obtener la posición real. La consulta intenta primero el frame `base_link` y, si no está disponible, recurre a `base_footprint` como alternativa de respaldo:

```python
candidate_frames = ['base_link', 'base_footprint']

for source_frame in candidate_frames:
    try:
        transform = self.tf_buffer.lookup_transform(
            'map',
            source_frame,
            rclpy.time.Time()
        )
        x = transform.transform.translation.x
        y = transform.transform.translation.y
        return x, y
    except (LookupException, ConnectivityException, ExtrapolationException):
        continue
```

Si ninguno de los dos frames está disponible, la muestra se descarta y se emite una advertencia en el log, acotada a una vez cada cinco segundos para no saturar la consola.

---

## Generación del mapa bajo demanda

Cuando la aplicación Unity solicita el mapa a través del endpoint `/generate_map`, el servidor Flask ejecuta el siguiente comando ROS 2 mediante `subprocess`:

```bash
ros2 run nav2_map_server map_saver_cli -f /home/iberomsc02/maps/current_map
```

`map_saver_cli` se suscribe al topic `/map`, toma la última imagen de ocupación publicada por `slam_toolbox` y escribe dos archivos en el directorio de mapas:

| Archivo | Descripción |
|---|---|
| `current_map.pgm` | Imagen en escala de grises (Portable Gray Map) con los valores de ocupación: blanco (libre), negro (ocupado), gris (desconocido) |
| `current_map.yaml` | Metadatos del mapa: resolución en metros por píxel, coordenadas del origen en el frame `map`, umbral de ocupación y ruta al archivo PGM |

Tras la escritura de ambos archivos, el servidor ejecuta el script `extract_walls.py` para segmentar los obstáculos en segmentos de pared. El resultado se guarda como `current_map_walls.json`. El endpoint devuelve los tres artefactos codificados en una respuesta JSON única: el PGM en Base64, el YAML como texto plano y el JSON de paredes, con indicación del tamaño en bytes de cada uno. Si la extracción de paredes falla, se devuelve un JSON de paredes vacío para no bloquear la visualización del mapa en Unity.

---

## Archivos de referencia

- [slam_server.py]({{ "/assets/downloads/rosmaster/slam_server.py" | relative_url }})
- [wifi_sampler_realtime.py]({{ "/assets/downloads/rosmaster/wifi_sampler_realtime.py" | relative_url }})
- [start_slam_server.sh]({{ "/assets/downloads/rosmaster/start_slam_server.sh" | relative_url }})
