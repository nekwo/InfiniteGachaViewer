using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Motion;
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
    public class AzurLaneViewer : NikkeViewerBase
    {
        [Serializable]
        public class AnimationOverrideJson
        {
            public MotionOverrideEntry[] motionOverrides;
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

        public override void Update()
        {
            // Mirror transform state back to AlCharacterData so SaveSettings persists correctly.
            AlCharacterData.Position = NikkeData.Position;
            AlCharacterData.Scale = NikkeData.Scale;
            AlCharacterData.Lock = NikkeData.Lock;
            AlCharacterData.HideName = NikkeData.HideName;
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
            
            // Add box collider for easier dragging
            AddBoxCollider();

            // Floating name label
            EnsureNameText();
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
            
            // Recreate with 2 layers so idle (0) and touch (1) can play simultaneously
            motionController.RecreateLayers(2);
            
            // Set layer 1 to additive blend
            motionController.SetLayerAdditive(1, true);
            motionController.SetLayerWeight(1, 1.0f);
            
            // Subscribe to animation end to return to idle
            motionController.AnimationEndHandler += OnMotionAnimationEnd;
            Debug.Log("[AzurLaneViewer] Subscribed to AnimationEndHandler");
            
            // Play idle if available
            if (idleMotions.Count > 0)
            {
                motionController.PlayAnimation(idleMotions[0], isLoop: true, 
                    priority: Live2D.Cubism.Framework.Motion.CubismMotionPriority.PriorityIdle);
            }
        }
        
        void OnMotionAnimationEnd(int instanceId)
        {
            // Check if this was a touch motion (layer 1)
            bool wasTouchMotion = false;
            foreach (var clip in touchMotions)
            {
                if (clip.GetInstanceID() == instanceId)
                {
                    wasTouchMotion = true;
                    break;
                }
            }
            
            if (wasTouchMotion)
            {
                isPlayingTouchMotion = false;
            }
            // Idle continues on layer 0, no action needed
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
            boxCollider.center = new Vector3(0, modelHeight / 2f, 0);
            // Make it much larger for easier clicking
            boxCollider.size = new Vector3(modelWidth * 3f, modelHeight * 3f, 2f);
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
            if (cubismModel == null || NikkeData.Lock || !Application.isPlaying) return;

            // Raycast to detect which body part was clicked
            Camera cam = Camera.main;
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
                
                hitAreaName = closestTouchPart;
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
                // Use PriorityForce to allow interrupting current animation
                motionController.PlayAnimation(clipToPlay, layerIndex: 1, isLoop: false, 
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

        string[] GetMotionNames()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var c in allMotions) names.Add(c.name);
            return names.ToArray();
        }
    }
}
