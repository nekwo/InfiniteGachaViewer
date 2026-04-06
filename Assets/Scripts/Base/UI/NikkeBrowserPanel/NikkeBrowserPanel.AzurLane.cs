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
        Button alFilterL2dBtn;
        Button alFilterSpineBtn;
        Button alFilterStaticBtn;
        Button alFilterFavoritesBtn;

        bool alFilterL2d = false;
        bool alFilterSpine = false;
        bool alFilterStatic = false;
        bool alFilterFavorites = false;

        // Data
        NikkeDatabaseEntry[] alDatabase;
        readonly Dictionary<string, AzurLaneAssetInfo> resolvedAlAssets = new();
        readonly List<(VisualElement element, NikkeDatabaseEntry entry, string assetType)> alBrowserItems = new();

        // Voice mapping: painting_name -> { voice_type -> wav_path }
        Dictionary<string, Dictionary<string, string>> voiceMapping;

        void QueryAzurLaneElements()
        {
            alSearchInput = root.Q<TextField>("al-search-input");
            alBrowserCount = root.Q<Label>("al-browser-count");
            alBrowserList = root.Q<ScrollView>("al-browser-list");
            alBrowserEmpty = root.Q("al-browser-empty");
            alFilterL2dBtn = root.Q<Button>("al-filter-l2d");
            alFilterSpineBtn = root.Q<Button>("al-filter-spine");
            alFilterStaticBtn = root.Q<Button>("al-filter-static");
            alFilterFavoritesBtn = root.Q<Button>("al-filter-favorites");
        }

        void BindAzurLaneEvents()
        {
            alSearchInput.RegisterValueChangedCallback(evt => ApplyAlFilters());
            alFilterL2dBtn.clicked += ToggleAlFilterL2d;
            alFilterSpineBtn.clicked += ToggleAlFilterSpine;
            alFilterStaticBtn.clicked += ToggleAlFilterStatic;
            alFilterFavoritesBtn.clicked += ToggleAlFilterFavorites;
        }

        void UnbindAzurLaneEvents()
        {
            alSearchInput.UnregisterValueChangedCallback(evt => ApplyAlFilters());
            alFilterL2dBtn.clicked -= ToggleAlFilterL2d;
            alFilterSpineBtn.clicked -= ToggleAlFilterSpine;
            alFilterStaticBtn.clicked -= ToggleAlFilterStatic;
            alFilterFavoritesBtn.clicked -= ToggleAlFilterFavorites;
        }

        void ToggleAlFilterL2d()
        {
            alFilterL2d = !alFilterL2d;
            alFilterL2dBtn.EnableInClassList("filter-active", alFilterL2d);
            ApplyAlFilters();
        }

        void ToggleAlFilterSpine()
        {
            alFilterSpine = !alFilterSpine;
            alFilterSpineBtn.EnableInClassList("filter-active", alFilterSpine);
            ApplyAlFilters();
        }

        void ToggleAlFilterStatic()
        {
            alFilterStatic = !alFilterStatic;
            alFilterStaticBtn.EnableInClassList("filter-active", alFilterStatic);
            ApplyAlFilters();
        }

        void ToggleAlFilterFavorites()
        {
            alFilterFavorites = !alFilterFavorites;
            alFilterFavoritesBtn.EnableInClassList("filter-active", alFilterFavorites);
            ApplyAlFilters();
        }

        void ApplyAlFilters()
        {
            FilterAlBrowserList(alSearchInput.value);
        }

        public void PopulateAzurLaneList()
        {
            alBrowserList.Clear();
            alBrowserItems.Clear();

            // Load voice mapping if available
            LoadVoiceMapping();

            string jsonPath = settingsManager.NikkeSettings.AzurLaneDatabaseJsonPath;
            string assetsFolder = settingsManager.NikkeSettings.AzurLaneAssetsFolder;
            string staticAssetsFolder = settingsManager.NikkeSettings.StaticPaintingAssetsFolder;

            // Always scan folder so entries without a DB still appear
            resolvedAlAssets.Clear();
            if (!string.IsNullOrEmpty(assetsFolder) && Directory.Exists(assetsFolder))
            {
                var scanned = CharacterAssetResolver.ScanAzurLaneFolder(assetsFolder);
                foreach (var kvp in scanned)
                    resolvedAlAssets[kvp.Key] = kvp.Value;
            }

            // Scan static painting assets folder
            if (!string.IsNullOrEmpty(staticAssetsFolder) && Directory.Exists(staticAssetsFolder))
            {
                var scanned = CharacterAssetResolver.ScanAzurLaneFolder(staticAssetsFolder);
                foreach (var kvp in scanned)
                {
                    if (!resolvedAlAssets.ContainsKey(kvp.Key))
                        resolvedAlAssets[kvp.Key] = kvp.Value;
                }
            }

            // Build display entries: DB names take priority, fallback to folder id as name
            var entries = new List<NikkeDatabaseEntry>();
            var allDbIds = new HashSet<string>();

            if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    alDatabase = NikkeDatabaseParser.Parse(json);
                    foreach (var e in alDatabase)
                        allDbIds.Add(e.id);
                    entries.AddRange(alDatabase);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AzurLane] Failed to parse database: {ex.Message}");
                }
            }

            // Load static painting database
            string staticJsonPath = settingsManager.NikkeSettings.StaticPaintingDatabaseJsonPath;
            if (!string.IsNullOrEmpty(staticJsonPath) && File.Exists(staticJsonPath))
            {
                try
                {
                    string json = File.ReadAllText(staticJsonPath);
                    var staticDb = NikkeDatabaseParser.Parse(json);
                    foreach (var e in staticDb)
                    {
                        if (!allDbIds.Contains(e.id))
                        {
                            allDbIds.Add(e.id);
                            entries.Add(e);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AzurLane] Failed to parse static database: {ex.Message}");
                }
            }

            // Append any folder entries not in any DB
            foreach (string id in resolvedAlAssets.Keys)
            {
                if (!allDbIds.Contains(id))
                    entries.Add(new NikkeDatabaseEntry { name = id, id = id });
            }

            if (entries.Count == 0 && resolvedAlAssets.Count > 0)
            {
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
                bool hasAssets = resolvedAlAssets.TryGetValue(entry.id, out var assetInfo) && assetInfo.IsValid;
                bool isSpine = entry.IsSpine || (hasAssets && assetInfo.IsSpine);
                bool isStatic = string.Equals(entry.type, "static", StringComparison.OrdinalIgnoreCase)
                    || (hasAssets && assetInfo.IsStatic && !isSpine);
                string assetType = isStatic ? "static" : isSpine ? "spine" : "l2d";
                item.Q<Label>("character-version").text = isStatic ? "Static" : isSpine ? "Spine" : "Live2D";
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

                // Favorite heart button
                Button heartBtn = item.Q<Button>("favorite-button");
                bool isFav = IsFavorite(entry.id);
                heartBtn.text = isFav ? "\u2665" : "\u2661";
                heartBtn.EnableInClassList("favorited", isFav);
                heartBtn.clicked += () => ToggleFavorite(entry.id, heartBtn);

                // Thumbnail: check static thumbnails folder first, then shared thumbnails folder
                VisualElement thumbnailEl = item.Q("character-thumbnail");
                string staticThumbFolder = settingsManager.NikkeSettings.StaticPaintingThumbnailsFolder;
                string thumbFolder = settingsManager.NikkeSettings.ThumbnailsFolder;
                if (!string.IsNullOrEmpty(staticThumbFolder))
                    LoadThumbnailWithFallback(thumbnailEl, staticThumbFolder, thumbFolder, entry.id).Forget();
                else if (!string.IsNullOrEmpty(thumbFolder))
                    LoadThumbnail(thumbnailEl, thumbFolder, entry.id).Forget();
                else
                    thumbnailEl.AddToClassList("thumbnail-missing");

                addBtn.clicked += () => AddAzurLaneCharacter(entry, addBtn, assetInfo).Forget();

                alBrowserList.Add(item);
                alBrowserItems.Add((item, entry, assetType));
            }

            UpdateAlBrowserCount();
        }

        void FilterAlBrowserList(string search)
        {
            if (alBrowserItems.Count == 0) return;

            string filter = search?.ToLowerInvariant() ?? "";
            bool anyTypeFilter = alFilterL2d || alFilterSpine || alFilterStatic;
            int visible = 0;

            foreach (var (element, entry, assetType) in alBrowserItems)
            {
                bool match = string.IsNullOrEmpty(filter)
                    || entry.name.ToLowerInvariant().Contains(filter)
                    || entry.id.ToLowerInvariant().Contains(filter);

                // Type filters act as OR: show if it matches any active type filter
                if (match && anyTypeFilter)
                {
                    bool typeMatch = false;
                    if (alFilterL2d && assetType == "l2d") typeMatch = true;
                    if (alFilterSpine && assetType == "spine") typeMatch = true;
                    if (alFilterStatic && assetType == "static") typeMatch = true;
                    if (!typeMatch) match = false;
                }

                if (match && alFilterFavorites && !IsFavorite(entry.id))
                    match = false;

                element.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
                if (match) visible++;
            }

            bool hasActiveFilter = !string.IsNullOrEmpty(filter) || anyTypeFilter || alFilterFavorites;
            alBrowserCount.text = hasActiveFilter
                ? $"{visible} of {alBrowserItems.Count} characters"
                : $"{alBrowserItems.Count} characters available";
        }

        void UpdateAlBrowserCount()
        {
            int active = settingsManager.NikkeSettings.AzurLaneList.Count;
            alBrowserCount.text = $"{alBrowserItems.Count} characters available, {active} active";
        }

        async UniTaskVoid AddAzurLaneCharacter(NikkeDatabaseEntry entry, Button addBtn, AzurLaneAssetInfo assetInfo)
        {
            if (assetInfo == null || !assetInfo.IsValid)
            {
                Debug.LogError($"[AzurLane] No valid assets for {entry.id}");
                return;
            }

            addBtn.SetEnabled(false);
            addBtn.text = "...";

            bool isSpine = entry.IsSpine || assetInfo.IsSpine;
            bool isStatic = string.Equals(entry.type, "static", StringComparison.OrdinalIgnoreCase)
                || (assetInfo.IsStatic && !isSpine);

            try
            {
                NikkeViewerBase viewerBase;
                int instanceId = nextInstanceId++;

                var character = new AzurLaneCharacter
                {
                    InstanceId = instanceId,
                    DisplayName = entry.name,
                    AssetName = entry.id,
                    Type = isStatic ? "static" : isSpine ? "spine" : "l2d",
                    VoicesPath = GetVoicePaths(entry.id),
                };

                if (isStatic)
                {
                    var viewer = mainControl.InstantiateStaticPaintingViewer();
                    if (viewer == null)
                    {
                        addBtn.SetEnabled(true);
                        addBtn.text = "Add";
                        return;
                    }

                    viewer.AlCharacterData = character;
                    viewerBase = viewer;
                }
                else if (isSpine)
                {
                    viewerBase = mainControl.InstantiateAzurLaneSpineViewer();
                    if (viewerBase == null)
                    {
                        addBtn.SetEnabled(true);
                        addBtn.text = "Add";
                        return;
                    }

                    character.SkelPath = assetInfo.SkelPath;
                    character.AtlasPath = assetInfo.AtlasPath;
                    character.TexturesPath = new List<string>(assetInfo.TexturesPath);

                    viewerBase.AlCharacterData = character;
                }
                else
                {
                    var viewer = mainControl.InstantiateAzurLaneViewer();
                    if (viewer == null)
                    {
                        addBtn.SetEnabled(true);
                        addBtn.text = "Add";
                        return;
                    }

                    character.Model3JsonPath = assetInfo.Model3JsonPath;
                    viewer.AlCharacterData = character;
                    viewerBase = viewer;
                }

                viewerBase.name = entry.name;

                // Load voice clips if available
                if (character.VoicesPath.Count > 0)
                {
                    viewerBase.TouchVoices = (
                        await UniTask.WhenAll(
                            character.VoicesPath.Select(async p =>
                                await WebRequestHelper.GetAudioClip(p))
                        )
                    ).Where(c => c != null).ToList();
                }

                if (Camera.main != null)
                {
                    Vector3 camPos = Camera.main.transform.position;
                    viewerBase.transform.position = camPos + Camera.main.transform.forward * 5f;
                }

                viewerBase.TriggerSpawn();

                activeViewers[instanceId] = viewerBase;
                settingsManager.NikkeSettings.AzurLaneList.Add(character);
                await settingsManager.SaveSettings();

                int count = settingsManager.NikkeSettings.AzurLaneList.Count(c => c.AssetName == entry.id);
                addBtn.text = $"Added ({count})";
                addBtn.SetEnabled(true);

                RefreshActiveList();
                UpdateAlBrowserCount();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AzurLane] Failed to add {entry.id}: {ex.Message}");
                addBtn.SetEnabled(true);
                addBtn.text = "Add";
            }
        }

        async UniTaskVoid RemoveAzurLaneCharacter(int instanceId)
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
            foreach (var (element, entry, _) in alBrowserItems)
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

        void LoadVoiceMapping()
        {
            string path = settingsManager.NikkeSettings.VoiceMappingJsonPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                voiceMapping = null;
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                voiceMapping = ParseVoiceMappingJson(json);
                Debug.Log($"[VoiceMapping] Loaded {voiceMapping.Count} entries");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VoiceMapping] Failed to parse: {ex.Message}");
                voiceMapping = null;
            }
        }

        /// <summary>
        /// Minimal JSON parser for { "key": { "key": "value", ... }, ... } structure.
        /// JsonUtility can't handle Dictionary so we parse manually.
        /// </summary>
        static Dictionary<string, Dictionary<string, string>> ParseVoiceMappingJson(string json)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();

            // Use Unity's JSON parser by wrapping: we parse token by token
            // Simple approach: split by top-level entries using brace counting
            int i = 0;
            void SkipWhitespace() { while (i < json.Length && char.IsWhiteSpace(json[i])) i++; }

            string ReadString()
            {
                if (json[i] != '"') throw new FormatException($"Expected '\"' at {i}");
                i++; // skip opening quote
                int start = i;
                while (i < json.Length && json[i] != '"')
                {
                    if (json[i] == '\\') i++; // skip escaped char
                    i++;
                }
                string val = json.Substring(start, i - start)
                    .Replace("\\\\", "\x01")
                    .Replace("\\\"", "\"")
                    .Replace("\\n", "\n")
                    .Replace("\\t", "\t")
                    .Replace("\x01", "\\");
                i++; // skip closing quote
                return val;
            }

            SkipWhitespace();
            if (json[i] != '{') throw new FormatException("Expected '{'");
            i++;

            while (true)
            {
                SkipWhitespace();
                if (i >= json.Length || json[i] == '}') break;
                if (json[i] == ',') { i++; SkipWhitespace(); }
                if (i >= json.Length || json[i] == '}') break;

                string paintingName = ReadString();
                SkipWhitespace();
                if (json[i] != ':') throw new FormatException("Expected ':'");
                i++;
                SkipWhitespace();

                // Parse inner object
                var inner = new Dictionary<string, string>();
                if (json[i] != '{') throw new FormatException("Expected '{'");
                i++;

                while (true)
                {
                    SkipWhitespace();
                    if (i >= json.Length || json[i] == '}') break;
                    if (json[i] == ',') { i++; SkipWhitespace(); }
                    if (i >= json.Length || json[i] == '}') break;

                    string voiceType = ReadString();
                    SkipWhitespace();
                    if (json[i] != ':') throw new FormatException("Expected ':'");
                    i++;
                    SkipWhitespace();
                    string wavPath = ReadString();
                    inner[voiceType] = wavPath;
                }

                if (i < json.Length && json[i] == '}') i++;
                result[paintingName] = inner;
            }

            return result;
        }

        /// <summary>
        /// Look up touch voice paths for a painting name from the voice mapping.
        /// </summary>
        List<string> GetVoicePaths(string paintingName)
        {
            if (voiceMapping == null) return new List<string>();

            // Try exact name, then without _static suffix
            if (!voiceMapping.TryGetValue(paintingName, out var voices))
            {
                if (paintingName.EndsWith("_static"))
                {
                    string baseName = paintingName.Substring(0, paintingName.Length - "_static".Length);
                    voiceMapping.TryGetValue(baseName, out voices);
                }
            }

            if (voices == null) return new List<string>();

            // Return touch voices first, then home/main as fallback
            var result = new List<string>();
            string[] preferred = { "touch_1", "touch_2", "touch_3", "main_1", "main_2", "main_3", "home", "detail" };
            foreach (string vt in preferred)
            {
                if (voices.TryGetValue(vt, out string wavPath) && File.Exists(wavPath))
                    result.Add(wavPath);
            }

            return result;
        }
    }
}
