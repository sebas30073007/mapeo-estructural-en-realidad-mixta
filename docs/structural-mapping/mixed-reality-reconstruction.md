---
layout: default
title: "Reconstrucción en Realidad Mixta"
parent: "Mapeo Estructural"
nav_order: 4
---

# Reconstrucción en Realidad Mixta

Una vez que Unity descarga el JSON del servidor, tres componentes coordinan la reconstrucción de las paredes en el espacio mixto: `SLAMMapDownloader` gestiona la comunicación HTTP y la persistencia de archivos; `MapFrameConverter` actúa como pivote de coordenadas entre el frame `map` de ROS y el espacio local de Unity; y `WallGenerator` instancia los objetos 3D que representan las paredes en la escena del Meta Quest 3.

---

## Descarga y distribución del mapa (SLAMMapDownloader)

`SLAMMapDownloader` orquesta todo el proceso de actualización del mapa cuando el operador pulsa el botón de la interfaz. El flujo completo es el siguiente:

1. **Verificacion de conectividad.** Se envía un GET a `/health` con timeout de 5 segundos. Si el robot no responde, el proceso se interrumpe y se muestra el error en la interfaz de texto.

2. **Peticion del mapa.** Se envía un GET a `/generate_map` con timeout de 60 segundos. La IP del robot se obtiene de `RosbotEndpointManager`; si este no está disponible en escena, se usa la IP de fallback configurada en el Inspector.

3. **Parseo de la respuesta.** La cadena JSON recibida se deserializa en una instancia de `MapResponse`. Si el campo `status` no es `"success"`, el proceso se aborta con el mensaje de error del servidor.

4. **Escritura en disco.** Los tres archivos se guardan en `Application.persistentDataPath`:

   | Archivo            | Origen en la respuesta | Modo de escritura |
   |--------------------|------------------------|-------------------|
   | `current_map.pgm`  | Campo `pgm` (Base64)   | Binario           |
   | `current_map.yaml` | Campo `yaml`           | Texto             |
   | `walls.json`       | Campo `walls`          | Texto             |

5. **Actualizacion del pivote de coordenadas.** Se extrae `map_info` del JSON de paredes y se llama a `mapFrame.SetMapInfo()` con `originX`, `originY`, `resolution`, `width` y `height`. A partir de este momento todos los componentes que usan `MapFrameConverter` trabajan con la geometría del mapa recién capturado.

6. **Distribucion a los componentes visuales.** La descarga finaliza disparando tres llamadas:
   - `pgmViewer.LoadPGMFromPath(pgmPath)` — actualiza la textura del minimapa en la interfaz.
   - `wallGenerator.LoadWallsFromPath(wallsPath)` — reconstruye los cubos de pared en la escena.
   - `heatmap.UpdateMapDimensionsAndRefresh(wallsPath)` — recalcula la cuadrícula del heatmap con las nuevas dimensiones del mapa.

   Si la opción `clearSamplesBeforeUpdate` está activa, se llama además a `heatmap.ClearSession()` antes de refrescar, descartando las muestras acumuladas en la sesión anterior.

---

## Conversion de coordenadas (MapFrameConverter)

`MapFrameConverter` es el componente central de referencia espacial. Todos los scripts que necesitan posiciones en metros dentro de la escena de Unity consultan este componente en lugar de realizar sus propias conversiones, lo que garantiza coherencia cuando cambia el mapa.

### SetMapInfo

```csharp
public void SetMapInfo(float originX, float originY, float resolution, int width, int height)
```

Almacena los metadatos del mapa YAML: la posición del origen del frame `map` en el mundo ROS (`originX`, `originY`), la resolución en metros por pixel (`resolution`), y el tamaño en pixels (`width`, `height`). Con estos valores calcula las dimensiones totales en metros: `MapWidthMeters = width * resolution` y `MapHeightMeters = height * resolution`.

### MapToLocal

```csharp
public Vector3 MapToLocal(float mapX, float mapY, float yHeight = 0f)
```

Convierte una posicion en coordenadas globales del frame `map` de ROS al espacio local del GameObject que contiene el componente. El sistema de coordenadas de ROS situa el origen del mapa en la esquina inferior izquierda, con el eje X hacia la derecha y el eje Y hacia arriba del plano horizontal. Unity, en cambio, usa el eje Y para la altura visual y el plano horizontal es XZ. La conversion aplica:

```
Unity X  =  mapX - originX
Unity Z  =  mapY - originY
Unity Y  =  yHeight  (altura visual, pasada como parametro)
```

El resultado es un `Vector3` en el sistema local de Unity donde la posicion horizontal del punto queda correctamente mapeada y la altura puede controlarse de forma independiente.

---

## Generacion de paredes (WallGenerator)

`WallGenerator` lee el archivo `walls.json` guardado por `SLAMMapDownloader` y construye en escena una representacion tridimensional de cada segmento de pared detectado.

### Inicializacion del frame de mapa

Antes de instanciar ninguna geometria, `LoadWallsFromJSON` llama a `mapFrame.SetMapInfo()` con los valores de `map_info` contenidos en el propio JSON de paredes. Esta llamada sincroniza el pivote de coordenadas incluso si `WallGenerator` se usa de forma independiente sin pasar por `SLAMMapDownloader`.

### Creacion de segmentos

Por cada entrada en `wall_segments`, el componente realiza los siguientes calculos:

1. Convierte los extremos `(x1, y1)` y `(x2, y2)` del frame `map` al espacio local de Unity mediante `mapFrame.MapToLocal()`, usando `wallHeight * 0.5f` como altura para que el cubo quede centrado verticalmente.
2. Calcula la longitud del segmento con `Vector3.Distance`. Los segmentos menores de `minSegmentLength` (0.05 m por defecto) se descartan.
3. Calcula el centro del segmento como el punto medio entre los dos extremos convertidos.
4. Calcula el angulo de rotacion `yaw` en el plano XZ mediante `Atan2(direction.x, direction.z)`.
5. Instancia un `GameObject.CreatePrimitive(PrimitiveType.Cube)` y le aplica:

   | Propiedad         | Valor                                    |
   |-------------------|------------------------------------------|
   | `localPosition`   | Centro del segmento                      |
   | `localRotation`   | `Quaternion.Euler(0, yaw, 0)`            |
   | `localScale`      | `(wallThickness, wallHeight, length)`    |
   | `layer`           | `"Wall"`                                 |

   Los valores por defecto son `wallThickness = 0.1 m` y `wallHeight = 2.0 m`, configurables desde el Inspector.

### Contenedor de paredes

Todos los cubos se agrupan bajo un GameObject hijo llamado `WallsContainer`. Al inicio de cada actualizacion, el contenedor existente se destruye completamente antes de crear uno nuevo, de modo que el resultado siempre refleja el mapa actual sin acumulacion de geometria de versiones anteriores.

---

## Flujo completo

```
SLAMMapDownloader
     |
     |-- GET :5008/health  (timeout 5 s)
     |
     |-- GET :5008/generate_map  (timeout 60 s)
     |
     |-- Guarda archivos en persistentDataPath
     |        current_map.pgm  (binario, desde Base64)
     |        current_map.yaml (texto)
     |        walls.json       (texto)
     |
     |-- MapFrameConverter.SetMapInfo()
     |        originX, originY, resolution, width, height
     |        --> pivote de coordenadas actualizado
     |
     |-- PGMViewer.LoadPGMFromPath()
     |        --> textura del minimapa actualizada en UI
     |
     |-- WallGenerator.LoadWallsFromPath()
     |        |
     |        |-- MapFrameConverter.SetMapInfo()  (desde map_info del JSON)
     |        |
     |        |-- Destruye WallsContainer anterior
     |        |
     |        +-- Por cada wall_segment:
     |              MapToLocal(x1,y1) + MapToLocal(x2,y2)
     |              --> Cube en layer "Wall" bajo WallsContainer
     |
     +-- HeatmapGridRenderer.UpdateMapDimensionsAndRefresh()
              --> cuadricula del heatmap recalculada
```

---

## Archivos de referencia

- [SLAMMapDownloader.cs]({{ "/assets/downloads/Scripts_Unity_App/SLAMMapDownloader.cs" | relative_url }})
- [WallGenerator.cs]({{ "/assets/downloads/Scripts_Unity_App/WallGenerator.cs" | relative_url }})
- [MapFrameConverter.cs]({{ "/assets/downloads/Scripts_Unity_App/MapFrameConverter.cs" | relative_url }})
