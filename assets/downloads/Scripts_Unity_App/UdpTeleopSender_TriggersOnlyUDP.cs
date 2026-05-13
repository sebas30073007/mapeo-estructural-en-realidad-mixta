using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// UDP Teleop Sender mínimo para ROSMASTER / Meta Quest.
/// 
/// SOLO UDP. No robot digital. No ZMQ. No HTTP.
/// 
/// Envía:
/// {"vx":0.1500,"vy":0.0000,"wz":0.0000}
///
/// Mapeo Quest:
/// - Joystick izquierdo: avance/retroceso y lateral.
/// - Gatillo izquierdo: gira a la izquierda.
/// - Gatillo derecho: gira a la derecha.
///
/// Mapeo teclado en Unity Editor:
/// - W/S: adelante/atrás.
/// - A/D: lateral izquierda/derecha.
/// - Q/E: giro izquierda/derecha.
/// </summary>
public class UdpTeleopSender : MonoBehaviour
{
    public enum InputBackend
    {
        Auto,
        MetaOVRInput,
        UnityXRInput
    }

    [Header("UDP Robot")]
    public string robotIp = "172.22.98.190";
    public int robotUdpPort = 5002;
    [Range(1f, 60f)] public float commandHz = 10f;

    [Header("Velocidades")]
    public float maxLinear = 0.15f;
    public float maxAngular = 0.5f;

    [Header("Input Quest")]
    public InputBackend inputBackend = InputBackend.Auto;

    [Tooltip("Joystick para movimiento lineal. Normalmente LeftHand.")]
    public XRNode joystickNode = XRNode.LeftHand;

    [Range(0f, 0.5f)] public float joystickDeadzone = 0.15f;
    [Range(0f, 0.5f)] public float triggerDeadzone = 0.05f;

    [Header("Teclado para pruebas en Editor")]
    public bool useKeyboardInEditor = true;

    [Header("Mapeo")]
    [Tooltip("Igual que tu teleop web: joystick a la derecha genera vy negativo.")]
    public bool joystickRightIsNegativeVy = true;

    public bool invertVx = false;
    public bool invertVy = false;
    public bool invertWz = false;

    [Header("Seguridad")]
    public bool sendZeroWhenIdle = true;
    public bool sendStopOnDisable = true;

    [Header("Debug")]
    public bool showDebugLogs = true;
    public string lastPayload = "";
    public string lastStatus = "";
    public string lastInputDebug = "";
    public int sentCount = 0;
    public int errorCount = 0;

    [Header("Debug input")]
    public bool ovrAvailable = false;
    public bool unityXrJoystickValid = false;
    public bool unityXrLeftValid = false;
    public bool unityXrRightValid = false;
    public string unityXrJoystickName = "";
    public Vector2 lastJoystick = Vector2.zero;
    public float lastLeftTrigger = 0f;
    public float lastRightTrigger = 0f;
    public float lastTurn = 0f;

    private UdpClient udpClient;
    private IPEndPoint robotEndPoint;

    private InputDevice joystickDevice;
    private InputDevice leftDevice;
    private InputDevice rightDevice;

    private float nextSendTime;

    private void Start()
    {
        OpenUdp();
        TryInitializeUnityXRDevices();

        // STOP inicial de seguridad.
        for (int i = 0; i < 10; i++)
            SendUdpCommand(0f, 0f, 0f);
    }

    private void Update()
    {
        if (Time.time < nextSendTime)
            return;

        nextSendTime = Time.time + 1f / Mathf.Max(commandHz, 1f);

        Vector2 joystick = Vector2.zero;
        float leftTrigger = 0f;
        float rightTrigger = 0f;

        ReadQuestInput(ref joystick, ref leftTrigger, ref rightTrigger);

#if UNITY_EDITOR
        if (useKeyboardInEditor)
            ReadKeyboardFallback(ref joystick, ref leftTrigger, ref rightTrigger);
#endif

        joystick = ApplyRadialDeadzone(joystick, joystickDeadzone);
        leftTrigger = ApplyTriggerDeadzone(leftTrigger, triggerDeadzone);
        rightTrigger = ApplyTriggerDeadzone(rightTrigger, triggerDeadzone);

        // Gatillo izquierdo = giro izquierda positivo.
        // Gatillo derecho = giro derecha negativo.
        float turn = leftTrigger - rightTrigger;

        float vx = joystick.y * maxLinear;
        float vy = joystick.x * maxLinear;
        float wz = turn * maxAngular;

        if (joystickRightIsNegativeVy)
            vy *= -1f;

        if (invertVx) vx *= -1f;
        if (invertVy) vy *= -1f;
        if (invertWz) wz *= -1f;

        lastJoystick = joystick;
        lastLeftTrigger = leftTrigger;
        lastRightTrigger = rightTrigger;
        lastTurn = turn;

        lastInputDebug =
            $"joy=({joystick.x:F2},{joystick.y:F2}) LT={leftTrigger:F2} RT={rightTrigger:F2} turn={turn:F2} -> vx={vx:F4}, vy={vy:F4}, wz={wz:F4}";

        bool isIdle =
            Mathf.Abs(vx) < 0.0005f &&
            Mathf.Abs(vy) < 0.0005f &&
            Mathf.Abs(wz) < 0.0005f;

        if (isIdle && !sendZeroWhenIdle)
            return;

        SendUdpCommand(vx, vy, wz);
    }

    private void ReadQuestInput(ref Vector2 joystick, ref float leftTrigger, ref float rightTrigger)
    {
        bool readOk = false;

        if (inputBackend == InputBackend.Auto || inputBackend == InputBackend.MetaOVRInput)
            readOk = TryReadOVRInput(ref joystick, ref leftTrigger, ref rightTrigger);

        if (!readOk && (inputBackend == InputBackend.Auto || inputBackend == InputBackend.UnityXRInput))
            TryReadUnityXRInput(ref joystick, ref leftTrigger, ref rightTrigger);
    }

    private bool TryReadOVRInput(ref Vector2 joystick, ref float leftTrigger, ref float rightTrigger)
    {
        try
        {
            OVRInput.Update();

            joystick = OVRInput.Get(
                OVRInput.Axis2D.PrimaryThumbstick,
                OVRInput.Controller.LTouch
            );

            leftTrigger = OVRInput.Get(
                OVRInput.Axis1D.PrimaryIndexTrigger,
                OVRInput.Controller.LTouch
            );

            rightTrigger = OVRInput.Get(
                OVRInput.Axis1D.PrimaryIndexTrigger,
                OVRInput.Controller.RTouch
            );

            ovrAvailable = true;
            return true;
        }
        catch
        {
            ovrAvailable = false;
            return false;
        }
    }

    private bool TryReadUnityXRInput(ref Vector2 joystick, ref float leftTrigger, ref float rightTrigger)
    {
        if (!joystickDevice.isValid || !leftDevice.isValid || !rightDevice.isValid)
            TryInitializeUnityXRDevices();

        unityXrJoystickValid = joystickDevice.isValid;
        unityXrLeftValid = leftDevice.isValid;
        unityXrRightValid = rightDevice.isValid;

        bool gotSomething = false;

        if (joystickDevice.isValid)
        {
            unityXrJoystickName = joystickDevice.name;

            if (joystickDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 joy))
            {
                joystick = joy;
                gotSomething = true;
            }
        }

        if (leftDevice.isValid)
        {
            if (leftDevice.TryGetFeatureValue(CommonUsages.trigger, out float lt))
            {
                leftTrigger = lt;
                gotSomething = true;
            }
        }

        if (rightDevice.isValid)
        {
            if (rightDevice.TryGetFeatureValue(CommonUsages.trigger, out float rt))
            {
                rightTrigger = rt;
                gotSomething = true;
            }
        }

        return gotSomething;
    }

    private void TryInitializeUnityXRDevices()
    {
        joystickDevice = InputDevices.GetDeviceAtXRNode(joystickNode);
        leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        unityXrJoystickValid = joystickDevice.isValid;
        unityXrLeftValid = leftDevice.isValid;
        unityXrRightValid = rightDevice.isValid;
        unityXrJoystickName = joystickDevice.isValid ? joystickDevice.name : "No joystick device";

        if (showDebugLogs)
        {
            Debug.Log("[UDP TELEOP] Joystick device: " + joystickNode + " | valid=" + joystickDevice.isValid + " | " + unityXrJoystickName);
            Debug.Log("[UDP TELEOP] Left valid=" + leftDevice.isValid + " | Right valid=" + rightDevice.isValid);
        }
    }

#if UNITY_EDITOR
    private void ReadKeyboardFallback(ref Vector2 joystick, ref float leftTrigger, ref float rightTrigger)
    {
        Vector2 keys = Vector2.zero;

        if (Input.GetKey(KeyCode.W)) keys.y += 1f;
        if (Input.GetKey(KeyCode.S)) keys.y -= 1f;
        if (Input.GetKey(KeyCode.A)) keys.x -= 1f;
        if (Input.GetKey(KeyCode.D)) keys.x += 1f;

        if (keys.sqrMagnitude > 0.001f)
            joystick = Vector2.ClampMagnitude(keys, 1f);

        // Q simula gatillo izquierdo.
        // E simula gatillo derecho.
        if (Input.GetKey(KeyCode.Q))
            leftTrigger = 1f;

        if (Input.GetKey(KeyCode.E))
            rightTrigger = 1f;
    }
#endif

    private void OpenUdp()
    {
        try
        {
            CloseUdp();

            udpClient = new UdpClient();
            robotEndPoint = new IPEndPoint(IPAddress.Parse(robotIp), robotUdpPort);

            lastStatus = $"UDP listo -> {robotIp}:{robotUdpPort}";

            if (showDebugLogs)
                Debug.Log("[UDP TELEOP] " + lastStatus);
        }
        catch (Exception ex)
        {
            errorCount++;
            lastStatus = "Error abriendo UDP: " + ex.GetType().Name + " | " + ex.Message;
            Debug.LogError("[UDP TELEOP] " + lastStatus);
        }
    }

    private void SendUdpCommand(float vx, float vy, float wz)
    {
        if (udpClient == null || robotEndPoint == null)
            OpenUdp();

        if (udpClient == null || robotEndPoint == null)
            return;

        try
        {
            string rawJson =
                $"{{\"vx\":{vx.ToString("F4", CultureInfo.InvariantCulture)}," +
                $"\"vy\":{vy.ToString("F4", CultureInfo.InvariantCulture)}," +
                $"\"wz\":{wz.ToString("F4", CultureInfo.InvariantCulture)}}}";

            byte[] data = Encoding.UTF8.GetBytes(rawJson);
            udpClient.Send(data, data.Length, robotEndPoint);

            lastPayload = rawJson;
            sentCount++;
            lastStatus = $"UDP enviado -> {robotIp}:{robotUdpPort}";

            if (showDebugLogs && sentCount % 10 == 0)
                Debug.Log("[UDP TELEOP] " + rawJson + " | " + lastInputDebug);
        }
        catch (Exception ex)
        {
            errorCount++;
            lastStatus = "Error enviando UDP: " + ex.GetType().Name + " | " + ex.Message;
            Debug.LogWarning("[UDP TELEOP] " + lastStatus);
        }
    }

    private static Vector2 ApplyRadialDeadzone(Vector2 value, float deadzone)
    {
        float mag = value.magnitude;

        if (mag <= deadzone)
            return Vector2.zero;

        float scaledMag = Mathf.InverseLerp(deadzone, 1f, Mathf.Clamp01(mag));
        return value.normalized * scaledMag;
    }

    private static float ApplyTriggerDeadzone(float value, float deadzone)
    {
        if (value <= deadzone)
            return 0f;

        return Mathf.InverseLerp(deadzone, 1f, Mathf.Clamp01(value));
    }

    [ContextMenu("Enviar STOP")]
    public void SendStop()
    {
        SendUdpCommand(0f, 0f, 0f);
    }

    [ContextMenu("Test Adelante")]
    public void TestForward()
    {
        SendUdpCommand(maxLinear, 0f, 0f);
    }

    [ContextMenu("Test Giro Izquierda")]
    public void TestTurnLeft()
    {
        SendUdpCommand(0f, 0f, maxAngular);
    }

    [ContextMenu("Test Giro Derecha")]
    public void TestTurnRight()
    {
        SendUdpCommand(0f, 0f, -maxAngular);
    }

    private void OnDisable()
    {
        if (sendStopOnDisable)
        {
            for (int i = 0; i < 5; i++)
                SendUdpCommand(0f, 0f, 0f);
        }

        CloseUdp();
    }

    private void OnDestroy()
    {
        CloseUdp();
    }

    private void CloseUdp()
    {
        try
        {
            udpClient?.Close();
            udpClient?.Dispose();
        }
        catch { }

        udpClient = null;
        robotEndPoint = null;
    }
}
