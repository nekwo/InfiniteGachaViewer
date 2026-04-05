using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NikkeViewerEX.Serialization;
using NikkeViewerEX.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace NikkeViewerEX.UI
{
    public partial class NikkeBrowserPanel
    {
        // Active tab elements
        Label activeCount;
        ScrollView activeList;
        VisualElement activeEmpty;

        VisualElement activeBgPreview;
        Label activeBgEmptyLabel;
        Slider activeBgSlider;
        Label activeBgScaleValue;
        Slider activeBgPanX;
        Label activeBgPanXValue;
        Slider activeBgPanY;
        Label activeBgPanYValue;

        // Music controls in active tab
        Label activeMusicName;
        Button activeMusicPlayPause;
        Slider activeMusicVolume;
        Label activeMusicVolumeValue;

        void QueryActiveElements()
        {
            activeCount = root.Q<Label>("active-count");
            activeList = root.Q<ScrollView>("active-list");
            activeEmpty = root.Q("active-empty");

            activeBgPreview = root.Q("active-bg-preview");
            activeBgEmptyLabel = root.Q<Label>("active-bg-empty-label");
            activeBgSlider = root.Q<Slider>("active-bg-slider");
            activeBgScaleValue = root.Q<Label>("active-bg-scale-value");
            activeBgPanX = root.Q<Slider>("active-bg-pan-x");
            activeBgPanXValue = root.Q<Label>("active-bg-panx-value");
            activeBgPanY = root.Q<Slider>("active-bg-pan-y");
            activeBgPanYValue = root.Q<Label>("active-bg-pany-value");

            activeMusicName = root.Q<Label>("active-music-name");
            activeMusicPlayPause = root.Q<Button>("active-music-playpause");
            activeMusicVolume = root.Q<Slider>("active-music-volume");
            activeMusicVolumeValue = root.Q<Label>("active-music-volume-value");
        }

        void BindActiveEvents()
        {
            activeMusicPlayPause.clicked += ToggleMusicPlayPause;
            activeMusicVolume.RegisterValueChangedCallback(evt =>
            {
                float vol = evt.newValue;
                activeMusicVolumeValue.text = $"{Mathf.RoundToInt(vol * 100)}%";
                settingsManager.BackgroundMusicAudio.volume = vol;
                settingsManager.NikkeSettings.BackgroundMusicVolume = vol;
                SaveSettingsDebounced();
            });

            activeBgSlider.RegisterValueChangedCallback(evt =>
            {
                float val = evt.newValue;
                activeBgScaleValue.text = $"{val:F1}x";
                settingsManager.BackgroundImage.transform.localScale = Vector3.one * val;
                settingsManager.NikkeSettings.BackgroundScale = val;
                SaveSettingsDebounced();
            });

            activeBgPanX.RegisterValueChangedCallback(evt =>
            {
                float val = Mathf.Round(evt.newValue);
                activeBgPanXValue.text = $"{val:F0}";
                var pos = settingsManager.BackgroundImage.rectTransform.anchoredPosition;
                settingsManager.BackgroundImage.rectTransform.anchoredPosition = new Vector2(val, pos.y);
                settingsManager.NikkeSettings.BackgroundPanX = val;
                SaveSettingsDebounced();
            });

            activeBgPanY.RegisterValueChangedCallback(evt =>
            {
                float val = Mathf.Round(evt.newValue);
                activeBgPanYValue.text = $"{val:F0}";
                var pos = settingsManager.BackgroundImage.rectTransform.anchoredPosition;
                settingsManager.BackgroundImage.rectTransform.anchoredPosition = new Vector2(pos.x, val);
                settingsManager.NikkeSettings.BackgroundPanY = val;
                SaveSettingsDebounced();
            });
        }

        public void RefreshActiveList()
        {
            RebuildActiveViewers();
            RefreshBackgroundPreview();
            RefreshActiveMusicInfo();

            activeList.Clear();
            var nikkeList = settingsManager.NikkeSettings.NikkeList;
            var alList = settingsManager.NikkeSettings.AzurLaneList;
            int totalCount = nikkeList.Count + alList.Count;

            if (totalCount == 0)
            {
                activeEmpty.style.display = DisplayStyle.Flex;
                activeList.style.display = DisplayStyle.None;
                activeCount.text = "0 characters active";
                return;
            }

            activeEmpty.style.display = DisplayStyle.None;
            activeList.style.display = DisplayStyle.Flex;
            activeCount.text = $"{totalCount} characters active";

            foreach (Nikke nikke in nikkeList)
            {
                VisualElement item = m_ActiveItemTemplate.Instantiate();

                int instanceCount = 0;
                foreach (var n in nikkeList)
                    if (n.AssetName == nikke.AssetName) instanceCount++;
                string displayName = string.IsNullOrEmpty(nikke.NikkeName)
                    ? nikke.AssetName
                    : nikke.NikkeName;
                if (instanceCount > 1)
                    displayName += $" #{nikke.InstanceId}";
                item.Q<Label>("character-name").text = displayName;
                item.Q<Label>("character-id").text = nikke.AssetName;

                var dbEntry = database?.FirstOrDefault(e => e.id == nikke.AssetName);
                resolvedAssets.TryGetValue(nikke.AssetName, out CharacterAssetInfo assetInfo);

                string meta = dbEntry?.VersionLabel ?? "";
                if (assetInfo != null && assetInfo.VariationCount > 1)
                {
                    currentVariation.TryGetValue(nikke.AssetName, out int cur);
                    meta += $" | Texture {cur + 1}/{assetInfo.VariationCount}";
                }
                if (nikke.Poses.Count > 1)
                    meta += $" | Pose: {nikke.ActivePose}";
                item.Q<Label>("character-version").text = meta;

                VisualElement poseContainer = item.Q("pose-buttons");
                if (nikke.Poses.Count > 1)
                {
                    foreach (var pose in nikke.Poses)
                    {
                        var poseBtn = new Button { text = pose.PoseType.ToString() };
                        poseBtn.AddToClassList("pose-button");
                        if (pose.PoseType == nikke.ActivePose)
                            poseBtn.AddToClassList("pose-active");

                        NikkePoseType poseType = pose.PoseType;
                        int capturedInstanceId = nikke.InstanceId;
                        poseBtn.clicked += () =>
                        {
                            if (activeViewers.TryGetValue(capturedInstanceId, out var viewer))
                            {
                                viewer.SetActivePose(poseType);
                                RefreshActiveList();
                            }
                        };
                        poseContainer.Add(poseBtn);
                    }
                }
                else
                {
                    poseContainer.style.display = DisplayStyle.None;
                }

                int instanceId = nikke.InstanceId;

                // Scale slider
                var scaleSlider = item.Q<Slider>("active-scale-slider");
                var scaleValueLabel = item.Q<Label>("active-scale-value");
                float currentScale = nikke.Scale.x;
                scaleSlider.SetValueWithoutNotify(currentScale);
                scaleValueLabel.text = $"{currentScale:F1}x";
                scaleSlider.RegisterValueChangedCallback(evt =>
                {
                    float val = evt.newValue;
                    scaleValueLabel.text = $"{val:F1}x";
                    if (activeViewers.TryGetValue(instanceId, out var viewer))
                    {
                        Vector3 newScale = Vector3.one * val;
                        viewer.transform.localScale = newScale;
                        viewer.NikkeData.Scale = newScale;
                        viewer.OnNikkeDataChanged();
                        SaveSettingsDebounced();
                    }
                });

                // Brightness slider
                var brightnessSlider = item.Q<Slider>("active-brightness-slider");
                var brightnessValueLabel = item.Q<Label>("active-brightness-value");
                float currentBrightness = nikke.Brightness;
                brightnessSlider.SetValueWithoutNotify(currentBrightness);
                brightnessValueLabel.text = $"{currentBrightness:F1}x";
                brightnessSlider.RegisterValueChangedCallback(evt =>
                {
                    float val = evt.newValue;
                    brightnessValueLabel.text = $"{val:F1}x";
                    if (activeViewers.TryGetValue(instanceId, out var viewer))
                    {
                        viewer.NikkeData.Brightness = val;
                        viewer.ApplyBrightness(val);
                        SaveSettingsDebounced();
                    }
                });

                // Show name toggle (inverted: checked = visible, unchecked = hidden)
                var hideNameToggle = item.Q<Toggle>("toggle-checkbox");
                hideNameToggle.SetValueWithoutNotify(!nikke.HideName);
                hideNameToggle.RegisterValueChangedCallback(evt =>
                {
                    if (activeViewers.TryGetValue(instanceId, out var viewer))
                    {
                        viewer.NikkeData.HideName = !evt.newValue;
                        viewer.OnNikkeDataChanged();
                        viewer.EnsureNameText();
                        viewer.ToggleDisplayName(false);
                        settingsManager.SaveSettings().Forget();
                    }
                });

                // Lock toggle
                var lockToggle = item.Q<Toggle>("lock-checkbox");
                lockToggle.SetValueWithoutNotify(nikke.Lock);
                lockToggle.RegisterValueChangedCallback(evt =>
                {
                    if (activeViewers.TryGetValue(instanceId, out var viewer))
                    {
                        viewer.NikkeData.Lock = evt.newValue;
                        viewer.OnNikkeDataChanged();
                        settingsManager.SaveSettings().Forget();
                    }
                });

                item.Q<Button>("remove-button").clicked += () =>
                {
                    RemoveCharacter(instanceId).Forget();
                };

                activeList.Add(item);
            }

            // Azur Lane characters
            foreach (AzurLaneCharacter alChar in alList)
            {
                VisualElement item = m_ActiveItemTemplate.Instantiate();

                string displayName = string.IsNullOrEmpty(alChar.DisplayName)
                    ? alChar.AssetName
                    : alChar.DisplayName;
                item.Q<Label>("character-name").text = displayName;
                item.Q<Label>("character-id").text = alChar.AssetName;
                item.Q<Label>("character-version").text = "Live2D";
                item.Q("pose-buttons").style.display = DisplayStyle.None;

                int instanceId = alChar.InstanceId;

                // Scale slider
                var scaleSlider = item.Q<Slider>("active-scale-slider");
                var scaleValueLabel = item.Q<Label>("active-scale-value");
                float currentScale = alChar.Scale.x;
                scaleSlider.SetValueWithoutNotify(currentScale);
                scaleValueLabel.text = $"{currentScale:F1}x";
                scaleSlider.RegisterValueChangedCallback(evt =>
                {
                    float val = evt.newValue;
                    scaleValueLabel.text = $"{val:F1}x";
                    if (activeViewers.TryGetValue(instanceId, out var viewer))
                    {
                        Vector3 newScale = Vector3.one * val;
                        viewer.transform.localScale = newScale;
                        viewer.NikkeData.Scale = newScale;
                        viewer.OnNikkeDataChanged();
                        SaveSettingsDebounced();
                    }
                });

                // Brightness slider
                var brightnessSlider = item.Q<Slider>("active-brightness-slider");
                var brightnessValueLabel = item.Q<Label>("active-brightness-value");
                float currentBrightness = alChar.Brightness;
                brightnessSlider.SetValueWithoutNotify(currentBrightness);
                brightnessValueLabel.text = $"{currentBrightness:F1}x";
                brightnessSlider.RegisterValueChangedCallback(evt =>
                {
                    float val = evt.newValue;
                    brightnessValueLabel.text = $"{val:F1}x";
                    if (activeViewers.TryGetValue(instanceId, out var viewer))
                    {
                        alChar.Brightness = val;
                        viewer.ApplyBrightness(val);
                        SaveSettingsDebounced();
                    }
                });

                // Show name toggle
                var hideNameToggle = item.Q<Toggle>("toggle-checkbox");
                hideNameToggle.SetValueWithoutNotify(!alChar.HideName);
                hideNameToggle.RegisterValueChangedCallback(evt =>
                {
                    if (activeViewers.TryGetValue(instanceId, out var viewer))
                    {
                        viewer.NikkeData.HideName = !evt.newValue;
                        viewer.OnNikkeDataChanged();
                        viewer.EnsureNameText();
                        viewer.ToggleDisplayName(false);
                        settingsManager.SaveSettings().Forget();
                    }
                });

                // Lock toggle
                var lockToggle = item.Q<Toggle>("lock-checkbox");
                lockToggle.SetValueWithoutNotify(alChar.Lock);
                lockToggle.RegisterValueChangedCallback(evt =>
                {
                    if (activeViewers.TryGetValue(instanceId, out var viewer))
                    {
                        viewer.NikkeData.Lock = evt.newValue;
                        viewer.OnNikkeDataChanged();
                        settingsManager.SaveSettings().Forget();
                    }
                });

                item.Q<Button>("remove-button").clicked += () =>
                {
                    RemoveAzurLaneCharacter(instanceId).Forget();
                };

                activeList.Add(item);
            }

            tabActiveBtn.text = $"Active ({totalCount})";
        }

        void RefreshActiveMusicInfo()
        {
            string musicPath = settingsManager.NikkeSettings.BackgroundMusic;
            bool hasMusic = !string.IsNullOrEmpty(musicPath);

            activeMusicName.text = hasMusic
                ? System.IO.Path.GetFileNameWithoutExtension(musicPath)
                : "No track selected";

            bool isPlaying = settingsManager.NikkeSettings.BackgroundMusicPlaying;
            activeMusicPlayPause.text = isPlaying ? "Pause" : "Play";
            activeMusicPlayPause.SetEnabled(hasMusic);

            float vol = settingsManager.NikkeSettings.BackgroundMusicVolume;
            activeMusicVolume.SetValueWithoutNotify(vol);
            activeMusicVolumeValue.text = $"{Mathf.RoundToInt(vol * 100)}%";
        }

        void ToggleMusicPlayPause()
        {
            bool playing = settingsManager.NikkeSettings.BackgroundMusicPlaying;
            bool newState = !playing;
            settingsManager.NikkeSettings.BackgroundMusicPlaying = newState;

            if (newState)
                settingsManager.BackgroundMusicAudio.UnPause();
            else
                settingsManager.BackgroundMusicAudio.Pause();

            activeMusicPlayPause.text = newState ? "Pause" : "Play";
            settingsManager.SaveSettings().Forget();
        }

        void RefreshBackgroundPreview()
        {
            var sprite = settingsManager.BackgroundImage.sprite;
            bool hasBackground = sprite != null;

            if (hasBackground)
            {
                activeBgPreview.style.backgroundImage = new StyleBackground(sprite.texture);
                activeBgEmptyLabel.style.display = DisplayStyle.None;
                activeBgPreview.RemoveFromClassList("active-bg-empty");
            }
            else
            {
                activeBgPreview.style.backgroundImage = StyleKeyword.None;
                activeBgEmptyLabel.style.display = DisplayStyle.Flex;
                activeBgPreview.AddToClassList("active-bg-empty");
            }

            var settings = settingsManager.NikkeSettings;

            float scale = settings.BackgroundScale;
            activeBgSlider.SetValueWithoutNotify(scale);
            activeBgScaleValue.text = $"{scale:F1}x";

            float panX = settings.BackgroundPanX;
            activeBgPanX.SetValueWithoutNotify(panX);
            activeBgPanXValue.text = $"{panX:F0}";

            float panY = settings.BackgroundPanY;
            activeBgPanY.SetValueWithoutNotify(panY);
            activeBgPanYValue.text = $"{panY:F0}";
        }
    }
}
