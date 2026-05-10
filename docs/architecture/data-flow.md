---
layout: default
title: "Flujo de Datos"
parent: "Arquitectura del Sistema"
nav_order: 4
---

# Flujo de Datos

Toda la comunicación entre el robot y los lentes Meta Quest se realiza mediante **UDP sobre la red virtual de Tailscale**. Se definen cuatro canales independientes, cada uno en un puerto distinto, según la naturaleza y la dirección del dato.

## Mapa de puertos

| Puerto | Dirección | Contenido | Frecuencia |
|---|---|---|---|
| **5000** | Robot → Meta Quest | Stream de video (cámara) | Continuo (~30 fps) |
| **5002** | Robot → Meta Quest | Datos del LiDAR (escaneo 360°) | Continuo (~10 Hz) |
| **5005** | Robot → Meta Quest | Datos genéricos / mapa SLAM | Bajo demanda |
| **5007** | Meta Quest → Robot | Comandos de movimiento | Continuo mientras hay entrada |

## Por qué UDP

El protocolo UDP se eligió frente a TCP por dos razones principales:

- **Latencia mínima** — el video y los datos del LiDAR deben llegar con la menor demora posible. Con TCP, las retransmisiones de paquetes perdidos introducirían jitter perceptible; con UDP, un paquete perdido simplemente se descarta y se usa el siguiente.
- **Tolerancia a pérdidas** — tanto el video como el LiDAR son streams donde un frame perdido tiene impacto visual mínimo frente al coste de retrasar todos los frames posteriores.

Para los datos de SLAM (puerto 5005), aunque la transmisión es bajo demanda, se mantiene UDP por consistencia de la arquitectura. A nivel de aplicación se implementa confirmación de recepción cuando es necesario.

---

## Puerto 5000 — Stream de video

El nodo de la cámara RealSense D435 captura frames y los comprime antes de enviarlos:

- **Resolución de transmisión:** 720 × 680 píxeles.
- **Compresión:** JPEG al 50% de calidad, lo que reduce significativamente el tamaño del frame manteniendo una calidad visual aceptable para teleoperación.
- **Frecuencia objetivo:** ~30 fps, condicionada al ancho de banda disponible en la VPN.

En el lado de los lentes, la aplicación Unity recibe cada datagrama, decodifica el JPEG y actualiza la textura del panel de video en tiempo real.

## Puerto 5002 — Datos del LiDAR

El nodo `rplidar_ros` publica los escaneos en el tópico ROS `/scan`. Un nodo puente serializa esos datos y los retransmite por UDP hacia los lentes:

- **Formato:** array de distancias (float32) correspondientes a las 360° del escaneo, acompañado de metadatos de ángulo mínimo, máximo e incremento.
- **Frecuencia:** ~10 Hz (sincronizado con el ciclo de escaneo del RPLIDAR A2M8).

Estos datos se usan en la aplicación Unity para representar en tiempo real la nube de puntos 2D del entorno inmediato del robot, como retroalimentación visual al operador.

## Puerto 5005 — Datos genéricos / SLAM bajo demanda

Este canal transporta datos que no requieren transmisión continua. El caso de uso principal es el mapa SLAM:

1. El operador pulsa **Calculate Map** en la aplicación de los lentes.
2. Los lentes envían una solicitud al robot (puede hacerse por el mismo canal o por el puerto de comandos).
3. El nodo de comunicación del robot consulta el mapa actual de SLAM Toolbox (`/map`).
4. Se aplican filtros para eliminar ruido y extraer las estructuras de mayor relevancia (paredes, esquinas, límites del espacio).
5. Los puntos resultantes se serializan y envían por el puerto 5005.
6. La aplicación Unity recibe el payload, deserializa los puntos e instancia los objetos 3D que representan las paredes en el mundo digital.

Este canal puede usarse en el futuro para transmitir otros datos no periódicos, como datos de intensidad WiFi acumulados, comandos de configuración, etc.

## Puerto 5007 — Comandos de movimiento

Este es el único canal en dirección Meta Quest → Robot. El operador usa el joystick virtual de la aplicación para generar comandos de velocidad:

- **Velocidad lineal (`linear.x`)** — desplazamiento adelante/atrás, en m/s.
- **Velocidad angular (`angular.z`)** — rotación sobre el eje vertical, en rad/s.

El mensaje sigue la estructura estándar `geometry_msgs/Twist` de ROS, serializado en formato compacto para su envío por UDP. En el robot, el nodo receptor convierte el datagrama en un mensaje ROS y lo publica en `/cmd_vel`, que es consumido por el stack de control de motores del ROSbot 2R.

La frecuencia de envío se adapta a la entrada del operador: se envía un nuevo datagrama cada vez que cambia el valor del joystick, con un mínimo de un mensaje de "parada" si el operador suelta el control.

---

## Diagrama de flujo completo

```
META QUEST                              ROBOT (Raspberry Pi 4)
──────────────────────────────────────────────────────────────────
                    Tailscale VPN

                ◄── UDP :5000 ──────── Cámara RealSense D435
                                       (720×680 px, JPEG 50%)

                ◄── UDP :5002 ──────── LiDAR RPLIDAR A2M8
                                       (escaneo 360°, ~10 Hz)

[Calculate Map] ──► solicitud ────────►
                ◄── UDP :5005 ──────── SLAM Toolbox → filtros
                                       (mapa de paredes bajo demanda)

[Joystick]      ──► UDP :5007 ────────► /cmd_vel → motores
                    (linear.x,          (velocidad lineal + angular)
                     angular.z)
```
