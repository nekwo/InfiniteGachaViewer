using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace NikkeViewerEX.UI
{
    public partial class NikkeBrowserPanel
    {
        // Config tab elements
        TextField jsonPathInput;
        TextField assetsFolderInput;
        TextField thumbnailsFolderInput;
        TextField backgroundsFolderInput;
        TextField bgmPathInput;
        TextField alJsonPathInput;
        TextField alAssetsFolderInput;
        Button loadButton;
        Label statusText;

        void QueryConfigElements()
        {
            jsonPathInput = root.Q<TextField>("json-path-input");
            assetsFolderInput = root.Q<TextField>("assets-folder-input");
            thumbnailsFolderInput = root.Q<TextField>("thumbnails-folder-input");
            backgroundsFolderInput = root.Q<TextField>("backgrounds-folder-input");
            bgmPathInput = root.Q<TextField>("bgm-path-input");
            alJsonPathInput = root.Q<TextField>("al-json-path-input");
            alAssetsFolderInput = root.Q<TextField>("al-assets-folder-input");
            loadButton = root.Q<Button>("load-button");
            statusText = root.Q<Label>("status-text");
        }

        void BindConfigEvents()
        {
            root.Q<Button>("browse-json").clicked += () => BrowseJsonFile().Forget();
            root.Q<Button>("browse-assets").clicked += () => BrowseAssetsFolder().Forget();
            root.Q<Button>("browse-thumbnails").clicked += () => BrowseThumbnailsFolder().Forget();
            root.Q<Button>("browse-backgrounds").clicked += () => BrowseBackgroundsFolder().Forget();
            root.Q<Button>("browse-bgm").clicked += () => BrowseBgmFile().Forget();
            root.Q<Button>("browse-al-json").clicked += () => BrowseAlJsonFile().Forget();
            root.Q<Button>("browse-al-assets").clicked += () => BrowseAlAssetsFolder().Forget();
            loadButton.clicked += () => LoadDatabase().Forget();
        }

        void RestoreConfig()
        {
            string jsonPath = settingsManager.NikkeSettings.DatabaseJsonPath;
            string assetsFolder = settingsManager.NikkeSettings.AssetsFolder;
            string thumbnailsFolder = settingsManager.NikkeSettings.ThumbnailsFolder;

            if (!string.IsNullOrEmpty(jsonPath))
                jsonPathInput.value = jsonPath;
            if (!string.IsNullOrEmpty(assetsFolder))
                assetsFolderInput.value = assetsFolder;
            if (!string.IsNullOrEmpty(thumbnailsFolder))
                thumbnailsFolderInput.value = thumbnailsFolder;
            if (!string.IsNullOrEmpty(settingsManager.NikkeSettings.BackgroundsFolder))
                backgroundsFolderInput.value = settingsManager.NikkeSettings.BackgroundsFolder;
            if (!string.IsNullOrEmpty(settingsManager.NikkeSettings.BgmFolder))
                bgmPathInput.value = settingsManager.NikkeSettings.BgmFolder;
            if (!string.IsNullOrEmpty(settingsManager.NikkeSettings.AzurLaneDatabaseJsonPath))
                alJsonPathInput.value = settingsManager.NikkeSettings.AzurLaneDatabaseJsonPath;
            if (!string.IsNullOrEmpty(settingsManager.NikkeSettings.AzurLaneAssetsFolder))
                alAssetsFolderInput.value = settingsManager.NikkeSettings.AzurLaneAssetsFolder;

            bool hideUI = settingsManager.NikkeSettings.HideUI;
            isHoverModeEnabled = hideUI;
            hideUiToggle.SetValueWithoutNotify(hideUI);
            if (hideUI)
            {
                Hide();
                hoverZone.style.opacity = 0;
            }
            else
            {
                hoverZone.style.opacity = 1;
            }

            if (!string.IsNullOrEmpty(jsonPath) && !string.IsNullOrEmpty(assetsFolder))
                LoadDatabase().Forget();
            else if (!string.IsNullOrEmpty(settingsManager.NikkeSettings.AzurLaneAssetsFolder))
                PopulateAzurLaneList();
        }

        async UniTaskVoid BrowseJsonFile()
        {
            string path = await OpenFileBrowser("Select Character Database JSON", new[] { "json" });
            if (!string.IsNullOrEmpty(path))
                jsonPathInput.value = path;
        }

        async UniTaskVoid BrowseAssetsFolder()
        {
            string path = await OpenFolderBrowser("Select Assets Root Folder");
            if (!string.IsNullOrEmpty(path))
                assetsFolderInput.value = path;
        }

        async UniTaskVoid BrowseThumbnailsFolder()
        {
            string path = await OpenFolderBrowser("Select Thumbnails Folder");
            if (!string.IsNullOrEmpty(path))
                thumbnailsFolderInput.value = path;
        }

        async UniTaskVoid BrowseBackgroundsFolder()
        {
            string path = await OpenFolderBrowser("Select Backgrounds Folder");
            if (!string.IsNullOrEmpty(path))
            {
                backgroundsFolderInput.value = path;
                settingsManager.NikkeSettings.BackgroundsFolder = path;
                await settingsManager.SaveSettings();
            }
        }

        async UniTaskVoid BrowseBgmFile()
        {
            string path = await OpenFolderBrowser("Select BGM Folder");
            if (!string.IsNullOrEmpty(path))
            {
                bgmPathInput.value = path;
                settingsManager.NikkeSettings.BgmFolder = path;
                await settingsManager.SaveSettings();
            }
        }

        async UniTaskVoid BrowseAlJsonFile()
        {
            string path = await OpenFileBrowser("Select Azur Lane Database JSON", new[] { "json" });
            if (!string.IsNullOrEmpty(path))
                alJsonPathInput.value = path;
        }

        async UniTaskVoid BrowseAlAssetsFolder()
        {
            string path = await OpenFolderBrowser("Select Azur Lane Assets Root Folder");
            if (!string.IsNullOrEmpty(path))
                alAssetsFolderInput.value = path;
        }

        async UniTask LoadDatabase()
        {
            string jsonPath = jsonPathInput.value;
            string assetsFolder = assetsFolderInput.value;

            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                SetStatus("Please set the JSON database path.", true);
                return;
            }
            if (string.IsNullOrWhiteSpace(assetsFolder))
            {
                SetStatus("Please set the assets folder path.", true);
                return;
            }
            if (!File.Exists(jsonPath))
            {
                SetStatus($"JSON file not found: {jsonPath}", true);
                return;
            }
            if (!Directory.Exists(assetsFolder))
            {
                SetStatus($"Assets folder not found: {assetsFolder}", true);
                return;
            }

            settingsManager.NikkeSettings.DatabaseJsonPath = jsonPath;
            settingsManager.NikkeSettings.AssetsFolder = assetsFolder;
            settingsManager.NikkeSettings.ThumbnailsFolder = thumbnailsFolderInput.value;
            settingsManager.NikkeSettings.BackgroundsFolder = backgroundsFolderInput.value;
            settingsManager.NikkeSettings.BgmFolder = bgmPathInput.value;
            settingsManager.NikkeSettings.AzurLaneDatabaseJsonPath = alJsonPathInput.value;
            settingsManager.NikkeSettings.AzurLaneAssetsFolder = alAssetsFolderInput.value;
            await settingsManager.SaveSettings();

            try
            {
                SetStatus("Loading database...", false);
                string json = await File.ReadAllTextAsync(jsonPath);
                database = NikkeViewerEX.Serialization.NikkeDatabaseParser.Parse(json);
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to parse JSON: {ex.Message}", true);
                return;
            }

            resolvedAssets.Clear();
            int withAssets = 0;
            foreach (var entry in database)
            {
                var info = NikkeViewerEX.Utils.CharacterAssetResolver.Resolve(assetsFolder, entry.id);
                resolvedAssets[entry.id] = info;
                if (info.IsValid)
                    withAssets++;
            }

            if (withAssets == 0 && database.Length > 0)
            {
                var first = database[0];
                var firstInfo = resolvedAssets[first.id];
                string charFolder = Path.Combine(assetsFolder, first.id);
                Debug.Log($"[NikkeBrowser] Debug - folder: {charFolder} (exists={Directory.Exists(charFolder)}), skel: {firstInfo.SkelPath ?? "null"}, atlas: {firstInfo.AtlasPath ?? "null"}");
            }

            int totalVariations = 0;
            foreach (var a in resolvedAssets.Values)
                totalVariations += Math.Max(0, a.VariationCount - 1);

            SetStatus($"Loaded {database.Length} characters ({withAssets} with assets, {totalVariations} texture variations)", false);
            statusText.RemoveFromClassList("status-error");
            statusText.AddToClassList("status-success");

            PopulateBrowserList();
            PopulateAzurLaneList();
            SwitchTab(BrowserTab.Browser);
        }

        void SetStatus(string message, bool isError)
        {
            statusText.text = message;
            statusText.RemoveFromClassList("status-error");
            statusText.RemoveFromClassList("status-success");
            if (isError)
                statusText.AddToClassList("status-error");
        }
    }
}
