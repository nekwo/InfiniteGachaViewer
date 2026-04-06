using System;
using System.Collections.Generic;
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
        TextField presetNameInput;
        ScrollView presetList;
        VisualElement presetEmpty;

        void QueryPresetElements()
        {
            presetNameInput = root.Q<TextField>("preset-name-input");
            presetList = root.Q<ScrollView>("preset-list");
            presetEmpty = root.Q("preset-empty");
        }

        void BindPresetEvents()
        {
            root.Q<Button>("preset-save-button").clicked += SaveCurrentPreset;
        }

        void SaveCurrentPreset()
        {
            string name = presetNameInput.value?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var settings = settingsManager.NikkeSettings;

            var snapshot = new List<Nikke>();
            foreach (var nikke in settings.NikkeList)
                snapshot.Add(JsonUtility.FromJson<Nikke>(JsonUtility.ToJson(nikke)));

            var alSnapshot = new List<AzurLaneCharacter>();
            foreach (var alChar in settings.AzurLaneList)
                alSnapshot.Add(JsonUtility.FromJson<AzurLaneCharacter>(JsonUtility.ToJson(alChar)));

            var preset = new NikkePreset
            {
                Name = name,
                NikkeList = snapshot,
                AzurLaneList = alSnapshot,
                BackgroundImage = settings.BackgroundImage,
                BackgroundScale = settings.BackgroundScale,
                BackgroundPanX = settings.BackgroundPanX,
                BackgroundPanY = settings.BackgroundPanY,
                BackgroundMusic = settings.BackgroundMusic,
                BackgroundMusicVolume = settings.BackgroundMusicVolume,
                BackgroundMusicPlaying = settings.BackgroundMusicPlaying,
            };

            settings.Presets.Add(preset);
            settingsManager.SaveSettings().Forget();

            presetNameInput.value = "";
            RefreshPresetList();
        }

        void RefreshPresetList()
        {
            presetList.Clear();
            var presets = settingsManager.NikkeSettings.Presets;

            if (presets.Count == 0)
            {
                presetEmpty.style.display = DisplayStyle.Flex;
                presetList.style.display = DisplayStyle.None;
                return;
            }

            presetEmpty.style.display = DisplayStyle.None;
            presetList.style.display = DisplayStyle.Flex;

            foreach (var preset in presets)
            {
                var item = new VisualElement();
                item.AddToClassList("preset-item");

                var info = new VisualElement();
                info.AddToClassList("preset-info");

                var nameLabel = new Label(preset.Name);
                nameLabel.AddToClassList("preset-name");

                int count = preset.NikkeList.Count + preset.AzurLaneList.Count;
                string details = $"{count} character{(count != 1 ? "s" : "")}";
                if (!string.IsNullOrEmpty(preset.BackgroundImage))
                    details += " | BG";
                if (!string.IsNullOrEmpty(preset.BackgroundMusic))
                    details += " | Music";
                var countLabel = new Label(details);
                countLabel.AddToClassList("preset-count");

                info.Add(nameLabel);
                info.Add(countLabel);

                var actions = new VisualElement();
                actions.AddToClassList("preset-actions");

                var loadBtn = new Button { text = "Load" };
                loadBtn.AddToClassList("preset-load-button");

                var deleteBtn = new Button { text = "✕" };
                deleteBtn.AddToClassList("preset-delete-button");

                NikkePreset captured = preset;
                loadBtn.clicked += () => LoadPreset(captured).Forget();
                deleteBtn.clicked += () =>
                {
                    settingsManager.NikkeSettings.Presets.Remove(captured);
                    settingsManager.SaveSettings().Forget();
                    RefreshPresetList();
                };

                actions.Add(loadBtn);
                actions.Add(deleteBtn);

                item.Add(info);
                item.Add(actions);
                presetList.Add(item);
            }
        }

        async UniTask LoadPreset(NikkePreset preset)
        {
            // Destroy all active viewers
            var allViewers = FindObjectsByType<NikkeViewerBase>(FindObjectsSortMode.None);
            foreach (var viewer in allViewers)
            {
                if (viewer.NikkeNameText != null)
                    Destroy(viewer.NikkeNameText.gameObject);
                Destroy(viewer.gameObject);
            }
            activeViewers.Clear();
            currentVariation.Clear();
            settingsManager.NikkeSettings.NikkeList.Clear();
            settingsManager.NikkeSettings.AzurLaneList.Clear();

            await UniTask.NextFrame();

            // Restore background
            var settings = settingsManager.NikkeSettings;
            settings.BackgroundImage = preset.BackgroundImage;
            settings.BackgroundScale = preset.BackgroundScale;
            settings.BackgroundPanX = preset.BackgroundPanX;
            settings.BackgroundPanY = preset.BackgroundPanY;
            settingsManager.BackgroundImageInput.text = preset.BackgroundImage ?? "";
            settingsManager.ApplySettings();

            // Restore music
            settings.BackgroundMusic = preset.BackgroundMusic;
            settings.BackgroundMusicVolume = preset.BackgroundMusicVolume;
            settings.BackgroundMusicPlaying = preset.BackgroundMusicPlaying;
            settingsManager.BackgroundMusicAudio.volume = preset.BackgroundMusicVolume;
            settingsManager.BackgroundMusicInput.text = preset.BackgroundMusic ?? "";

            if (!string.IsNullOrEmpty(preset.BackgroundMusic))
            {
                var clip = await WebRequestHelper.GetAudioClip(preset.BackgroundMusic);
                if (clip != null)
                {
                    settingsManager.BackgroundMusicAudio.clip = clip;
                    if (preset.BackgroundMusicPlaying)
                        settingsManager.BackgroundMusicAudio.Play();
                    else
                        settingsManager.BackgroundMusicAudio.Pause();
                }
            }
            else
            {
                settingsManager.BackgroundMusicAudio.Stop();
                settingsManager.BackgroundMusicAudio.clip = null;
            }

            // Restore characters
            var newList = new List<Nikke>();

            foreach (var savedNikke in preset.NikkeList)
            {
                try
                {
                    NikkeViewerBase viewer = await mainControl.InstantiateViewer(savedNikke.SkelPath);
                    if (viewer == null) continue;

                    Nikke nikkeData = JsonUtility.FromJson<Nikke>(JsonUtility.ToJson(savedNikke));
                    viewer.NikkeData = nikkeData;
                    viewer.gameObject.transform.position = nikkeData.Position;
                    viewer.gameObject.transform.localScale = nikkeData.Scale;
                    viewer.name = string.IsNullOrEmpty(nikkeData.NikkeName)
                        ? nikkeData.AssetName
                        : nikkeData.NikkeName;

                    if (nikkeData.VoicesPath.Count > 0)
                    {
                        var clips = new List<AudioClip>();
                        foreach (string path in nikkeData.VoicesPath)
                        {
                            var clip = await WebRequestHelper.GetAudioClip(path);
                            if (clip != null) clips.Add(clip);
                        }
                        viewer.TouchVoices = clips;
                    }

                    viewer.TriggerSpawn();
                    activeViewers[nikkeData.InstanceId] = viewer;
                    newList.Add(nikkeData);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to load '{savedNikke.AssetName}' from preset '{preset.Name}': {ex.Message}");
                }
            }

            settings.NikkeList = newList;

            // Restore Azur Lane characters
            var newAlList = new List<AzurLaneCharacter>();

            foreach (var savedAlChar in preset.AzurLaneList)
            {
                try
                {
                    AzurLaneViewer alViewer = mainControl.InstantiateAzurLaneViewer();
                    if (alViewer == null) continue;

                    var character = JsonUtility.FromJson<AzurLaneCharacter>(JsonUtility.ToJson(savedAlChar));
                    alViewer.AlCharacterData = character;
                    alViewer.NikkeData.InstanceId = character.InstanceId;
                    alViewer.name = character.DisplayName;
                    alViewer.gameObject.transform.position = character.Position;
                    alViewer.gameObject.transform.localScale = character.Scale;

                    if (character.VoicesPath.Count > 0)
                    {
                        var clips = new List<AudioClip>();
                        foreach (string path in character.VoicesPath)
                        {
                            var clip = await WebRequestHelper.GetAudioClip(path);
                            if (clip != null) clips.Add(clip);
                        }
                        alViewer.TouchVoices = clips;
                    }

                    alViewer.TriggerSpawn();
                    activeViewers[character.InstanceId] = alViewer;
                    newAlList.Add(character);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to load AL '{savedAlChar.AssetName}' from preset '{preset.Name}': {ex.Message}");
                }
            }

            settings.AzurLaneList = newAlList;
            await settingsManager.SaveSettings();

            UpdateBrowserCount();
            PopulateBrowserList();
        }
    }
}
