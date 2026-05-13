---
layout: default
title: "Arquitectura General"
parent: "Arquitectura del Sistema"
nav_order: 1
---

# Arquitectura General

## Evolución del diseño

El diseño original contemplaba tres nodos: el robot, un servidor intermedio de mayor capacidad de cómputo que procesaría los datos antes de enviarlos, y los lentes Meta Quest. Sin embargo, durante el desarrollo se comprobó que la Raspberry Pi 4 del robot tiene capacidad suficiente para ejecutar el stack de ROS y SLAM Toolbox de forma embebida, por lo que el servidor intermedio fue eliminado. La arquitectura final opera con **dos nodos únicamente**.

## Arquitectura de dos nodos

```
┌─────────────────────────────────┐         Tailscale VPN        ┌──────────────────────────────┐
│           ROBOT                 │  ◄──────────────────────────► │       META QUEST             │
│                                 │                               │                              │
│  LiDAR ──► ROS 2 / SLAM Toolbox │  ──► Video   (ZMQ :5555)      │  Aplicación Unity            │
│  Cámara ──► Streaming de video  │  ──► WiFi    (UDP :5007)      │  Meta XR All-in-One SDK      │
│  WiFi ───► Medición de señal    │  ──► Mapa    (HTTP :5008)     │                              │
│                                 │  ◄── Cmd mov (UDP :5002)      │  Joystick virtual            │
│  Ubuntu 24.04 + ROS 2 Jazzy     │                               │  Reconstrucción 3D           │
└─────────────────────────────────┘                               │  Mapa de calor WiFi          │
                                                                  └──────────────────────────────┘
```

## Responsabilidades por nodo

### Robot
- Adquisición de datos del entorno (LiDAR y cámara de profundidad).
- Ejecución local de SLAM Toolbox para construcción del mapa estructural.
- Medición de intensidad de señal WiFi por posición.
- Transmisión de video, datos de LiDAR y datos bajo demanda hacia los lentes.
- Recepción y ejecución de comandos de movimiento (velocidad lineal y angular).

### Meta Quest
- Visualización del video del robot en tiempo real.
- Renderizado de la reconstrucción digital del espacio (paredes, geometría).
- Presentación del mapa de calor de señal WiFi.
- Envío de comandos de movimiento al robot mediante joystick virtual.
- Solicitud del mapa SLAM procesado bajo demanda (botón *Calculate Map*).

## Conectividad — Tailscale

Ambos dispositivos están dados de alta en una red **Tailscale** (VPN basada en WireGuard). Tailscale asigna una IP fija a cada nodo independientemente de la red física en la que se encuentren, lo que hace que el robot y los lentes se comporten como si estuvieran en la misma red local aunque estén en ubicaciones distintas. La comunicación viaja cifrada de extremo a extremo sin necesidad de abrir puertos en el router.

## Cálculo del mapa bajo demanda

El mapa SLAM no se transmite en continuo. Cuando el operador presiona el botón **Calculate Map** en la aplicación de los lentes, se envía una solicitud al robot, que devuelve el mapa actual de SLAM Toolbox. Sobre ese mapa se aplican filtros para eliminar ruido, delimitar las estructuras de mayor relevancia (paredes, esquinas) y generar los puntos que se renderizan como geometría 3D en el mundo digital.
