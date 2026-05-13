---
layout: default
title: "Generación del SLAM"
parent: "Mapeo Estructural"
nav_order: 1
---

# Generación del SLAM

El mapa del entorno no se genera de forma continua durante la operación del robot. En su lugar, el proceso de captura se activa bajo demanda: cuando el operador lo decide desde la aplicación Unity en el Meta Quest 3, se envía una petición HTTP al robot, que entonces ejecuta el guardado del mapa, extrae las paredes y devuelve todos los datos en una única respuesta. Este diseño evita la carga computacional de actualizar el mapa en tiempo real y garantiza que Unity reciba una instantánea coherente del entorno.

---

## Servidor de mapas en el robot

El archivo `slam_server.py` implementa un servidor Flask que corre directamente en el ROSbot 2R, a la escucha en el puerto **5008** de todas las interfaces de red (`0.0.0.0`). El servidor habilita CORS para permitir peticiones desde la red local donde opera el Meta Quest 3.

El script de arranque `start_slam_server.sh` se encarga de preparar el entorno antes de lanzar el servidor. Primero carga la instalación base de ROS 2 Jazzy y luego el workspace personalizado del robot, de modo que los comandos `ros2` estén disponibles en el mismo proceso que ejecuta Flask:

```bash
# Cargar entorno de ROS2 Jazzy
source /opt/ros/jazzy/setup.bash

# Cargar tu workspace
source /home/iberomsc02/ros2_ws_02/install/setup.bash

# Ejecutar servidor Flask
exec python3 /home/iberomsc02/slam_server.py
```

El uso de `exec` reemplaza el proceso shell por el proceso Python, de forma que las señales del sistema operativo se entreguen directamente al servidor Flask.

---

## Endpoints del servidor

El servidor expone dos endpoints GET:

### GET /health

Permite a Unity verificar que el robot es alcanzable antes de solicitar la generación del mapa. Devuelve siempre HTTP 200 con el siguiente cuerpo:

```json
{
  "status": "ok",
  "message": "SLAM Server running",
  "maps_dir": "/home/iberomsc02/maps"
}
```

`SLAMMapDownloader` consulta este endpoint con un timeout de 5 segundos antes de proceder con la petición de mapa.

### GET /generate_map

Desencadena la secuencia completa de generación y empaquetado del mapa. El servidor ejecuta los siguientes pasos de forma secuencial:

1. **Guardar el mapa con `map_saver_cli`**

   Invoca el nodo de ROS 2 `nav2_map_server` para capturar el mapa que SLAM Toolbox mantiene en memoria y escribirlo en disco:

   ```
   ros2 run nav2_map_server map_saver_cli -f /home/iberomsc02/maps/current_map
   ```

   Esto genera dos archivos: `current_map.pgm` (imagen de ocupación en escala de grises) y `current_map.yaml` (metadatos del mapa: resolución, origen y umbrales de ocupación). Tras el comando se introduce una pausa de 2 segundos para asegurar que el sistema de archivos haya terminado de escribir.

2. **Extraer segmentos de pared con `extract_walls.py`**

   Se ejecuta el script de procesamiento de imagen que analiza el PGM y detecta las paredes como segmentos de línea en coordenadas métricas del frame `map`:

   ```
   python3 /home/iberomsc02/extract_walls.py \
       /home/iberomsc02/maps/current_map.yaml \
       /home/iberomsc02/maps/current_map_walls.json
   ```

   Si la extracción falla, el servidor continúa sin interrumpirse y devuelve un JSON de paredes vacío con `wall_segments: []`.

3. **Leer los archivos generados**

   El servidor abre los tres archivos producidos: el PGM se lee en modo binario para su posterior codificación en Base64, el YAML se lee como texto plano, y el JSON de paredes se lee como cadena de texto.

4. **Construir y devolver la respuesta unificada**

   Los tres contenidos se empaquetan en un único objeto JSON que se envía como respuesta HTTP al cliente Unity.

---

## Estructura de la respuesta

La respuesta del endpoint `/generate_map` es un objeto JSON con los siguientes campos:

| Campo        | Tipo    | Descripción                                                                       |
|--------------|---------|-----------------------------------------------------------------------------------|
| `status`     | string  | `"success"` si la operación fue correcta; `"error"` en caso contrario             |
| `map_name`   | string  | Nombre base del mapa generado (`"current_map"`)                                   |
| `pgm`        | string  | Contenido del archivo `.pgm` codificado en Base64                                 |
| `yaml`       | string  | Contenido del archivo `.yaml` tal como fue escrito por `map_saver_cli`            |
| `walls`      | string  | Cadena de texto que contiene el JSON de paredes (stringificado dentro del objeto) |
| `pgm_size`   | integer | Tamaño en bytes del archivo PGM original, antes de la codificación Base64         |
| `yaml_size`  | integer | Tamaño en bytes del archivo YAML                                                  |
| `walls_size` | integer | Tamaño en bytes del JSON de paredes                                               |

Cada llamada a `subprocess.run` tiene un timeout de **30 segundos**. Si alguno de los dos subprocesos —guardado del mapa o extracción de paredes— supera ese tiempo, el servidor responde con HTTP 500 e incluye `"Command timeout"` en el campo `message`. El cliente Unity aplica por su parte un timeout de 60 segundos a la petición HTTP completa para cubrir el tiempo total de ejecución en el robot.

---

## Archivos de referencia

- [slam_server.py]({{ "/assets/downloads/rosmaster/slam_server.py" | relative_url }})
- [start_slam_server.sh]({{ "/assets/downloads/rosmaster/start_slam_server.sh" | relative_url }})
