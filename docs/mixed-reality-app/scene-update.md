---
layout: default
title: "Actualización de Escena"
parent: "Aplicación en Realidad Mixta"
nav_order: 4
---

# Actualizacion de Escena

La actualizacion de escena es la operacion que sincroniza el mundo digital con el estado actual del mapa SLAM almacenado en el robot. No ocurre de forma automatica: el operador la dispara manualmente pulsando el boton "Update SLAM" en la interfaz del visor cuando desea que las paredes virtuales reflejen el recorrido mas reciente del robot. Este diseno deliberado evita interrupciones no deseadas en la visualizacion mientras el operador esta inspeccionando el espacio.

---

## Secuencia de actualizacion

Toda la logica reside en el metodo `DownloadMapFromRobot()` del componente `SLAMMapDownloader`, implementado como una corrutina de Unity. Los pasos se ejecutan en orden secuencial; si cualquiera de ellos falla, la corrutina se detiene y muestra un mensaje de error descriptivo en el panel de estado.

```
Operador pulsa "Update SLAM"
         |
         v
[1] Health check HTTP  GET /health  (timeout 5 s)
         |
         v
[2] GET /generate_map  (timeout 60 s)
         |
         v
[3] Deserializar respuesta JSON -> MapResponse
         |
         v
[4] Guardar archivos en persistentDataPath
     current_map.pgm  (Base64 -> bytes)
     current_map.yaml (texto plano)
     walls.json       (texto plano)
         |
         v
[5] Parsear walls.json -> WallData (extrae map_info)
         |
         v
[6] MapFrameConverter.SetMapInfo(origin, resolution, width, height)
         |
         v
[7] PGMViewer.LoadPGMFromPath(pgmPath)
         |
         v
[8] WallGenerator.LoadWallsFromPath(wallsPath)
         |
         v
[9] HeatmapGridRenderer.UpdateMapDimensionsAndRefresh(wallsPath)
     (si clearSamplesBeforeUpdate == true: ClearSession() antes)
         |
         v
[10] MapBoundsAutoCollider.Recompute()
         |
         v
     Escena actualizada
```

### Descripcion de cada paso

**Paso 1 - Health check.** Antes de pedir el mapa, se verifica que el servidor del robot esta activo con una peticion `GET /health` con timeout de 5 segundos. Si el robot no responde, la corrutina se detiene aqui y el operador recibe un mensaje sin esperar 60 segundos innecesarios.

**Paso 2 - Solicitud del mapa.** Se realiza `GET /generate_map` con un timeout de 60 segundos. Este endpoint activa en el robot la generacion y serializacion del mapa SLAM actual. El tiempo de espera largo es necesario porque el proceso de exportacion puede tomar varios segundos dependiendo del tamano del mapa.

**Paso 3 - Deserializacion JSON.** La respuesta es un JSON que se deserializa en un objeto `MapResponse` con los campos: `status`, `message`, `map_name`, `pgm` (contenido del archivo PGM en Base64), `yaml` (contenido del archivo YAML como texto), `walls` (JSON de paredes), y los tamaños en bytes de cada archivo. Si `status != "success"`, la corrutina aborta con el mensaje del campo `message`.

**Paso 4 - Guardado de archivos.** Los tres archivos se escriben en `Application.persistentDataPath`:

| Archivo | Ruta local | Contenido |
|---|---|---|
| `current_map.pgm` | `persistentDataPath/current_map.pgm` | Imagen del mapa en escala de grises (decodificada de Base64) |
| `current_map.yaml` | `persistentDataPath/current_map.yaml` | Metadatos del mapa: resolucion, origen, modo de umbral |
| `walls.json` | `persistentDataPath/walls.json` | Lista de segmentos de pared con coordenadas en el frame `map`, mas `map_info` |

**Paso 5 - Parseo de `walls.json`.** Se deserializa el JSON de paredes en un objeto `WallData` para extraer el campo `map_info`, que contiene `origin` (array [x, y]), `resolution` y las dimensiones `width` y `height` del mapa en celdas. Este bloque de informacion es el que alimenta los pasos siguientes.

**Paso 6 - Actualizacion de MapFrameConverter.** Se llama a `SetMapInfo()` con el origen, resolucion y dimensiones extraidos del paso anterior. A partir de este momento, cualquier conversion de coordenadas del frame `map` a espacio local de Unity devuelve resultados coherentes con el nuevo mapa.

**Paso 7 - Actualizacion de PGMViewer.** Se llama a `LoadPGMFromPath()` con la ruta del archivo PGM recien guardado. El componente parsea el archivo, aplica el Y-flip y actualiza la textura del `RawImage` del panel de miniatura del mapa.

**Paso 8 - Generacion de paredes.** Se llama a `LoadWallsFromPath()` en `WallGenerator`. Este componente destruye el `WallsContainer` existente (si lo hay) y genera un conjunto nuevo de cubos 3D a partir de los segmentos de pared del JSON, posicionados segun el sistema de coordenadas actualizado en el paso 6.

**Paso 9 - Actualizacion del heatmap.** Se llama a `UpdateMapDimensionsAndRefresh()` en `HeatmapGridRenderer`, pasando la ruta del `walls.json`. El componente reescala el plano del heatmap para que coincida con las nuevas dimensiones del mapa. Si la opcion `clearSamplesBeforeUpdate` esta activada, se borran primero todas las muestras de senal WiFi de la sesion anterior (equivalente a reiniciar el registro de cobertura).

**Paso 10 - Recomputo del collider.** Se llama a `Recompute()` en `MapBoundsAutoCollider`, que recalcula el `BoxCollider` para que envuelva exactamente el nuevo conjunto de objetos generados.

---

## Feedback al operador

Durante toda la secuencia, el componente actualiza continuamente un campo de texto `TMP_Text` (`statusText`) visible en el panel del visor. Cada paso tiene su propio mensaje de estado para que el operador sepa exactamente en que etapa se encuentra la operacion:

| Etapa | Mensaje mostrado |
|---|---|
| Inicio | "Conectando al robot..." |
| Peticion del mapa | "Generando mapa SLAM..." |
| Guardado de archivos | "Guardando archivos..." |
| Actualizacion de miniatura | "Actualizando PGM..." |
| Generacion de paredes | "Generando paredes..." |
| Completado | "Mapa, paredes y heatmap actualizados" |

Si se produce un error en cualquier paso, el mensaje describe tanto el tipo de fallo como el paso donde ocurrio. Por ejemplo, si la peticion HTTP falla, el mensaje incluye el codigo de respuesta HTTP y el texto del error de red; si el JSON es invalido, se muestra la excepcion del parser. Este nivel de detalle permite diagnosticar problemas de red, configuracion del servidor o corrupcion de datos sin necesidad de conectar un depurador.

---

## Persistencia entre sesiones

Los tres archivos (`current_map.pgm`, `current_map.yaml`, `walls.json`) se escriben en `Application.persistentDataPath`, que en el Meta Quest 3 corresponde al almacenamiento interno de la aplicacion, protegido de borrados accidentales y accesible entre ejecuciones.

Esto tiene una consecuencia practica importante: cuando el operador reinicia la aplicacion sin conectarse al robot, el ultimo mapa descargado sigue disponible en disco. Los componentes `PGMViewer` y `WallGenerator` pueden cargarlo directamente desde esa ruta sin necesidad de realizar una nueva descarga, lo que permite revisar el espacio mapeado en modo sin conexion.

La opcion `clearSamplesBeforeUpdate` en `SLAMMapDownloader` controla si las muestras de senal WiFi registradas en el `HeatmapGridRenderer` se borran antes de aplicar el nuevo mapa. Cuando esta opcion esta desactivada (valor por defecto `false`), las muestras previas se conservan aunque el mapa se actualice, lo que permite acumular mediciones de cobertura a lo largo de varias sesiones de exploracion con el mismo mapa base.

---

## Archivos de referencia

- [SLAMMapDownloader.cs]({{ "/assets/downloads/Scripts_Unity_App/SLAMMapDownloader.cs" | relative_url }})
