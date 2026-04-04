using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NikkeViewerEX.Core;
using NikkeViewerEX.Serialization;
using NikkeViewerEX.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NikkeViewerEX.Components
{
    /// <summary>
    /// The base class of Nikke Viewer.
    /// </summary>
    public abstract class NikkeViewerBase : MonoBehaviour
    {
        /// <summary>
        /// The data of Nikke.
        /// </summary>
        /// <returns></returns>
        public Nikke NikkeData = new();
        public string[] Skins { get; set; }
        public static NikkeViewerBase Possessed { get; set; }

        public delegate void OnSkinChangedHandler(int index);
        public event OnSkinChangedHandler OnSkinChanged;

        public MainControl MainControl { get; private set; }
        public SettingsManager SettingsManager { get; private set; }
        public InputManager InputManager { get; private set; }
        readonly SpineHelperBase spineHelper = new();
        public TextMeshPro NikkeNameText { get; set; }

        /// <summary>
        /// Cached main camera reference. Use this instead of Camera.main in hot paths.
        /// Falls back to Camera.main if the cached reference becomes null (e.g. camera recreated).
        /// </summary>
        Camera _cachedCamera;
        public Camera CachedCamera
        {
            get
            {
                if (_cachedCamera == null)
                    _cachedCamera = Camera.main;
                return _cachedCamera;
            }
        }

        readonly float dragSmoothTime = .1f;
        Vector2 dragObjectVelocity;
        Vector3 dragObjectOffset;

        /// <summary>
        /// Does Nikke currently being dragged?
        /// </summary>
        /// <value></value>
        public bool IsDragged { get; private set; }

        /// <summary>
        /// The AudioSource component of the Nikke.
        /// </summary>
        /// <value></value>
        public AudioSource NikkeAudioSource { get; private set; }

        /// <summary>
        /// List of touch voices AudioClip.
        /// </summary>
        /// <returns></returns>
        public List<AudioClip> TouchVoices { get; set; } = new();

        /// <summary>
        /// Touch animations discovered from the skeleton (all animations matching the touch prefix).
        /// Populated at spawn time. Cycled in lockstep with TouchVoices.
        /// </summary>
        public List<string> TouchAnimations { get; set; } = new();

        /// <summary>
        /// Shared touch index, advanced once per interaction.
        /// Used modulo TouchVoices.Count and TouchAnimations.Count independently.
        /// </summary>
        public int TouchVoiceIndex = 0;

        /// <summary>
        /// Allow interacting with the Nikke?
        /// </summary>
        /// <value></value>
        public bool AllowInteraction { get; set; } = true;

        private void Awake()
        {
            MainControl = Core.MainControl.Instance
                ?? FindObjectsByType<MainControl>(FindObjectsSortMode.None)[0];
            SettingsManager = MainControl.GetComponent<SettingsManager>();
            InputManager = FindObjectsByType<InputManager>(FindObjectsSortMode.None)[0];
            _cachedCamera = Camera.main;
            NikkeAudioSource = GetComponent<AudioSource>();
        }

        public virtual void OnEnable()
        {
            InputManager.PointerHold.started += DragNikke;
            MainControl.HideUIToggle.onValueChanged.AddListener(ToggleDisplayName);
        }

        public virtual void OnDestroy()
        {
            InputManager.PointerHold.started -= DragNikke;
            MainControl.HideUIToggle.onValueChanged.RemoveListener(ToggleDisplayName);
        }

        public virtual void Update()
        {
        }


        public void ToggleDisplayName(bool hideUI)
        {
            if (NikkeNameText != null)
                NikkeNameText.gameObject.SetActive(!NikkeData.HideName);
        }

        /// <summary>
        /// Invoke OnSkinChanged event.
        /// </summary>
        /// <param name="index"></param>
        public void InvokeChangeSkin(int index) => OnSkinChanged?.Invoke(index);

        /// <summary>
        /// Immediately trigger Spine loading without waiting for an event.
        /// Called by NikkeBrowserPanel after setting NikkeData directly.
        /// </summary>
        public virtual void TriggerSpawn() { }

        /// <summary>
        /// Create the floating name text if it doesn't exist yet, then show/hide it.
        /// Called lazily the first time the user enables the name display.
        /// </summary>
        public virtual void EnsureNameText() { }

        /// <summary>
        /// Switch the visible pose. Only one pose is active at a time.
        /// </summary>
        public virtual void SetActivePose(Serialization.NikkePoseType poseType) { }

        /// <summary>
        /// Debug information for a single pose's skeleton.
        /// </summary>
        public struct PoseDebugInfo
        {
            public NikkePoseType PoseType;
            public bool IsActive;
            public string[] Animations;
            public string CurrentAnimation;
            public string[] SkinNames;
            public string CurrentSkin;
        }

        /// <summary>
        /// Returns debug information for all loaded poses. Override in subclasses.
        /// </summary>
        public virtual List<PoseDebugInfo> GetPoseDebugInfo() => new();

        /// <summary>
        /// Play an animation by name. Override in subclasses.
        /// </summary>
        public virtual void PlayAnimationByName(string animationName) { }

        /// <summary>
        /// Add MeshCollider component to the specified target (or this GameObject).
        /// </summary>
        public void AddMeshCollider(GameObject target = null)
        {
            target ??= gameObject;
            MeshCollider meshCollider = target.AddComponent<MeshCollider>();
            if (target.TryGetComponent(out MeshFilter meshFilter))
                meshCollider.sharedMesh = meshFilter.sharedMesh;
        }

        #region Drag & Drop Nikke
        /// <summary>
        /// Perform Raycast from pointer to start dragging.
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        private void DragNikke(InputAction.CallbackContext ctx)
        {
            DragNikkeAsync(ctx).Forget();
        }

        private async UniTaskVoid DragNikkeAsync(InputAction.CallbackContext ctx)
        {
            if (!NikkeData.Lock)
            {
                Ray ray = CachedCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    var viewer = hit.collider.GetComponentInParent<NikkeViewerBase>();
                    if (viewer != null && viewer == this)
                        await DragUpdate(viewer.gameObject);
                }
            }
        }

        /// <summary>
        /// Update Nikke position based on pointer.
        /// </summary>
        /// <param name="clickedObject"></param>
        /// <returns></returns>
        private async UniTask DragUpdate(GameObject clickedObject)
        {
            if (NikkeData.Lock)
                return;

            float initialDistance = Vector3.Distance(
                clickedObject.transform.position,
                CachedCamera.transform.position
            );

            Ray initialRay = CachedCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            Vector3 initialPoint = initialRay.GetPoint(initialDistance);
            dragObjectOffset = clickedObject.transform.position - initialPoint;

            while (InputManager.PointerHold.ReadValue<float>() != 0)
            {
                Ray ray = CachedCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
                Vector3 targetPoint = ray.GetPoint(initialDistance) + dragObjectOffset;
                clickedObject.transform.position = Vector2.SmoothDamp(
                    clickedObject.transform.position,
                    targetPoint,
                    ref dragObjectVelocity,
                    dragSmoothTime
                );
                IsDragged = true;
                await UniTask.Yield();
            }

            await PostDragNikke();
        }

        /// <summary>
        /// Post action after dropping the Nikke.
        /// </summary>
        /// <returns></returns>
        private async UniTask PostDragNikke()
        {
            if (this != null)
            {
                NikkeData.Position = gameObject.transform.position;
                dragObjectVelocity = Vector2.zero;
                IsDragged = false;
                OnNikkeDataChanged();
                await SettingsManager.SaveSettings();
            }
        }
        #endregion

        /// <summary>
        /// Called after NikkeData fields are mutated. Override to sync derived data.
        /// </summary>
        public virtual void OnNikkeDataChanged() { }

        public void AdjustNikkeScale(float scale)
        {
            Vector3 newScale = Vector3.one * scale;
            transform.localScale = newScale;
            NikkeData.Scale = newScale;
            OnNikkeDataChanged();
            SettingsManager.SaveSettings().Forget();
        }

        public void ResetNikkeScale()
        {
            transform.localScale = Vector3.one;
        }
    }
}
