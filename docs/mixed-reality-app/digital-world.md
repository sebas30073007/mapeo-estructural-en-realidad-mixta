---
layout: default
title: "Mundo Digital"
parent: "Aplicación en Realidad Mixta"
nav_order: 2
---

# Mundo Digital

El "mundo digital" es el conjunto de objetos Unity cuya geometría se construye dinámicamente a partir del mapa SLAM generado por el robot. Este conjunto de objetos queda anclado al espacio físico del entorno mediante la cámara de passthrough del Meta Quest 3, de modo que las paredes virtuales se superponen sobre las paredes reales y el operador puede percibir ambas simultáneamente en la misma escena.

---

## Jerarquía de objetos en escena

La escena Unity organiza los elementos del mundo digital en la siguiente estructura:

| Objeto | Tipo | Responsabilidad |
|---|---|---|
| `MapFrameConverter` | MonoBehaviour (GameObject raíz) | Pivote de coordenadas; convierte entre el sistema de referencia del mapa ROS y el espacio local de Unity |
| `WallsContainer` | GameObject hijo de `WallGenerator` | Contenedor de los cubos de pared; se destruye y recrea completamente en cada actualización del mapa |
| Panel del mapa (`RawImage`) | Elemento UI del Canvas | Muestra la imagen PGM del mapa como miniatura 2D visible en el visor |
| Plano del heatmap | Mesh plana | Superficie sobre la que se proyecta la interpolación de cobertura de señal WiFi medida durante el recorrido |

El flujo de dependencias entre componentes es el siguiente:

```
SLAMMapDownloader
       |
       +---> MapFrameConverter  (actualiza origen, resolución, dimensiones)
       |
       +---> PGMViewer          (carga el archivo PGM y actualiza el RawImage)
       |
       +---> WallGenerator      (destruye WallsContainer y genera cubos nuevos)
       |
       +---> HeatmapGridRenderer (reescala el plano del heatmap)
       |
       +---> MapBoundsAutoCollider (recomputa el BoxCollider global)
```

---

## Sistema de coordenadas

El mapa SLAM se publica en el frame `map` de ROS 2, cuyo sistema de referencia sigue la convención REP-103:

| Eje ROS (`map` frame) | Direccion | Eje Unity equivalente |
|---|---|---|
| X | Este (derecha del mapa) | X |
| Y | Norte (arriba del mapa) | Z |
| Z | Arriba | Y (altura visual) |

El origen del frame `map` se ubica en la esquina inferior-izquierda del mapa y sus unidades son metros. Unity, en cambio, usa un sistema diestro con Y hacia arriba, por lo que el eje Y de ROS se mapea al eje Z de Unity y la altura se expresa en el eje Y de Unity.

La conversión la realiza el método `MapToLocal` del componente `MapFrameConverter`:

```
localX = mapX - mapOriginX
localZ = mapY - mapOriginY
Unity position = Vector3(localX, yHeight, localZ)
```

El origen del mapa (`mapOriginX`, `mapOriginY`) se extrae del campo `origin` del archivo `walls.json` devuelto por el servidor del robot y se almacena en `MapFrameConverter` mediante `SetMapInfo()`. Todos los scripts que necesitan posicionar objetos en el espacio del mapa (generador de paredes, heatmap, collider de límites) obtienen esta referencia llamando a `FindObjectOfType<MapFrameConverter>()` o mediante referencia directa asignada en el Inspector.

`MapFrameConverter` debe existir como `MonoBehaviour` en la jerarquía de la escena porque Unity solo puede localizar componentes en escena a través de `FindObjectOfType`; no funciona con clases estáticas ni ScriptableObjects para este propósito. Esto garantiza que el pivote de coordenadas esté siempre activo mientras la escena esté cargada.

Además del método principal, el componente expone utilidades de apoyo:

| Método | Descripcion |
|---|---|
| `MapToLocal(mapX, mapY, yHeight)` | Convierte un punto del frame `map` a Vector3 local de Unity |
| `MapToLocal2D(mapX, mapY)` | Variante que devuelve solo los componentes X y Z |
| `LocalToMap(localX, localZ)` | Conversión inversa, de Unity al frame `map` |
| `GetLocalMapCenter(yHeight)` | Devuelve el centro del mapa en coordenadas locales |
| `IsInsideMap(mapX, mapY)` | Comprueba si un punto del frame `map` cae dentro del área mapeada |
| `IsInsideLocalMap(localX, localZ)` | Igual que el anterior pero con coordenadas locales de Unity |

---

## Manipulación de paredes (WallManipulator)

Una vez generadas las paredes 3D de forma automática, puede existir una pequeña desalineación entre la geometría virtual y las paredes físicas reales, ya sea por imprecisiones del SLAM o por la ubicación del punto de anclaje del passthrough. El componente `WallManipulator` permite al operador corregir esta desalineación directamente dentro del visor, sin abandonar la experiencia de realidad mixta.

El script utiliza los controladores OVR del Meta Quest para emitir un rayo (`Raycast`) desde cada mano. Al detectar con ese rayo un objeto que sea hijo del transform donde está el componente, el operador puede:

- **Arrastrar** el conjunto de paredes agarrándolo con el gatillo de agarre (`PrimaryHandTrigger`) de la mano derecha o izquierda. Mientras se mantiene apretado, el bloque de paredes sigue la posición de la mano sumada al offset registrado en el momento del agarre.
- **Rotar** el bloque alrededor del eje vertical (Y mundial) usando el thumbstick del controlador activo. La velocidad de rotación es configurable mediante `rotationSpeed`.
- **Escalar** el bloque uniformemente con el gatillo de índice (`PrimaryIndexTrigger`). La escala queda limitada entre `minScale` y `maxScale` para evitar dimensiones incoherentes.

El script proporciona retroalimentacion visual a través de dos `LineRenderer` (uno verde para la mano derecha, uno azul para la izquierda) que representan los rayos de interacción. Al agarrar un objeto, el controlador activo emite una vibración háptica breve.

Si los `Transform` de las anclas de mano no se asignan manualmente en el Inspector, el script los busca automáticamente en `Start()` buscando el `OVRCameraRig` por nombre en la escena.

---

## Collider de límites del mapa (MapBoundsAutoCollider)

`MapBoundsAutoCollider` es un componente que requiere un `BoxCollider` en el mismo `GameObject`. Su función es calcular automáticamente un volumen que encierre todos los objetos renderizables que cuelgan del `contentRoot` asignado (tipicamente el `MapRoot` o equivalente), y asignar ese volumen al `BoxCollider`.

El método publico `Recompute()` es llamado por `SLAMMapDownloader` cada vez que el mapa se actualiza. Su logica interna:

1. Obtiene todos los componentes `Renderer` que son hijos de `contentRoot` mediante `GetComponentsInChildren<Renderer>()`.
2. Excluye cualquier objeto cuyo nombre sea `"VisualBounds"`, que es el propio objeto visual que marca los limites y no debe influir en el calculo.
3. Itera sobre los renderers restantes y acumula sus bounds en el espacio mundial usando `Bounds.Encapsulate`.
4. Convierte el centro resultante al espacio local del `GameObject` con `InverseTransformPoint` y calcula el tamaño local dividiendo por la escala absoluta (`lossyScale`).
5. Aplica un margen configurable (`padding`) sumandolo uniformemente a los tres ejes del tamaño local.
6. Asigna el centro y tamaño calculados al `BoxCollider`.

Este collider habilita la interaccion fisica y de raycasting con el area del mapa: por ejemplo, permite que los rayos del `WallManipulator` colisionen con el area mapeada y que futuros mecanismos de interaccion puedan detectar si un punto virtual esta dentro de la zona documentada.

---

## Orientacion de paneles UI (BillboardToCamera)

`BillboardToCamera` es un componente auxiliar de una sola responsabilidad: en cada `LateUpdate`, alinea el eje `forward` del `GameObject` donde reside con el eje `forward` de la camara principal (`Camera.main`).

Esto hace que cualquier panel UI o etiqueta flotante que tenga este componente siempre quede orientado de cara al operador, independientemente de hacia donde mire. Se usa tipicamente en los paneles de estado y etiquetas informativas que flotan sobre objetos del mundo digital, garantizando legibilidad en todo momento sin necesidad de animar ni calcular la orientacion manualmente.

---

## Archivos de referencia

- [MapFrameConverter.cs]({{ "/assets/downloads/Scripts_Unity_App/MapFrameConverter.cs" | relative_url }})
- [WallManipulator.cs]({{ "/assets/downloads/Scripts_Unity_App/WallManipulator.cs" | relative_url }})
- [MapBoundsAutoCollider.cs]({{ "/assets/downloads/Scripts_Unity_App/MapBoundsAutoCollider.cs" | relative_url }})
