using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using NikkeViewerEX.UI;

namespace NikkeViewerEX.Core
{
    /// <summary>
    /// Component that manage user input.
    /// </summary>
    [AddComponentMenu("Nikke Viewer EX/Core/Input Manager")]
    public class InputManager : MonoBehaviour
    {
        public InputAction PointerClick { get; private set; }
        public InputAction PointerHold { get; private set; }
        public InputAction PointerPosition { get; private set; }
        public InputAction MiddleClick { get; private set; }
        public InputAction RightClick { get; private set; }

        public InputAction ToggleUI { get; private set; }

        [SerializeField]
        InputActionAsset inputSettings;

        private static EventSystem _eventSystem;

        public static bool IsPointerOverUI()
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            float screenHeight = Screen.height;
            
            var panels = Object.FindObjectsOfType<NikkeBrowserPanel>();
            
            foreach (var browserPanel in panels)
            {
                if (browserPanel == null) continue;
                
                Rect bounds = browserPanel.GetPanelBounds();
                
                if (bounds.width > 0 && bounds.height > 0)
                {
                    float mouseY = screenHeight - mousePos.y;
                    Vector2 convertedMouse = new Vector2(mousePos.x, mouseY);
                    bool contains = bounds.Contains(convertedMouse);
                    Debug.Log($"[InputManager] Panel: {bounds}, Mouse: {mousePos} -> {convertedMouse}, Contains: {contains}");
                    if (contains)
                        return true;
                }
            }
            
            return false;
        }

        void Awake()
        {
            PointerClick = inputSettings.FindActionMap("Nikke").FindAction("PointerClick");
            PointerHold = inputSettings.FindActionMap("Nikke").FindAction("PointerHold");
            MiddleClick = inputSettings.FindActionMap("Nikke").FindAction("MiddleClick");
            RightClick = inputSettings.FindActionMap("Nikke").FindAction("RightClick");
            ToggleUI = inputSettings.FindActionMap("UI").FindAction("ToggleUI");
        }

        void OnEnable()
        {
            inputSettings.Enable();
        }

        void OnDestroy()
        {
            inputSettings.Disable();
        }
    }
}
