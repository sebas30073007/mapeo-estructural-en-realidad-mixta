---
layout: default
title: "Stack Unity"
parent: "Ficha Técnica"
nav_order: 3
---

# Stack Unity

La aplicación Unity corre en el Meta Quest 3 como APK de Android. Esta sección describe la arquitectura interna del código C#, los patrones de diseño utilizados y la organización de los scripts que componen el sistema.

---

## Versiones y dependencias

| Tecnología | Versión |
|---|---|
| Unity Editor | 2022.3.58f1 LTS |
| Meta XR All-in-One SDK | v83 |
| Target platform | Android (Meta Quest) |
| API Level mínimo | Android 10 (API 29) |
| NetMQ (ZeroMQ para C#) | Recepción de stream de video |

---

## Patrón de red: hilo + cola

Unity aplica una restricción fundamental: la API del motor (objetos de escena, renderizado, físicas) solo puede invocarse desde el hilo principal. Los sockets de red bloqueantes no pueden correr en el hilo principal sin congelar la aplicación. Para resolver esta tensión, todos los scripts de recepción de red en el proyecto desacoplan el trabajo en dos etapas:

1. Un hilo secundario recibe datos de la red de forma continua y los deposita en una `ConcurrentQueue<T>`.
2. El método `Update()` del hilo principal drena la cola en cada fotograma y actualiza los objetos de Unity con los datos recibidos.

```csharp
// Hilo de recepción
while (running) {
    data = socket.Receive();
    inbox.Enqueue(data);
}

// Update() — main thread
while (inbox.TryDequeue(out var data)) {
    ProcessAndUpdateUnityObjects(data);
}
```

Este patrón está implementado de forma idéntica en `UdpWifiReceiver` y `RosbotVideoStreamReceiver`. En `UdpWifiReceiver`, el hilo de recepción usa un `UdpClient` con `ReceiveTimeout` de 500 ms para no bloquear indefinidamente, y el método `Update()` procesa hasta 100 mensajes por fotograma para evitar acumulación de latencia.

`UdpTeleopSender` no requiere hilo separado porque únicamente envía datos: las llamadas de envío UDP son no bloqueantes y pueden ejecutarse de forma segura en el hilo principal.

---

## Gestión de sesión (Singleton persistente)

`RosbotEndpointManager` es el componente encargado de mantener la dirección IP del robot disponible en toda la aplicación, independientemente de qué escena esté activa. Implementa el patrón Singleton con persistencia entre escenas mediante `DontDestroyOnLoad`.

La garantía de instancia única se aplica en `Awake()`: si ya existe una instancia registrada en `Instance` y no es el objeto actual, el objeto duplicado se destruye inmediatamente. De lo contrario, el objeto se registra como instancia y se marca como persistente:

```csharp
private void Awake()
{
    if (Instance != null && Instance != this)
    {
        Destroy(gameObject);
        return;
    }

    Instance = this;
    DontDestroyOnLoad(gameObject);
    LoadSavedIp();
}
```

La IP activa se expone a través de la propiedad `Instance` para acceso estático desde cualquier script, y a través del método `GetIp()` que valida que el valor almacenado sea una dirección IPv4 bien formada antes de devolverla. La persistencia entre sesiones de la aplicación se logra mediante `PlayerPrefs`, con una clave configurable desde el Inspector (`playerPrefsKey`). Al iniciarse, `LoadSavedIp()` recupera el último valor guardado o utiliza la IP por defecto definida en el Inspector.

Este patrón es adecuado para la IP del robot porque dicho valor debe estar disponible desde la pantalla de bienvenida, durante el flujo de configuración y en todas las escenas de operación, sin requerir que cada escena lo cargue de forma independiente.

---

## Categorías de scripts

| Categoría | Scripts principales | Patrón |
|---|---|---|
| Red — recepción | `UdpWifiReceiver`, `RosbotVideoStreamReceiver` | Hilo secundario + `ConcurrentQueue` |
| Red — envío | `UdpTeleopSender_TriggersOnlyUDP`, `SLAMMapDownloader` | Main thread (UDP) / Coroutine (HTTP) |
| Mapeo y coordenadas | `MapFrameConverter`, `WallGenerator`, `PGMViewer`, `HeatmapGridRenderer` | `MonoBehaviour` coordinados |
| Sesión y configuración | `RosbotEndpointManager`, `SessionPersistenceManager` | Singleton / `PlayerPrefs` |
| UI en XR | `ControlsPanelUI`, `WelcomeFlowController`, `BillboardToCamera` | `MonoBehaviour` UI |

`SLAMMapDownloader` merece una mención específica: combina una coroutine de Unity (`UnityWebRequest`) para descargar el mapa vía HTTP desde el servidor Flask del robot, con código de procesamiento en el hilo principal para construir la textura y las geometrías de pared a partir de los datos recibidos.

---

## Archivos de referencia

- [UdpWifiReceiver.cs]({{ "/assets/downloads/Scripts_Unity_App/UdpWifiReceiver.cs" | relative_url }})
- [RosbotEndpointManager.cs]({{ "/assets/downloads/Scripts_Unity_App/Scripts_Rosbot/RosbotEndpointManager.cs" | relative_url }})
- [Carpeta Scripts_Unity_App]({{ "/assets/downloads/Scripts_Unity_App/" | relative_url }})
- [Carpeta Scripts_Rosbot]({{ "/assets/downloads/Scripts_Unity_App/Scripts_Rosbot/" | relative_url }})
