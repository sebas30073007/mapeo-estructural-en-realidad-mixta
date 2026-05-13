---
layout: default
title: "Flujo del Operador"
parent: "Teleoperación"
nav_order: 3
---

# Flujo del Operador

Una sesion de operacion abarca el ciclo completo desde el arranque del Meta Quest 3 hasta la finalizacion del recorrido con el mapa de calor de cobertura WiFi visible en realidad mixta. El operador interactua con paneles UI flotantes dentro de los lentes para configurar la conexion, actualizar el mapa SLAM del robot y controlar el movimiento, todo sin necesidad de acceder a ningun otro dispositivo durante la sesion activa.

---

## Secuencia de sesion

1. **Arranque de los lentes:** al iniciar la aplicacion, el sistema detecta si es la primera vez que se ejecuta. En ese caso muestra el flujo de bienvenida con instrucciones iniciales. En sesiones posteriores se carga directamente la pantalla principal con todos los paneles operativos.

2. **Introduccion de la IP del robot:** el operador introduce la direccion IP Tailscale del ROSbot 2R mediante el teclado virtual del Quest. La IP se valida en el momento de confirmar y, si es correcta, se guarda de forma persistente en el dispositivo.

3. **Pre-relleno en sesiones posteriores:** gracias a la persistencia via `PlayerPrefs`, el campo de IP aparece pre-rellenado con el ultimo valor guardado al abrir el panel en cualquier sesion posterior, eliminando la necesidad de introducirla de nuevo salvo que cambie la red.

4. **Actualizacion del mapa SLAM:** el operador pulsa el boton "Update SLAM", lo que desencadena una secuencia de health check de la conexion con el robot, descarga del mapa de ocupacion y renderizado de las paredes detectadas en el espacio fisico del operador.

5. **Control de movimiento activo:** con el `UdpTeleopSender` habilitado, el operador mueve el robot con el joystick izquierdo y los gatillos del controlador izquierdo. Los comandos se envian continuamente a 10 Hz.

6. **Monitoreo del estado:** un panel de estado permanece visible durante la sesion e indica la conectividad actual con el robot, la IP activa y el ultimo payload enviado, permitiendo al operador detectar problemas de red sin salir del entorno de realidad mixta.

---

## Persistencia de configuracion

El componente `SessionPersistenceManager` gestiona el guardado y la restauracion de los datos generados durante una sesion. Los datos se organizan en carpetas con marca de tiempo dentro de `Application.persistentDataPath/SavedSessions/`, donde cada carpeta recibe el nombre en formato `yyyyMMdd_HHmmss`.

Cada sesion guardada contiene dos archivos:

| Archivo | Contenido | Componente origen |
|---------|-----------|-------------------|
| `walls.json` | Geometria de paredes generada por el WallGenerator a partir del mapa SLAM | `WallGenerator` |
| `wifi_samples.json` | Lista de muestras de RSSI WiFi con sus posiciones en el mapa | `HeatmapGridRenderer` |

Al cargar una sesion (metodo `LoadLatestSession` o `LoadSessionFromPath`), el manager primero limpia el estado visual actual (muestras del heatmap y paredes renderizadas) y luego reconstruye la escena a partir de los archivos guardados. La operacion de carga ordena las carpetas de sesion por nombre descendente para identificar automaticamente la mas reciente.

La IP del robot se gestiona de forma independiente a traves de `RosbotEndpointManager`, que usa la clave `"ROSBOT_ENDPOINT_IP"` en `PlayerPrefs`. Esta separacion permite que la IP persista incluso cuando no hay ninguna sesion de datos guardada.

---

## Modificacion de parametros en runtime

Todos los parametros de red (direcciones IP, puertos) son modificables desde los paneles UI dentro del casco sin necesidad de recompilar ni redeployar la aplicacion. El componente `RosbotEndpointPanelController_v2` actua como controlador UI para el `RosbotEndpointManager` y expone las siguientes acciones desde botones de la interfaz:

- **ApplyIp():** lee el texto del campo de entrada, lo valida a traves de `TrySetIp()` y actualiza la IP activa si es valida. Muestra retroalimentacion textual inmediata sobre el resultado.
- **ResetInputToSavedIp():** restaura el campo de texto al ultimo valor guardado, descartando cualquier edicion en curso.
- **ResetEndpointToDefault():** restablece la IP a la direccion por defecto definida en el Inspector.

El metodo `SetTargetInputField(TMP_InputField field)` permite reutilizar una unica instancia del controlador para gestionar multiples campos de IP distintos. En lugar de instanciar un controlador por cada campo, un boton o evento puede llamar a `SetTargetInputField` para apuntar el controlador al campo relevante en cada momento, manteniendo la escena mas limpia y reduciendo la duplicacion de logica.

Al arrancar el panel (`syncInputOnStart = true`), el controlador llama automaticamente a `ResetInputToSavedIp()` para que el campo muestre siempre la IP actualmente guardada, evitando que el operador vea un campo vacio o un valor incorrecto al abrir el panel por primera vez en la sesion.

---

## Archivos de referencia

- [RosbotEndpointPanelController_v2.cs]({{ "/assets/downloads/Scripts_Unity_App/Scripts_Rosbot/RosbotEndpointPanelController_v2.cs" | relative_url }})
- [SessionPersistenceManager.cs]({{ "/assets/downloads/Scripts_Unity_App/SessionPersistenceManager.cs" | relative_url }})
