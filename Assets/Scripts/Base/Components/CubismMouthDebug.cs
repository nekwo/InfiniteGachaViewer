using Live2D.Cubism.Core;
using UnityEngine;

namespace NikkeViewerEX.Components
{
    public class CubismMouthDebug : MonoBehaviour
    {
        void Start()
        {
            var model = GetComponentInParent<CubismModel>();
            if (model == null) return;

            foreach (var p in model.Parameters)
            {
                if (p.Id == "ParamMouthForm")
                {
                    Debug.Log($"[MouthDebug] min={p.MinimumValue}, max={p.MaximumValue}, default={p.DefaultValue}");
                    break;
                }
            }
        }

        public void PrintValue()
        {
            var model = GetComponentInParent<CubismModel>();
            if (model == null) return;

            foreach (var p in model.Parameters)
            {
                if (p.Id == "ParamMouthForm" || p.Id == "ParamMouthOpenY")
                {
                    Debug.Log($"[MouthDebug] {p.Id}: value={p.Value}, min={p.MinimumValue}, max={p.MaximumValue}");
                }
            }
        }
    }
}
