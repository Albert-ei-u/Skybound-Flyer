using System;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/// <summary>
/// Reads the Skybound Arduino joystick (arduino/flight_joystick) over USB serial.
/// The sketch sends "joy1_x,joy1_y,joy2_x,joy2_y,brake,camera,reset" lines at
/// 115200 baud. Reading happens on a background thread so a slow port never
/// stalls the game; the drone polls the latest values each physics step.
/// Requires Player Settings > Api Compatibility Level = .NET Framework.
/// </summary>
public sealed class ArduinoJoystick : IDisposable
{
    private const int BaudRate = 115200;
    private const float DeadZone = 0.12f;

    private readonly object gate = new object();
    private SerialPort port;
    private Thread reader;
    private volatile bool running;

    private float roll, lift, yaw, throttle;
    private bool resetHeld, resetPressed, brakeHeld;
    private DateTime lastLine = DateTime.MinValue;

    public string PortName { get; private set; }

    /// <summary>True while fresh data has arrived in the last half second.</summary>
    public bool Connected
    {
        get { lock (gate) return (DateTime.UtcNow - lastLine).TotalSeconds < 0.5; }
    }

    public float Roll { get { lock (gate) return roll; } }
    public float Lift { get { lock (gate) return lift; } }
    public float Yaw { get { lock (gate) return yaw; } }
    /// <summary>-1 (stick back, brake) .. +1 (stick forward, fly).</summary>
    public float Throttle { get { lock (gate) return throttle; } }
    public bool Brake { get { lock (gate) return brakeHeld; } }

    /// <summary>True once per press of the left joystick button.</summary>
    public bool ConsumeResetPress()
    {
        lock (gate)
        {
            bool pressed = resetPressed;
            resetPressed = false;
            return pressed;
        }
    }

    /// <summary>
    /// Opens the given port, or when portName is empty tries every port and
    /// keeps the first one that sends joystick lines.
    /// </summary>
    public static ArduinoJoystick Open(string portName)
    {
        string[] candidates = string.IsNullOrWhiteSpace(portName)
            ? SerialPort.GetPortNames()
            : new[] { portName.Trim() };

        // Try high-numbered ports first: USB adapters usually get them, COM1 is often built in.
        Array.Sort(candidates, (a, b) => ExtractNumber(b).CompareTo(ExtractNumber(a)));

        foreach (string name in candidates)
        {
            var joystick = new ArduinoJoystick();
            if (joystick.TryStart(name)) return joystick;
            joystick.Dispose();
        }
        return null;
    }

    private bool TryStart(string name)
    {
        try
        {
            port = new SerialPort(name, BaudRate) { ReadTimeout = 1500, NewLine = "\n", DtrEnable = true };
            port.Open();
            // The Uno resets when the port opens; wait for a valid line.
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (Parse(port.ReadLine()))
                {
                    PortName = name;
                    port.ReadTimeout = 500;
                    running = true;
                    reader = new Thread(ReadLoop) { IsBackground = true, Name = "ArduinoJoystick" };
                    reader.Start();
                    Debug.Log($"Arduino joystick connected on {name}");
                    return true;
                }
            }
        }
        catch (Exception) { /* not our device or busy (close Arduino Serial Monitor) */ }
        return false;
    }

    private void ReadLoop()
    {
        while (running)
        {
            try { Parse(port.ReadLine()); }
            catch (TimeoutException) { }
            catch (Exception) { running = false; }
        }
    }

    private bool Parse(string line)
    {
        string[] parts = line.Trim().Split(',');
        if (parts.Length < 7) return false;
        var raw = new int[7];
        for (int i = 0; i < 7; i++)
            if (!int.TryParse(parts[i], out raw[i])) return false;

        lock (gate)
        {
            roll = Axis(raw[0]);
            lift = -Axis(raw[1]);     // push stick up to climb
            yaw = Axis(raw[2]);
            throttle = -Axis(raw[3]); // push stick forward to fly
            brakeHeld = raw[4] == 1;
            bool reset = raw[6] == 1;
            if (reset && !resetHeld) resetPressed = true;
            resetHeld = reset;
            lastLine = DateTime.UtcNow;
        }
        return true;
    }

    /// <summary>Maps a 0..1023 analog reading to -1..1 with a centre dead zone.</summary>
    private static float Axis(int value)
    {
        float centred = Mathf.Clamp((value - 512f) / 512f, -1f, 1f);
        if (Mathf.Abs(centred) < DeadZone) return 0f;
        return Mathf.Sign(centred) * (Mathf.Abs(centred) - DeadZone) / (1f - DeadZone);
    }

    private static int ExtractNumber(string portName)
    {
        int.TryParse(new string(Array.FindAll(portName.ToCharArray(), char.IsDigit)), out int n);
        return n;
    }

    public void Dispose()
    {
        running = false;
        try { reader?.Join(600); } catch (Exception) { }
        try { if (port != null && port.IsOpen) port.Close(); } catch (Exception) { }
        port = null;
    }
}
