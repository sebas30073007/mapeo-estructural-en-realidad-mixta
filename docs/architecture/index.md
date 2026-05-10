---
layout: default
title: "Arquitectura del Sistema"
nav_order: 2
has_children: true
---

# Arquitectura del Sistema

El sistema está compuesto por **dos nodos físicos** — el robot y los lentes Meta Quest — conectados mediante una red privada virtual (Tailscale). Toda la adquisición de datos y el procesamiento de SLAM se ejecutan de forma embebida en el robot; los lentes actúan exclusivamente como interfaz de visualización y control.

## Contenido de esta sección

- [Arquitectura General](full-system.md)
- [Hardware del Robot](hardware.md)
- [Software del Sistema](software.md)
- [Flujo de Datos](data-flow.md)
