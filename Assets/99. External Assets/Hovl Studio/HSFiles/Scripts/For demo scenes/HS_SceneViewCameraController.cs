using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Hovl
{
    /// <summary>
    /// Runtime free-camera controls modelled after the Unity Scene view.
    /// Supports the New Input System, the Legacy Input Manager, or Both.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class HS_SceneViewCameraController : MonoBehaviour
    {
        [Header("Movement")]
        [Min(0.01f)] public float movementSpeed = 5f;
        [Min(1f)] public float fastMovementMultiplier = 4f;
        [Min(0.01f)] public float minimumMovementSpeed = 0.05f;
        [Min(0.01f)] public float maximumMovementSpeed = 500f;
        [Min(1.01f)] public float speedChangePerScrollStep = 1.2f;
        public bool useUnscaledTime = true;

        [Header("Mouse")]
        [Min(0f)] public float lookSensitivity = 1f;
        [Min(0f)] public float panSensitivity = 0.0025f;
        [Min(0f)] public float scrollDollyDistance = 1f;
        public bool invertLookY;
        public bool lockCursorWhileLooking = true;

        [Header("Orbit and Focus (Optional)")]
        [Tooltip("Alt + LMB orbits around this transform. F focuses on it.")]
        public Transform focusTarget;
        [Min(0.01f)] public float focusDistance = 5f;

        private float _yaw;
        private float _pitch;
        private bool _cursorCaptured;

        private void OnEnable()
        {
            ReadRotationFromTransform();
        }

        private void OnDisable()
        {
            ReleaseCursor();
        }

        private void Update()
        {
            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            bool rightMouse = GetMouseButton(1);
            bool middleMouse = GetMouseButton(2);
            bool orbit = focusTarget != null && IsAltPressed() && GetMouseButton(0);
            Vector2 mouseDelta = GetMouseDelta();
            float scroll = GetScrollSteps();

            UpdateCursor(rightMouse);

            if (rightMouse)
            {
                Look(mouseDelta);
                Fly(deltaTime);

                if (!Mathf.Approximately(scroll, 0f))
                {
                    movementSpeed *= Mathf.Pow(speedChangePerScrollStep, scroll);
                    movementSpeed = Mathf.Clamp(
                        movementSpeed,
                        minimumMovementSpeed,
                        maximumMovementSpeed);
                }
            }
            else
            {
                if (middleMouse)
                    Pan(mouseDelta);

                if (orbit)
                    Orbit(mouseDelta);

                if (!Mathf.Approximately(scroll, 0f))
                    transform.position += transform.forward * (scroll * scrollDollyDistance);
            }

            if (focusTarget != null && WasFocusPressed())
                FocusOnTarget();
        }

        private void Look(Vector2 mouseDelta)
        {
            Vector2 look = ConvertMouseDeltaToDegrees(mouseDelta) * lookSensitivity;
            _yaw += look.x;
            _pitch += invertLookY ? look.y : -look.y;
            _pitch = Mathf.Clamp(_pitch, -89.9f, 89.9f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void Fly(float deltaTime)
        {
            Vector3 input = new Vector3(
                GetHorizontalMovement(),
                GetVerticalMovement(),
                GetForwardMovement());

            if (input.sqrMagnitude > 1f)
                input.Normalize();

            float speed = movementSpeed;
            if (IsFastMovePressed())
                speed *= fastMovementMultiplier;

            transform.position += transform.TransformDirection(input) * (speed * deltaTime);
        }

        private void Pan(Vector2 mouseDelta)
        {
            float distanceScale = focusTarget != null
                ? Mathf.Max(1f, Vector3.Distance(transform.position, focusTarget.position))
                : Mathf.Max(1f, focusDistance);

            Vector2 pan = ConvertMouseDeltaToPixels(mouseDelta);
            transform.position +=
                (-transform.right * pan.x - transform.up * pan.y) *
                (panSensitivity * distanceScale);
        }

        private void Orbit(Vector2 mouseDelta)
        {
            Vector3 pivot = focusTarget.position;
            Vector3 offset = transform.position - pivot;

            if (offset.sqrMagnitude < 0.0001f)
                offset = -transform.forward * focusDistance;

            Vector2 look = ConvertMouseDeltaToDegrees(mouseDelta) * lookSensitivity;
            float yawDelta = look.x;
            float pitchDelta = invertLookY ? -look.y : look.y;

            Quaternion yawRotation = Quaternion.AngleAxis(yawDelta, Vector3.up);
            Vector3 yawedOffset = yawRotation * offset;
            Vector3 rightAxis = Vector3.Cross(Vector3.up, yawedOffset).normalized;
            Quaternion pitchRotation = Quaternion.AngleAxis(pitchDelta, rightAxis);
            Vector3 newOffset = pitchRotation * yawedOffset;

            Vector3 directionFromPivot = newOffset.normalized;
            float verticalDot = Mathf.Abs(Vector3.Dot(directionFromPivot, Vector3.up));
            if (verticalDot > 0.999f)
                newOffset = yawedOffset;

            transform.position = pivot + newOffset;
            transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
            ReadRotationFromTransform();
        }

        public void FocusOnTarget()
        {
            if (focusTarget == null)
                return;

            Vector3 backward = -transform.forward;
            if (backward.sqrMagnitude < 0.0001f)
                backward = Vector3.back;

            transform.position = focusTarget.position + backward.normalized * focusDistance;
            transform.LookAt(focusTarget.position, Vector3.up);
            ReadRotationFromTransform();
        }

        private void ReadRotationFromTransform()
        {
            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        private void UpdateCursor(bool shouldCapture)
        {
            if (!lockCursorWhileLooking)
                return;

            if (shouldCapture && !_cursorCaptured)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                _cursorCaptured = true;
            }
            else if (!shouldCapture && _cursorCaptured)
            {
                ReleaseCursor();
            }
        }

        private void ReleaseCursor()
        {
            if (!_cursorCaptured)
                return;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorCaptured = false;
        }

        private static Vector2 ConvertMouseDeltaToDegrees(Vector2 delta)
        {
#if ENABLE_INPUT_SYSTEM
            return delta * 0.08f;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return delta * 3f;
#else
            return Vector2.zero;
#endif
        }

        private static Vector2 ConvertMouseDeltaToPixels(Vector2 delta)
        {
#if ENABLE_INPUT_SYSTEM
            return delta;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return delta * 25f;
#else
            return Vector2.zero;
#endif
        }

        private static Vector2 GetMouseDelta()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#else
            return Vector2.zero;
#endif
        }

        private static bool GetMouseButton(int button)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
                return false;

            switch (button)
            {
                case 0: return Mouse.current.leftButton.isPressed;
                case 1: return Mouse.current.rightButton.isPressed;
                case 2: return Mouse.current.middleButton.isPressed;
                default: return false;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(button);
#else
            return false;
#endif
        }

        private static float GetScrollSteps()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
                return 0f;
            return Mathf.Clamp(Mouse.current.scroll.ReadValue().y / 120f, -5f, 5f);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Mathf.Clamp(Input.mouseScrollDelta.y, -5f, 5f);
#else
            return 0f;
#endif
        }

        private static float GetHorizontalMovement()
        {
            float value = 0f;
            if (IsKeyPressed(KeyCode.A, "a") || IsKeyPressed(KeyCode.LeftArrow, "left")) value -= 1f;
            if (IsKeyPressed(KeyCode.D, "d") || IsKeyPressed(KeyCode.RightArrow, "right")) value += 1f;
            return value;
        }

        private static float GetForwardMovement()
        {
            float value = 0f;
            if (IsKeyPressed(KeyCode.S, "s") || IsKeyPressed(KeyCode.DownArrow, "down")) value -= 1f;
            if (IsKeyPressed(KeyCode.W, "w") || IsKeyPressed(KeyCode.UpArrow, "up")) value += 1f;
            return value;
        }

        private static float GetVerticalMovement()
        {
            float value = 0f;
            if (IsKeyPressed(KeyCode.Q, "q")) value -= 1f;
            if (IsKeyPressed(KeyCode.E, "e")) value += 1f;
            return value;
        }

        private static bool IsFastMovePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null &&
                   (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#else
            return false;
#endif
        }

        private static bool IsAltPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null &&
                   (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#else
            return false;
#endif
        }

        private static bool WasFocusPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.F);
#else
            return false;
#endif
        }

        // The string is used only by the New Input System branch; KeyCode is used only by Legacy.
        private static bool IsKeyPressed(KeyCode legacyKey, string newInputKey)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
                return false;

            switch (newInputKey)
            {
                case "a": return Keyboard.current.aKey.isPressed;
                case "d": return Keyboard.current.dKey.isPressed;
                case "w": return Keyboard.current.wKey.isPressed;
                case "s": return Keyboard.current.sKey.isPressed;
                case "q": return Keyboard.current.qKey.isPressed;
                case "e": return Keyboard.current.eKey.isPressed;
                case "left": return Keyboard.current.leftArrowKey.isPressed;
                case "right": return Keyboard.current.rightArrowKey.isPressed;
                case "up": return Keyboard.current.upArrowKey.isPressed;
                case "down": return Keyboard.current.downArrowKey.isPressed;
                default: return false;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(legacyKey);
#else
            return false;
#endif
        }
    }
}