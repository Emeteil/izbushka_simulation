using UnityEngine;
using Newtonsoft.Json.Linq;

public class SensorDataProvider : MonoBehaviour
{
    [Header("Distance Sensor")]
    [SerializeField] private Transform distanceSensorOrigin;
    [SerializeField] private float maxDistance = 400f;
    [SerializeField] private LayerMask distanceLayerMask = ~0;

    [Header("Subscriptions")]
    [SerializeField] private float subscriptionInterval = 0.1f;

    private static SensorDataProvider _instance;
    private Rigidbody _rb;
    private float _startTime;
    private float _lastSubscriptionPush;

    private static float _lastDistance = 400f;
    private static Vector3 _lastAccel = Vector3.up;
    private static Vector3 _lastGyro = Vector3.zero;
    private static float _lastTemp = 25f;

    private void Awake()
    {
        _instance = this;
        _startTime = Time.time;
    }

    private void Start()
    {
        var car = FindObjectOfType<IzbushkaCarController>();
        if (car) _rb = car.GetComponent<Rigidbody>();
        if (distanceSensorOrigin == null && car != null)
            distanceSensorOrigin = car.transform;
    }

    private void FixedUpdate()
    {
        UpdateDistance();
        UpdateGyro();
    }

    private void Update()
    {
        if (TransportReceiver.Instance == null) return;
        if (Time.time - _lastSubscriptionPush < subscriptionInterval) return;
        _lastSubscriptionPush = Time.time;

        TransportReceiver.Instance.PushSubscriptionData("distance", GetDistance());
        TransportReceiver.Instance.PushSubscriptionData("millis", GetMillis());
        TransportReceiver.Instance.PushSubscriptionData("gyro", GetGyroJson());
    }

    private void UpdateDistance()
    {
        if (distanceSensorOrigin == null) return;
        if (Physics.Raycast(distanceSensorOrigin.position, distanceSensorOrigin.forward, out RaycastHit hit, maxDistance, distanceLayerMask))
            _lastDistance = hit.distance * 100f;
        else
            _lastDistance = maxDistance * 100f;
    }

    private void UpdateGyro()
    {
        if (_rb == null) return;
        _lastAccel = _rb.transform.InverseTransformDirection(Physics.gravity).normalized * -1f;
        _lastGyro = _rb.angularVelocity;
    }

    public static int GetDistance()
    {
        return Mathf.RoundToInt(_lastDistance);
    }

    public static int GetMillis()
    {
        if (_instance == null) return 0;
        return Mathf.RoundToInt((Time.time - _instance._startTime) * 1000f);
    }

    public static JObject GetGyroJson()
    {
        return new JObject
        {
            ["accel"] = new JArray(_lastAccel.x, _lastAccel.y, _lastAccel.z),
            ["gyro"] = new JArray(_lastGyro.x, _lastGyro.y, _lastGyro.z),
            ["temperature"] = _lastTemp
        };
    }
}