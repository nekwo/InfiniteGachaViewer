using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.LookAt;
using Live2D.Cubism.Framework.Motion;
using Live2D.Cubism.Framework.Physics;
using Live2D.Cubism.Framework.Raycasting;
using NikkeViewerEX.Core;
using NikkeViewerEX.Serialization;
using NikkeViewerEX.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NikkeViewerEX.Components
{
    [AddComponentMenu("Nikke Viewer EX/Components/Azur Lane Viewer")]
    [DefaultExecutionOrder(20000)]
    public class AzurLaneViewer : NikkeViewerBase
    {
        [Serializable]
        public class AnimationOverrideJson
        {
            public MotionOverrideEntry[] motionOverrides;
            public string idleOverride;
            public string[] layer0Keys;
            public string[] layer1Keys;
        }

        [Serializable]
        public class MotionOverrideEntry
        {
            public string key;
            public string[] motions;
        }

        [Header("UI")]
        [SerializeField]
        TextMeshPro m_NamePrefab;

        /// <summary>AL-specific character data. Set by NikkeBrowserPanel before TriggerSpawn.</summary>
        public AzurLaneCharacter AlCharacterData = new();

        AnimationOverrideJson animationOverrideJson;
        Dictionary<string, List<AnimationClip>> motionOverridesMap = new();
        readonly HashSet<string> layer0Keys = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> layer1Keys = new(StringComparer.OrdinalIgnoreCase);
        AnimationClip idleOverrideClip;

        static int s_SortOrderCounter = 0;
        static readonly List<AzurLaneViewer> s_AllViewers = new();

        CubismModel cubismModel;
        CubismRaycaster raycaster;
        CubismParameterStore parameterStore;
        CubismRaycastHit[] raycastBuffer = new CubismRaycastHit[4];

        List<AnimationClip> touchMotions = new();
        List<AnimationClip> idleMotions = new();
        List<AnimationClip> allMotions = new();
        Dictionary<string, string> motionJsonStrings = new();
        int touchIndex;
        bool spawned;
        bool isPlayingTouchMotion;
        int lastTouchLayer = 1;
        CubismMouthDebug mouthDebug;
        CubismMouthFormOverride mouthFormOverride;

        /// <summary>Debug parameter overrides applied in LateUpdate (not persisted).</summary>
        readonly Dictionary<string, float> parameterOverrides = new();

        /// <summary>Tracks current angle scale ratio per physics sub-rig name.</summary>
        readonly Dictionary<string, float> physicsSubRigScales = new();

        /// <summary>Part opacity overrides applied in LateUpdate.</summary>
        readonly Dictionary<string, float> partOpacityOverrides = new();

        /// <summary>Original part opacities cached at spawn time for reset.</summary>
        readonly Dictionary<string, float> originalPartOpacities = new();

        /// <summary>Drawable color alpha overrides applied in LateUpdate.</summary>
        readonly Dictionary<string, float> drawableOpacityOverrides = new();

        /// <summary>Original drawable color alphas cached at spawn time for reset.</summary>
        readonly Dictionary<string, float> originalDrawableOpacities = new();

        public override void OnEnable()
        {
            base.OnEnable();
            s_AllViewers.Add(this);
            MainControl.OnSettingsApplied += TriggerSpawn;
            SettingsManager.OnSettingsLoaded += TriggerSpawn;
            InputManager.PointerClick.performed += OnPointerClick;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            s_AllViewers.Remove(this);
            MainControl.OnSettingsApplied -= TriggerSpawn;
            SettingsManager.OnSettingsLoaded -= TriggerSpawn;
            InputManager.PointerClick.performed -= OnPointerClick;
        }

        public override void OnNikkeDataChanged()
        {
            AlCharacterData.Position = NikkeData.Position;
            AlCharacterData.Scale = NikkeData.Scale;
            AlCharacterData.Lock = NikkeData.Lock;
            AlCharacterData.HideName = NikkeData.HideName;
            SyncParameterOverridesToData();
        }

        void SyncParameterOverridesToData()
        {
            AlCharacterData.ParameterOverrides.Clear();
            foreach (var kvp in parameterOverrides)
            {
                AlCharacterData.ParameterOverrides.Add(new Serialization.ParameterOverride
                {
                    Id = kvp.Key,
                    Value = kvp.Value,
                });
            }

            AlCharacterData.PhysicsScaleOverrides.Clear();
            foreach (var kvp in physicsSubRigScales)
            {
                AlCharacterData.PhysicsScaleOverrides.Add(new Serialization.PhysicsScaleOverride
                {
                    SubRigName = kvp.Key,
                    Ratio = kvp.Value,
                });
            }

            AlCharacterData.PartOpacityOverrides.Clear();
            foreach (var kvp in partOpacityOverrides)
            {
                AlCharacterData.PartOpacityOverrides.Add(new Serialization.PartOpacityOverride
                {
                    PartId = kvp.Key,
                    Opacity = kvp.Value,
                });
            }

            AlCharacterData.DrawableOpacityOverrides.Clear();
            foreach (var kvp in drawableOpacityOverrides)
            {
                AlCharacterData.DrawableOpacityOverrides.Add(new Serialization.DrawableOpacityOverride
                {
                    DrawableId = kvp.Key,
                    Opacity = kvp.Value,
                });
            }
        }

        public override void TriggerSpawn()
        {
            if (spawned) return;
            SpawnModel().Forget();
        }

        async UniTask SpawnModel()
        {
            if (spawned) return;
            if (string.IsNullOrEmpty(AlCharacterData.Model3JsonPath))
            {
                Debug.LogWarning($"[AzurLaneViewer] Model3JsonPath is empty for {AlCharacterData.DisplayName}");
                return;
            }

            spawned = true;
            Debug.Log($"[AzurLaneViewer] Starting spawn for {AlCharacterData.DisplayName}");

            // Populate NikkeData so base-class drag/scale/name systems work
            NikkeData.InstanceId = AlCharacterData.InstanceId;
            NikkeData.NikkeName = AlCharacterData.DisplayName;
            NikkeData.AssetName = AlCharacterData.AssetName;
            NikkeData.SkelPath = AlCharacterData.Model3JsonPath;
            NikkeData.Lock = AlCharacterData.Lock;
            NikkeData.HideName = AlCharacterData.HideName;
            NikkeData.Scale = AlCharacterData.Scale;
            NikkeData.Position = AlCharacterData.Position;

            var modelData = await CubismHelper.LoadModelAsync(AlCharacterData.Model3JsonPath);
            if (modelData == null)
            {
                spawned = false;
                Debug.LogError($"[AzurLaneViewer] Failed to load model for {AlCharacterData.DisplayName}");
                return;
            }

            cubismModel = modelData.Model;
            touchMotions = modelData.TouchMotions;
            idleMotions = modelData.IdleMotions;
            allMotions = modelData.AllMotions;
            motionJsonStrings = modelData.MotionJsonStrings;

            // Add CubismUpdateController FIRST - it controls update order
            var updateController = cubismModel.GetComponent<CubismUpdateController>();
            if (updateController == null)
                updateController = cubismModel.gameObject.AddComponent<CubismUpdateController>();
            
            // Add CubismParameterStore - needed for fade transitions
            parameterStore = cubismModel.GetComponent<CubismParameterStore>();
            if (parameterStore == null)
                parameterStore = cubismModel.gameObject.AddComponent<CubismParameterStore>();

            // Parent model under this viewer
            cubismModel.transform.SetParent(transform, false);

            // Configure sorting for proper Z-order rendering
            ConfigureSorting();

            // Restore persisted position / scale
            transform.position = NikkeData.Position;
            transform.localScale = NikkeData.Scale;

            // Force initial update
            cubismModel.ForceUpdateNow();

            // Cache original part opacities before any overrides
            foreach (var part in cubismModel.Parts)
                originalPartOpacities[part.Id] = part.Opacity;

            // Cache original drawable color alphas before any overrides
            foreach (var drawable in cubismModel.Drawables)
            {
                var renderer = drawable.GetComponent<Live2D.Cubism.Rendering.CubismRenderer>();
                if (renderer != null)
                    originalDrawableOpacities[drawable.Id] = renderer.Color.a;
            }

            raycaster = cubismModel.GetComponent<CubismRaycaster>();
            if (raycaster == null)
                raycaster = cubismModel.gameObject.AddComponent<CubismRaycaster>();

            // Mark all drawables as raycastable for touch detection
            foreach (var drawable in cubismModel.Drawables)
            {
                if (drawable.GetComponent<Live2D.Cubism.Framework.Raycasting.CubismRaycastable>() == null)
                {
                    var raycastable = drawable.gameObject.AddComponent<Live2D.Cubism.Framework.Raycasting.CubismRaycastable>();
                    raycastable.Precision = Live2D.Cubism.Framework.Raycasting.CubismRaycastablePrecision.BoundingBox;
                }
            }

            // Log all Touch* drawables
            string touchParts = "";
            foreach (var d in cubismModel.Drawables)
            {
                if (d.name.Contains("Touch", StringComparison.OrdinalIgnoreCase))
                {
                    touchParts += d.name + ", ";
                }
            }
            Debug.Log($"[AzurLaneViewer] TouchParts: {touchParts}");

            // Load animation override JSON if it exists
            LoadAnimationOverrides();

            // Setup animation system
            SetupAnimation();

            // Setup mouse look-at tracking
            SetupLookAt();

            // Mouth form override (sets smile at end of touch animations)
            mouthFormOverride = cubismModel.gameObject.AddComponent<CubismMouthFormOverride>();
            var updateCtrl = cubismModel.GetComponent<CubismUpdateController>();
            if (updateCtrl != null)
                updateCtrl.Refresh();

            // Debug: track mouth parameter values
            mouthDebug = cubismModel.gameObject.AddComponent<CubismMouthDebug>();

            // Add box collider for easier dragging
            AddBoxCollider();

            // Floating name label
            EnsureNameText();

            if (Mathf.Approximately(AlCharacterData.Brightness, 1f) == false)
                ApplyBrightness(AlCharacterData.Brightness);

            // Restore persisted parameter overrides
            foreach (var po in AlCharacterData.ParameterOverrides)
                parameterOverrides[po.Id] = po.Value;

            // Restore persisted physics scale overrides
            foreach (var pso in AlCharacterData.PhysicsScaleOverrides)
                SetPhysicsSubRigScale(pso.SubRigName, pso.Ratio);

            // Restore persisted part opacity overrides
            foreach (var po in AlCharacterData.PartOpacityOverrides)
                SetPartOpacity(po.PartId, po.Opacity);

            // Restore persisted drawable opacity overrides
            foreach (var doo in AlCharacterData.DrawableOpacityOverrides)
                SetDrawableOpacity(doo.DrawableId, doo.Opacity);
        }

        static readonly int BrightnessPropertyId = Shader.PropertyToID("_Brightness");

        public override void ApplyBrightness(float brightness)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in r.materials)
                {
                    if (mat.HasFloat(BrightnessPropertyId))
                        mat.SetFloat(BrightnessPropertyId, brightness);
                }
            }
        }

        void LoadAnimationOverrides()
        {
            if (string.IsNullOrEmpty(AlCharacterData.Model3JsonPath)) return;

            string modelDir = Path.GetDirectoryName(AlCharacterData.Model3JsonPath);
            string overridePath = Path.Combine(modelDir, "animationoverride.json");

            if (!File.Exists(overridePath))
            {
                Debug.Log("[AzurLaneViewer] No animation override JSON found, using defaults");
                return;
            }

            try
            {
                string json = File.ReadAllText(overridePath);
                Debug.Log($"[AzurLaneViewer] JSON content: {json}");
                animationOverrideJson = JsonUtility.FromJson<AnimationOverrideJson>(json);
                
                if (animationOverrideJson == null || animationOverrideJson.motionOverrides == null)
                {
                    Debug.LogWarning("[AzurLaneViewer] motionOverrides is null or empty");
                    return;
                }
                
                // Log all available motion names for debugging
                var allMotionNames = allMotions.Select(c => c.name).ToList();
                Debug.Log($"[AzurLaneViewer] Available motions: {string.Join(", ", allMotionNames)}");

                // Build motion overrides map
                if (animationOverrideJson.motionOverrides != null)
                {
                    foreach (var entry in animationOverrideJson.motionOverrides)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                        
                        string key = entry.key.ToLower();
                        string[] motionNames = entry.motions ?? Array.Empty<string>();
                        var clips = new List<AnimationClip>();

                        foreach (string motionName in motionNames)
                        {
                            // Find matching motion in allMotions (names include .motion3 extension)
                            string searchName = motionName.EndsWith(".motion3", StringComparison.OrdinalIgnoreCase) 
                                ? motionName 
                                : motionName + ".motion3";
                            
                            foreach (var clip in allMotions)
                            {
                                if (clip.name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
                                {
                                    clips.Add(clip);
                                    break;
                                }
                            }
                        }

                        if (clips.Count > 0)
                        {
                            motionOverridesMap[key] = clips;
                            Debug.Log($"[AzurLaneViewer] Override '{key}' loaded {clips.Count} clips: {string.Join(", ", clips.Select(c => c.name))}");
                        }
                        else
                        {
                            Debug.LogWarning($"[AzurLaneViewer] Override '{key}' found NO matching clips for: {string.Join(", ", motionNames)}");
                        }
                    }
                }

                // Process idle override
                if (!string.IsNullOrEmpty(animationOverrideJson.idleOverride))
                {
                    string idleName = animationOverrideJson.idleOverride;
                    string searchName = idleName.EndsWith(".motion3", StringComparison.OrdinalIgnoreCase)
                        ? idleName
                        : idleName + ".motion3";

                    foreach (var clip in allMotions)
                    {
                        if (clip.name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
                        {
                            idleOverrideClip = clip;
                            Debug.Log($"[AzurLaneViewer] Idle override set to: {clip.name}");
                            break;
                        }
                    }

                    if (idleOverrideClip == null)
                        Debug.LogWarning($"[AzurLaneViewer] Idle override '{idleName}' not found in available motions");
                }

                // Process layer0 keys
                if (animationOverrideJson.layer0Keys != null)
                {
                    foreach (string k in animationOverrideJson.layer0Keys)
                        layer0Keys.Add(k);
                    Debug.Log($"[AzurLaneViewer] Layer 0 keys: {string.Join(", ", layer0Keys)}");
                }

                // Process layer1 keys (non-additive override on layer 2)
                if (animationOverrideJson.layer1Keys != null)
                {
                    foreach (string k in animationOverrideJson.layer1Keys)
                        layer1Keys.Add(k);
                    Debug.Log($"[AzurLaneViewer] Layer 1 (non-additive) keys: {string.Join(", ", layer1Keys)}");
                }

                Debug.Log($"[AzurLaneViewer] Loaded animation overrides for: {string.Join(", ", motionOverridesMap.Keys)}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AzurLaneViewer] Failed to load animation override JSON: {ex.Message}");
            }
        }
        
        void SetupAnimation()
        {
            // Order matters: add FadeController, set fade list, THEN add MotionController
            
            // 1. Add CubismFadeController first
            var fadeController = cubismModel.GetComponent<Live2D.Cubism.Framework.MotionFade.CubismFadeController>();
            if (fadeController == null)
            {
                fadeController = cubismModel.gameObject.AddComponent<Live2D.Cubism.Framework.MotionFade.CubismFadeController>();
            }
            
            // 2. Create and assign fade motion list BEFORE adding motion controller
            var fadeMotionList = ScriptableObject.CreateInstance<Live2D.Cubism.Framework.MotionFade.CubismFadeMotionList>();
            var fadeMotionObjects = new Live2D.Cubism.Framework.MotionFade.CubismFadeMotionData[allMotions.Count];
            var instanceIds = new int[allMotions.Count];
            
            for (int i = 0; i < allMotions.Count; i++)
            {
                var clip = allMotions[i];
                if (motionJsonStrings.TryGetValue(clip.name, out string jsonString))
                {
                    var motionJson = Live2D.Cubism.Framework.Json.CubismMotion3Json.LoadFrom(jsonString, false);
                    if (motionJson != null)
                    {
                        var fadeData = Live2D.Cubism.Framework.MotionFade.CubismFadeMotionData.CreateInstance(
                            motionJson, clip.name, clip.length, true);
                        fadeMotionObjects[i] = fadeData;
                        instanceIds[i] = clip.GetInstanceID();
                    }
                }
            }
            
            fadeMotionList.CubismFadeMotionObjects = fadeMotionObjects;
            fadeMotionList.MotionInstanceIds = instanceIds;
            
            fadeController.CubismFadeMotionList = fadeMotionList;
            
            // 3. NOW add CubismMotionController - its OnEnable will find the fade list
            var motionController = cubismModel.GetComponent<Live2D.Cubism.Framework.Motion.CubismMotionController>();
            if (motionController == null)
            {
                motionController = cubismModel.gameObject.AddComponent<Live2D.Cubism.Framework.Motion.CubismMotionController>();
            }
            
            // Layer 0: idle, Layer 1: additive touch, Layer 2: non-additive touch override
            motionController.RecreateLayers(3);

            // Set layer 1 to additive blend
            motionController.SetLayerAdditive(1, true);
            motionController.SetLayerWeight(1, 1.0f);

            // Layer 2: non-additive override (normal blend)
            motionController.SetLayerAdditive(2, false);
            motionController.SetLayerWeight(2, 1.0f);
            
            // Subscribe to animation end to return to idle
            motionController.AnimationEndHandler += OnMotionAnimationEnd;
            Debug.Log("[AzurLaneViewer] Subscribed to AnimationEndHandler");
            
            // Play idle if available (override takes priority)
            var idleClip = idleOverrideClip ?? (idleMotions.Count > 0 ? idleMotions[0] : null);
            if (idleClip != null)
            {
                motionController.PlayAnimation(idleClip, isLoop: true,
                    priority: Live2D.Cubism.Framework.Motion.CubismMotionPriority.PriorityIdle);
            }
        }
        
        /// <summary>Standard Live2D parameter IDs used for look-at tracking.</summary>
        static readonly (string id, CubismLookAxis axis, float factor)[] LookAtParams = new[]
        {
            ("ParamAngleX",     CubismLookAxis.X, 30f),
            ("ParamAngleY",     CubismLookAxis.Y, 30f),
            ("ParamBodyAngleX", CubismLookAxis.X, 10f),
            ("ParamEyeBallX",   CubismLookAxis.X, 1f),
            ("ParamEyeBallY",   CubismLookAxis.Y, 1f),
        };

        void SetupLookAt()
        {
            // Log all parameter IDs on the model for debugging
            var allIds = new List<string>();
            foreach (var p in cubismModel.Parameters) allIds.Add(p.Id);
            Debug.Log($"[AzurLaneViewer] All model parameters: {string.Join(", ", allIds)}");

            // Add CubismLookParameter to each matching parameter on the model
            bool anyFound = false;
            foreach (var param in cubismModel.Parameters)
            {
                foreach (var (id, axis, factor) in LookAtParams)
                {
                    if (param.Id == id)
                    {
                        var lookParam = param.gameObject.AddComponent<CubismLookParameter>();
                        lookParam.Axis = axis;
                        lookParam.Factor = factor;
                        anyFound = true;
                        break;
                    }
                }
            }

            if (!anyFound)
            {
                Debug.Log("[AzurLaneViewer] No look-at parameters found on model, skipping look-at setup");
                return;
            }

            // Create the mouse look target
            var targetGo = new GameObject("MouseLookTarget");
            targetGo.transform.SetParent(cubismModel.transform, false);
            var lookTarget = targetGo.AddComponent<CubismMouseLookTarget>();

            // Add and configure the look controller
            var lookController = cubismModel.gameObject.AddComponent<CubismLookController>();
            lookController.BlendMode = CubismParameterBlendMode.Additive;
            lookController.Target = lookTarget;
            lookController.Damping = 0.15f;

            lookController.Refresh();

            // Re-refresh the update controller so it picks up the new CubismLookController
            var updateController = cubismModel.GetComponent<CubismUpdateController>();
            if (updateController != null)
                updateController.Refresh();

            Debug.Log("[AzurLaneViewer] Look-at mouse tracking enabled");
        }

        void OnMotionAnimationEnd(int instanceId)
        {
            // Check if this was a touch motion
            bool wasTouchMotion = false;
            foreach (var clip in touchMotions)
            {
                if (clip.GetInstanceID() == instanceId)
                {
                    wasTouchMotion = true;
                    break;
                }
            }

            // Also check override clips
            if (!wasTouchMotion)
            {
                foreach (var clips in motionOverridesMap.Values)
                {
                    foreach (var clip in clips)
                    {
                        if (clip.GetInstanceID() == instanceId)
                        {
                            wasTouchMotion = true;
                            break;
                        }
                    }
                    if (wasTouchMotion) break;
                }
            }

            if (!wasTouchMotion) return;

            isPlayingTouchMotion = false;
            if (mouthFormOverride != null)
            {
                mouthFormOverride.Paused = false;
                mouthFormOverride.SetMouthForm(1f);
            }
            mouthDebug.PrintValue();

            var mc = cubismModel.GetComponent<Live2D.Cubism.Framework.Motion.CubismMotionController>();

            // Layer 2 (non-additive override): zero weight so frozen last frame doesn't stick
            if (lastTouchLayer == 2 && mc != null)
            {
                mc.SetLayerWeight(2, 0f);
            }

            // If a layer 0 or layer 2 motion ended, resume idle
            if (lastTouchLayer == 0 || lastTouchLayer == 2)
            {
                var idleClip = idleOverrideClip ?? (idleMotions.Count > 0 ? idleMotions[0] : null);
                if (idleClip != null)
                {
                    mc?.PlayAnimation(idleClip, isLoop: true,
                        priority: Live2D.Cubism.Framework.Motion.CubismMotionPriority.PriorityIdle);
                }
            }
        }

        void LateUpdate()
        {


            if (mouthFormOverride != null && !mouthFormOverride.Paused)
            {
                mouthFormOverride.SetMouthForm(1f);
            }

            // Apply debug parameter overrides after animation system has written values
            if (ParameterOverridesEnabled && parameterOverrides.Count > 0 && cubismModel != null)
            {
                foreach (var p in cubismModel.Parameters)
                {
                    if (parameterOverrides.TryGetValue(p.Id, out float val))
                        p.Value = val;
                }
            }

            // Apply part opacity overrides
            if (PartOverridesEnabled && partOpacityOverrides.Count > 0 && cubismModel != null)
            {
                foreach (var part in cubismModel.Parts)
                {
                    if (partOpacityOverrides.TryGetValue(part.Id, out float opacity))
                        part.Opacity = opacity;
                }
            }

            // Apply drawable opacity overrides via CubismRenderer.Color alpha
            if (DrawableOverridesEnabled && drawableOpacityOverrides.Count > 0 && cubismModel != null)
            {
                foreach (var drawable in cubismModel.Drawables)
                {
                    if (drawableOpacityOverrides.TryGetValue(drawable.Id, out float alpha))
                    {
                        var renderer = drawable.GetComponent<Live2D.Cubism.Rendering.CubismRenderer>();
                        if (renderer != null)
                        {
                            var c = renderer.Color;
                            c.a = alpha;
                            renderer.Color = c;
                        }
                    }
                }
            }
        }

        void AddBoxCollider()
        {
            // Add a box collider that covers the model for easier dragging
            var canvasInfo = cubismModel.CanvasInformation;
            float width = canvasInfo?.CanvasWidth ?? 1000f;
            float height = canvasInfo?.CanvasHeight ?? 1000f;
            float pixelsPerUnit = canvasInfo?.PixelsPerUnit ?? 100f;
            
            float modelWidth = width / pixelsPerUnit;
            float modelHeight = height / pixelsPerUnit;
            
            var boxCollider = cubismModel.GetComponent<BoxCollider>();
            if (boxCollider == null)
                boxCollider = cubismModel.gameObject.AddComponent<BoxCollider>();
            
            // Center the collider on the model
            boxCollider.center = Vector3.zero;
            // Make it much larger for easier clicking
            boxCollider.size = new Vector3(modelWidth * 1f, modelHeight * 1f, 1f);
        }

        // ── Sorting ───────────────────────────────────────────────────────────────────

        void ConfigureSorting()
        {
            var renderController = cubismModel.GetComponent<Live2D.Cubism.Rendering.CubismRenderController>();
            if (renderController == null) return;

            int sortOrder = s_SortOrderCounter++;
            renderController.SortingMode = Live2D.Cubism.Rendering.CubismSortingMode.BackToFrontZ;
            renderController.SortingOrder = sortOrder;
            renderController.DepthOffset = 0.005f;

            float zPosition = -sortOrder * 0.1f;
            cubismModel.transform.localPosition = new Vector3(0, 0, zPosition);
        }

        // ── Interaction ────────────────────────────────────────────────────────────────

        void OnPointerClick(InputAction.CallbackContext ctx)
        {
            if (cubismModel == null || !Application.isPlaying) return;

            // Raycast to detect which body part was clicked
            Camera cam = CachedCamera;
            string hitAreaName = "Body";
            
            if (cam != null && raycaster != null)
            {
                Vector2 mousePos = UnityEngine.InputSystem.Pointer.current.position.ReadValue();
                Ray ray = cam.ScreenPointToRay(mousePos);
                
                // First, check Touch* drawables with a manual bounds check
                // (they might be behind visible meshes in the raycast)
                // Get world space mouse position for bounds checking
                float closestDist = float.MaxValue;
                string closestTouchPart = "Body";
                
                foreach (var d in cubismModel.Drawables)
                {
                    if (!d.name.Contains("Touch", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    var meshRenderer = d.GetComponent<UnityEngine.MeshRenderer>();
                    if (meshRenderer == null) continue;
                    
                    var bounds = meshRenderer.bounds;
                    if (bounds.IntersectRay(ray))
                    {
                        // Calculate distance to find closest
                        float dist = Vector3.Distance(ray.origin, bounds.center);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            if (d.name.Contains("TouchHead", StringComparison.OrdinalIgnoreCase))
                                closestTouchPart = "Head";
                            else if (d.name.Contains("TouchSpecial", StringComparison.OrdinalIgnoreCase))
                                closestTouchPart = "Special";
                            else
                                closestTouchPart = "Body";
                        }
                    }
                }
                
                if (closestDist < float.MaxValue)
                {
                    // A Touch* drawable was hit — use it
                    hitAreaName = closestTouchPart;
                }
                else
                {
                    return; // No Touch* drawable hit — don't play animation
                }
            }
            else
            {
                return; // No camera or raycaster — can't determine hit
            }

            Debug.Log($"[AzurLaneViewer] TouchPart: {hitAreaName}");

            // Play touch voice if available
            if (TouchVoices.Count > 0)
            {
                NikkeAudioSource.PlayOneShot(TouchVoices[TouchVoiceIndex % TouchVoices.Count]);
                TouchVoiceIndex++;
            }

            // Play touch motion based on body part
            var motionController = cubismModel.GetComponent<Live2D.Cubism.Framework.Motion.CubismMotionController>();
            if (motionController != null && touchMotions.Count > 0)
            {
                AnimationClip clipToPlay = null;
                
                // First, check if there's an override for this hit area
                if (motionOverridesMap.Count > 0)
                {
                    string overrideKey = hitAreaName.ToLower();
                    if (motionOverridesMap.TryGetValue(overrideKey, out var overrideClips) && overrideClips.Count > 0)
                    {
                        clipToPlay = overrideClips[UnityEngine.Random.Range(0, overrideClips.Count)];
                    }
                }
                
                // Fallback to default behavior if no override found
                if (clipToPlay == null)
                {
                    // Find the matching motion for the hit area
                    foreach (var clip in touchMotions)
                    {
                        string clipName = clip.name.ToLower();
                        if (hitAreaName == "Head" && clipName.Contains("touch_head"))
                            clipToPlay = clip;
                        else if (hitAreaName == "Special" && clipName.Contains("touch_special"))
                            clipToPlay = clip;
                        else if (hitAreaName == "Body" && clipName.Contains("touch_body"))
                            clipToPlay = clip;
                    }
                    
                    // Fallback to current behavior if no specific motion found
                    if (clipToPlay == null)
                        clipToPlay = touchMotions[touchIndex % touchMotions.Count];
                }
                
                if (hitAreaName != "Body")
                    touchIndex++;
                
                isPlayingTouchMotion = true;
                // layer0Keys → layer 0 (replaces idle), layer1Keys → layer 2 (non-additive override), default → layer 1 (additive)
                int layerIndex = layer0Keys.Contains(hitAreaName) ? 0
                    : layer1Keys.Contains(hitAreaName) ? 2
                    : 1;
                lastTouchLayer = layerIndex;
                // Re-enable layer 2 weight if it was zeroed out
                if (layerIndex == 2)
                    motionController.SetLayerWeight(2, 1.0f);
                if (mouthFormOverride != null)
                    mouthFormOverride.Paused = true;
                mouthDebug.PrintValue();
                // Use PriorityForce to allow interrupting current animation
                motionController.PlayAnimation(clipToPlay, layerIndex: layerIndex, isLoop: false,
                    priority: Live2D.Cubism.Framework.Motion.CubismMotionPriority.PriorityForce);
            }
        }

        // ── Name text ──────────────────────────────────────────────────────────────────

        public override void EnsureNameText()
        {
            if (NikkeNameText != null || m_NamePrefab == null) return;

            NikkeNameText = Instantiate(m_NamePrefab, transform);
            NikkeNameText.text = AlCharacterData.DisplayName;
            NikkeNameText.gameObject.SetActive(!AlCharacterData.HideName);
        }

        public override List<PoseDebugInfo> GetPoseDebugInfo()
        {
            if (cubismModel == null) return new();

            return new List<PoseDebugInfo>
            {
                new()
                {
                    PoseType = NikkePoseType.Base,
                    IsActive = true,
                    Animations = GetMotionNames(),
                    CurrentAnimation = "(idle)",
                    SkinNames = new string[0],
                    CurrentSkin = "default",
                }
            };
        }

        public override List<ParameterDebugInfo> GetParameterDebugInfo()
        {
            if (cubismModel == null) return new();

            var list = new List<ParameterDebugInfo>();
            foreach (var p in cubismModel.Parameters)
            {
                list.Add(new ParameterDebugInfo
                {
                    Id = p.Id,
                    Value = p.Value,
                    MinValue = p.MinimumValue,
                    MaxValue = p.MaximumValue,
                    DefaultValue = p.DefaultValue,
                });
            }
            return list;
        }

        public override void SetParameterValue(string parameterId, float value)
        {
            if (cubismModel == null) return;
            parameterOverrides[parameterId] = value;
        }

        public override void ClearParameterOverride(string parameterId)
        {
            parameterOverrides.Remove(parameterId);
        }

        public override List<PhysicsSubRigDebugInfo> GetPhysicsSubRigDebugInfo()
        {
            if (cubismModel == null) return new();

            var physicsController = cubismModel.GetComponent<CubismPhysicsController>();
            if (physicsController == null) return new();

            var rig = physicsController.GetType()
                .GetField("_rig", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(physicsController) as CubismPhysicsRig;
            if (rig?.SubRigs == null) return new();

            var list = new List<PhysicsSubRigDebugInfo>();
            foreach (var subRig in rig.SubRigs)
            {
                physicsSubRigScales.TryGetValue(subRig.Name, out float ratio);
                if (ratio == 0f) ratio = 1f;
                list.Add(new PhysicsSubRigDebugInfo
                {
                    Name = subRig.Name,
                    AngleScaleRatio = ratio,
                });
            }
            return list;
        }

        public override void SetPhysicsSubRigScale(string subRigName, float ratio)
        {
            if (cubismModel == null) return;

            physicsSubRigScales[subRigName] = ratio;

            if (!PhysicsOverridesEnabled) return;

            var physicsController = cubismModel.GetComponent<CubismPhysicsController>();
            if (physicsController == null) return;

            var rig = physicsController.GetType()
                .GetField("_rig", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(physicsController) as CubismPhysicsRig;
            var subRig = rig?.GetSubRig(subRigName);
            if (subRig == null) return;

            physicsController.SetPhysicsSubRigOutputAngleScaleRatio(subRig, ratio);
        }

        public override List<PartOpacityDebugInfo> GetPartOpacityDebugInfo()
        {
            if (cubismModel == null) return new();

            var list = new List<PartOpacityDebugInfo>();
            foreach (var part in cubismModel.Parts)
            {
                float opacity = partOpacityOverrides.TryGetValue(part.Id, out float ov) ? ov : part.Opacity;
                originalPartOpacities.TryGetValue(part.Id, out float defaultOp);
                list.Add(new PartOpacityDebugInfo
                {
                    Id = part.Id,
                    Opacity = opacity,
                    DefaultOpacity = defaultOp,
                });
            }
            return list;
        }

        public override void SetPartOpacity(string partId, float opacity)
        {
            partOpacityOverrides[partId] = opacity;
        }

        public override void ClearPartOpacityOverride(string partId)
        {
            partOpacityOverrides.Remove(partId);

            // Restore original opacity immediately
            if (cubismModel != null && originalPartOpacities.TryGetValue(partId, out float original))
            {
                foreach (var part in cubismModel.Parts)
                {
                    if (part.Id == partId)
                    {
                        part.Opacity = original;
                        break;
                    }
                }
            }
        }

        public override List<DrawableOpacityDebugInfo> GetDrawableOpacityDebugInfo()
        {
            if (cubismModel == null) return new();

            var list = new List<DrawableOpacityDebugInfo>();
            foreach (var drawable in cubismModel.Drawables)
            {
                var renderer = drawable.GetComponent<Live2D.Cubism.Rendering.CubismRenderer>();
                float currentAlpha = renderer != null ? renderer.Color.a : 1f;
                if (drawableOpacityOverrides.TryGetValue(drawable.Id, out float ov))
                    currentAlpha = ov;
                originalDrawableOpacities.TryGetValue(drawable.Id, out float defaultAlpha);
                list.Add(new DrawableOpacityDebugInfo
                {
                    Id = drawable.Id,
                    Opacity = currentAlpha,
                    DefaultOpacity = defaultAlpha,
                });
            }
            return list;
        }

        public override void SetDrawableOpacity(string drawableId, float opacity)
        {
            drawableOpacityOverrides[drawableId] = opacity;
        }

        public override void ClearDrawableOpacityOverride(string drawableId)
        {
            drawableOpacityOverrides.Remove(drawableId);

            // Restore original alpha immediately
            if (cubismModel != null && originalDrawableOpacities.TryGetValue(drawableId, out float original))
            {
                foreach (var drawable in cubismModel.Drawables)
                {
                    if (drawable.Id == drawableId)
                    {
                        var renderer = drawable.GetComponent<Live2D.Cubism.Rendering.CubismRenderer>();
                        if (renderer != null)
                        {
                            var c = renderer.Color;
                            c.a = original;
                            renderer.Color = c;
                        }
                        break;
                    }
                }
            }
        }

        public override List<string> GetDrawablesAtScreenPosition(Vector2 screenPos)
        {
            var hits = new List<string>();
            if (cubismModel == null || CachedCamera == null) return hits;

            Ray ray = CachedCamera.ScreenPointToRay(screenPos);

            foreach (var drawable in cubismModel.Drawables)
            {
                var meshRenderer = drawable.GetComponent<MeshRenderer>();
                if (meshRenderer == null || !meshRenderer.enabled) continue;

                if (meshRenderer.bounds.IntersectRay(ray))
                    hits.Add(drawable.Id);
            }
            return hits;
        }

        string[] GetMotionNames()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var c in allMotions) names.Add(c.name);
            return names.ToArray();
        }

        public override void PlayAnimationByName(string animationName)
        {
            if (cubismModel == null || allMotions.Count == 0) return;

            // Find the matching animation clip
            AnimationClip clipToPlay = null;
            foreach (var clip in allMotions)
            {
                if (clip.name.Equals(animationName, StringComparison.OrdinalIgnoreCase))
                {
                    clipToPlay = clip;
                    break;
                }
            }

            if (clipToPlay == null)
            {
                Debug.LogWarning($"[AzurLaneViewer] Animation not found: {animationName}");
                return;
            }

            var motionController = cubismModel.GetComponent<Live2D.Cubism.Framework.Motion.CubismMotionController>();
            if (motionController != null)
            {
                isPlayingTouchMotion = true;
                if (mouthFormOverride != null)
                    mouthFormOverride.Paused = true;
                mouthDebug.PrintValue();
                motionController.PlayAnimation(clipToPlay, layerIndex: 1, isLoop: false, 
                    priority: Live2D.Cubism.Framework.Motion.CubismMotionPriority.PriorityForce);
            }
        }
    }
}
