---
layout: default
title: "Procesamiento del Mapa"
parent: "Mapeo Estructural"
nav_order: 2
---

# Procesamiento del Mapa

El script `extract_walls.py` recibe los archivos `current_map.yaml` y `current_map.pgm` producidos por SLAM Toolbox durante la sesión de exploración del robot. A partir de esos archivos aplica una cadena de procesamiento de imagen para producir un JSON con los segmentos de pared que delimitan el espacio navegable y, opcionalmente, las personas detectadas en planta. Ese JSON es el que Unity consume para reconstruir la geometría del entorno en realidad mixta.

---

## El mapa de ocupación en PGM

SLAM Toolbox escribe el mapa como una imagen en escala de grises en formato PGM. Cada píxel representa una celda del grid de ocupación:

| Rango de valor | Significado |
|---|---|
| >= 252 (blanco) | Espacio libre: el robot puede navegar aquí |
| ~128 (gris medio) | Zona desconocida: el LIDAR no ha llegado a cubrir esa área |
| ~0 (negro) | Obstáculo: pared, mueble u objeto sólido |

El archivo YAML que acompaña al PGM proporciona los metadatos necesarios para convertir coordenadas de imagen a coordenadas del mundo:

```yaml
image: current_map.pgm
resolution: 0.05        # metros por píxel
origin: [-3.5, -2.1, 0.0]  # [x, y, theta] del píxel (0,0) en el frame del mapa ROS
```

El campo `resolution` define cuántos metros representa cada píxel. El campo `origin` fija la posición en metros del píxel en la esquina superior izquierda (fila 0, columna 0) dentro del frame de coordenadas del mapa de ROS.

---

## Pipeline de procesamiento

El procesamiento sigue estos pasos en orden:

1. **Carga de archivos.** El YAML se lee con PyYAML. La imagen PGM se carga con PIL en modo escala de grises y se convierte a un array NumPy de tipo `uint8`.

2. **Umbralización.** Se construye una imagen binaria donde los píxeles con valor `>= FREE_MIN` se marcan como espacio libre:

   ```python
   FREE_MIN = 252
   ```

   Todo lo que quede por debajo de ese umbral se trata como obstáculo o zona desconocida.

3. **Limpieza morfológica.** Se aplican dos operaciones consecutivas sobre la imagen binaria:

   ```
   MORPH_OPEN(kernel=3)   -- elimina ruido puntual (píxeles aislados)
   MORPH_CLOSE(kernel=8)  -- cierra pequeñas protuberancias en el contorno
   ```

   La apertura con kernel 3 elimina grupos pequeños de píxeles que aparecen en el espacio libre por reflexiones del LIDAR. El cierre con kernel 8 suaviza las indentaciones menores del contorno que no corresponden a geometría real, sin destruir las indentaciones más grandes que sí pueden indicar la presencia de personas.

4. **Componente conexo principal.** El espacio libre puede quedar fragmentado en varias regiones no conectadas (pasillos separados, áreas fuera del campo de exploración). Se usa `cv2.connectedComponentsWithStats` y se retiene únicamente el componente de mayor área, siempre que supere el umbral mínimo:

   ```python
   MIN_FREE_AREA_PERCENT = 0.01  # 1% del área total del mapa en píxeles
   ```

5. **Contorno interior.** Sobre el componente principal se extrae el contorno con `cv2.findContours` usando el modo `RETR_CCOMP` y la aproximación `CHAIN_APPROX_NONE`. De todos los contornos devueltos se toma el primero (`contours[0]`), que corresponde al borde interno del espacio libre, es decir, la línea que separa el área navegable de las paredes.

6. **Aproximación poligonal.** El contorno crudo tiene cientos o miles de puntos. Se simplifica con el algoritmo Ramer-Douglas-Peucker:

   ```python
   APPROX_EPS_PX = 12.0  # tolerancia en píxeles
   ```

   Con este valor, solo se conservan los vértices que implican un desvío geométrico mayor a 12 píxeles respecto a la línea que une sus vecinos. El resultado es un polígono con un número manejable de vértices que preserva los cambios de dirección significativos del contorno.

7. **Segmentación.** Cada par de vértices consecutivos del polígono simplificado se convierte en un segmento si su longitud supera el umbral mínimo:

   ```python
   MIN_SEG_LEN_PX = 5.0  # píxeles
   ```

   Los segmentos resultantes aún están en coordenadas de píxel.

---

## Fusión de segmentos colineales

La aproximación poligonal puede producir segmentos cortos y casi alineados que en realidad pertenecen a la misma pared. Para consolidarlos se aplica un algoritmo de fusión iterativo.

**Criterios de fusión.** Dos segmentos se fusionan si cumplen simultáneamente las tres condiciones siguientes:

| Criterio | Parámetro | Valor |
|---|---|---|
| Diferencia de ángulo entre segmentos | `COLINEAR_ANGLE_TOL_DEG` | 25.0 grados |
| Distancia perpendicular de un extremo a la recta del otro | `COLINEAR_DIST_TOL_PX` | 15.0 px |
| Gap (distancia mínima entre extremos) | `COLINEAR_GAP_TOL_PX` | 12.0 px |

Cuando un grupo de segmentos supera los tres criterios, todos sus puntos extremos se proyectan sobre el eje de dirección del grupo y el segmento fusionado va del punto con proyección mínima al punto con proyección máxima. Esto garantiza que el segmento resultante cubre el tramo completo sin acortar ni alargar artificialmente.

**Por qué se necesitan múltiples iteraciones.** En una sola pasada, cuando el segmento A y el segmento C están separados por el segmento B, puede ocurrir que A y B se fusionen en la primera pasada y que el nuevo segmento A+B quede ahora alineado con C, siendo posible una segunda fusión que no era detectable al inicio. El script ejecuta hasta 5 iteraciones y se detiene antes si el número de segmentos no cambia entre dos pasadas consecutivas:

```
max_iterations = 5
```

```
Iteracion 1: N  segmentos
Iteracion 2: N' segmentos  (N' < N  si hubo fusiones)
...
Se detiene cuando new_count == prev_count
```

El resultado es un conjunto reducido de segmentos en coordenadas de píxel listos para la conversión a metros.

---

## Archivos de referencia

[extract_walls.py]({{ "/assets/downloads/rosmaster/extract_walls.py" | relative_url }})
