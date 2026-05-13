using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections;
using UnityEngine.Networking;

public class PGMViewer : MonoBehaviour
{
    [Header("Referencias")]
    public RawImage rawImage;
    
    [Header("Configuración")]
    public bool useRobotMap = false;  // FALSE = usar archivo de prueba, TRUE = esperar robot
    public string pgmFileName = "small_warehouse.pgm";  // Solo para pruebas en Editor
    
    private Texture2D pgmTexture;
    
    void Start()
    {
        if (!useRobotMap)
        {
            // Modo prueba: cargar desde StreamingAssets
            LoadAndDisplayPGM();
        }
        else
        {
            // Modo producción: esperar archivo del robot
            Debug.Log("🤖 Esperando mapa del robot. Presiona 'Update SLAM'");
        }
    }
    
    void LoadAndDisplayPGM()
    {
        StartCoroutine(LoadPGMCoroutine());
    }
    
    IEnumerator LoadPGMCoroutine()
    {
        string filePath = System.IO.Path.Combine(Application.streamingAssetsPath, pgmFileName);
        
        byte[] fileData = null;
        
        // En Android (Quest), StreamingAssets está dentro del APK
        #if UNITY_ANDROID && !UNITY_EDITOR
            UnityEngine.Networking.UnityWebRequest www = UnityEngine.Networking.UnityWebRequest.Get(filePath);
            yield return www.SendWebRequest();
            
            if (www.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogError($"❌ Error cargando PGM en Quest: {www.error}");
                yield break;
            }
            
            fileData = www.downloadHandler.data;
            Debug.Log($"✅ PGM cargado desde APK: {fileData.Length} bytes");
        #else
            // En Editor y PC, lectura normal
            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogError($"❌ No se encontró {pgmFileName} en StreamingAssets");
                yield break;
            }
            
            fileData = System.IO.File.ReadAllBytes(filePath);
            Debug.Log($"✅ PGM cargado desde disco: {fileData.Length} bytes");
            yield return null;
        #endif
        
        // Parsear PGM
        pgmTexture = ParsePGM(fileData);
        
        if (pgmTexture == null)
        {
            Debug.LogError("❌ Error al parsear el archivo PGM");
            yield break;
        }
        
        // Mostrar en RawImage
        if (rawImage != null)
        {
            rawImage.texture = pgmTexture;
            
            // Ajustar tamaño usando la función reutilizable
            AdjustRawImageSize(rawImage, 200f);
            
            Debug.Log($"✅ PGM mostrado: {pgmTexture.width}×{pgmTexture.height}");
            Debug.Log($"🎨 Formato: {pgmTexture.format}");
        }
        else
        {
            Debug.LogError("❌ No hay RawImage asignado");
        }
    }
    
    // ✨ FUNCIÓN AGREGADA - Ajusta el tamaño del RawImage manteniendo aspect ratio
    void AdjustRawImageSize(RawImage targetImage, float maxSize)
    {
        if (targetImage == null || pgmTexture == null) return;
        
        RectTransform rt = targetImage.GetComponent<RectTransform>();
        
        float imageWidth = pgmTexture.width;
        float imageHeight = pgmTexture.height;
        float aspectRatio = imageWidth / imageHeight;
        
        float newWidth, newHeight;
        
        if (aspectRatio > 1f)
        {
            // La imagen es más ancha que alta
            newWidth = maxSize;
            newHeight = maxSize / aspectRatio;
        }
        else
        {
            // La imagen es más alta que ancha
            newHeight = maxSize;
            newWidth = maxSize * aspectRatio;
        }
        
        rt.sizeDelta = new Vector2(newWidth, newHeight);
        
        Debug.Log($"📐 Tamaño ajustado en UI: {newWidth:F1}×{newHeight:F1}");
    }
    
    // ✨ Método llamado por SLAMMapDownloader cuando descarga un nuevo mapa
    public void LoadPGMFromPath(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"❌ No se encontró el archivo: {fullPath}");
            return;
        }
        
        byte[] fileData = File.ReadAllBytes(fullPath);
        pgmTexture = ParsePGM(fileData);
        
        if (pgmTexture == null)
        {
            Debug.LogError("❌ Error al parsear PGM descargado del robot");
            return;
        }
        
        // Mostrar en RawImage
        if (rawImage != null)
        {
            rawImage.texture = null;  // Limpiar textura anterior
            rawImage.texture = pgmTexture;
            rawImage.SetAllDirty();
            
            AdjustRawImageSize(rawImage, 200f);
            
            Debug.Log($"✅ PGM del robot cargado desde: {fullPath}");
            Debug.Log($"📐 Dimensiones: {pgmTexture.width}×{pgmTexture.height}");
        }
    }
    
    Texture2D ParsePGM(byte[] data)
    {
        try
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(data))
            using (System.IO.BinaryReader reader = new System.IO.BinaryReader(stream))
            {
                // Leer magic number "P5"
                string magic = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(2));
                
                if (magic != "P5")
                {
                    Debug.LogError($"❌ Formato incorrecto. Se esperaba 'P5', se encontró '{magic}'");
                    return null;
                }
                
                SkipWhitespaceAndComments(reader);
                int width = ReadInteger(reader);
                SkipWhitespaceAndComments(reader);
                int height = ReadInteger(reader);
                SkipWhitespaceAndComments(reader);
                int maxVal = ReadInteger(reader);
                reader.ReadByte(); // Saltar último whitespace
                
                Debug.Log($"📐 PGM Header: {width}×{height}, maxVal={maxVal}");
                
                // Crear textura con formato compatible Quest
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true);
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                
                Color[] pixels = new Color[width * height];
                
                // Leer píxeles
                for (int y = height - 1; y >= 0; y--)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte value = reader.ReadByte();
                        float gray = value / 255f;
                        
                        // RGBA con alpha completo
                        pixels[y * width + x] = new Color(gray, gray, gray, 1f);
                    }
                }
                
                texture.SetPixels(pixels);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                
                return texture;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error al parsear PGM: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }
    
    void SkipWhitespaceAndComments(BinaryReader reader)
    {
        while (true)
        {
            long pos = reader.BaseStream.Position;
            if (pos >= reader.BaseStream.Length) break;
            
            byte b = reader.ReadByte();
            
            // Comentario (empieza con #)
            if (b == '#')
            {
                // Leer hasta fin de línea
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    byte c = reader.ReadByte();
                    if (c == '\n') break;
                }
            }
            // Whitespace
            else if (b == ' ' || b == '\t' || b == '\n' || b == '\r')
            {
                continue;
            }
            // Carácter válido, retroceder
            else
            {
                reader.BaseStream.Position = pos;
                break;
            }
        }
    }
    
    int ReadInteger(BinaryReader reader)
    {
        string numStr = "";
        
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            byte b = reader.ReadByte();
            
            if (b >= '0' && b <= '9')
            {
                numStr += (char)b;
            }
            else
            {
                // Retroceder un byte
                reader.BaseStream.Position--;
                break;
            }
        }
        
        if (string.IsNullOrEmpty(numStr))
        {
            Debug.LogError("❌ No se pudo leer número entero del PGM");
            return 0;
        }
        
        return int.Parse(numStr);
    }
    
    // Método público para recargar (útil para debugging)
    public void ReloadPGM()
    {
        LoadAndDisplayPGM();
    }
}