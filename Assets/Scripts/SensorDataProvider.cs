using UnityEngine;

public class SensorDataProvider : MonoBehaviour
{
    [Header("Subscriptions")]
    [SerializeField] private float subscriptionInterval = 0.1f;

    private static SensorDataProvider _instance;
    private float _startTime;
    private float _lastSubscriptionPush;

    private void Awake()
    {
        _instance = this;
        _startTime = Time.time;
    }

    private void Update()
    {
        if (TransportReceiver.Instance == null) return;
        if (Time.time - _lastSubscriptionPush < subscriptionInterval) return;
        _lastSubscriptionPush = Time.time;

        TransportReceiver.Instance.PushSubscriptionData("millis", GetMillis());
    }

    public static int GetMillis()
    {
        if (_instance == null) return 0;
        return Mathf.RoundToInt((Time.time - _instance._startTime) * 1000f);
    }
}
