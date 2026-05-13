---
layout: default
title: "Control del Robot"
parent: "Teleoperación"
nav_order: 1
---

# Control del Robot

La teleoperación del ROSbot 2R desde el Meta Quest 3 funciona convirtiendo la entrada analógica del joystick y los gatillos en comandos de velocidad que se transmiten por UDP al robot. El componente `UdpTeleopSender` en la aplicación Unity construye un payload JSON con las tres componentes de velocidad y lo envía a la dirección IP del robot a una frecuencia configurable, sin depender de ningún protocolo intermedio como ZMQ o HTTP.

---

## Formato del comando

Cada paquete enviado al puerto **5002** del robot contiene un objeto JSON con tres campos numéricos:

| Campo | Descripción | Unidad |
|-------|-------------|--------|
| `vx`  | Velocidad lineal adelante (positivo) / atrás (negativo) | m/s |
| `vy`  | Velocidad lateral izquierda/derecha (tracción omnidireccional) | m/s |
| `wz`  | Velocidad angular de giro (izquierda positivo, derecha negativo) | rad/s |

Ejemplo de payload con movimiento hacia adelante y giro leve a la izquierda:

```json
{"vx":0.1500,"vy":0.0000,"wz":0.3200}
```

Los valores se serializan siempre con cuatro decimales usando `CultureInfo.InvariantCulture`. Esto garantiza que el separador decimal sea el punto (`.`) independientemente de la configuración regional del sistema operativo del dispositivo, evitando el problema habitual en locales europeas donde la coma (`,`) es el separador decimal y rompería el JSON.

---

## Frecuencia y comportamiento idle

El parámetro `commandHz` controla la cadencia de envío. Su valor por defecto es **10 Hz**, lo que equivale a un paquete cada 100 ms. El rango configurable desde el Inspector de Unity es de 1 a 60 Hz.

Cuando todos los ejes están en reposo (magnitud de velocidad inferior a 0.0005 m/s o rad/s), el campo `sendZeroWhenIdle` determina el comportamiento:

| Valor de `sendZeroWhenIdle` | Comportamiento |
|-----------------------------|----------------|
| `true` (por defecto)        | Se envía `{"vx":0.0000,"vy":0.0000,"wz":0.0000}` a la misma frecuencia |
| `false`                     | No se envía ningún paquete mientras el input es nulo |

Mantener `sendZeroWhenIdle` activo es la configuración recomendada para operación real: el robot recibe paquetes continuamente y puede detectar la pérdida de conexión si los paquetes cesan. Además, previene que el robot siga moviéndose si el último comando recibido antes de un silencio tenía velocidad distinta de cero.

---

## Seguridad y startup

El componente implementa dos mecanismos de parada de emergencia por software:

**Al iniciar (`Start`):** se envían **10 comandos de stop consecutivos** (`vx=0, vy=0, wz=0`) antes de comenzar el bucle normal de teleoperación. Esto neutraliza cualquier comando residual que el robot pudiera haber recibido de una sesión anterior y garantiza que el robot esté detenido en el momento en que la aplicación toma el control.

**Al deshabilitarse (`OnDisable`):** si `sendStopOnDisable` está activo, se envían **5 comandos de stop** antes de cerrar el socket UDP. Este mecanismo cubre el escenario de desconexión inesperada del casco, cambio de escena en Unity, o apagado de la aplicación: el robot recibe la ráfaga de parada antes de que el socket se cierre y quede sin respuesta.

| Evento         | Comandos de stop enviados | Condición             |
|----------------|---------------------------|-----------------------|
| `Start()`      | 10                        | Siempre               |
| `OnDisable()`  | 5                         | `sendStopOnDisable = true` |

---

## Gestión de la IP del robot (RosbotEndpointManager)

La IP de destino del robot no está codificada de forma fija en `UdpTeleopSender`. En su lugar, se gestiona a través del singleton `RosbotEndpointManager`, que persiste entre escenas gracias a `DontDestroyOnLoad`.

Al arrancar la aplicación, el manager carga la IP guardada desde `PlayerPrefs` usando la clave `"ROSBOT_ENDPOINT_IP"`. Si no existe ningún valor guardado, utiliza la IP por defecto definida en el Inspector (`100.90.163.4`, correspondiente a la red Tailscale del robot).

El método `TrySetIp(string newIp)` valida el valor introducido antes de persistirlo:

1. Elimina espacios en blanco al inicio y al final.
2. Comprueba que la cadena pueda parsearse como una dirección IP válida con `IPAddress.TryParse`.
3. Si `requireIPv4` está activo, verifica que la familia de direcciones sea `InterNetwork` (IPv4).
4. Solo si pasa todas las comprobaciones, guarda el valor en `PlayerPrefs` y llama a `PlayerPrefs.Save()`.

Si la instancia del singleton no está disponible en escena, cualquier componente que necesite la IP puede llamar a `GetIp()` directamente sobre la instancia; en caso de que la IP almacenada no sea válida, el método devuelve la `defaultIp` como valor de respaldo.

---

## Archivos de referencia

- [UdpTeleopSender_TriggersOnlyUDP.cs]({{ "/assets/downloads/Scripts_Unity_App/UdpTeleopSender_TriggersOnlyUDP.cs" | relative_url }})
- [RosbotEndpointManager.cs]({{ "/assets/downloads/Scripts_Unity_App/Scripts_Rosbot/RosbotEndpointManager.cs" | relative_url }})
