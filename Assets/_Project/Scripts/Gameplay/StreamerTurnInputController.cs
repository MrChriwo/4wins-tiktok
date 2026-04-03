using FourWinsTikTok.World;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FourWinsTikTok.Gameplay
{
    public class StreamerTurnInputController : MonoBehaviour
    {
        [SerializeField] private GameFlowController gameFlowController;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private LayerMask clickLayerMask = ~0;
        [SerializeField] private bool enableKeyboardInput = true;
        [SerializeField] private bool enableMouseInput = true;
        [SerializeField] private bool logInput;

        private void Update()
        {
            if (gameFlowController == null || gameFlowController.CurrentState != GameFlowState.WaitingForStreamerMove)
            {
                return;
            }

            if (gameFlowController.IsSabotageTurnActive)
            {
                return;
            }

            if (enableKeyboardInput)
            {
                HandleKeyboardInput();
            }

            if (enableMouseInput)
            {
                HandleMouseInput();
            }
        }

        private void HandleKeyboardInput()
        {
            if (TryHandleNumericKey(1)) return;
            if (TryHandleNumericKey(2)) return;
            if (TryHandleNumericKey(3)) return;
            if (TryHandleNumericKey(4)) return;
            if (TryHandleNumericKey(5)) return;
            if (TryHandleNumericKey(6)) return;
            TryHandleNumericKey(7);
        }

        private bool TryHandleNumericKey(int displayColumn)
        {
            if (!IsDigitPressed(displayColumn))
            {
                return false;
            }

            bool accepted = gameFlowController.TrySubmitStreamerMoveFromDisplayColumn(displayColumn);
            if (logInput)
            {
                Debug.Log($"StreamerInput: Keyboard '{displayColumn}' -> accepted={accepted}");
            }

            return true;
        }

        private void HandleMouseInput()
        {
            if (!WasPrimaryPointerPressedThisFrame())
            {
                return;
            }

            Camera activeCamera = inputCamera != null ? inputCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            Ray ray = activeCamera.ScreenPointToRay(GetPointerScreenPosition());
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, clickLayerMask))
            {
                return;
            }

            ConnectFourColumnClickTarget clickTarget = hit.collider.GetComponentInParent<ConnectFourColumnClickTarget>();
            if (clickTarget == null)
            {
                return;
            }

            bool accepted = gameFlowController.TrySubmitStreamerMoveFromColumnIndex(clickTarget.ColumnIndex);
            if (logInput)
            {
                Debug.Log($"StreamerInput: Click columnIndex={clickTarget.ColumnIndex} -> accepted={accepted}");
            }
        }

        private bool IsDigitPressed(int displayColumn)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            switch (displayColumn)
            {
                case 1: return keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame;
                case 2: return keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame;
                case 3: return keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame;
                case 4: return keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame;
                case 5: return keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame;
                case 6: return keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame;
                case 7: return keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame;
                default: return false;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            switch (displayColumn)
            {
                case 1: return Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1);
                case 2: return Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2);
                case 3: return Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3);
                case 4: return Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4);
                case 5: return Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5);
                case 6: return Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6);
                case 7: return Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7);
                default: return false;
            }
#else
            return false;
#endif
        }

        private bool WasPrimaryPointerPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }

        private Vector3 GetPointerScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                Vector2 mousePosition = Mouse.current.position.ReadValue();
                return new Vector3(mousePosition.x, mousePosition.y, 0f);
            }

            return Vector3.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector3.zero;
#endif
        }
    }
}
