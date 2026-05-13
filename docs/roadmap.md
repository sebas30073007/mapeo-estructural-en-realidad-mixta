---
layout: default
title: "Roadmap"
nav_order: 9
---

# Roadmap

El sistema base de mapeo estructural, teleoperación del ROSbot 2R y visualización del mapa de calor WiFi está implementado y operativo. En paralelo, se desarrollaron dos módulos complementarios que extienden las capacidades del sistema con funciones de percepción avanzada. Cada módulo es autónomo, tiene su propio flujo de procesamiento y puede incorporarse al sistema principal de forma independiente.

---

## Módulos complementarios

Cada módulo opera con su propio pipeline y expone una interfaz estándar —JSON— compatible con el sistema principal. Los pasos de integración están descritos en cada subsección.

---

### Detección de personas en tiempo real (LIDAR)

El script `extract_walls.py`, que opera en producción en el servidor del robot, ya invoca la función `detecta_personas()` en cada ciclo de actualización del mapa. El JSON de versión 3 que el servidor publica incluye el campo `personas[]`, donde cada entrada contiene los campos `x`, `y`, `radio_m` y `arco_deg` que describen la posición y la geometría aparente del objeto detectado.

**Algoritmo de detección**

La detección se basa en RANSAC circular aplicado con ventana deslizante sobre el contorno del espacio libre generado por el mapa SLAM. Las personas producen indentaciones semicirculares —con forma de media luna— en ese contorno, ya que el sensor LIDAR pierde visibilidad de la zona ocupada por el cuerpo. El algoritmo ajusta arcos de círculo a esas indentaciones y extrae su centro, radio estimado y amplitud angular.

**Renderizado en Unity**

El script `WallGeneratorConPersonas.cs` extiende el generador de paredes existente para leer el campo `personas[]` del mismo JSON y crear cilindros primitivos en las posiciones correspondientes. Cada cilindro adopta el diámetro reportado por RANSAC (`radio_m * 2`) y una altura configurable mediante el campo `personHeight` del componente. Si no se asigna un material específico en el Inspector, el cilindro se renderiza en azul semitransparente como indicador visual de depuración.

| Campo del JSON | Tipo | Descripción |
|---|---|---|
| `x` | float (m) | Posición en el eje X del mapa SLAM |
| `y` | float (m) | Posición en el eje Y del mapa SLAM |
| `radio_m` | float (m) | Radio estimado del objeto por ajuste RANSAC |
| `arco_deg` | float (grados) | Amplitud del arco detectado en el contorno libre |

**Seguimiento bayesiano de objetos**

El script `prob_persona.py` implementa un modelo de probabilidad simple para distinguir personas de objetos estáticos a lo largo del tiempo. La función `calcular_probabilidad_persona()` evalúa dos señales: el desplazamiento euclidiano entre la posición actual y la posición previa del objeto, y el tiempo transcurrido entre ambas observaciones.

Las reglas de actualización son las siguientes:

| Condición | Efecto sobre la probabilidad |
|---|---|
| Desplazamiento > 15 cm en menos de 5 minutos | +0.3 (objeto dinámico, probable persona) |
| Tiempo >= 2 horas y desplazamiento < 5 cm | -0.4 (objeto estático, probable mobiliario) |
| Desplazamiento > 10 cm y tiempo > 30 minutos | +0.1 (movimiento lento o ruido) |

La probabilidad resultante se mantiene acotada entre 0.0 y 1.0. El estado de cada objeto se persiste en el archivo `robot_state.json`, lo que permite que el sistema conserve el historial entre ejecuciones del servidor.

**Integración con la escena principal**

Para activar este módulo basta con reemplazar el componente `WallGenerator.cs` por `WallGeneratorConPersonas.cs` en el GameObject que gestiona la geometría del mapa.

---

### Reconocimiento de objetos con visión y profundidad (Orbbec Astra + Google Vision)

El script `detector_objetos_google_vision.py` combina la cámara RGB-D Orbbec Astra del robot con la API de visión artificial de Google para detectar, clasificar y posicionar objetos en el espacio tridimensional.

**Flujo de captura**

En cada ejecución el script captura simultáneamente una imagen RGB y un mapa de profundidad desde la cámara Astra. La imagen se codifica en base64 y se envía a la Google Vision API con la característica `OBJECT_LOCALIZATION`, que devuelve hasta 20 objetos con sus bounding boxes normalizados y una puntuación de confianza. En caso de fallo de red, el script reintenta la llamada hasta tres veces antes de lanzar un error.

**Estimación de profundidad**

Para cada objeto detectado, el script estima su profundidad usando un mecanismo de consenso entre múltiples ventanas de análisis dentro del bounding box. Se prueban tres tamaños de ventana (8 %, 12 % y 18 % del área del objeto) y cinco posiciones de anclaje (centro, izquierda, derecha, arriba, abajo). Una estimación es válida solo si cumple las siguientes condiciones:

- Al menos el 35 % de los píxeles de la ventana contienen datos de profundidad válidos.
- Hay un mínimo de 24 puntos válidos en la ventana.
- El rango intercuartílico (p75 - p25) no supera los 450 mm.

Si al menos dos ventanas producen estimaciones válidas y consistentes entre sí (diferencia menor a 450 mm), se reporta la mediana de esos candidatos. De lo contrario, el objeto se publica sin profundidad.

**Salida JSON para Unity**

El script genera un archivo en formato `astra_unity_scene_v1` con la lista de objetos detectados. Cada entrada incluye el nombre del objeto, la clave de prefab derivada (`prefab_key`), la puntuación de confianza, la profundidad en milímetros y la posición en coordenadas de Unity (`position_unity_m: {x, y, z}`). La conversión de coordenadas invierte el eje Y para ajustarse a la convención de ejes de Unity (derecha, arriba, adelante).

**Backends de captura soportados**

| Backend | Descripción |
|---|---|
| OpenNI2 (primario) | Captura profundidad y, si el modelo lo permite, también color por el mismo driver. Si el color no está disponible por OpenNI, lo adquiere desde la cámara UVC de la Astra de forma secuencial. |
| pyorbbecsdk (alternativo) | Recomendado para modelos Astra que no entregan color por OpenNI2. Soporta alineación hardware (D2C) y alineación software como fallback. |

La selección del backend se controla mediante la variable de entorno `ASTRA_BACKEND` (`openni`, `pyorbbecsdk` o `auto`). En modo `auto`, el script intenta primero pyorbbecsdk y luego OpenNI.

**Dependencias externas requeridas**

| Dependencia | Variable de entorno |
|---|---|
| Google Vision API key | `GOOGLE_VISION_API_KEY` |
| Ruta al directorio Redist de OpenNI2 | `OPENNI2_REDIST` (opcional si está en una ruta estándar) |
| Índice de cámara UVC forzado | `ASTRA_COLOR_DEVICE_INDEX` (opcional) |

---

## Líneas de desarrollo abiertas

Las siguientes mejoras han sido identificadas durante el desarrollo del proyecto como pasos naturales para extender el sistema en iteraciones futuras.

**Recomendación automática de ubicación de punto de acceso**

El mapa de calor WiFi ya captura la distribución espacial de la intensidad de señal en todo el recorrido del robot. Falta implementar el algoritmo que identifique el centroide del área con peor cobertura y lo presente al operador en la escena de realidad mixta como la ubicación óptima para instalar un repetidor o reubicar el punto de acceso existente.

**Mapeado 3D con datos de profundidad**

La cámara Orbbec Astra ya entrega un mapa de profundidad por frame, pero en el sistema actual ese canal solo se usa para estimar la distancia a objetos detectados por visión. Una fase futura podría acumular los datos de profundidad frame a frame, registrarlos con la odometría del robot y construir una nube de puntos o una malla volumétrica del entorno, superando la representación 2D que actualmente provee el SLAM LIDAR.

**Navegación autónoma durante el recorrido de mapeo WiFi**

El stack de ROS 2 del ROSbot ya dispone de odometría y mapa de ocupación. Integrar un planificador de exploración autónoma —por ejemplo, frontier-based exploration— permitiría que el robot recorra el espacio sin intervención continua del operador, reduciendo el tiempo necesario para completar el mapa de calor en espacios de gran superficie.

**Spatial Anchors para persistencia del mapa entre sesiones**

El Meta XR SDK incluye la API de Spatial Anchors, que permite anclar objetos virtuales a puntos físicos del entorno y recuperarlos en sesiones posteriores de realidad mixta. Actualmente el mapa estructural se alinea de forma manual al inicio de cada sesión. Incorporar Spatial Anchors automatizaría ese proceso y haría el sistema viable para inspecciones recurrentes del mismo espacio.

---

## Archivos de referencia

| Módulo | Archivos |
|---|---|
| Detección de personas | [`extract_walls.py`]({{ "/assets/downloads/rosmaster/extract_walls.py" | relative_url }}) · [`WallGeneratorConPersonas.cs`]({{ "/assets/downloads/Lidar_render/WallGeneratorConPersonas.cs" | relative_url }}) · [`prob_persona.py`]({{ "/assets/downloads/Lidar_render/prob_persona.py" | relative_url }}) |
| Vision + profundidad | [`detector_objetos_google_vision.py`]({{ "/assets/downloads/Procesamiento de imagenes/detector_objetos_google_vision.py" | relative_url }}) |
