using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using NikkeViewerEX.Core;
using NikkeViewerEX.Serialization;
using NikkeViewerEX.UI;
using NikkeViewerEX.Utils;
using Spine.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NikkeViewerEX.Components
{
    [AddComponentMenu("Nikke Viewer EX/Components/Nikke Viewer 4.1")]
    public class NikkeViewer : NikkeViewerBase
    {
        [Header("Spine Settings")]
        [SerializeField]
        string m_DefaultAnimation = "idle";

        [SerializeField]
        string m_CoverDefaultAnimation = "cover_idle";

        [SerializeField]
        string m_AimDefaultAnimation = "aim_idle";

        [SerializeField]
        string m_TouchAnimation = "action";

        [SerializeField]
        string m_TransitionToCover = "to_cover";

        [SerializeField]
        string m_TransitionToAim = "to_aim";

        [SerializeField]
        string m_AimXAnimation = "aim_x";

        [SerializeField]
        string m_AimYAnimation = "aim_y";

        [SerializeField]
        [Range(0.1f, 3f)]
        float m_TransitionSpeed = 2f;

        [SerializeField]
        string m_ShaderName = "Universal Render Pipeline/Spine 4.1/Skeleton";

        [Header("UI")]
        [SerializeField]
        TextMeshPro m_NikkeNamePrefab;

        readonly Dictionary<NikkePoseType, (GameObject go, SkeletonAnimation anim)> poseInstances = new();
        readonly Dictionary<NikkePoseType, Mesh> idleColliderMeshes = new();
        CancellationTokenSource transitionCts;
        bool aimBlendActive;
        Spine.TrackEntry aimXTrack;
        Spine.TrackEntry aimYTrack;
        JigglePhysics jigglePhysics;
        int currentTriggerId;
        RectTransform aimReticle;

        string GetDefaultAnimation(NikkePoseType poseType) => poseType switch
        {
            NikkePoseType.Cover => m_CoverDefaultAnimation,
            NikkePoseType.Aim => m_AimDefaultAnimation,
            _ => m_DefaultAnimation
        };

        SkeletonAnimation ActiveSkeleton =>
            poseInstances.TryGetValue(NikkeData.ActivePose, out var inst) ? inst.anim : null;

        public override void OnEnable()
        {
            base.OnEnable();
            MainControl.OnSettingsApplied += SpawnNikke;
            MainControl.HideUIToggle.onValueChanged.AddListener(ToggleDisplayName);
            SettingsManager.OnSettingsLoaded += SpawnNikke;
            InputManager.PointerClick.performed += Interact;
            InputManager.MiddleClick.performed += ToggleCoverPose;
            InputManager.RightClick.started += AimStart;
            InputManager.RightClick.canceled += AimEnd;
            OnSkinChanged += ChangeSkin;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            MainControl.OnSettingsApplied -= SpawnNikke;
            MainControl.HideUIToggle.onValueChanged.RemoveListener(ToggleDisplayName);
            SettingsManager.OnSettingsLoaded -= SpawnNikke;
            InputManager.PointerClick.performed -= Interact;
            InputManager.MiddleClick.performed -= ToggleCoverPose;
            InputManager.RightClick.started -= AimStart;
            InputManager.RightClick.canceled -= AimEnd;
            OnSkinChanged -= ChangeSkin;
        }

        public override void Update()
        {
            base.Update();
            if (NikkeNameText != null)
                UpdateDisplayName(NikkeNameText);
            UpdateAimBlend();
        }

        void ChangeSkin(int index)
        {
            var skel = ActiveSkeleton;
            if (skel == null) return;
            skel.Skeleton.SetSkin(Skins[index]);
            skel.Skeleton.SetSlotsToSetupPose();
            skel.Update(0);
            NikkeData.Skin = Skins[index];
        }

        public override void TriggerSpawn() => SpawnNikke();

        void SpawnNikke() => SpawnNikkeAsync().Forget();

        public override void EnsureNameText()
        {
            if (NikkeNameText == null)
                NikkeNameText = CreateDisplayName(NikkeData.NikkeName);
        }

        private async UniTaskVoid SpawnNikkeAsync()
        {
            try
            {
                if (poseInstances.Count > 0) return;

                // Migrate legacy saves that have no Poses list
                if (NikkeData.Poses.Count == 0 && !string.IsNullOrEmpty(NikkeData.SkelPath))
                {
                    NikkeData.Poses.Add(new NikkePose
                    {
                        PoseType = NikkePoseType.Base,
                        SkelPath = NikkeData.SkelPath,
                        AtlasPath = NikkeData.AtlasPath,
                        TexturesPath = new List<string>(NikkeData.TexturesPath)
                    });
                }

                // Spawn all poses (all stay active so Spine animations keep running)
                foreach (var pose in NikkeData.Poses)
                {
                    if (string.IsNullOrEmpty(pose.SkelPath)) continue;

                    GameObject poseGO = new GameObject($"Pose_{pose.PoseType}");
                    poseGO.transform.SetParent(transform, false);

                    SkeletonAnimation skelAnim = await SpineHelper.InstantiateSpine(
                        pose.SkelPath,
                        pose.AtlasPath,
                        pose.TexturesPath,
                        poseGO,
                        Shader.Find(m_ShaderName),
                        spineScale: 0.25f,
                        loop: true,
                        defaultAnimation: GetDefaultAnimation(pose.PoseType)
                    );

                    if (skelAnim == null)
                    {
                        UnityEngine.Object.Destroy(poseGO);
                        continue;
                    }

                    poseInstances[pose.PoseType] = (poseGO, skelAnim);
                }

                // Wait a frame so Spine generates meshes for all poses
                await UniTask.Yield();

                // Add mesh colliders to all poses, then hide non-active ones
                // Toggle renderer/collider instead of SetActive to keep animations running
                // Add colliders and snapshot idle meshes before hiding any poses
                foreach (var (type, (go, anim)) in poseInstances)
                    AddMeshCollider(go);

                // All poses are visible here — snapshot their idle meshes
                foreach (var (type, (go, anim)) in poseInstances)
                {
                    if (go.TryGetComponent(out MeshFilter mf))
                        idleColliderMeshes[type] = UnityEngine.Object.Instantiate(mf.sharedMesh);
                }

                // Now hide non-active poses
                foreach (var (type, (go, anim)) in poseInstances)
                {
                    if (type != NikkeData.ActivePose)
                        SetPoseVisible(go, false);
                }

                // Setup from active pose
                var active = ActiveSkeleton;
                if (active != null)
                {
                    if (NikkeData.ActivePose == NikkePoseType.Aim)
                        SetupAimBlend(active);

                    Skins = active.Skeleton.Data.Skins?.Select(skin => skin.Name).ToArray();
                    TouchAnimations = active.Skeleton.Data.Animations.Items
                        .Take(active.Skeleton.Data.Animations.Count)
                        .Where(a => a.Name.StartsWith(m_TouchAnimation))
                        .OrderBy(a => a.Name)
                        .Select(a => a.Name)
                        .ToList();

                    if (!NikkeData.HideName)
                    {
                        NikkeNameText = CreateDisplayName(NikkeData.NikkeName);
                        ToggleDisplayName(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error creating Nikke Viewer! {ex}");
            }
        }

        public override void SetActivePose(NikkePoseType poseType)
        {
            if (!poseInstances.ContainsKey(poseType)) return;
            if (poseType == NikkeData.ActivePose) return;

            // Cancel any in-progress transition and snap
            transitionCts?.Cancel();
            transitionCts = new CancellationTokenSource();
            TransitionToPose(poseType, transitionCts.Token).Forget();
        }

        async UniTaskVoid TransitionToPose(NikkePoseType poseType, CancellationToken ct)
        {
            var previousPose = NikkeData.ActivePose;
            var current = poseInstances[previousPose];

            // Update active pose immediately so subsequent calls see the correct state
            NikkeData.ActivePose = poseType;

            // Pick transition animation based on target pose
            string transitionAnim = poseType switch
            {
                NikkePoseType.Cover => m_TransitionToCover,
                NikkePoseType.Aim => m_TransitionToAim,
                _ => null
            };

            // Pin collider to idle snapshot so the transition animation can't shrink it
            if (current.go.TryGetComponent(out MeshCollider mc)
                && idleColliderMeshes.TryGetValue(previousPose, out var idleMesh))
                mc.sharedMesh = idleMesh;

            // Play transition on the outgoing skeleton if the animation exists
            if (transitionAnim != null
                && current.anim.Skeleton.Data.FindAnimation(transitionAnim) != null)
            {
                var entry = current.anim.AnimationState.SetAnimation(0, transitionAnim, false);
                entry.TimeScale = m_TransitionSpeed;

                float duration = current.anim.Skeleton.Data.FindAnimation(transitionAnim).Duration / m_TransitionSpeed;
                try
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(duration),
                        cancellationToken: ct
                    );
                }
                catch (OperationCanceledException)
                {
                    // Interrupted — snap immediately to target pose below
                }
            }

            // Clear aim blend if leaving Aim pose
            if (previousPose == NikkePoseType.Aim)
                ClearAimBlend(current.anim);

            // Swap visibility
            SetPoseVisible(current.go, false);
            // Restore outgoing skeleton to its default idle
            current.anim.AnimationState.SetAnimation(
                0, GetDefaultAnimation(previousPose), true
            );
            var target = poseInstances[poseType];
            SetPoseVisible(target.go, true);

            // Pin incoming collider to idle snapshot — the live mesh may still
            // reflect the last transition animation frame and MeshCollider doesn't
            // auto-rebake when Spine mutates the underlying Mesh data.
            if (target.go.TryGetComponent(out MeshCollider targetMc)
                && idleColliderMeshes.TryGetValue(poseType, out var targetIdleMesh))
                targetMc.sharedMesh = targetIdleMesh;

            // Set up aim blend if entering Aim pose
            if (poseType == NikkePoseType.Aim)
                SetupAimBlend(target.anim);

            // Update metadata from new active skeleton
            Skins = target.anim.Skeleton.Data.Skins?.Select(s => s.Name).ToArray();
            TouchAnimations = target.anim.Skeleton.Data.Animations.Items
                .Take(target.anim.Skeleton.Data.Animations.Count)
                .Where(a => a.Name.StartsWith(m_TouchAnimation))
                .OrderBy(a => a.Name)
                .Select(a => a.Name)
                .ToList();

            SettingsManager.SaveSettings().Forget();
        }

        void SetupAimBlend(SkeletonAnimation skel)
        {
            var data = skel.Skeleton.Data;
            var aimX = data.FindAnimation(m_AimXAnimation);
            var aimY = data.FindAnimation(m_AimYAnimation);
            if (aimX == null && aimY == null) return;

            aimBlendActive = true;

            var reticleGO = new GameObject("AimReticle", typeof(RectTransform));
            aimReticle = reticleGO.GetComponent<RectTransform>();
            aimReticle.SetParent(SettingsManager.BackgroundImage.transform.parent, false);
            aimReticle.SetAsLastSibling();
            reticleGO.AddComponent<AimReticle>();

            if (aimX != null)
            {
                aimXTrack = skel.AnimationState.SetAnimation(1, aimX, false);
                aimXTrack.Alpha = 0.05f;
                aimXTrack.TimeScale = 0;
            }
            if (aimY != null)
            {
                aimYTrack = skel.AnimationState.SetAnimation(2, aimY, false);
                aimYTrack.Alpha = 0.05f;
                aimYTrack.TimeScale = 0;
            }
            
            if (aimX != null)
            {
                aimXTrack = skel.AnimationState.SetAnimation(3, aimX, false);
                aimXTrack.MixBlend = Spine.MixBlend.Add;
                aimXTrack.Alpha = 1f;
                aimXTrack.TimeScale = 0;
            }
            if (aimY != null)
            {
                aimYTrack = skel.AnimationState.SetAnimation(4, aimY, false);
                aimYTrack.MixBlend = Spine.MixBlend.Add;
                aimYTrack.Alpha = 1f;
                aimYTrack.TimeScale = 0;
            }

            // Jiggle physics
            var jiggleFile = JiggleSettingsManager.Load();
            if (jiggleFile.GlobalEnabled)
                SetupJiggle(skel);
        }

        void ClearAimBlend(SkeletonAnimation skel)
        {
            if (!aimBlendActive) return;
            aimBlendActive = false;
            for (int i = 1; i <= 4; i++)
                skel.AnimationState.SetEmptyAnimation(i, 0);
            aimXTrack = null;
            aimYTrack = null;

            if (jigglePhysics != null)
            {
                jigglePhysics.Clear();
                jigglePhysics = null;
            }

            if (aimReticle != null)
            {
                Destroy(aimReticle.gameObject);
                aimReticle = null;
            }
        }

        void SetupJiggle(SkeletonAnimation skel)
        {
            string characterFolder = Path.Combine(SettingsManager.NikkeSettings.AssetsFolder, NikkeData.AssetName);
            var charSettings = JiggleSettingsManager.GetForCharacter(characterFolder);
            if (charSettings != null && !charSettings.Enabled) return;

            var patterns = JiggleSettingsManager.GetPatterns(charSettings);
            var explicitBones = charSettings?.Bones ?? new List<JiggleBoneSettings>();

            var discovered = new List<JiggleBoneSettings>();
            var registeredNames = new HashSet<string>(explicitBones.ConvertAll(b => b.BoneName));

            foreach (var pattern in patterns)
            {
                foreach (var bone in skel.Skeleton.Bones)
                {
                    string name = bone.Data.Name;
                    if (name.Length == 0 || (name[0] != '@' && name[0] != '#')) continue;
                    if (name.Contains("shadow", System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (name == "@a_hair_") continue;
                    if (name.IndexOf("hair_0", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (name.IndexOf("acc_1", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (registeredNames.Contains(name)) continue;
                    if (name.IndexOf(pattern.Keyword, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    registeredNames.Add(name);
                    discovered.Add(new JiggleBoneSettings
                    {
                        BoneName = name,
                        Stiffness = pattern.Stiffness,
                        Damping = pattern.Damping,
                        ForceFactor = pattern.ForceFactor,
                        MaxRotDisplacement = pattern.MaxRotDisplacement,
                        PosStiffness = pattern.PosStiffness,
                        PosDamping = pattern.PosDamping,
                        PosForceFactor = pattern.PosForceFactor,
                        MaxPosDisplacement = pattern.MaxPosDisplacement,
                    });
                }
            }

            var allBones = new List<JiggleBoneSettings>(explicitBones);
            allBones.AddRange(discovered);
            if (allBones.Count == 0) return;

            jigglePhysics = new JigglePhysics(allBones);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Jiggle] {NikkeData.NikkeName} — registering bones:");
            int registered = 0;

            foreach (var bs in allBones)
            {
                var bone = skel.Skeleton.FindBone(bs.BoneName);
                if (bone == null)
                {
                    sb.AppendLine($"  MISSING: {bs.BoneName}");
                    continue;
                }
                sb.AppendLine($"  OK: {bs.BoneName} (stiff={bs.Stiffness} damp={bs.Damping} force={bs.ForceFactor})");
                registered++;
                jigglePhysics.AddBone(bs.BoneName, new JigglePhysics.BoneHandle
                {
                    GetRotation = () => bone.Rotation,
                    SetRotation = v => bone.Rotation = v,
                    GetX = () => bone.X,
                    GetY = () => bone.Y,
                    SetX = v => bone.X = v,
                    SetY = v => bone.Y = v,
                });
            }
            sb.AppendLine($"Total: {registered} bones registered");
            Debug.Log(sb);

            if (registered > 0)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                jigglePhysics.SetInitialMouse(new Vector2(mousePos.x / Screen.width, mousePos.y / Screen.height));
                Debug.Log($"[AimStart] jigglePhysics CREATED for {NikkeData.AssetName}");
            }
        }

        void UpdateAimBlend()
        {
            if (!aimBlendActive) return;
            if (!poseInstances.TryGetValue(NikkePoseType.Aim, out var inst)) return;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            float normalizedX = mousePos.x / Screen.width;
            float normalizedY = mousePos.y / Screen.height;

            if (aimXTrack != null)
                aimXTrack.TrackTime = aimXTrack.Animation.Duration * normalizedX;
            if (aimYTrack != null)
                aimYTrack.TrackTime = aimYTrack.Animation.Duration * normalizedY;

            float aimAngleX = (normalizedX - 0.5f) * 2f;
            var spine = inst.anim.Skeleton.FindBone("spine");

            if (spine != null)
            {
                spine.Rotation += aimAngleX * 50f;
            }

            {
                var canvasRect = aimReticle.GetComponentInParent<Canvas>().GetComponent<RectTransform>();
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    mousePos,
                    CachedCamera,
                    out Vector2 canvasPos
                );
                aimReticle.anchoredPosition = canvasPos;
            }

            jigglePhysics?.Update(new Vector2(normalizedX, normalizedY));
        }

        public override List<PoseDebugInfo> GetPoseDebugInfo()
        {
            var list = new List<PoseDebugInfo>();
            foreach (var (type, (go, anim)) in poseInstances)
            {
                if (anim == null) continue;
                var skData = anim.Skeleton.Data;
                var current = anim.AnimationState.GetCurrent(0);
                list.Add(new PoseDebugInfo
                {
                    PoseType = type,
                    IsActive = type == NikkeData.ActivePose,
                    Animations = skData.Animations.Items
                        .Take(skData.Animations.Count)
                        .Select(a => a.Name).ToArray(),
                    CurrentAnimation = current?.Animation?.Name ?? "(none)",
                    SkinNames = skData.Skins?.Select(s => s.Name).ToArray() ?? System.Array.Empty<string>(),
                    CurrentSkin = anim.Skeleton.Skin?.Name ?? "(none)"
                });
            }
            return list;
        }

        /// <summary>
        /// Toggle a pose's renderer and collider without deactivating the GameObject.
        /// This keeps the SkeletonAnimation running so animations don't freeze.
        /// </summary>
        static void SetPoseVisible(GameObject poseGO, bool visible)
        {
            if (poseGO.TryGetComponent(out MeshRenderer renderer))
                renderer.enabled = visible;
            if (poseGO.TryGetComponent(out MeshCollider collider))
            {
                collider.enabled = visible;
                // Refresh collider mesh — transition animations change the mesh
                if (visible && poseGO.TryGetComponent(out MeshFilter meshFilter))
                    collider.sharedMesh = meshFilter.sharedMesh;
            }
        }

        private TextMeshPro CreateDisplayName(string name)
        {
            TextMeshPro tmp = Instantiate(m_NikkeNamePrefab, Vector3.zero, Quaternion.identity);
            tmp.transform.SetParent(SettingsManager.BackgroundImage.transform.parent, false);
            tmp.transform.localScale = Vector3.one;
            tmp.name = tmp.text = name;
            return tmp;
        }

        private void UpdateDisplayName(TextMeshPro tmp)
        {
            var skel = ActiveSkeleton;
            if (skel == null) return;

            Vector2 skeletonBounds = SpineHelper.GetSkeletonBounds(skel.Skeleton);

            Vector3 worldPosition = new Vector3(
                transform.position.x * transform.localScale.x,
                (transform.position.y + skeletonBounds.y + 0.5f) * transform.localScale.y,
                transform.position.z
            );

            Vector3 screenPosition = CachedCamera.WorldToScreenPoint(worldPosition);

            // Convert screen position to Canvas space
            RectTransform canvasRect = tmp.canvas.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPosition,
                CachedCamera,
                out Vector2 canvasPosition
            );

            tmp.rectTransform.anchoredPosition = canvasPosition;
        }

        void ToggleCoverPose(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed) return;

            Ray ray = CachedCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                var viewer = hit.collider.GetComponentInParent<NikkeViewer>();
                if (viewer != null && viewer == this)
                {
                    Possessed = this;
                    NikkePoseType targetPose = NikkeData.ActivePose == NikkePoseType.Cover
                        ? NikkePoseType.Base
                        : NikkePoseType.Cover;
                    SetActivePose(targetPose);
                }
            }
        }

        void AimStart(InputAction.CallbackContext ctx)
        {
            if (Possessed == this)
            {
                int triggerId = ++currentTriggerId;
                SetActivePose(NikkePoseType.Aim);
                TriggerJiggleImpulse(triggerId).Forget();
            }
        }

        async UniTaskVoid TriggerJiggleImpulse(int triggerId)
        {
            int waitFrames = 0;
            while (jigglePhysics == null && waitFrames < 30)
            {
                await UniTask.NextFrame();
                waitFrames++;
            }
            if (triggerId == currentTriggerId)
                jigglePhysics?.TriggerImpulse(1f, 1f);
        }

        void AimEnd(InputAction.CallbackContext ctx)
        {
            if (Possessed == this)
            {
                currentTriggerId++;
                SetActivePose(NikkePoseType.Cover);
            }
        }

        void Interact(InputAction.CallbackContext ctx)
        {
            if (ctx.performed)
            {
                Ray ray = CachedCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    var viewer = hit.collider.GetComponentInParent<NikkeViewer>();
                    if (viewer != null && viewer == this)
                    {
                        var skel = ActiveSkeleton;
                        if (skel == null) return;

                        string animName = TouchAnimations.Count > 0
                            ? TouchAnimations[TouchVoiceIndex % TouchAnimations.Count]
                            : m_TouchAnimation;

                        Spine.Animation touchAnimation = skel
                            .skeletonDataAsset.GetAnimationStateData()
                            .SkeletonData.FindAnimation(animName);

                        if (touchAnimation != null)
                        {
                            if (TouchVoices.Count > 0)
                            {
                                NikkeAudioSource.Stop();
                                NikkeAudioSource.clip = TouchVoices[TouchVoiceIndex % TouchVoices.Count];
                                NikkeAudioSource.Play();
                            }

                            TouchVoiceIndex++;

                            skel.AnimationState.SetAnimation(0, animName, false);
                            skel.AnimationState.AddAnimation(
                                0,
                                m_DefaultAnimation,
                                true,
                                0
                            );
                        }
                    }
                }
            }
        }
    }
}
