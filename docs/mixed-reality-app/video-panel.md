---
layout: default
title: "Panel de Video"
parent: "Aplicación en Realidad Mixta"
nav_order: 3
---

# Panel de Video

El operador dispone de dos fuentes de informacion visual sobre el estado del robot y del entorno: un panel flotante que transmite en tiempo real la imagen de la camara del robot, y una miniatura del mapa SLAM descargado. Ambas visualizaciones aparecen dentro del visor del Meta Quest 3 como elementos del Canvas de la escena Unity, superpuestos al passthrough del mundo fisico.

---

## Stream de video (RosbotVideoStreamReceiver)

### Transporte y suscripcion

El script `RosbotVideoStreamReceiver` usa la biblioteca NetMQ (implementacion de ZeroMQ en C#) para suscribirse al stream de video del robot. Se conecta mediante un socket `SubscriberSocket` al endpoint:

```
tcp://<IP_robot>:5555
```

y se suscribe al topic `video_rgb`. El servidor del robot publica cada frame como un mensaje ZMQ de dos partes: la primera parte es el nombre del topic (usado como filtro de suscripcion) y la segunda parte contiene los bytes del frame JPEG.

El socket se configura con un `ReceiveHighWatermark` de 1, lo que significa que ZMQ descarta frames antiguos si la cola interna se llena, priorizando siempre el frame mas reciente sobre la latencia acumulada.

### Patron de recepcion

Para no bloquear el hilo principal de Unity (que maneja rendering y fisica), la recepcion de frames se realiza en un hilo de fondo dedicado (`recvThread`). Los frames recibidos se depositan en una `ConcurrentQueue<byte[]>` que actua como buffer thread-safe entre el hilo receptor y el hilo principal.

```
Hilo receptor (recvThread)
    |
    | TryReceiveMultipartBytes (timeout 100 ms)
    |
    +---> frameQueue.Enqueue(msg[1])   -- solo parte de datos, sin topic
    
Hilo principal (Update)
    |
    | frameQueue.TryDequeue (consume hasta vaciar, queda con el mas reciente)
    |
    +---> videoTexture.LoadImage(latestFrame)  -- decodifica JPEG
    +---> targetImage.texture = videoTexture
```

En `Update()`, el script vacia la cola completamente en cada frame y decodifica unicamente el ultimo elemento, lo que garantiza que la imagen mostrada sea siempre la mas reciente y no la mas antigua en el buffer.

### Metricas de estado

El componente expone las siguientes propiedades de solo lectura que otros scripts y paneles UI pueden consultar:

| Propiedad | Tipo | Descripcion |
|---|---|---|
| `IsConnected` | `bool` | `true` si el tiempo desde el ultimo frame recibido es menor que `disconnectTimeout` (1 segundo por defecto) |
| `CurrentFps` | `float` | FPS medidos en ventana de 1 segundo |
| `CurrentWidth` | `int` | Ancho en pixeles del ultimo frame decodificado |
| `CurrentHeight` | `int` | Alto en pixeles del ultimo frame decodificado |
| `CurrentCameraMode` | `string` | Modo de camara activo: `normal`, `pose`, `segment` u `off` |

La propiedad `IsConnected` se calcula comparando `Time.unscaledTime` con `lastFrameRealtime`, que se actualiza en cada frame exitosamente decodificado. Si pasa mas de `disconnectTimeout` segundos sin recibir un frame valido, el componente se considera desconectado.

### Canal de comandos

Ademas del hilo receptor, el script lanza un segundo hilo (`cmdThread`) que gestiona un socket `PublisherSocket` conectado al puerto 5002. Este canal permite enviar comandos al robot, en particular el cambio de modo de camara. Los comandos se serializan como JSON y se envian dos veces con un retardo de 30 ms entre envios para compensar posibles perdidas:

```json
{"type": "set_camera_mode", "mode": "normal"}
```

Los modos disponibles son `normal` (camara RGB estandar), `pose` (estimacion de pose visual), `segment` (segmentacion semantica) y `off` (camara desactivada).

---

## Visualizacion del mapa SLAM (PGMViewer)

### Formato PGM

El mapa SLAM generado por Nav2/SLAM Toolbox se exporta en formato PGM binario (magic number `P5`). El archivo tiene la siguiente estructura:

```
P5\n
<width> <height>\n
<maxval>\n
<datos de pixeles en bruto, un byte por pixel>
```

Cada pixel representa la probabilidad de ocupacion de una celda: 255 (blanco) indica espacio libre, 0 (negro) indica obstaculo, y los valores intermedios representan zona desconocida.

### Parseo y construccion de textura

El metodo `ParsePGM(byte[] data)` lee el archivo byte a byte usando un `BinaryReader` sobre un `MemoryStream`. El proceso es:

1. Lee los dos primeros bytes y verifica que forman la cadena `"P5"`. Si no coinciden, aborta con error.
2. Llama a `SkipWhitespaceAndComments` para saltar espacios, tabulaciones, saltos de linea y lineas de comentario (que empiezan con `#`).
3. Lee los enteros `width`, `height` y `maxval` del header de texto.
4. Lee un byte adicional para consumir el whitespace separador entre el header y los datos binarios.
5. Crea una `Texture2D` con formato `RGBA32` (compatible con Meta Quest) y filtro bilinear.
6. Lee los pixeles en orden de fila, pero los escribe en la textura de forma invertida en Y: la primera fila del archivo (parte superior del mapa en ROS) se escribe en la ultima fila de la textura. Esto aplica el Y-flip necesario para que el origen del mapa quede en la esquina inferior-izquierda dentro de Unity, coherente con la convencion del frame `map` de ROS.

```
Archivo PGM (origen arriba-izquierda, convencion imagen)
  fila 0 (y=0 en archivo) -> fila (height-1) en textura Unity
  fila 1                  -> fila (height-2) en textura Unity
  ...
  fila (height-1)         -> fila 0 en textura Unity (origen abajo-izquierda)
```

El resultado es una textura en escala de grises (R=G=B=valor/255, A=1) que se asigna al `RawImage` del panel. El tamaño del `RectTransform` se ajusta automaticamente manteniendo el aspect ratio original del mapa, con un maximo configurable de 200 unidades UI.

### Modos de carga

`PGMViewer` soporta dos modos de operacion seleccionables desde el Inspector:

| Campo `useRobotMap` | Comportamiento |
|---|---|
| `false` | Carga un archivo PGM de prueba desde `StreamingAssets` al arrancar la escena. Util para desarrollo en Editor. |
| `true` | Espera a que `SLAMMapDownloader` llame a `LoadPGMFromPath(fullPath)` con la ruta del archivo descargado del robot. |

En la build de produccion para el Meta Quest, `StreamingAssets` reside dentro del APK y se accede mediante `UnityWebRequest`; en Editor y PC se usa lectura directa de disco.

---

## Diferencia entre las dos visualizaciones

| Aspecto | Stream de video | Miniatura del mapa SLAM |
|---|---|---|
| Proposito | Ver en tiempo real lo que ve la camara del robot para teleoperar y verificar su entorno inmediato | Revisar la geometria del espacio mapeado y la cobertura del recorrido completo |
| Formato de datos | JPEG comprimido, frames individuales en bytes | Archivo PGM binario (formato P5), imagen en escala de grises |
| Frecuencia de actualizacion | Continua, aproximadamente a la tasa de publicacion del robot (dependiente de la red) | Manual, se actualiza unicamente cuando el operador pulsa "Update SLAM" |
| Fuente | Socket ZeroMQ (NetMQ) suscrito al topic `video_rgb` en puerto 5555 | Archivo `current_map.pgm` guardado en `Application.persistentDataPath` tras la descarga HTTP |
| Componente Unity | `RosbotVideoStreamReceiver` | `PGMViewer` |
| Decodificacion | `Texture2D.LoadImage` (JPEG nativo de Unity) | Parser PGM manual byte a byte con Y-flip |

---

## Archivos de referencia

- [RosbotVideoStreamReceiver.cs]({{ "/assets/downloads/Scripts_Unity_App/Scripts_Rosbot/RosbotVideoStreamReceiver.cs" | relative_url }})
- [PGMViewer.cs]({{ "/assets/downloads/Scripts_Unity_App/PGMViewer.cs" | relative_url }})
