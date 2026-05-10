---
layout: default
title: "Resumen del Sistema"
parent: "Visión General"
nav_order: 3
---

# Resumen del Sistema

El sistema se compone de dos nodos físicos que se comunican de forma remota a través de una red privada virtual, y una capa de software que integra navegación autónoma, análisis de señal y visualización en realidad mixta.

## Hardware del robot

| Componente | Función |
|---|---|
| Chasis omnidireccional | Movilidad en cualquier dirección sin necesidad de girar —clave para escanear espacios estrechos con precisión— |
| LiDAR | Escaneo 2D del entorno para construcción del mapa estructural (paredes, obstáculos) |
| Cámara de profundidad | Percepción 3D del espacio para enriquecer la reconstrucción y detectar desniveles u objetos |
| Raspberry Pi | Unidad de cómputo embebida; ejecuta el stack de ROS, gestiona sensores y publica datos hacia los lentes |

## Interfaz del operador — Meta Quest

El operador usa unos lentes **Meta Quest** con la aplicación desarrollada en Unity. Desde ahí puede:

- Ver el **video en tiempo real** del robot superpuesto en su campo de visión.
- Observar la **reconstrucción digital del espacio** conforme el robot lo recorre.
- **Controlar el movimiento** del robot mediante un joystick virtual o gestos.
- Consultar el **mapa de calor de señal WiFi** generado durante el recorrido.

## Comunicación — Tailscale

Robot y lentes se conectan mediante **Tailscale**, una VPN basada en WireGuard que asigna una IP fija a cada dispositivo independientemente de la red local en la que se encuentren. Esto permite operación remota sin configuración de puertos ni infraestructura adicional, y mantiene la comunicación cifrada de extremo a extremo.

```
Robot (Raspberry Pi)  ←→  Tailscale VPN  ←→  Lentes de realidad mixta
     IP fija                                        IP fija
```

## Flujo de operación resumido

1. El operador lanza la aplicación en los lentes y se conecta al robot vía Tailscale.
2. El robot inicia el recorrido autónomo del espacio (o es guiado manualmente).
3. El LiDAR y la cámara de profundidad construyen el mapa estructural en tiempo real mediante SLAM.
4. Simultáneamente, el robot registra la intensidad de señal WiFi en cada posición del recorrido.
5. Los datos se transmiten a los lentes, que actualizan la reconstrucción 3D y el mapa de calor.
6. Al finalizar el recorrido, el sistema presenta el mapa completo con la distribución de señal y, en versiones futuras, la recomendación de ubicación óptima del módem.
