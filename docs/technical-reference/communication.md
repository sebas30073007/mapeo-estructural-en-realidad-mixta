---
layout: default
title: "Comunicación"
parent: "Ficha Técnica"
nav_order: 4
---

# Comunicación

Esta página es una referencia completa de los canales de comunicación entre los lentes Meta Quest 3 y el robot Husarion ROSbot 2R. Todos los valores de puerto, protocolo y dirección han sido confirmados directamente en los scripts de producción del proyecto.

---

## Tabla de canales

| Puerto | Protocolo | Dirección | Script productor | Script consumidor | Contenido |
|---|---|---|---|---|---|
| 5002 | UDP | Quest → Robot | `UdpTeleopSender_TriggersOnlyUDP` | Nodo ROS 2 robot | `{"vx", "vy", "wz"}` JSON |
| 5007 | UDP | Robot → Quest | `wifi_sampler_realtime.py` | `UdpWifiReceiver` | Muestra WiFi JSON |
| 5008 | HTTP | Quest → Robot | `SLAMMapDownloader` | `slam_server.py` Flask | GET /health, GET /generate_map |
| 5555 | ZMQ | Robot → Quest | Servidor ZMQ robot | `RosbotVideoStreamReceiver` | Frames de video JPEG (topic: `video_rgb`) |

Cada protocolo se eligió según la naturaleza del dato transportado:

- **UDP** en los puertos 5002 y 5007: la latencia mínima es prioritaria. Un paquete perdido se descarta y el siguiente refleja el estado actual del joystick o de la señal.
- **HTTP** en el puerto 5008: la transferencia de mapa es una operación petición-respuesta con payload grande (PGM en base64 más JSON de paredes). TCP garantiza la entrega completa sin confirmación manual.
- **ZMQ** en el puerto 5555: el patrón publish/subscribe desacopla el productor del consumidor y facilita el manejo de backpressure cuando los frames llegan más rápido de lo que Unity los procesa.

---

## Red Tailscale

Ambos dispositivos se conectan a través de una red privada virtual **Tailscale**, que utiliza WireGuard como protocolo de transporte. Las IPs asignadas pertenecen al rango `100.x.x.x` y son estables entre sesiones una vez que cada dispositivo se ha registrado en la misma tailnet.

La IP del robot no se detecta de forma automática. El operador la introduce manualmente desde el panel de configuración dentro de los lentes, al que se accede durante el flujo de bienvenida o en cualquier momento desde los controles. El valor queda almacenado en `PlayerPrefs` mediante `SessionPersistenceManager` y se recupera en el siguiente arranque.

En el código de `wifi_sampler_realtime.py` aparece la IP de fallback del Quest utilizada durante el desarrollo:

```
100.119.230.103
```

Esta dirección corresponde a la IP Tailscale del Meta Quest 3 en el entorno de pruebas. En un despliegue diferente debe reemplazarse por la IP Tailscale real del dispositivo receptor.

---

## Formato de teleop verificado

El comando de movimiento es un string JSON con cuatro decimales de precisión. El formateo se realiza con `CultureInfo.InvariantCulture` para garantizar el uso de punto decimal en sistemas con locale europeo, donde la coma es el separador por defecto:

```json
{"vx":0.1200,"vy":0.0000,"wz":-0.3000}
```

Los tres campos tienen los siguientes rangos operativos en el ROSbot 2R:

| Campo | Descripcion | Rango en produccion |
|---|---|---|
| `vx` | Velocidad lineal adelante/atras (m/s) | -0.15 a +0.15 |
| `vy` | Velocidad lateral (m/s) | -0.15 a +0.15 |
| `wz` | Velocidad angular sobre eje vertical (rad/s) | -0.50 a +0.50 |

El nodo receptor en el robot parsea el datagrama y publica en `/cmd_vel` como `geometry_msgs/Twist`. Si no llega ningun comando durante varios ciclos consecutivos, el robot se detiene por seguridad.

---

## Nota de coherencia

Las paginas de arquitectura publicadas anteriormente mencionaban puertos 5000, 5005 y 5007 en algunos diagramas. Esos valores correspondian al diseno original del sistema antes de la integracion con el ROSbot 2R. La tabla de esta pagina refleja la implementacion final confirmada por los scripts de produccion. El puerto 5007 es el unico que se mantiene igual entre el diseno original y la implementacion final.
