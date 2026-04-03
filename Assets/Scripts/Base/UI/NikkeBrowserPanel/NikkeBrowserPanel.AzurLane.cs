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
        // Azur Lane browser sub-tab elements
        TextField alSearchInput;
        Label alBrowserCount;
        ScrollView alBrowserList;
        VisualElement alBrowserEmpty;

        // Data
        NikkeDatabaseEntry[] alDatabase;
        readonly Dictionary<string, AzurLaneAssetInfo> resolvedAlAssets = new();
        readonly List<(VisualElement element, NikkeDatabaseEntry entry)> alBrowserItems = new();

        void QueryAzurLaneElements()
        {
            alSearchInput = root.Q<TextField>("al-search-input");
            alBrowserCount = root.Q<Label>("al-browser-count");
            alBrowserList = root.Q<ScrollView>("al-browser-list");
            alBrowserEmpty = root.Q("al-browser-empty");
        }

        void BindAzurLaneEvents()
        {
            alSearchInput.RegisterValueChangedCallback(evt => FilterAlBrowserList(evt.newValue));
        }

        void UnbindAzurLaneEvents()
        {
            alSearchInput.UnregisterValueChangedCallback(evt => FilterAlBrowserList(evt.newValue));
        }

        public void PopulateAzurLaneList()
        {
            alBrowserList.Clear();
            alBrowserItems.Clear();

            string jsonPath = settingsManager.NikkeSettings.AzurLaneDatabaseJsonPath;
            string assetsFolder = settingsManager.NikkeSettings.AzurLaneAssetsFolder;

            // Always scan folder so entries without a DB still appear
            resolvedAlAssets.Clear();
            if (!string.IsNullOrEmpty(assetsFolder) && Directory.Exists(assetsFolder))
            {
                var scanned = CharacterAssetResolver.ScanAzurLaneFolder(assetsFolder);
                foreach (var kvp in scanned)
                    resolvedAlAssets[kvp.Key] = kvp.Value;
            }

            // Build display entries: DB names take priority, fallback to folder id as name
            var entries = new List<NikkeDatabaseEntry>();
            if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    alDatabase = NikkeDatabaseParser.Parse(json);
                    var dbIds = new HashSet<string>(alDatabase.Select(e => e.id));
                    entries.AddRange(alDatabase);

                    // Append any folder entries not in DB
                    foreach (string id in resolvedAlAssets.Keys)
                    {
                        if (!dbIds.Contains(id))
                            entries.Add(new NikkeDatabaseEntry { name = id, id = id });
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AzurLane] Failed to parse database: {ex.Message}");
                    entries.AddRange(resolvedAlAssets.Keys.Select(id =>
                        new NikkeDatabaseEntry { name = id, id = id }));
                }
            }
            else
            {
                // No DB — use folder names
                entries.AddRange(resolvedAlAssets.Keys.Select(id =>
                    new NikkeDatabaseEntry { name = id, id = id }));
            }

            if (entries.Count == 0)
            {
                alBrowserEmpty.style.display = DisplayStyle.Flex;
                alBrowserList.style.display = DisplayStyle.None;
                alBrowserCount.text = "0 characters available";
                return;
            }

            alBrowserEmpty.style.display = DisplayStyle.None;
            alBrowserList.style.display = DisplayStyle.Flex;

            foreach (var entry in entries)
            {
                VisualElement item = m_BrowserItemTemplate.Instantiate();

                item.Q<Label>("character-name").text = entry.name;
                item.Q<Label>("character-id").text = entry.id;
                item.Q<Label>("character-version").text = "Live2D";

                bool hasAssets = resolvedAlAssets.TryGetValue(entry.id, out var assetInfo) && assetInfo.IsValid;
                Label noAssetsLabel = item.Q<Label>("no-assets-label");
                Button addBtn = item.Q<Button>("add-button");
                Label addedLabel = item.Q<Label>("added-label");

                noAssetsLabel.text = hasAssets ? "" : "missing assets";
                addBtn.SetEnabled(hasAssets);
                addedLabel.style.display = DisplayStyle.None;

                int instanceCount = settingsManager.NikkeSettings.AzurLaneList
                    .Count(c => c.AssetName == entry.id);
                if (instanceCount > 0)
                    addBtn.text = $"Added ({instanceCount})";

                // Thumbnail: same thumbnails folder as Nikke, same si_{id}*.png convention
                VisualElement thumbnailEl = item.Q("character-thumbnail");
                string thumbFolder = settingsManager.NikkeSettings.ThumbnailsFolder;
                if (!string.IsNullOrEmpty(thumbFolder))
                    LoadThumbnail(thumbnailEl, thumbFolder, entry.id).Forget();
                else
                    thumbnailEl.AddToClassList("thumbnail-missing");

                addBtn.clicked += () => AddAzurLaneCharacter(entry, addBtn, assetInfo);

                alBrowserList.Add(item);
                alBrowserItems.Add((item, entry));
            }

            UpdateAlBrowserCount();
        }

        void FilterAlBrowserList(string search)
        {
            if (alBrowserItems.Count == 0) return;

            string filter = search?.ToLowerInvariant() ?? "";
            int visible = 0;

            foreach (var (element, entry) in alBrowserItems)
            {
                bool match = string.IsNullOrEmpty(filter)
                    || entry.name.ToLowerInvariant().Contains(filter)
                    || entry.id.ToLowerInvariant().Contains(filter);

                element.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
                if (match) visible++;
            }

            alBrowserCount.text = string.IsNullOrEmpty(filter)
                ? $"{alBrowserItems.Count} characters available"
                : $"{visible} of {alBrowserItems.Count} characters";
        }

        void UpdateAlBrowserCount()
        {
            int active = settingsManager.NikkeSettings.AzurLaneList.Count;
            alBrowserCount.text = $"{alBrowserItems.Count} characters available, {active} active";
        }

        async void AddAzurLaneCharacter(NikkeDatabaseEntry entry, Button addBtn, AzurLaneAssetInfo assetInfo)
        {
            if (assetInfo == null || !assetInfo.IsValid)
            {
                Debug.LogError($"[AzurLane] No valid assets for {entry.id}");
                return;
            }

            addBtn.SetEnabled(false);
            addBtn.text = "...";

            try
            {
                AzurLaneViewer viewer = mainControl.InstantiateAzurLaneViewer();
                if (viewer == null)
                {
                    addBtn.SetEnabled(true);
                    addBtn.text = "Add";
                    return;
                }

                int instanceId = nextInstanceId++;

                var character = new AzurLaneCharacter
                {
                    InstanceId = instanceId,
                    DisplayName = entry.name,
                    AssetName = entry.id,
                    Model3JsonPath = assetInfo.Model3JsonPath,
                };

                viewer.AlCharacterData = character;
                viewer.name = entry.name;
                
                if (Camera.main != null)
                {
                    Vector3 camPos = Camera.main.transform.position;
                    viewer.transform.position = camPos + Camera.main.transform.forward * 5f;
                }
                
                viewer.TriggerSpawn();

                activeViewers[instanceId] = viewer;
                settingsManager.NikkeSettings.AzurLaneList.Add(character);
                await settingsManager.SaveSettings();

                int count = settingsManager.NikkeSettings.AzurLaneList.Count(c => c.AssetName == entry.id);
                addBtn.text = $"Added ({count})";
                addBtn.SetEnabled(true);

                UpdateAlBrowserCount();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AzurLane] Failed to add {entry.id}: {ex.Message}");
                addBtn.SetEnabled(true);
                addBtn.text = "Add";
            }
        }

        async void RemoveAzurLaneCharacter(int instanceId)
        {
            string assetNameToUpdate = null;

            if (activeViewers.TryGetValue(instanceId, out var viewer))
            {
                activeViewers.Remove(instanceId);
                if (viewer != null)
                {
                    assetNameToUpdate = viewer.NikkeData.AssetName;
                    if (viewer.NikkeNameText != null)
                        UnityEngine.Object.Destroy(viewer.NikkeNameText.gameObject);
                    UnityEngine.Object.Destroy(viewer.gameObject);
                }
            }

            settingsManager.NikkeSettings.AzurLaneList.RemoveAll(c => c.InstanceId == instanceId);

            RefreshActiveList();
            if (assetNameToUpdate != null)
                UpdateAlBrowserAddedCount(assetNameToUpdate);
            UpdateAlBrowserCount();
            UpdateBrowserCount();

            await settingsManager.SaveSettings();
        }

        void UpdateAlBrowserAddedCount(string characterId)
        {
            foreach (var (element, entry) in alBrowserItems)
            {
                if (entry.id == characterId)
                {
                    Button addBtn = element.Q<Button>("add-button");
                    int count = settingsManager.NikkeSettings.AzurLaneList.Count(c => c.AssetName == characterId);
                    addBtn.text = count > 0 ? $"Added ({count})" : "Add";
                    break;
                }
            }
        }
    }
}
