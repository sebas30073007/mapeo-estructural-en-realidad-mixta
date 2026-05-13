---
layout: default
title: "Control con Joystick"
parent: "Teleoperación"
nav_order: 2
---

# Control con Joystick

El script `UdpTeleopSender` soporta dos backends de lectura de input de los controladores del Meta Quest 3: el SDK nativo de Meta (OVR) y el sistema genérico Unity XR Input. Ambos se pueden seleccionar explícitamente o dejar en modo `Auto`, en el que el script intenta OVR primero y cae al backend genérico si OVR no está disponible. Adicionalmente, cuando la aplicación se ejecuta dentro del editor de Unity, se activa un modo de teclado que permite probar la teleoperación sin necesidad del casco.

---

## Mapeo de ejes

La siguiente tabla describe cómo cada entrada física del controlador se convierte en una componente de velocidad del robot:

| Entrada fisica | Componente de velocidad | Velocidad maxima | Notas |
|----------------|------------------------|-------------------|-------|
| Joystick izquierdo, eje Y (adelante/atras) | `vx` (lineal) | 0.15 m/s | Eje Y positivo = avance |
| Joystick izquierdo, eje X (izquierda/derecha) | `vy` (lateral) | 0.15 m/s | Si `joystickRightIsNegativeVy = true`, joystick a la derecha genera `vy` negativo, igual que la convención del teleop web |
| Gatillo izquierdo - gatillo derecho | `wz` (angular) | 0.5 rad/s | Gatillo izquierdo = giro a la izquierda (positivo); gatillo derecho = giro a la derecha (negativo) |

Los valores maximos se leen de los campos `maxLinear` (0.15 m/s) y `maxAngular` (0.5 rad/s) del componente. Ambos son configurables desde el Inspector de Unity sin necesidad de recompilar.

El calculo de velocidades aplica directamente la magnitud normalizada del joystick o el diferencial de gatillos:

```
vx = joystick.y * maxLinear
vy = joystick.x * maxLinear
wz = (gatilloIzquierdo - gatilloDerecho) * maxAngular
```

---

## Deadzones

Para evitar deriva involuntaria causada por imprecision mecanica de los joysticks y los gatillos, el script aplica dos tipos de deadzone independientes:

**Deadzone radial del joystick** (`joystickDeadzone = 0.15`): se evalua sobre la magnitud del vector 2D completo, no sobre cada eje por separado. Si la magnitud del joystick es menor o igual a 0.15, la salida es cero en ambos ejes. Esto evita que una presion diagonal pequena genere movimiento solo en uno de los dos ejes.

**Deadzone lineal de los gatillos** (`triggerDeadzone = 0.05`): se aplica a cada gatillo de forma escalar. Si el valor del gatillo es menor o igual a 0.05, la salida es cero.

En ambos casos, una vez superado el umbral del deadzone, se usa `Mathf.InverseLerp(deadzone, 1f, valor)` para que la salida empiece en 0 en el borde del deadzone y llegue a 1 en el maximo recorrido. Sin esta normalizacion, existiria un salto brusco de velocidad en el momento en que el joystick o el gatillo cruzara el umbral, porque la velocidad pasaria instantaneamente de 0 al valor correspondiente al borde de la deadzone.

| Parametro        | Valor por defecto | Tipo de deadzone | Aplicacion |
|------------------|-------------------|------------------|------------|
| `joystickDeadzone` | 0.15            | Radial (magnitud del vector) | Joystick izquierdo X e Y conjuntamente |
| `triggerDeadzone`  | 0.05            | Lineal (escalar) | Gatillo izquierdo y derecho por separado |

---

## Backends de input

**Modo Auto (por defecto):** el script intenta leer con `OVRInput.Get()`. Si la llamada lanza una excepcion (OVR SDK no presente o no inicializado), marca `ovrAvailable = false` y reintenta con el backend Unity XR Input en el mismo frame.

**Meta OVR Input:** usa `OVRInput.Axis2D.PrimaryThumbstick` para el joystick y `OVRInput.Axis1D.PrimaryIndexTrigger` para los gatillos, a traves de `OVRInput.Controller.LTouch` y `RTouch` respectivamente.

**Unity XR Input:** obtiene los dispositivos mediante `InputDevices.GetDeviceAtXRNode` para `LeftHand`, `RightHand` y el nodo del joystick configurable. Lee los valores con `TryGetFeatureValue` usando `CommonUsages.primary2DAxis` y `CommonUsages.trigger`. Si algun dispositivo no es valido, intenta reinicializar los handles antes de cada lectura.

**Teclado en Unity Editor** (solo compilaciones con `UNITY_EDITOR`): activo cuando `useKeyboardInEditor = true`.

| Tecla | Accion |
|-------|--------|
| W     | Avance (vx positivo) |
| S     | Retroceso (vx negativo) |
| A     | Lateral izquierda (vy negativo) |
| D     | Lateral derecha (vy positivo) |
| Q     | Simula gatillo izquierdo (giro a la izquierda) |
| E     | Simula gatillo derecho (giro a la derecha) |

---

## Archivos de referencia

- [UdpTeleopSender_TriggersOnlyUDP.cs]({{ "/assets/downloads/Scripts_Unity_App/UdpTeleopSender_TriggersOnlyUDP.cs" | relative_url }})
