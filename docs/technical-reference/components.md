---
layout: default
title: "Componentes"
parent: "Ficha Técnica"
nav_order: 1
---

# Componentes

Esta pagina es el inventario completo de los scripts y modulos que forman el sistema, organizados por nodo de ejecucion y funcion dentro de la arquitectura. Los scripts del robot corren sobre Ubuntu 24.04 con ROS 2 Jazzy. Los scripts de Unity corren dentro de la aplicacion Meta Quest 3.

---

## Scripts del robot

| Script | Rol | Puerto | Dependencias |
|---|---|---|---|
| `slam_server.py` | Servidor HTTP Flask — genera y sirve el mapa SLAM bajo demanda | :5008 HTTP | Flask, flask-cors, ROS 2 nav2_map_server |
| `extract_walls.py` | Procesa el mapa PGM y genera un JSON de segmentos de pared y posiciones de personas | — | OpenCV, NumPy, PIL, PyYAML |
| `wifi_sampler_realtime.py` | Nodo ROS 2 — muestrea la intensidad de senal WiFi (RSSI) via `iw` y envia cada muestra por UDP al Quest | :5007 UDP out | rclpy, tf2_ros, `iw` |
| `start_slam_server.sh` | Script de arranque del servidor Flask; configura el entorno ROS 2 antes de lanzar el proceso | — | ROS 2 Jazzy, workspace del robot |

El flujo de generacion de mapa arranca en `slam_server.py`: cuando recibe `GET /generate_map`, llama a `map_saver_cli` de Nav2 para guardar `current_map.pgm` y `current_map.yaml`, despues ejecuta `extract_walls.py` sobre esos archivos y devuelve los tres artefactos empaquetados en un unico JSON.

---

## Scripts Unity — Nucleo del sistema

| Script | Rol | Puerto |
|---|---|---|
| `SLAMMapDownloader` | Descarga el mapa por HTTP y distribuye los datos (PGM, YAML, walls JSON) a los demas componentes | :5008 HTTP |
| `MapFrameConverter` | Pivote de coordenadas entre el sistema ROS (x adelante, y izquierda) y el sistema Unity (x derecha, z adelante) | — |
| `WallGenerator` | Instancia cubos que representan paredes a partir del archivo `walls.json` descargado | — |
| `PGMViewer` | Parsea el mapa PGM recibido en base64 y genera una textura que se muestra en el panel de UI | — |
| `UdpWifiReceiver` | Recibe las muestras de senal WiFi enviadas por el robot y las pasa al renderer del mapa de calor | :5007 UDP in |
| `HeatmapGridRenderer` | Renderiza el mapa de calor de cobertura WiFi usando interpolacion de distancia inversa ponderada (IDW) | — |
| `WifiColorMap` | Convierte valores de RSSI en dBm a un color en un gradiente configurable (rojo-amarillo-verde) | — |
| `WifiSample` | Modelo de datos de una muestra WiFi individual: RSSI, coordenadas de mapa, timestamp, SSID, BSSID | — |
| `UdpTeleopSender_TriggersOnlyUDP` | Captura la entrada de los triggers del controlador y envia comandos de movimiento `{vx, vy, wz}` por UDP | :5002 UDP out |
| `RosbotVideoStreamReceiver` | Recibe el stream de video por ZMQ en un hilo secundario y actualiza la textura de video en cada `Update()` | :5555 ZMQ |
| `RosbotEndpointManager` | Singleton marcado `DontDestroyOnLoad` que almacena la IP actual del robot y la expone al resto del sistema | — |
| `SessionPersistenceManager` | Guarda y recupera la configuracion de sesion (IP, preferencias) usando `PlayerPrefs` | — |
| `MapBoundsAutoCollider` | Calcula automaticamente el collider del area del mapa a partir de las dimensiones del PGM y la resolucion YAML | — |
| `WallManipulator` | Permite al operador reposicionar paredes individuales manualmente en el espacio mixto | — |

---

## Scripts Unity — UI y sesion

| Script | Rol |
|---|---|
| `WelcomeFlowController` | Controla el flujo de onboarding que se muestra en el primer arranque de la aplicacion |
| `ControlsPanelUI` | Panel principal de controles del operador: botones de accion, indicadores de estado y acceso a submodos |
| `RosbotEndpointPanelController_v2` | Panel de configuracion de la IP del robot, integrado en el flujo de sesion |
| `RosbotIpKeypadController` | Teclado virtual en XR para introducir direcciones IP sin controlador fisico de teclado |
| `BillboardToCamera` | Orienta los paneles de UI hacia la camara del usuario en cada frame para mantener la legibilidad |
| `ControllerToggleUI` | Alterna la visibilidad del conjunto de paneles de UI con un gesto o boton del controlador |
| `RosbotSensorStatusReceiver` | Recibe mensajes de estado de los sensores del robot y actualiza los indicadores visuales |
| `RosbotTelemetryStatusPanel` | Panel que muestra datos de telemetria del robot: bateria, conectividad, estado de los sensores |
| `TMPInputFocusHelper` | Resuelve los problemas de foco en campos de texto TextMeshPro dentro de entornos XR de OpenXR/OVR |

---

## Scripts de desarrollo experimental

Los siguientes scripts se encuentran en el repositorio pero no estan integrados en la build de produccion. Representan prototipos, herramientas de prueba o funcionalidades en estudio para iteraciones futuras.

| Script / Modulo | Descripcion |
|---|---|
| `WallGeneratorConPersonas.cs` | Version de `WallGenerator` que ademas renderiza en la escena las personas detectadas por el algoritmo RANSAC en `extract_walls.py` |
| `AccessPointSignalSimulator.cs` | Simula la distribucion espacial de la senal WiFi sin necesidad de robot fisico; util para pruebas de UI del mapa de calor |
| `OVRControllerTriggerDebug.cs` | Imprime en consola los valores raw de los triggers del controlador OVR para depuracion de input |
| `prob_persona.py` | Calcula la probabilidad bayesiana de que un contorno detectado en el mapa de ocupacion corresponda a una persona |
| `detector_objetos_google_vision.py` | Captura una imagen con la camara Orbbec Astra Pro, la envia a Google Vision API y devuelve un JSON de objetos detectados para consumo en Unity |
| `extract_walls_test_local.py` | Version del procesador de mapa para pruebas en maquina de desarrollo; usa el archivo de referencia `casa.yaml` en lugar del mapa del robot |

---

## Archivos de referencia

Los scripts de Unity estan disponibles para su descarga directa:

[Scripts Unity completos]({{ "/assets/downloads/Scripts_Unity_App/" | relative_url }})

Los scripts del robot (`slam_server.py`, `extract_walls.py`, `wifi_sampler_realtime.py`, `start_slam_server.sh`) se distribuyen junto con el workspace de ROS 2 del proyecto.
