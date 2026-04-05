using Live2D.Cubism.Framework.LookAt;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NikkeViewerEX.Components
{
    public class CubismMouseLookTarget : MonoBehaviour, ICubismLookTarget
    {
        Camera cachedCamera;

        public Vector3 GetPosition()
        {
            if (cachedCamera == null)
                cachedCamera = Camera.main;
            if (cachedCamera == null)
                return transform.position;

            var mouse = Mouse.current;
            if (mouse == null)
                return transform.position;

            Vector2 screenPos = mouse.position.ReadValue();
            var viewport = cachedCamera.ScreenToViewportPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            float nx = (viewport.x * 2f) - 1f;
            float ny = (viewport.y * 2f) - 1f;

            return transform.position + new Vector3(nx, ny, 0f);
        }

        public bool IsActive()
        {
            return isActiveAndEnabled;
        }
    }
}
