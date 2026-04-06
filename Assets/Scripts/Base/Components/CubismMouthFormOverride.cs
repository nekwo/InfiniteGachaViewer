using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using UnityEngine;

namespace NikkeViewerEX.Components
{
    public class CubismMouthFormOverride : MonoBehaviour, ICubismUpdatable
    {
        CubismParameter paramMouthForm;
        CubismParameter paramMouthOpenY;
        public bool Paused { get; set; }

        public int ExecutionOrder => 450;
        public bool NeedsUpdateOnEditing => false;
        public bool HasUpdateController { get; set; }

        public void OnLateUpdate() { }

        void Start()
        {
            var model = GetComponentInParent<CubismModel>();
            if (model == null) return;

            foreach (var p in model.Parameters)
            {
                if (p.Id == "ParamMouthForm")
                    paramMouthForm = p;
                else if (p.Id == "ParamMouthOpenY")
                    paramMouthOpenY = p;
            }

            HasUpdateController = GetComponent<CubismUpdateController>() != null;
        }

        public void SetMouthForm(float value)
        {
            if (Paused) return;
            
            if (paramMouthForm != null)
            {
                value = Mathf.Clamp(value, paramMouthForm.MinimumValue, paramMouthForm.MaximumValue);
                paramMouthForm.BlendToValue(CubismParameterBlendMode.Override, value);
            }
            
            if (paramMouthOpenY != null)
            {
                // For smile, keep mouth closed (0)
                paramMouthOpenY.BlendToValue(CubismParameterBlendMode.Override, 0f);
            }
        }
    }
}
