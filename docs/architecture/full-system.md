---
layout: default
title: "Arquitectura General"
parent: "Arquitectura del Sistema"
nav_order: 1
---

# Arquitectura General

El sistema se compone de tres bloques principales:

1. Robot omnidireccional.
2. Procesamiento ROS para mapeo estructural.
3. Aplicación de realidad mixta en Meta Quest.

El robot captura información del entorno mediante LiDAR y cámara de profundidad. La Raspberry Pi ejecuta ROS y utiliza SLAM Toolbox para generar un mapa del espacio. Posteriormente, el mapa es procesado para limpiar ruido, extraer estructuras principales y obtener puntos representativos de muros y esquinas.

Estos puntos son enviados a la aplicación desarrollada en Unity, donde se reconstruye un mundo digital visible desde Meta Quest.
