---
layout: default
title: "Aplicación en Unity"
parent: "Aplicación en Realidad Mixta"
nav_order: 1
---

# Aplicación en Unity

## Entorno de desarrollo

| Elemento | Versión / Detalle |
|---|---|
| Motor | Unity Editor 2022.3.58f1 (LTS) |
| SDK de realidad mixta | Meta XR All-in-One SDK v83 |
| Plataforma de compilación | Android (Meta Quest) |
| Comunicación con el robot | UDP (scripts en C#) |

### Por qué Unity 2022.3.58f1

Esta versión del editor no es la más reciente, pero es la que ofreció mayor compatibilidad con el Meta XR All-in-One SDK v83 y fue la recomendada durante el desarrollo del proyecto. La rama LTS (Long-Term Support) garantiza estabilidad y correcciones de seguridad sin cambios de API que pudieran romper la integración con el SDK de Meta.

### Meta XR All-in-One SDK v83

El SDK unificado de Meta concentra en un solo paquete de Unity todos los módulos necesarios para desarrollar en Meta Quest:

- **OVR Plugin** — rendering estéreo, tracking de posición y rotación de los lentes y los controladores.
- **Interaction SDK** — sistema de interacción 3D (botones virtuales, joystick, raycast).
- **Passthrough API** — acceso a la cámara del visor para superponer el mundo digital sobre el entorno real.

## Comunicación UDP

Todos los scripts de red están implementados en C# utilizando la clase `System.Net.Sockets.UdpClient` de .NET. No se usa ninguna librería de red de terceros. Cada canal de comunicación se gestiona en un hilo de fondo independiente para no bloquear el hilo principal de Unity (render loop).

Los cuatro canales activos son:

| Puerto | Rol en la app |
|---|---|
| 5000 | Recepción de video — decodifica JPEG y actualiza textura del panel |
| 5002 | Recepción de datos LiDAR — parsea distancias y actualiza visualización |
| 5005 | Recepción de datos SLAM — genera geometría 3D de paredes bajo demanda |
| 5007 | Envío de comandos de movimiento — publica velocidad lineal y angular |

## Compilación y despliegue

La aplicación se compila como un APK de Android y se instala directamente en los lentes Meta Quest mediante ADB (Android Debug Bridge) o a través de Meta Quest Developer Hub. Los lentes deben estar en **Developer Mode** para aceptar instalaciones externas a la Meta Store.
