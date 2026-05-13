---
layout: default
title: "Detección de Muros y Esquinas"
parent: "Mapeo Estructural"
nav_order: 3
---

# Detección de Muros y Esquinas

Esta página continúa el pipeline descrito en [Procesamiento del Mapa](map-processing.md). Una vez que los segmentos han sido extraídos y fusionados en coordenadas de píxel, el script realiza dos operaciones finales antes de escribir el JSON: convierte todas las coordenadas al sistema de referencia del mundo en metros y aplica deduplicación de esquinas. Adicionalmente, sobre el contorno crudo (antes de la aproximación) se ejecuta un detector de personas basado en RANSAC circular. El resultado de las tres operaciones se serializa en el JSON de versión 3 que Unity importa.

---

## Conversión a coordenadas del mundo

La función `pixel_to_world_m` aplica la transformación lineal definida por los metadatos del YAML:

```
wx = origin_x + px * resolution
wy = origin_y + (height - 1 - py) * resolution
```

Donde `px` y `py` son las coordenadas del píxel en la imagen, `origin_x` y `origin_y` son los dos primeros valores del campo `origin` del YAML, `resolution` es el tamaño del píxel en metros, y `height` es el número de filas de la imagen.

La inversión en el eje Y, `(height - 1 - py)`, existe porque los dos sistemas de coordenadas tienen orientaciones opuestas: en una imagen PGM el índice de fila crece hacia abajo (Y=0 es la fila superior), mientras que en el frame del mapa de ROS el eje Y crece hacia arriba. Sin esta inversión, el mapa aparecería volteado verticalmente en Unity.

---

## Estructura del JSON de salida

El archivo JSON de salida sigue el esquema de la versión 3:

```json
{
  "version": 3,
  "unit": "m",
  "extraction_type": "interior_borders",
  "map_info": {
    "image": "current_map.pgm",
    "resolution": 0.05,
    "origin": [-3.5, -2.1, 0.0],
    "width": 384,
    "height": 384
  },
  "wall_segments": [
    { "x1": 0.45, "y1": 1.20, "x2": 2.30, "y2": 1.20 }
  ],
  "corners": [
    { "x": 0.45, "y": 1.20 }
  ],
  "personas": [
    { "x": 1.10, "y": 0.85, "radio_m": 0.09, "arco_deg": 142.3 }
  ]
}
```

Descripcion de los campos:

| Campo | Tipo | Descripcion |
|---|---|---|
| `version` | entero | Version del esquema. Unity valida este numero antes de parsear. |
| `unit` | cadena | Unidad de todas las coordenadas y radios. Siempre `"m"`. |
| `extraction_type` | cadena | Identifica el metodo de extraccion. Siempre `"interior_borders"`. |
| `map_info.image` | cadena | Nombre del archivo PGM de origen. |
| `map_info.resolution` | flotante | Metros por pixel del mapa original. |
| `map_info.origin` | array | Posicion [x, y, theta] del pixel (0,0) en el frame del mapa ROS. |
| `map_info.width` | entero | Ancho del mapa en pixeles. |
| `map_info.height` | entero | Alto del mapa en pixeles. |
| `wall_segments` | array | Lista de segmentos de pared, cada uno con sus dos extremos en metros. |
| `corners` | array | Extremos unicos de los segmentos, deduplicados. Usados para colocar esquinas en Unity. |
| `personas` | array | Personas detectadas por RANSAC circular. Puede estar vacio. |
| `personas[i].x`, `.y` | flotante | Centro de la persona en metros (frame del mapa ROS). |
| `personas[i].radio_m` | flotante | Radio del arco ajustado en metros. |
| `personas[i].arco_deg` | flotante | Amplitud angular del arco detectado en grados. |

---

## Deduplicacion de esquinas

Cada segmento de pared aporta dos extremos al conjunto de esquinas. Como varios segmentos comparten extremos en los cambios de direccion del contorno, es necesario eliminar duplicados antes de escribir el JSON.

El criterio de deduplicacion es espacial: un punto se considera duplicado si cae dentro de una tolerancia en ambas coordenadas respecto a cualquier punto ya registrado.

```python
CORNER_TOL_PX = 3.0  # pixeles
```

La deduplicacion se aplica en coordenadas de pixel antes de la conversion a metros, lo que hace la tolerancia independiente de la resolucion del mapa. Un valor de 3 px con una resolucion tipica de 0.05 m/px equivale a unos 15 cm en el mundo real.

---

## Deteccion de personas por RANSAC circular

El contorno del espacio libre tiene indentaciones circulares en los puntos donde hay personas de pie. Visto desde arriba, una persona ocupa un area circular pequena y su presencia en el mapa aparece como una media luna clavada en el borde del espacio navegable: la parte de la circunferencia que da al interior del pasillo queda registrada en el contorno LIDAR, mientras que el lado que da a la pared queda oculto.

El detector aplica RANSAC sobre ventanas deslizantes del contorno crudo (antes de la aproximacion poligonal) para encontrar arcos que se ajusten a circulos del tamano de una persona.

### Parametros del detector

| Parametro | Variable | Valor |
|---|---|---|
| Radio minimo | `PERSON_RADIUS_MIN_M` | 0.05 m |
| Radio maximo | `PERSON_RADIUS_MAX_M` | 0.30 m |
| Tolerancia inlier | `PERSON_RANSAC_INLIER_DIST_M` | 0.02 m (2 cm) |
| Iteraciones RANSAC por ventana | `PERSON_RANSAC_ITERATIONS` | 120 |
| Tamano de ventana | `PERSON_WINDOW_PX` | 30 puntos |
| Paso entre ventanas | `PERSON_WINDOW_STEP_PX` | 8 puntos |
| Minimo de inliers para aceptar | `PERSON_MIN_INLIERS` | 6 |
| Umbral de fusion de centros | `PERSON_CENTER_MERGE_M` | 0.15 m |

### Logica del algoritmo

El proceso para cada ventana es el siguiente:

```
Para cada ventana de 30 puntos del contorno (paso de 8):
    Repetir 120 veces:
        Tomar 3 puntos al azar de la ventana
        Ajustar un circulo a esos 3 puntos
        Si el radio no esta en [0.05, 0.30] m -> descartar
        Contar inliers: puntos cuya distancia al circulo <= 2 cm
        Guardar si es el mejor hasta ahora
    Si el mejor tiene >= 6 inliers:
        Calcular amplitud angular del arco (arc span)
        Si 60 <= arc_span <= 300 grados -> guardar candidato
```

El limite inferior de 60 grados garantiza que el arco tenga suficiente curvatura para identificarse como objeto circular. El limite superior de 300 grados descarta circulos casi completos que corresponden a columnas, postes u objetos cilindricos, no a personas.

### Fusion de candidatos

La misma persona puede ser detectada multiples veces por ventanas superpuestas. Los candidatos cuyo centro este a menos de 15 cm entre si se agrupan y se conserva el representante con mayor numero de inliers:

```
Si dist(centro_i, centro_j) < PERSON_CENTER_MERGE_M (0.15 m):
    Fusionar grupo, conservar el de mayor n_inliers
```

### Integracion en Unity

La deteccion de personas esta incluida en el JSON v3, pero el script principal de Unity en produccion (`WallGenerator.cs`) solo renderiza los segmentos de pared y las esquinas. La version que ademas instancia representaciones de personas en la escena es `WallGeneratorConPersonas.cs`.

---

## Archivos de referencia

[extract_walls.py]({{ "/assets/downloads/rosmaster/extract_walls.py" | relative_url }})

[WallGeneratorConPersonas.cs]({{ "/assets/downloads/Lidar_render/WallGeneratorConPersonas.cs" | relative_url }})
