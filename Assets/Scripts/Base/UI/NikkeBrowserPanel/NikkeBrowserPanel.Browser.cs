using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NikkeViewerEX.Serialization;
using UnityEngine;
using UnityEngine.UIElements;

namespace NikkeViewerEX.UI
{
    public partial class NikkeBrowserPanel
    {
        // Browser sub-tab elements
        Button subTabNikkeBtn;
        Button subTabAzurLaneBtn;
        VisualElement subContentNikke;
        VisualElement subContentAzurLane;

        // Nikke sub-tab elements
        TextField searchInput;
        Label browserCount;
        ScrollView browserList;
        VisualElement browserEmpty;
        Button filterHasAssetsBtn;
        Button filterFullBtn;

        bool filterHasAssets = true;
        bool filterFull = true;

        readonly List<(VisualElement element, NikkeDatabaseEntry entry)> browserItems = new();

        void QueryBrowserElements()
        {
            subTabNikkeBtn = root.Q<Button>("sub-tab-nikke");
            subTabAzurLaneBtn = root.Q<Button>("sub-tab-azurlane");
            subContentNikke = root.Q("sub-content-nikke");
            subContentAzurLane = root.Q("sub-content-azurlane");

            searchInput = root.Q<TextField>("search-input");
            browserCount = root.Q<Label>("browser-count");
            browserList = root.Q<ScrollView>("browser-list");
            browserEmpty = root.Q("browser-empty");
            filterHasAssetsBtn = root.Q<Button>("filter-has-assets");
            filterFullBtn = root.Q<Button>("filter-full");
        }

        void BindBrowserEvents()
        {
            subTabNikkeBtn.clicked += () => SwitchBrowserSubTab(0);
            subTabAzurLaneBtn.clicked += () => SwitchBrowserSubTab(1);

            searchInput.RegisterValueChangedCallback(evt => ApplyBrowserFilters());
            filterHasAssetsBtn.clicked += ToggleFilterHasAssets;
            filterFullBtn.clicked += ToggleFilterFull;
        }

        void UnbindBrowserEvents()
        {
            subTabNikkeBtn.clicked -= () => SwitchBrowserSubTab(0);
            subTabAzurLaneBtn.clicked -= () => SwitchBrowserSubTab(1);

            searchInput.UnregisterValueChangedCallback(evt => ApplyBrowserFilters());
            filterHasAssetsBtn.clicked -= ToggleFilterHasAssets;
            filterFullBtn.clicked -= ToggleFilterFull;
        }

        void SwitchBrowserSubTab(int index)
        {
            subTabNikkeBtn.RemoveFromClassList("sub-tab-active");
            subTabAzurLaneBtn.RemoveFromClassList("sub-tab-active");
            subContentNikke.RemoveFromClassList("sub-tab-visible");
            subContentAzurLane.RemoveFromClassList("sub-tab-visible");

            switch (index)
            {
                case 0:
                    subTabNikkeBtn.AddToClassList("sub-tab-active");
                    subContentNikke.AddToClassList("sub-tab-visible");
                    ApplyBrowserFilters();
                    break;
                case 1:
                    subTabAzurLaneBtn.AddToClassList("sub-tab-active");
                    subContentAzurLane.AddToClassList("sub-tab-visible");
                    break;
            }
        }

        void ToggleFilterHasAssets()
        {
            filterHasAssets = !filterHasAssets;
            filterHasAssetsBtn.EnableInClassList("filter-active", filterHasAssets);
            ApplyBrowserFilters();
        }

        void ToggleFilterFull()
        {
            filterFull = !filterFull;
            filterFullBtn.EnableInClassList("filter-active", filterFull);
            ApplyBrowserFilters();
        }

        void ApplyBrowserFilters()
        {
            FilterBrowserList(searchInput.value);
        }

        void PopulateBrowserList()
        {
            browserList.Clear();
            browserItems.Clear();

            if (database == null || database.Length == 0)
            {
                browserEmpty.style.display = DisplayStyle.Flex;
                browserList.style.display = DisplayStyle.None;
                browserCount.text = "0 characters available";
                return;
            }

            browserEmpty.style.display = DisplayStyle.None;
            browserList.style.display = DisplayStyle.Flex;

            string thumbnailsFolder = thumbnailsFolderInput.value;

            foreach (var entry in database)
            {
                VisualElement item = m_BrowserItemTemplate.Instantiate();
                VisualElement itemRoot = item.Q("character-item");

                item.Q<Label>("character-name").text = entry.name;
                item.Q<Label>("character-id").text = entry.id;

                var assetInfo = resolvedAssets.GetValueOrDefault(entry.id);
                string versionText = entry.VersionLabel;
                if (assetInfo != null && assetInfo.VariationCount > 1)
                    versionText += $" | {assetInfo.VariationCount} textures";
                if (assetInfo != null && assetInfo.Poses.Count > 1)
                    versionText += $" | {assetInfo.Poses.Count} poses";
                item.Q<Label>("character-version").text = versionText;

                bool hasAssets = assetInfo is { IsValid: true };
                Label noAssetsLabel = item.Q<Label>("no-assets-label");
                Button addBtn = item.Q<Button>("add-button");
                Label addedLabel = item.Q<Label>("added-label");

                noAssetsLabel.text = hasAssets ? "" : "missing assets";
                addBtn.SetEnabled(hasAssets);

                int instanceCount = 0;
                foreach (var n in settingsManager.NikkeSettings.NikkeList)
                    if (n.AssetName == entry.id) instanceCount++;
                if (instanceCount > 0)
                {
                    addBtn.text = $"Added ({instanceCount})";
                    addedLabel.style.display = DisplayStyle.None;
                }

                addBtn.clicked += () => AddCharacter(entry, addBtn, addedLabel, itemRoot).Forget();

                VisualElement thumbnailEl = item.Q("character-thumbnail");
                if (!string.IsNullOrEmpty(thumbnailsFolder))
                    LoadThumbnail(thumbnailEl, thumbnailsFolder, entry.id).Forget();
                else
                    thumbnailEl.AddToClassList("thumbnail-missing");

                browserList.Add(item);
                browserItems.Add((item, entry));
            }

            UpdateBrowserCount();
        }

        async UniTask LoadThumbnail(VisualElement thumbnailEl, string thumbnailsFolder, string characterId)
        {
            string path = NikkeViewerEX.Utils.CharacterAssetResolver.FindThumbnail(thumbnailsFolder, characterId);
            if (path == null)
            {
                thumbnailEl.AddToClassList("thumbnail-missing");
                return;
            }

            try
            {
                byte[] data = await File.ReadAllBytesAsync(path);
                Texture2D tex = new(2, 2);
                tex.LoadImage(data);
                tex.name = characterId;
                thumbnailEl.style.backgroundImage = new StyleBackground(tex);
                thumbnailEl.RemoveFromClassList("thumbnail-missing");
            }
            catch (Exception ex)
            {
                thumbnailEl.AddToClassList("thumbnail-missing");
                Debug.LogWarning($"Could not load thumbnail for {characterId}: {ex.Message}");
            }
        }

        void FilterBrowserList(string search)
        {
            if (browserItems.Count == 0) return;

            string filter = search?.ToLowerInvariant() ?? "";
            int visible = 0;

            foreach (var (element, entry) in browserItems)
            {
                bool match = string.IsNullOrEmpty(filter)
                    || entry.name.ToLowerInvariant().Contains(filter)
                    || entry.id.ToLowerInvariant().Contains(filter);

                if (match && (filterHasAssets || filterFull))
                {
                    var assetInfo = resolvedAssets.GetValueOrDefault(entry.id);
                    if (filterHasAssets && assetInfo is not { IsValid: true })
                        match = false;
                    if (filterFull && (assetInfo == null || assetInfo.Poses.Count <= 1))
                        match = false;
                }

                element.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
                if (match) visible++;
            }

            bool hasActiveFilter = !string.IsNullOrEmpty(filter) || filterHasAssets || filterFull;
            browserCount.text = hasActiveFilter
                ? $"{visible} of {browserItems.Count} characters"
                : $"{browserItems.Count} characters available";
        }

        void UpdateBrowserCount()
        {
            int active = settingsManager.NikkeSettings.NikkeList.Count + settingsManager.NikkeSettings.AzurLaneList.Count;
            browserCount.text = $"{browserItems.Count} characters available, {active} active";
            tabActiveBtn.text = $"Active ({active})";
        }

        void UpdateBrowserAddedCount(string characterId)
        {
            foreach (var (element, entry) in browserItems)
            {
                if (entry.id == characterId)
                {
                    Button addBtn = element.Q<Button>("add-button");
                    int count = 0;
                    foreach (var n in settingsManager.NikkeSettings.NikkeList)
                        if (n.AssetName == characterId) count++;
                    if (count > 0)
                    {
                        addBtn.text = $"Added ({count})";
                    }
                    else
                    {
                        addBtn.text = "Add";
                    }
                    break;
                }
            }
        }
    }
}
