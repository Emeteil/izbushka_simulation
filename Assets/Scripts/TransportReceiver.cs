using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json.Linq;

public class TransportReceiver : MonoBehaviour
{
    [Header("TCP Connection")]
    [SerializeField] private string host = "127.0.0.1";
    [SerializeField] private int port = 5470;
    [SerializeField] private float reconnectInterval = 3f;

    public static event Action<string, string, JObject> OnCommand;

    public static event Action OnPing;
    public static event Action<int> OnDistanceRequest;
    public static event Action<int> OnMillisRequest;

    public static event Action<int, int, int> OnMotorsSetSpeed;
    public static event Action<int, int, int> OnMotorsSetDirection;
    public static event Action<int, int, int, int, int> OnMotorsSetBoth;
    public static event Action<int> OnMotorsMoveForward;
    public static event Action<int> OnMotorsMoveBackward;
    public static event Action<int> OnMotorsTurnLeft;
    public static event Action<int> OnMotorsTurnRight;
    public static event Action<int> OnMotorsRotateLeft;
    public static event Action<int> OnMotorsRotateRight;
    public static event Action OnMotorsStop;
    public static event Action OnMotorsBrake;
    public static event Action<int, int, int, int> OnMotorsSetDifferential;

    public static event Action<int, int> OnServoMoveImmediate;
    public static event Action<int, int, int> OnServoMoveSmoothLow;
    public static event Action<int, int, int> OnServoMoveSmoothHigh;
    public static event Action<int, int> OnServoMoveRelative;
    public static event Action<int, int> OnServoCalibrate;

    public static event Action<string> OnSubscribe;
    public static event Action<string> OnUnsubscribe;

    public static TransportReceiver Instance { get; private set; }

    private TcpClient _tcp;
    private NetworkStream _stream;
    private Thread _thread;
    private volatile bool _running;
    private readonly Queue<Action> _mainQueue = new Queue<Action>();
    private readonly object _writeLock = new object();
    private HashSet<string> _activeSubscriptions = new HashSet<string>();

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        _running = true;
        _thread = new Thread(ConnectionLoop) { IsBackground = true };
        _thread.Start();
    }

    private void OnDisable()
    {
        _running = false;
        _activeSubscriptions.Clear();
        Disconnect();
        _thread?.Join(1000);
    }

    private void Update()
    {
        lock (_mainQueue)
        {
            while (_mainQueue.Count > 0)
                _mainQueue.Dequeue()?.Invoke();
        }
    }

    private void ConnectionLoop()
    {
        while (_running)
        {
            try
            {
                _tcp = new TcpClient();
                _tcp.Connect(host, port);
                _stream = _tcp.GetStream();
                Debug.Log("[TransportReceiver] Connected!");

                ReadLoop();
            }
            catch (SocketException)
            {
                if (!_running) break;
            }
            catch (Exception e)
            {
                if (!_running) break;
                Debug.LogWarning($"[TransportReceiver] {e.Message}");
            }

            Disconnect();
            if (!_running) break;
            Thread.Sleep((int)(reconnectInterval * 1000));
        }
    }

    private void ReadLoop()
    {
        while (_running && _tcp != null && _tcp.Connected)
        {
            try
            {
                JObject msg = RecvMessage();
                if (msg == null) break;

                string type = msg.Value<string>("type") ?? "command";

                if (type == "command")
                {
                    string command = msg.Value<string>("command") ?? "";
                    string action = msg.Value<string>("action") ?? "";
                    JObject kwargs = msg.Value<JObject>("kwargs") ?? new JObject();

                    JObject result = ComputeResponse(command, action, kwargs);
                    SendMessage(result);

                    Enqueue(() => FireEvents(command, action, kwargs));
                }
                else if (type == "subscribe")
                {
                    string sensor = msg.Value<string>("sensor") ?? "";
                    _activeSubscriptions.Add(sensor);
                    Enqueue(() => OnSubscribe?.Invoke(sensor));
                }
                else if (type == "unsubscribe")
                {
                    string sensor = msg.Value<string>("sensor") ?? "";
                    _activeSubscriptions.Remove(sensor);
                    Enqueue(() => OnUnsubscribe?.Invoke(sensor));
                }
            }
            catch (IOException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private JObject ComputeResponse(string command, string action, JObject kwargs)
    {
        JObject result = new JObject();
        switch (command)
        {
            case "ping":
                result["result"] = true;
                break;
            case "distance":
                result["result"] = SensorDataProvider.GetDistance();
                break;
            case "millis":
                result["result"] = SensorDataProvider.GetMillis();
                break;
            case "gyro":
                result["result"] = SensorDataProvider.GetGyroJson();
                break;
            default:
                result["result"] = true;
                break;
        }
        return result;
    }

    private void FireEvents(string command, string action, JObject kwargs)
    {
        OnCommand?.Invoke(command, action, kwargs);

        switch (command)
        {
            case "ping": OnPing?.Invoke(); break;
            case "distance": OnDistanceRequest?.Invoke(kwargs.Value<int?>("timeout") ?? 5); break;
            case "millis": OnMillisRequest?.Invoke(kwargs.Value<int?>("timeout") ?? 5); break;
            case "motors": HandleMotors(action, kwargs); break;
            case "servo": HandleServo(action, kwargs); break;
        }
    }

    public bool IsConnected => _tcp != null && _tcp.Connected;

    public void SendFrame(byte[] jpgBytes)
    {
        JObject msg = new JObject
        {
            ["type"] = "frame",
            ["data"] = Convert.ToBase64String(jpgBytes)
        };
        SendMessage(msg);
    }

    public void PushSubscriptionData(string sensor, JToken data)
    {
        if (!_activeSubscriptions.Contains(sensor)) return;

        JObject msg = new JObject
        {
            ["type"] = "subscription_data",
            ["sensor"] = sensor,
            ["data"] = data
        };
        SendMessage(msg);
    }

    private void SendMessage(JObject obj)
    {
        lock (_writeLock)
        {
            if (_stream == null || _tcp == null || !_tcp.Connected) return;
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(obj.ToString(Newtonsoft.Json.Formatting.None));
                byte[] header = BitConverter.GetBytes(ToBigEndian(body.Length));
                _stream.Write(header, 0, 4);
                _stream.Write(body, 0, body.Length);
                _stream.Flush();
            }
            catch (Exception) { }
        }
    }

    private JObject RecvMessage()
    {
        byte[] header = ReadExact(4);
        if (header == null) return null;
        int length = FromBigEndian(BitConverter.ToInt32(header, 0));
        if (length <= 0 || length > 1_000_000) return null;
        byte[] body = ReadExact(length);
        if (body == null) return null;
        return JObject.Parse(Encoding.UTF8.GetString(body));
    }

    private byte[] ReadExact(int n)
    {
        byte[] buf = new byte[n];
        int offset = 0;
        while (offset < n)
        {
            int read = _stream.Read(buf, offset, n - offset);
            if (read == 0) return null;
            offset += read;
        }
        return buf;
    }

    private void Enqueue(Action action)
    {
        lock (_mainQueue) { _mainQueue.Enqueue(action); }
    }

    private void Disconnect()
    {
        try { _stream?.Close(); } catch { }
        try { _tcp?.Close(); } catch { }
        _stream = null;
        _tcp = null;
    }

    private static int ToBigEndian(int val)
    {
        if (BitConverter.IsLittleEndian)
        {
            byte[] bytes = BitConverter.GetBytes(val);
            Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
        return val;
    }

    private static int FromBigEndian(int val) => ToBigEndian(val);

    private void HandleMotors(string action, JObject kw)
    {
        switch (action)
        {
            case "set_speed":
                OnMotorsSetSpeed?.Invoke(kw.Value<int?>("motor_mask") ?? 3, kw.Value<int?>("speed_left") ?? 0, kw.Value<int?>("speed_right") ?? 0);
                break;
            case "set_direction":
                OnMotorsSetDirection?.Invoke(kw.Value<int?>("motor_mask") ?? 3, kw.Value<int?>("direction_left") ?? 0, kw.Value<int?>("direction_right") ?? 0);
                break;
            case "set_both":
                OnMotorsSetBoth?.Invoke(kw.Value<int?>("motor_mask") ?? 3, kw.Value<int?>("direction_left") ?? 0, kw.Value<int?>("direction_right") ?? 0, kw.Value<int?>("speed_left") ?? 0, kw.Value<int?>("speed_right") ?? 0);
                break;
            case "move_forward":
                OnMotorsMoveForward?.Invoke(kw.Value<int?>("speed") ?? 150);
                break;
            case "move_backward":
                OnMotorsMoveBackward?.Invoke(kw.Value<int?>("speed") ?? 150);
                break;
            case "turn_left":
                OnMotorsTurnLeft?.Invoke(kw.Value<int?>("speed") ?? 150);
                break;
            case "turn_right":
                OnMotorsTurnRight?.Invoke(kw.Value<int?>("speed") ?? 150);
                break;
            case "rotate_left":
                OnMotorsRotateLeft?.Invoke(kw.Value<int?>("speed") ?? 150);
                break;
            case "rotate_right":
                OnMotorsRotateRight?.Invoke(kw.Value<int?>("speed") ?? 150);
                break;
            case "stop_all":
                OnMotorsStop?.Invoke();
                break;
            case "brake":
                OnMotorsBrake?.Invoke();
                break;
            case "set_differential":
                OnMotorsSetDifferential?.Invoke(kw.Value<int?>("speed_left") ?? 0, kw.Value<int?>("speed_right") ?? 0, kw.Value<int?>("direction_left") ?? 1, kw.Value<int?>("direction_right") ?? 1);
                break;
        }
    }

    private void HandleServo(string action, JObject kw)
    {
        int channel = kw.Value<int?>("channel") ?? 0;
        int angle = kw.Value<int?>("angle") ?? 90;

        switch (action)
        {
            case "move_immediate":
                OnServoMoveImmediate?.Invoke(channel, angle);
                break;
            case "move_smooth_low":
                OnServoMoveSmoothLow?.Invoke(channel, angle, kw.Value<int?>("step_delay_ms") ?? 50);
                break;
            case "move_smooth_high":
                OnServoMoveSmoothHigh?.Invoke(channel, angle, kw.Value<int?>("step_delay_ms") ?? 50);
                break;
            case "move_relative":
                OnServoMoveRelative?.Invoke(channel, kw.Value<int?>("delta_angle") ?? 0);
                break;
            case "calibrate":
                OnServoCalibrate?.Invoke(channel, kw.Value<int?>("calibrate_angle") ?? 90);
                break;
        }
    }
}