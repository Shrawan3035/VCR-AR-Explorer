using UnityEngine;

namespace VCR
{
    /// <summary>
    /// Orbit / pan / zoom camera for mouse and touch.
    /// Mouse: left-drag orbit, right/middle-drag pan, wheel zoom.
    /// Touch: one finger orbit, two-finger pinch zoom and drag pan.
    /// Gestures that start over UI are ignored.
    /// </summary>
    public class OrbitCameraController : MonoBehaviour
    {
        public Vector3 defaultTarget = new Vector3(0f, 0.4f, 0f);
        public float defaultYaw = 149f; // looking at the front (+Z) of the rig from the front-left
        public float defaultPitch = 24f;
        public float defaultDistance = 2.1f;
        public float minDistance = 0.35f;
        public float maxDistance = 5f;
        public float minPitch = -5f;
        public float maxPitch = 85f;
        public float orbitSpeed = 0.25f;
        public float panSpeed = 1f;
        public float smoothing = 12f;
        public bool inputEnabled = true;

        private Vector3 _target, _targetGoal;
        private float _yaw, _pitch, _distance;
        private float _yawGoal, _pitchGoal, _distanceGoal;
        private bool _mouseGestureValid, _touchGestureValid;
        private float _lastPinch;
        private Vector2 _lastMid;
        private int _lastTouchCount;
        private Camera _cam;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            ResetView(true);
        }

        public void ResetView(bool instant = false)
        {
            _targetGoal = defaultTarget;
            _yawGoal = defaultYaw;
            _pitchGoal = defaultPitch;
            _distanceGoal = defaultDistance;
            if (instant) Snap();
        }

        public void SetView(Vector3 target, float yaw, float pitch, float distance, bool instant)
        {
            _targetGoal = target;
            _yawGoal = yaw;
            _pitchGoal = pitch;
            _distanceGoal = Mathf.Clamp(distance, minDistance, maxDistance);
            if (instant) Snap();
        }

        private void Snap()
        {
            _target = _targetGoal; _yaw = _yawGoal; _pitch = _pitchGoal; _distance = _distanceGoal;
            Apply();
        }

        public void FocusOn(Bounds b)
        {
            _targetGoal = b.center;
            _distanceGoal = Mathf.Clamp(b.size.magnitude * 2.2f, minDistance, 2.2f);
        }

        public void Zoom(float factor) => _distanceGoal = Mathf.Clamp(_distanceGoal * factor, minDistance, maxDistance);

        private void Update()
        {
            if (inputEnabled) HandleInput();
            float k = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            _target = Vector3.Lerp(_target, _targetGoal, k);
            _yaw = Mathf.LerpAngle(_yaw, _yawGoal, k);
            _pitch = Mathf.Lerp(_pitch, _pitchGoal, k);
            _distance = Mathf.Lerp(_distance, _distanceGoal, k);
            Apply();
        }

        private void Apply()
        {
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.position = _target + rot * new Vector3(0f, 0f, -_distance);
            transform.rotation = rot;
        }

        private void HandleInput()
        {
            var touches = VCRInput.Touches;
            if (touches.Count > 0)
            {
                if (touches.Count != _lastTouchCount)
                {
                    // gesture (re)starts whenever the number of fingers changes
                    _touchGestureValid = true;
                    foreach (var t in touches) if (t.began && VCRInput.IsOverUI(t.position)) _touchGestureValid = false;
                    if (touches.Count >= 2)
                    {
                        _lastPinch = Vector2.Distance(touches[0].position, touches[1].position);
                        _lastMid = (touches[0].position + touches[1].position) * 0.5f;
                    }
                }
                _lastTouchCount = touches.Count;
                if (!_touchGestureValid) return;
                if (touches.Count == 1)
                {
                    Orbit(touches[0].delta);
                }
                else
                {
                    float pinch = Vector2.Distance(touches[0].position, touches[1].position);
                    Vector2 mid = (touches[0].position + touches[1].position) * 0.5f;
                    if (_lastPinch > 1f && pinch > 1f) _distanceGoal = Mathf.Clamp(_distanceGoal * (_lastPinch / pinch), minDistance, maxDistance);
                    Pan(mid - _lastMid);
                    _lastPinch = pinch;
                    _lastMid = mid;
                }
                return;
            }
            _lastTouchCount = 0;

            if (!VCRInput.UseMouse) return;
            Vector2 mp = VCRInput.MousePosition;
            if (VCRInput.LeftDown || VCRInput.PanDown) _mouseGestureValid = !VCRInput.IsOverUI(mp);
            if (_mouseGestureValid && VCRInput.LeftHeld) Orbit(VCRInput.MouseDelta);
            else if (_mouseGestureValid && VCRInput.PanHeld) Pan(VCRInput.MouseDelta);
            float scroll = VCRInput.Scroll;
            if (Mathf.Abs(scroll) > 0.01f && !VCRInput.IsOverUI(mp)) _distanceGoal = Mathf.Clamp(_distanceGoal * Mathf.Pow(0.88f, scroll), minDistance, maxDistance);
            if (VCRInput.ResetPressed) ResetView();
        }

        private void Orbit(Vector2 delta)
        {
            float dpiScale = Screen.dpi > 0 ? 160f / Screen.dpi : 1f;
            _yawGoal += delta.x * orbitSpeed * dpiScale;
            _pitchGoal = Mathf.Clamp(_pitchGoal - delta.y * orbitSpeed * dpiScale, minPitch, maxPitch);
        }

        private void Pan(Vector2 delta)
        {
            if (_cam == null) return;
            // convert pixels to world units at the target distance
            float worldPerPixel = 2f * _distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            _targetGoal -= (transform.right * delta.x + transform.up * delta.y) * worldPerPixel * panSpeed;
        }
    }
}
