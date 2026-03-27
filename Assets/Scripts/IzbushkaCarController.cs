using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class IzbushkaCarController : MonoBehaviour
{
    [Header("Wheels Colliders")]
    public WheelCollider frontLeftWheel;
    public WheelCollider frontRightWheel;
    public WheelCollider rearLeftWheel;
    public WheelCollider rearRightWheel;

    [Header("Wheels Visuals (Optional)")]
    public Transform frontLeftMesh;
    public Transform frontRightMesh;
    public Transform rearLeftMesh;
    public Transform rearRightMesh;

    [Header("Movement Settings")]
    public float maxMotorTorque = 2500f;
    public float maxBrakeTorque = 5000f;
    
    public bool invertMotorDirection = true;
    public bool swapLeftRight = false;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        
        _rb.mass = 1200f;
        _rb.drag = 0.15f;
        _rb.angularDrag = 10f;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        
        _rb.centerOfMass = new Vector3(0, -0.6f, 0);

        ConfigureWheel(frontLeftWheel);
        ConfigureWheel(frontRightWheel);
        ConfigureWheel(rearLeftWheel);
        ConfigureWheel(rearRightWheel);
    }

    private void ConfigureWheel(WheelCollider wheel)
    {
        if (wheel == null) return;

        JointSpring suspension = wheel.suspensionSpring;
        suspension.spring = 40000f;
        suspension.damper = 5000f;
        suspension.targetPosition = 0.5f;
        wheel.suspensionSpring = suspension;
        wheel.suspensionDistance = 0.2f;

        WheelFrictionCurve forward = wheel.forwardFriction;
        forward.extremumSlip = 0.4f;
        forward.extremumValue = 1.0f;
        forward.asymptoteSlip = 0.8f;
        forward.asymptoteValue = 0.5f;
        forward.stiffness = 2.0f;
        wheel.forwardFriction = forward;

        WheelFrictionCurve sideways = wheel.sidewaysFriction;
        sideways.extremumSlip = 0.2f;
        sideways.extremumValue = 1.0f;
        sideways.asymptoteSlip = 0.5f;
        sideways.asymptoteValue = 0.75f;
        sideways.stiffness = 0.04f;
        wheel.sidewaysFriction = sideways;
    }

    public const int MOTOR_LEFT = 1;
    public const int MOTOR_RIGHT = 2;
    public const int MOTOR_BOTH = 3;

    public const int DIR_STOP = 0;
    public const int DIR_FORWARD = 1;
    public const int DIR_BACKWARD = 2;
    public const int DIR_BRAKE = 3;

    public int LeftDirection { get; private set; } = DIR_STOP;
    public int RightDirection { get; private set; } = DIR_STOP;
    public int LeftSpeed { get; private set; } = 0;
    public int RightSpeed { get; private set; } = 0;

    public float NormalizedLeftVelocity => (LeftSpeed / 255f) * (LeftDirection == DIR_BACKWARD ? -1f : 1f);
    public float NormalizedRightVelocity => (RightSpeed / 255f) * (RightDirection == DIR_BACKWARD ? -1f : 1f);

    private float _currentLeftTorque = 0f;
    private float _currentRightTorque = 0f;
    private float _currentLeftBrake = 0f;
    private float _currentRightBrake = 0f;

    public float CurrentTurnIntensity 
    {
        get 
        {
            float leftNorm = _currentLeftTorque / maxMotorTorque;
            float rightNorm = _currentRightTorque / maxMotorTorque;
            return Mathf.Clamp(rightNorm - leftNorm, -1f, 1f);
        }
    }

    public float CurrentForwardIntensity
    {
        get
        {
            float leftNorm = _currentLeftTorque / maxMotorTorque;
            float rightNorm = _currentRightTorque / maxMotorTorque;
            return (Mathf.Abs(leftNorm) + Mathf.Abs(rightNorm)) / 2f;
        }
    }

    private void OnEnable()
    {
        TransportReceiver.OnMotorsMoveForward += HandleMoveForward;
        TransportReceiver.OnMotorsMoveBackward += HandleMoveBackward;
        TransportReceiver.OnMotorsTurnLeft += HandleTurnLeft;
        TransportReceiver.OnMotorsTurnRight += HandleTurnRight;
        TransportReceiver.OnMotorsRotateLeft += HandleRotateLeft;
        TransportReceiver.OnMotorsRotateRight += HandleRotateRight;
        TransportReceiver.OnMotorsStop += HandleStopAll;
        TransportReceiver.OnMotorsBrake += HandleBrakeAll;

        TransportReceiver.OnMotorsSetSpeed += HandleSetSpeed;
        TransportReceiver.OnMotorsSetDirection += HandleSetDirection;
        TransportReceiver.OnMotorsSetBoth += HandleSetBoth;
        TransportReceiver.OnMotorsSetDifferential += HandleSetDifferential;
    }

    private void OnDisable()
    {
        TransportReceiver.OnMotorsMoveForward -= HandleMoveForward;
        TransportReceiver.OnMotorsMoveBackward -= HandleMoveBackward;
        TransportReceiver.OnMotorsTurnLeft -= HandleTurnLeft;
        TransportReceiver.OnMotorsTurnRight -= HandleTurnRight;
        TransportReceiver.OnMotorsRotateLeft -= HandleRotateLeft;
        TransportReceiver.OnMotorsRotateRight -= HandleRotateRight;
        TransportReceiver.OnMotorsStop -= HandleStopAll;
        TransportReceiver.OnMotorsBrake -= HandleBrakeAll;

        TransportReceiver.OnMotorsSetSpeed -= HandleSetSpeed;
        TransportReceiver.OnMotorsSetDirection -= HandleSetDirection;
        TransportReceiver.OnMotorsSetBoth -= HandleSetBoth;
        TransportReceiver.OnMotorsSetDifferential -= HandleSetDifferential;
    }

    private void FixedUpdate()
    {
        ApplyMotorLogics();
        ApplyToWheels();
        UpdateVisuals();
    }

    private void ApplyMotorLogics()
    {
        float flip = invertMotorDirection ? -1f : 1f;

        if (LeftDirection == DIR_BRAKE)
        {
            _currentLeftTorque = 0f;
            _currentLeftBrake = maxBrakeTorque;
        }
        else if (LeftDirection == DIR_STOP)
        {
            _currentLeftTorque = 0f;
            _currentLeftBrake = 0f;
        }
        else
        {
            _currentLeftBrake = 0f;
            float dirObj = (LeftDirection == DIR_BACKWARD) ? -1f : 1f;
            _currentLeftTorque = dirObj * flip * maxMotorTorque * (LeftSpeed / 255f);
        }

        if (RightDirection == DIR_BRAKE)
        {
            _currentRightTorque = 0f;
            _currentRightBrake = maxBrakeTorque;
        }
        else if (RightDirection == DIR_STOP)
        {
            _currentRightTorque = 0f;
            _currentRightBrake = 0f;
        }
        else
        {
            _currentRightBrake = 0f;
            float dirObj = (RightDirection == DIR_BACKWARD) ? -1f : 1f;
            _currentRightTorque = dirObj * flip * maxMotorTorque * (RightSpeed / 255f);
        }
        
        if (swapLeftRight)
        {
            float tempTorque = _currentLeftTorque;
            _currentLeftTorque = _currentRightTorque;
            _currentRightTorque = tempTorque;

            float tempBrake = _currentLeftBrake;
            _currentLeftBrake = _currentRightBrake;
            _currentRightBrake = tempBrake;
        }
    }

    private void ApplyToWheels()
    {
        if (frontLeftWheel) { frontLeftWheel.motorTorque = _currentLeftTorque; frontLeftWheel.brakeTorque = _currentLeftBrake; }
        if (rearLeftWheel) { rearLeftWheel.motorTorque = _currentLeftTorque; rearLeftWheel.brakeTorque = _currentLeftBrake; }
        
        if (frontRightWheel) { frontRightWheel.motorTorque = _currentRightTorque; frontRightWheel.brakeTorque = _currentRightBrake; }
        if (rearRightWheel) { rearRightWheel.motorTorque = _currentRightTorque; rearRightWheel.brakeTorque = _currentRightBrake; }
    }

    private void UpdateVisuals()
    {
        UpdateWheelMesh(frontLeftWheel, frontLeftMesh);
        UpdateWheelMesh(frontRightWheel, frontRightMesh);
        UpdateWheelMesh(rearLeftWheel, rearLeftMesh);
        UpdateWheelMesh(rearRightWheel, rearRightMesh);
    }

    private void UpdateWheelMesh(WheelCollider collider, Transform mesh)
    {
        if (collider == null || mesh == null) return;
        collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
        mesh.position = pos;
        mesh.rotation = rot;
    }

    private void HandleMoveForward(int speed) => HandleSetDifferential(speed, speed, DIR_FORWARD, DIR_FORWARD);
    private void HandleMoveBackward(int speed) => HandleSetDifferential(speed, speed, DIR_BACKWARD, DIR_BACKWARD);
    private void HandleTurnLeft(int speed) => HandleSetDifferential(speed, speed, DIR_BACKWARD, DIR_FORWARD);
    private void HandleTurnRight(int speed) => HandleSetDifferential(speed, speed, DIR_FORWARD, DIR_BACKWARD);
    private void HandleRotateLeft(int speed) => HandleTurnLeft(speed);
    private void HandleRotateRight(int speed) => HandleTurnRight(speed);
    private void HandleStopAll() => HandleSetDifferential(0, 0, DIR_STOP, DIR_STOP);
    private void HandleBrakeAll() => HandleSetDifferential(0, 0, DIR_BRAKE, DIR_BRAKE);

    private void HandleSetSpeed(int mask, int spdL, int spdR)
    {
        if ((mask & MOTOR_LEFT) != 0) LeftSpeed = spdL;
        if ((mask & MOTOR_RIGHT) != 0) RightSpeed = spdR;
    }

    private void HandleSetDirection(int mask, int dirL, int dirR)
    {
        if ((mask & MOTOR_LEFT) != 0) LeftDirection = dirL;
        if ((mask & MOTOR_RIGHT) != 0) RightDirection = dirR;
    }

    private void HandleSetBoth(int mask, int dirL, int dirR, int spdL, int spdR)
    {
        HandleSetDirection(mask, dirL, dirR);
        HandleSetSpeed(mask, spdL, spdR);
    }

    private void HandleSetDifferential(int spdL, int spdR, int dirL, int dirR)
    {
        LeftSpeed = spdL;
        RightSpeed = spdR;
        LeftDirection = dirL;
        RightDirection = dirR;
    }
}