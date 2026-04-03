using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NikkeViewerEX.Components;
using NikkeViewerEX.Serialization;
using NikkeViewerEX.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace NikkeViewerEX.UI
{
    public partial class NikkeBrowserPanel
    {
        #region Public API
        public void SwapVariation(string characterId, int variationIndex = -1)
        {
            NikkeViewerBase viewer = activeViewers.Values.FirstOrDefault(v => v.NikkeData.AssetName == characterId);
            if (viewer == null)
            {
                Debug.LogWarning($"No active viewer for {characterId}");
                return;
            }
            if (!resolvedAssets.TryGetValue(characterId, out CharacterAssetInfo assetInfo))
            {
                Debug.LogWarning($"No resolved assets for {characterId}");
                return;
            }
            if (assetInfo.VariationCount <= 1)
            {
                Debug.Log($"{characterId} has no texture variations to swap");
                return;
            }

            int current = currentVariation.GetValueOrDefault(characterId, 0);
            int target = variationIndex < 0
                ? (current + 1) % assetInfo.VariationCount
                : variationIndex % assetInfo.VariationCount;

            currentVariation[characterId] = target;
            List<string> textures = assetInfo.GetTextures(target);
            SwapViewerTexture(viewer, textures).Forget();
            viewer.NikkeData.TexturesPath = textures;
            settingsManager.SaveSettings().Forget();

            Debug.Log($"Swapped {characterId} to variation {target}/{assetInfo.VariationCount} ({Path.GetFileName(textures[0])})");
        }

        public int GetVariationCount(string characterId) =>
            resolvedAssets.TryGetValue(characterId, out CharacterAssetInfo info) ? info.VariationCount : 0;

        public int GetCurrentVariation(string characterId) =>
            currentVariation.GetValueOrDefault(characterId, 0);
        #endregion

        #region Texture Swapping
        async UniTask SwapViewerTexture(NikkeViewerBase viewer, List<string> texturePaths)
        {
            var renderer = viewer.GetComponentInChildren<MeshRenderer>();
            if (renderer == null)
            {
                Debug.LogWarning("No MeshRenderer found on viewer");
                return;
            }
            if (texturePaths.Count == 0) return;

            try
            {
                byte[] data = await File.ReadAllBytesAsync(texturePaths[0]);
                Texture2D tex = new(2, 2);
                tex.LoadImage(data);
                tex.name = Path.GetFileNameWithoutExtension(texturePaths[0]);
                renderer.material.mainTexture = tex;

                if (texturePaths.Count > 1 && renderer.materials.Length > 1)
                {
                    var materials = renderer.materials;
                    for (int i = 1; i < texturePaths.Count && i < materials.Length; i++)
                    {
                        byte[] pageData = await File.ReadAllBytesAsync(texturePaths[i]);
                        Texture2D pageTex = new(2, 2);
                        pageTex.LoadImage(pageData);
                        pageTex.name = Path.GetFileNameWithoutExtension(texturePaths[i]);
                        materials[i].mainTexture = pageTex;
                    }
                    renderer.materials = materials;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to swap texture: {ex.Message}");
            }
        }
        #endregion

        #region Character Management
        void RebuildActiveViewers()
        {
            activeViewers.Clear();
            int maxId = 0;
            var viewers = FindObjectsByType<NikkeViewerBase>(FindObjectsSortMode.None);
            foreach (var viewer in viewers)
            {
                int instanceId = viewer.NikkeData.InstanceId;
                if (instanceId > 0)
                {
                    activeViewers[instanceId] = viewer;
                    if (instanceId > maxId) maxId = instanceId;
                }
            }
            nextInstanceId = maxId + 1;
        }

        bool IsCharacterActive(string id) =>
            settingsManager.NikkeSettings.NikkeList.Exists(n => n.AssetName == id);

        async void AddCharacter(
            NikkeDatabaseEntry entry,
            Button addBtn,
            Label addedLabel,
            VisualElement itemRoot
        )
        {
            if (!resolvedAssets.TryGetValue(entry.id, out CharacterAssetInfo assetInfo) || !assetInfo.IsValid)
            {
                Debug.LogError($"No valid assets for {entry.id}");
                return;
            }

            addBtn.SetEnabled(false);
            addBtn.text = "...";

            try
            {
                NikkeViewerBase viewer = await mainControl.InstantiateViewer(assetInfo.SkelPath);
                if (viewer == null)
                {
                    addBtn.SetEnabled(true);
                    addBtn.text = "Add";
                    return;
                }

                List<string> defaultTextures = assetInfo.GetTextures(0);
                List<string> voicesPaths = CharacterAssetResolver.FindTouchSounds(
                    assetsFolderInput.value, entry.id);

                var poses = new List<NikkePose>();
                foreach (NikkePoseType poseType in assetInfo.AvailablePoses)
                {
                    var poseAsset = assetInfo.Poses[poseType];
                    poses.Add(new NikkePose
                    {
                        PoseType = poseType,
                        SkelPath = poseAsset.SkelPath,
                        AtlasPath = poseAsset.AtlasPath,
                        TexturesPath = poseAsset.GetTextures(0),
                    });
                }

                int instanceId = nextInstanceId++;
                string displayName = entry.name;

                viewer.NikkeData = new Nikke
                {
                    InstanceId = instanceId,
                    NikkeName = entry.name,
                    AssetName = entry.id,
                    SkelPath = assetInfo.SkelPath,
                    AtlasPath = assetInfo.AtlasPath,
                    TexturesPath = defaultTextures,
                    VoicesSource = new List<string>(),
                    VoicesPath = voicesPaths,
                    Poses = poses,
                    ActivePose = NikkePoseType.Base,
                };

                if (voicesPaths.Count > 0)
                {
                    var clips = new List<UnityEngine.AudioClip>();
                    foreach (string path in voicesPaths)
                    {
                        var clip = await WebRequestHelper.GetAudioClip(path);
                        if (clip != null)
                            clips.Add(clip);
                    }
                    viewer.TouchVoices = clips;
                }
                viewer.name = displayName;
                viewer.TriggerSpawn();

                activeViewers[instanceId] = viewer;
                currentVariation[entry.id] = 0;
                settingsManager.NikkeSettings.NikkeList.Add(viewer.NikkeData);
                await settingsManager.SaveSettings();

                int count = 0;
                foreach (var n in settingsManager.NikkeSettings.NikkeList)
                    if (n.AssetName == entry.id) count++;
                addBtn.text = $"Added ({count})";
                addBtn.SetEnabled(true);

                RefreshActiveList();
                UpdateBrowserAddedCount(entry.id);
                UpdateBrowserCount();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to add character {entry.id}: {ex.Message}");
                addBtn.SetEnabled(true);
                addBtn.text = "Add";
            }
        }

        async void RemoveCharacter(int instanceId)
        {
            string assetNameToUpdate = null;
            
            NikkeViewerBase viewerToRemove = null;
            if (activeViewers.TryGetValue(instanceId, out viewerToRemove))
            {
                activeViewers.Remove(instanceId);
                if (viewerToRemove != null)
                    assetNameToUpdate = viewerToRemove.NikkeData.AssetName;
            }
            
            if (viewerToRemove != null)
            {
                if (viewerToRemove.NikkeNameText != null)
                    Destroy(viewerToRemove.NikkeNameText.gameObject);
                Destroy(viewerToRemove.gameObject);
            }
            else
            {
                var allViewers = FindObjectsByType<NikkeViewerBase>(FindObjectsSortMode.None);
                foreach (var v in allViewers)
                {
                    if (v.NikkeData.InstanceId == instanceId)
                    {
                        assetNameToUpdate = v.NikkeData.AssetName;
                        if (v.NikkeNameText != null)
                            Destroy(v.NikkeNameText.gameObject);
                        Destroy(v.gameObject);
                        break;
                    }
                }
            }

            settingsManager.NikkeSettings.NikkeList.RemoveAll(n => n.InstanceId == instanceId);
            
            RefreshActiveList();
            if (assetNameToUpdate != null)
                UpdateBrowserAddedCount(assetNameToUpdate);
            UpdateBrowserCount();
            
            await settingsManager.SaveSettings();
        }
        #endregion
    }
}
