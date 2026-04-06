using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace NikkeViewerEX.Utils
{
    public class SpineHelper38 : SpineHelperBase
    {
        public static SkeletonDataAsset CreateSkeletonDataAsset(
            SkeletonData skeletonData,
            AnimationStateData stateData
        )
        {
            try
            {
                SkeletonDataAsset skeletonDataAsset =
                    ScriptableObject.CreateInstance<SkeletonDataAsset>();

                Type skeletonDataAssetType = skeletonDataAsset.GetType();

                FieldInfo skeletonDataField = skeletonDataAssetType.GetField(
                    "skeletonData",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                FieldInfo stateDataField = skeletonDataAssetType.GetField(
                    "stateData",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );

                skeletonDataField.SetValue(skeletonDataAsset, skeletonData);
                stateDataField.SetValue(skeletonDataAsset, stateData);

                skeletonDataAsset.skeletonJSON = new TextAsset("NIKKE");

                return skeletonDataAsset;
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
                return null;
            }
        }

        public static async UniTask<SkeletonAnimation> InstantiateSpine(
            string skelPath,
            string atlasPath,
            List<string> texturesPath,
            GameObject targetGameObject,
            Shader spineShader,
            float spineScale = 1f,
            float spineScaleMultiplier = 0.0115f,
            bool loop = false,
            string defaultAnimation = "idle"
        )
        {
            try
            {
                TextAsset atlasTextAsset = new(await WebRequestHelper.GetTextData(atlasPath));

                Texture2D[] imageTextures = new Texture2D[texturesPath.Count];
                for (int i = 0; i < texturesPath.Count; i++)
                {
                    Texture2D imageTexture = new(1, 1);
                    byte[] imageData = await WebRequestHelper.GetBinaryData(texturesPath[i]);
                    imageTexture.LoadImage(imageData);
                    imageTexture.name = Path.GetFileNameWithoutExtension(texturesPath[i]);
                    imageTextures[i] = imageTexture;
                }

                SpineAtlasAsset atlasAsset = SpineAtlasAsset.CreateRuntimeInstance(
                    atlasTextAsset,
                    imageTextures,
                    spineShader,
                    true
                );

                AtlasAttachmentLoader attachmentLoader = new(atlasAsset.GetAtlas());
                SkeletonBinary skeletonBinary = new(attachmentLoader);
                skeletonBinary.Scale *= spineScaleMultiplier;
                skeletonBinary.Scale *= spineScale;

                SkeletonData skeletonData = skeletonBinary.ReadSkeletonData(skelPath);
                AnimationStateData animationStateData = new(skeletonData);
                SkeletonDataAsset skeletonDataAsset = CreateSkeletonDataAsset(
                    skeletonData,
                    animationStateData
                );

                SkeletonAnimation skeletonAnimation = SkeletonAnimation.AddToGameObject(
                    targetGameObject,
                    skeletonDataAsset
                );

                skeletonAnimation.Initialize(false);
                foreach (Skin skin in skeletonData.Skins)
                {
                    if (CheckSkinMesh(skin))
                        skeletonAnimation.Skeleton.SetSkin(skin.Name);
                }
                skeletonAnimation.Skeleton.SetSlotsToSetupPose();

                Spine.Animation anim = skeletonData.FindAnimation(defaultAnimation);
                if (anim == null)
                {
                    // Try common idle names
                    string[] fallbacks = {"normal", "idle", "stand", "wait"}; 
                    foreach (string name in fallbacks)
                    {
                        anim = skeletonData.FindAnimation(name);
                        if (anim != null) break;
                    }
                }
                if (anim == null)
                {
                    string available = string.Join(", ", skeletonData.Animations.Items
                        .Take(skeletonData.Animations.Count)
                        .Select(a => $"\"{a.Name}\""));
                    Debug.LogWarning($"[SpineHelper38] Animation \"{defaultAnimation}\" not found in {skelPath}. Available: [{available}]");
                    if (skeletonData.Animations.Count > 0)
                        anim = skeletonData.Animations.Items[0];
                }
                if (anim != null)
                    skeletonAnimation.AnimationState.SetAnimation(0, anim, loop);

                skeletonAnimation.Update(0);
                skeletonAnimation.LateUpdate();

                return skeletonAnimation;
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
                return null;
            }
        }

        public static bool CheckSkinMesh(Skin skin)
        {
            foreach (var kvp in skin.Attachments)
            {
                if (kvp.Value is MeshAttachment meshAttachment)
                {
                    if (meshAttachment.Vertices.Length > 0 || meshAttachment.Triangles.Length > 0)
                        return true;
                }
            }
            return false;
        }
    }
}
