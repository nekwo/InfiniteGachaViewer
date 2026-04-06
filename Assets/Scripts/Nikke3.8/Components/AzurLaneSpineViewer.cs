using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NikkeViewerEX.Core;
using NikkeViewerEX.Serialization;
using NikkeViewerEX.Utils;
using Spine.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NikkeViewerEX.Components
{
    [AddComponentMenu("Nikke Viewer EX/Components/Azur Lane Spine Viewer")]
    public class AzurLaneSpineViewer : NikkeViewerBase
    {
        [Header("Spine Settings")]
        [SerializeField]
        string m_DefaultAnimation = "normal";

        [SerializeField]
        string m_ShaderName = "Universal Render Pipeline/Spine 3.8/Skeleton";

        [Header("UI")]
        [SerializeField]
        TextMeshPro m_NamePrefab;

        /// <summary>AL-specific character data. Set by NikkeBrowserPanel before TriggerSpawn.</summary>
        public override AzurLaneCharacter AlCharacterData { get; set; } = new();

        SkeletonAnimation skeletonAnimation;
        bool spawned;
        string resolvedIdleAnimation;
        static int s_SortOrderCounter = 0;
        readonly System.Random rng = new();

        public override void OnEnable()
        {
            base.OnEnable();
            MainControl.OnSettingsApplied += SpawnSkeleton;
            SettingsManager.OnSettingsLoaded += SpawnSkeleton;
            InputManager.PointerClick.performed += Interact;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            MainControl.OnSettingsApplied -= SpawnSkeleton;
            SettingsManager.OnSettingsLoaded -= SpawnSkeleton;
            InputManager.PointerClick.performed -= Interact;
        }

        public override void Update()
        {
            base.Update();
        }

        public override void TriggerSpawn() => SpawnSkeleton();

        void SpawnSkeleton() => SpawnSkeletonAsync().Forget();

        async UniTaskVoid SpawnSkeletonAsync()
        {
            try
            {
                if (spawned) return;

                // Sync NikkeData from AlCharacterData for base class compatibility
                NikkeData.InstanceId = AlCharacterData.InstanceId;
                NikkeData.NikkeName = AlCharacterData.DisplayName;
                NikkeData.AssetName = AlCharacterData.AssetName;
                NikkeData.Lock = AlCharacterData.Lock;
                NikkeData.HideName = AlCharacterData.HideName;
                NikkeData.Scale = AlCharacterData.Scale;
                NikkeData.Position = AlCharacterData.Position;

                string skelPath = AlCharacterData.SkelPath;
                string atlasPath = AlCharacterData.AtlasPath;
                var texturesPath = AlCharacterData.TexturesPath;

                if (string.IsNullOrEmpty(skelPath) || string.IsNullOrEmpty(atlasPath)
                    || texturesPath == null || texturesPath.Count == 0)
                {
                    Debug.LogError($"[AzurLaneSpineViewer] Missing spine assets for {AlCharacterData.AssetName}");
                    return;
                }

                spawned = true;

                Shader shader = Shader.Find(m_ShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[AzurLaneSpineViewer] Shader not found: {m_ShaderName}");
                    return;
                }

                GameObject skelGO = new GameObject("Skeleton");
                skelGO.transform.SetParent(transform, false);

                skeletonAnimation = await SpineHelper38.InstantiateSpine(
                    skelPath,
                    atlasPath,
                    texturesPath,
                    skelGO,
                    shader,
                    spineScale: 0.25f,
                    loop: true,
                    defaultAnimation: m_DefaultAnimation
                );

                if (skeletonAnimation == null)
                {
                    Debug.LogError($"[AzurLaneSpineViewer] Failed to instantiate spine for {AlCharacterData.AssetName}");
                    UnityEngine.Object.Destroy(skelGO);
                    spawned = false;
                    return;
                }

                // Set sort order
                var meshRenderer = skelGO.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                    meshRenderer.sortingOrder = s_SortOrderCounter++;

                await UniTask.Yield();

                // Add collider
                AddMeshCollider(skelGO);

                // Resolve the actual idle animation name
                var currentTrack = skeletonAnimation.AnimationState.GetCurrent(0);
                resolvedIdleAnimation = currentTrack?.Animation?.Name ?? m_DefaultAnimation;

                // Extract skins and touch animations
                var skelData = skeletonAnimation.Skeleton.Data;
                Skins = skelData.Skins?.Select(s => s.Name).ToArray();
                TouchAnimations = skelData.Animations.Items
                    .Take(skelData.Animations.Count)
                    .Where(a => a.Name.StartsWith("drag") || a.Name.StartsWith("touch")
                        || a.Name == "ex" || a.Name.StartsWith("action"))
                    .OrderBy(a => a.Name)
                    .Select(a => a.Name)
                    .ToList();

                // Apply saved position/scale
                if (AlCharacterData.Position != Vector2.zero)
                    transform.position = new Vector3(AlCharacterData.Position.x, AlCharacterData.Position.y, transform.position.z);
                if (AlCharacterData.Scale != Vector3.one)
                    transform.localScale = AlCharacterData.Scale;

                // Apply brightness
                if (Mathf.Approximately(AlCharacterData.Brightness, 1f) == false)
                    ApplyBrightness(AlCharacterData.Brightness);

                // Name text
                if (!AlCharacterData.HideName)
                    EnsureNameText();

                Debug.Log($"[AzurLaneSpineViewer] Spawned {AlCharacterData.AssetName} with {TouchAnimations.Count} touch anims, {Skins?.Length ?? 0} skins");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AzurLaneSpineViewer] Error spawning: {ex}");
                spawned = false;
            }
        }

        static readonly int BrightnessPropertyId = Shader.PropertyToID("_Brightness");

        public override void ApplyBrightness(float brightness)
        {
            if (skeletonAnimation == null) return;

            var renderer = skeletonAnimation.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            foreach (var mat in renderer.materials)
            {
                mat.SetFloat(BrightnessPropertyId, brightness);
            }

            skeletonAnimation.Skeleton.R = brightness;
            skeletonAnimation.Skeleton.G = brightness;
            skeletonAnimation.Skeleton.B = brightness;
        }

        public override void EnsureNameText()
        {
            if (NikkeNameText != null || m_NamePrefab == null) return;
            NikkeNameText = Instantiate(m_NamePrefab, transform);
            NikkeNameText.text = AlCharacterData.DisplayName;
            NikkeNameText.gameObject.SetActive(!AlCharacterData.HideName);
        }

        void Interact(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed || skeletonAnimation == null) return;
            if (InputManager.IsPointerOverUI()) return;

            Ray ray = CachedCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit)) return;

            var viewer = hit.collider.GetComponentInParent<AzurLaneSpineViewer>();
            if (viewer != this) return;

            if (TouchAnimations.Count == 0) return;

            string animName = TouchAnimations[rng.Next(TouchAnimations.Count)];

            if (TouchVoices.Count > 0)
            {
                NikkeAudioSource.Stop();
                NikkeAudioSource.clip = TouchVoices[TouchVoiceIndex % TouchVoices.Count];
                NikkeAudioSource.Play();
                TouchVoiceIndex++;
            }

            skeletonAnimation.AnimationState.SetAnimation(0, animName, false);
            skeletonAnimation.AnimationState.AddAnimation(0, resolvedIdleAnimation, true, 0);
        }

        public override List<PoseDebugInfo> GetPoseDebugInfo()
        {
            if (skeletonAnimation == null) return new();

            var skelData = skeletonAnimation.Skeleton.Data;
            var anims = skelData.Animations.Items
                .Take(skelData.Animations.Count)
                .Select(a => a.Name)
                .ToArray();

            return new List<PoseDebugInfo>
            {
                new()
                {
                    PoseType = NikkePoseType.Base,
                    IsActive = true,
                    Animations = anims,
                    CurrentAnimation = skeletonAnimation.AnimationState.GetCurrent(0)?.Animation?.Name ?? "(none)",
                    SkinNames = Skins ?? Array.Empty<string>(),
                    CurrentSkin = skeletonAnimation.Skeleton.Skin?.Name ?? "default",
                }
            };
        }
    }
}
