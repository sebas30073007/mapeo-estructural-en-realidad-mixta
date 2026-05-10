---
layout: default
title: "Objetivo del Proyecto"
parent: "Visión General"
nav_order: 1
---

# Objetivo del Proyecto

Desarrollar un sistema robótico capaz de mapear el entorno físico y representar su estructura dentro de una aplicación de realidad mixta, con el fin de **facilitar la exploración remota de espacios interiores** y extraer información útil de los recorridos realizados.

## Objetivo principal — Mapas de calor de señal WiFi

La aplicación concreta que impulsa el desarrollo actual es la generación automática de mapas de calor de intensidad de red inalámbrica. El robot recorre de forma autónoma el área de interés —de manera similar a una Roomba— midiendo la potencia de la señal WiFi en cada punto del espacio. Con esos datos se construye un mapa de calor que refleja la distribución real de la cobertura en el interior del recinto.

A partir de ese mapa se persiguen dos metas concretas:

1. **Identificar la ubicación óptima del módem** — mediante algoritmos de optimización (en desarrollo) que determinen el punto de colocación que maximice la cobertura homogénea en toda la vivienda u oficina.
2. **Detectar fuentes de interferencia estructural** — elementos como puertas metálicas, muros de concreto reforzado u otras estructuras que degraden de forma significativa la propagación de la señal, orientando al usuario hacia soluciones concretas (reposicionamiento del equipo, repetidores puntuales, etc.).

## Control remoto mediante realidad mixta

El operador interactúa con el sistema a través de una aplicación de realidad mixta ejecutada en unos lentes Meta Quest: visualiza el video en tiempo real del robot, observa la reconstrucción digital del entorno conforme se construye y controla el movimiento del robot sin necesidad de estar físicamente presente en el espacio escaneado.
