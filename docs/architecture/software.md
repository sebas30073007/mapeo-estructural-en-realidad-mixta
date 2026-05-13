---
layout: default
title: "Software del Sistema"
parent: "Arquitectura del Sistema"
nav_order: 3
---

# Software del Sistema

El sistema se apoya en dos stacks de software independientes que se comunican entre sí mediante UDP: uno que corre en el robot (Linux / ROS) y otro que corre en los lentes Meta Quest (Android / Unity).

## Stack del robot — ROS sobre Ubuntu

| Capa | Tecnología |
|---|---|
| Sistema operativo | Ubuntu 24.04 LTS (Noble) |
| Middleware robótico | ROS 2 Jazzy Jalisco |
| Mapeo y localización | SLAM Toolbox |
| Driver LiDAR | `rplidar_ros` |
| Driver cámara de profundidad | `astra_camera` (Orbbec ROS SDK / OpenNI2) |
| Comunicación hacia lentes | `slam_server.py` (Flask HTTP :5008) + nodo ROS 2 UDP |
| Control de motores | Stack de tracción del ROSbot 2R (Husarion) |

### ROS 2 Jazzy

ROS 2 (Robot Operating System 2) es el middleware estándar en robótica. Proporciona un sistema de publicación/suscripción de mensajes entre nodos, herramientas de visualización (RViz2), abstracción de drivers de sensores y un ecosistema de paquetes listos para usar. En este proyecto los nodos principales son:

- **`rplidar_ros`** — publica escaneos en `/scan`.
- **`astra_camera`** — publica frames de video y datos de profundidad de la Orbbec Astra Pro.
- **`slam_toolbox`** — suscribe a `/scan` y publica el mapa ocupacional en `/map`.
- **`wifi_sampler`** — nodo ROS 2 que mide RSSI por posición usando TF2 y envía muestras por UDP al puerto 5007.
- **`slam_server`** — servidor HTTP Flask en el puerto 5008 que genera el mapa bajo demanda y lo sirve a los lentes.

### SLAM Toolbox

SLAM Toolbox es el paquete de ROS utilizado para construir el mapa estructural del entorno. Implementa SLAM (Simultaneous Localization and Mapping) en 2D usando los datos del LiDAR:

- Construye y actualiza continuamente un mapa de celdas de ocupación.
- Localiza al robot dentro del mapa en tiempo real.
- Expone un servicio (`/slam_toolbox/save_map`, `/map`) que permite solicitar el mapa actual en cualquier momento.

El mapa **no se transmite en continuo**. Solo se envía cuando el operador solicita explícitamente el cálculo desde los lentes (botón *Calculate Map*), momento en el que el nodo recupera el mapa, aplica filtros de procesamiento y genera la geometría de paredes para su renderizado en Unity.

---

## Stack de los lentes — Unity sobre Android (Meta Quest)

| Capa | Tecnología |
|---|---|
| Plataforma de hardware | Meta Quest (2 / 3 / Pro) |
| Sistema operativo | Android (AOSP personalizado por Meta) |
| Motor de juego / desarrollo | Unity |
| SDK de realidad mixta | Meta XR All-in-One SDK v83 |
| Comunicación con el robot | Cliente UDP (C# dentro de Unity) |

### Meta Quest

Los lentes Meta Quest ejecutan Android como sistema operativo base, sobre el cual Meta añade su capa de runtime de realidad mixta (OpenXR + Meta XR Runtime). Las aplicaciones Unity se compilan como APKs de Android y se instalan directamente en el dispositivo.

### Meta XR All-in-One SDK v83

El Meta XR All-in-One SDK es el paquete unificado de Unity que engloba todos los módulos necesarios para desarrollar en Meta Quest:

- **OVR Plugin** — integración nativa con el hardware de Meta Quest (tracking de manos, controladores, passthrough, rendering estéreo).
- **Interaction SDK** — sistema de interacción con el entorno XR (raycast, botones 3D, joystick virtual).
- **Passthrough API** — acceso a la cámara del visor para mezclar el mundo real con el digital.
- **Spatial Anchors** — anclaje de objetos virtuales al espacio físico (para versiones futuras del proyecto).

La versión 83 es compatible con Unity 2022.3 LTS y con los modelos Quest 2, Quest 3 y Quest Pro.

### Aplicación Unity

La aplicación desarrollada en Unity dentro del SDK de Meta gestiona:

1. **Recepción y decodificación de video** — el stream UDP del robot se decodifica y renderiza en un panel visible en el espacio mixto.
2. **Recepción de datos LiDAR** — se parsean y almacenan las lecturas para retroalimentar la interfaz.
3. **Solicitud y renderizado del mapa** — cuando el operador pulsa *Calculate Map*, se envía la solicitud al robot; la respuesta se procesa y se instancian los objetos 3D que representan las paredes en el espacio digital.
4. **Mapa de calor WiFi** — los datos de intensidad de señal recibidos se proyectan sobre el plano del mapa para generar la visualización de calor.
5. **Joystick virtual** — captura la entrada del operador y publica comandos de velocidad lineal y angular hacia el robot vía UDP.
