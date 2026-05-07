---
layout: home
title: Inicio
nav_order: 0
---

# Mapeo Estructural en Realidad Mixta

Sistema de teleoperación robótica para exploración, mapeo estructural y visualización de entornos físicos en realidad mixta.

![Robot omnidireccional]({{ "/assets/img/robot/Rosbot.png" | relative_url }})

El proyecto integra un robot omnidireccional equipado con LiDAR, cámara de profundidad, ROS2 y una aplicación desarrollada en Unity para Meta Quest. A partir de mapas generados mediante SLAM, el sistema procesa la información del entorno, detecta estructuras principales y reconstruye paredes dentro de un mundo digital visible desde realidad mixta.

## Capacidades principales

- Teleoperación de un robot omnidireccional.
- Generación de mapas mediante SLAM.
- Procesamiento del mapa para limpieza y extracción estructural.
- Detección de muros, esquinas y puntos relevantes.
- Visualización del entorno en Meta Quest.
- Panel de video del robot dentro de la aplicación XR.
- Análisis de intensidad de red mediante mapa de calor.
