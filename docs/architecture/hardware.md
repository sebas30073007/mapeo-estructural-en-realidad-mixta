---
layout: default
title: "Hardware del Robot"
parent: "Arquitectura del Sistema"
nav_order: 2
---

# Hardware del Robot

El robot utilizado es un **Husarion ROSbot 2R**, una plataforma robótica comercial de código abierto diseñada específicamente para investigación y desarrollo con ROS. Su chasis omnidireccional y su conjunto de sensores lo hacen adecuado para tareas de mapeo autónomo en interiores.

## Especificaciones del ROSbot 2R

| Componente | Detalle |
|---|---|
| **Unidad de cómputo** | Raspberry Pi 4 Model B — ARM Cortex-A72 (64-bit), hasta 1.8 GHz, 2 GB RAM |
| **Sistema operativo** | Ubuntu 24.04 LTS (Noble) |
| **Middleware robótico** | ROS 2 Jazzy Jalisco |
| **LiDAR** | RPLIDAR A2M8 — escaneo 360°, rango hasta 12 m, 8 000 muestras/s |
| **Cámara de profundidad** | Orbbec Astra Pro — RGB + profundidad, rango 0.4–8 m, profundidad 640×480 @ 30 fps |
| **Tracción** | 4 ruedas con motores DC — configuración omnidireccional (mecanum-like) |
| **Fuente de energía** | Batería Li-Ion 3S 11.1 V integrada, cargador incluido |
| **Conectividad** | WiFi 802.11 b/g/n/ac (2.4 GHz y 5 GHz), USB 3.0, GPIO |

## LiDAR — RPLIDAR A2M8

El RPLIDAR A2M8 de Slamtec realiza un escaneo láser de 360° en un único plano horizontal. Sus características principales en el contexto de este proyecto son:

- **Frecuencia de escaneo:** 10 Hz (hasta 15 Hz en modo máximo).
- **Resolución angular:** 0.9° típico, hasta 0.45° en modo de alta densidad.
- **Rango efectivo:** 0.15–12 m, con precisión de ±1% de la distancia.
- **Interfaz:** UART / USB mediante adaptador incluido; driver ROS oficial (`rplidar_ros`).

El nodo de ROS publica los datos en el tópico `/scan` como mensajes de tipo `sensor_msgs/LaserScan`, que son consumidos directamente por SLAM Toolbox.

## Cámara de profundidad — Orbbec Astra Pro

La Orbbec Astra Pro es una cámara RGB-D que combina sensor de profundidad por luz estructurada con una cámara de color independiente:

- **Resolución de profundidad:** 640×480 px a 30 fps.
- **Resolución RGB:** 1280×960 px a 30 fps. En este proyecto se usa a resolución reducida para la transmisión de video al visor.
- **Rango efectivo de profundidad:** 0.4–8 m.
- **Tecnología:** luz estructurada infrarroja (compatible con OpenNI2).
- **Interfaz:** USB 2.0.
- **Driver ROS:** paquete `astra_camera` (Orbbec ROS SDK).

La cámara proporciona la imagen de video que se transmite a los lentes Meta Quest y puede usarse para enriquecer la reconstrucción 3D del espacio en fases posteriores del proyecto.

## Tracción omnidireccional

El ROSbot 2R incorpora cuatro motores DC con encoders que permiten movimiento en cualquier dirección sin necesidad de girar previamente el chasis. Esto es fundamental para:

- **Escaneo eficiente:** el robot puede moverse lateralmente o en diagonal para cubrir el área de forma más uniforme.
- **Maniobrabilidad en espacios estrechos:** pasillos, esquinas y zonas con obstáculos cercanos.

El controlador de motores publica y suscribe en los tópicos `/cmd_vel` (comandos) y `/odom` (odometría) siguiendo el estándar de ROS.

## Conectividad WiFi

La interfaz WiFi del robot cumple una doble función:

1. **Comunicación con los lentes** — a través de Tailscale, transmite video, datos del LiDAR y recibe comandos de movimiento.
2. **Medición de señal** — el módulo WiFi es también el sensor que registra la intensidad de la red (RSSI) en cada posición durante el recorrido, dato que alimenta el mapa de calor.
