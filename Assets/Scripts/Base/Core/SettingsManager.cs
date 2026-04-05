using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NikkeViewerEX.Components;
using NikkeViewerEX.Serialization;
using NikkeViewerEX.UI;
using NikkeViewerEX.Utils;
using SimpleFileBrowser;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace NikkeViewerEX.Core
{
    /// <summary>
    /// The component that manage settings.
    /// </summary>
    [AddComponentMenu("Nikke Viewer EX/Core/Settings Manager")]
    [RequireComponent(typeof(MainControl))]
    public class SettingsManager : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField, FormerlySerializedAs("BackgroundImageInput")] TMP_InputField m_BackgroundImageInput;
        [SerializeField, FormerlySerializedAs("BackgroundMusicInput")] TMP_InputField m_BackgroundMusicInput;
        [SerializeField, FormerlySerializedAs("BackgroundMusicVolumeSlider")] Slider m_BackgroundMusicVolumeSlider;
        [SerializeField, FormerlySerializedAs("BackgroundMusicStateToggle")] Toggle m_BackgroundMusicStateToggle;

        [Space]
        [SerializeField, FormerlySerializedAs("BackgroundImage")] Image m_BackgroundImage;
        [SerializeField, FormerlySerializedAs("BackgroundMusicAudio")] AudioSource m_BackgroundMusicAudio;

        [Space]
        [SerializeField, FormerlySerializedAs("FPSDropdown")] TMP_Dropdown m_FPSDropdown;

        public TMP_InputField BackgroundImageInput => m_BackgroundImageInput;
        public TMP_InputField BackgroundMusicInput => m_BackgroundMusicInput;
        public Image BackgroundImage => m_BackgroundImage;
        public AudioSource BackgroundMusicAudio => m_BackgroundMusicAudio;

        [Header("Settings")]
        /// <summary>
        /// Settings file name.
        /// </summary>
        [SerializeField]
        string m_SettingsFile = "settings.json";

        /// <summary>
        /// Cache directory name.
        /// </summary>
        [SerializeField]
        string m_CachedDataDirectoryName = "cache";

        [SerializeField, FormerlySerializedAs("SupportedAudioFiles")] string[] m_SupportedAudioFiles = { ".mp3", ".ogg", ".wav", ".flac" };
        public string[] SupportedAudioFiles => m_SupportedAudioFiles;

        /// <summary>
        /// Settings data.
        /// </summary>
        /// <returns></returns>
        public NikkeSettings NikkeSettings { get; set; } = new();

        /// <summary>
        /// Cache data directory.
        /// </summary>
        /// <value></value>
        public string CachedDataDirectory { get; set; }

        /// <summary>
        /// Event invoked after the settings loaded.
        /// </summary>
        public event Action OnSettingsLoaded;

        string settingsFilePath;
        MainControl mainControl;

        #region Initialization
        private void Awake()
        {
            mainControl = GetComponent<MainControl>();
            m_FPSDropdown.onValueChanged.AddListener(SetFrameRate);
            m_BackgroundMusicStateToggle.onValueChanged.AddListener(TogglePauseBGM);
            AwakeAsync().Forget();
        }

        private async UniTaskVoid AwakeAsync()
        {
            await Setup();
        }

        private void OnDestroy()
        {
            m_FPSDropdown.onValueChanged.RemoveListener(SetFrameRate);
            m_BackgroundMusicStateToggle.onValueChanged.RemoveListener(TogglePauseBGM);
            OnDestroyAsync().Forget();
        }

        private async UniTaskVoid OnDestroyAsync()
        {
            await SaveSettings();
        }

        private async UniTask Setup()
        {
            settingsFilePath = Path.Combine(StorageHelper.GetApplicationPath(), m_SettingsFile);
            CachedDataDirectory = Path.Combine(
                StorageHelper.GetApplicationPath(),
                Directory.CreateDirectory(m_CachedDataDirectoryName).Name
            );

            if (File.Exists(settingsFilePath))
            {
                NikkeSettings = await LoadSettings() ?? new NikkeSettings();
                if (NikkeSettings != null)
                    await LoadSaveData();
            }
        }

        private async UniTask LoadSaveData()
        {
            Debug.Log("Loading settings...");
            mainControl.WelcomePanel.gameObject.SetActive(NikkeSettings.IsFirstTime);
            mainControl.ToggleHideUI(NikkeSettings.HideUI);

            // Set framerate
            for (int i = 0; i < m_FPSDropdown.options.Count; i++)
            {
                string dropdownValue = m_FPSDropdown.options[i].text;
                if (dropdownValue.Equals(NikkeSettings.FPS))
                    SetFrameRate(i);
            }

            await LoadBackgroundSettings();
            await LoadNikkeData();
            await LoadAzurLaneData();
            
            // Refresh the active list count after loading all characters
            var panels = FindObjectsByType<UI.NikkeBrowserPanel>(FindObjectsSortMode.None);
            if (panels.Length > 0)
            {
                panels[0].RefreshActiveList();
            }
        }

        /// <summary>
        /// Load every Nikke data in Nikke List.
        /// </summary>
        /// <returns></returns>
        private async UniTask LoadNikkeData()
        {
            foreach (Nikke nikkeData in NikkeSettings.NikkeList)
            {
                NikkeListItem item = mainControl.AddNikkeListItem();
                UpdateNikkeListItemUI(item, nikkeData);
                await InitializeViewerData(item, nikkeData);
            }
            OnSettingsLoaded?.Invoke();
            await PopulateSkinDropdowns();
        }

        /// <summary>
        /// Restore every saved Azur Lane viewer from AzurLaneList.
        /// </summary>
        private async UniTask LoadAzurLaneData()
        {
            foreach (AzurLaneCharacter character in NikkeSettings.AzurLaneList)
            {
                AzurLaneViewer viewer = mainControl.InstantiateAzurLaneViewer();
                if (viewer == null) continue;

                viewer.AlCharacterData = character;
                viewer.NikkeData.InstanceId = character.InstanceId;  // set early for RebuildActiveViewers
                viewer.name = character.DisplayName;
                
                if (character.Position == Vector2.zero && Camera.main != null)
                {
                    Vector3 camPos = Camera.main.transform.position;
                    viewer.gameObject.transform.position = camPos + Camera.main.transform.forward * 5f;
                }
                else
                {
                    viewer.gameObject.transform.position = character.Position;
                }
                viewer.gameObject.transform.localScale = character.Scale;

                if (character.VoicesPath.Count > 0)
                {
                    viewer.TouchVoices = (
                        await UniTask.WhenAll(
                            character.VoicesPath.Select(async p => await LoadAudioClip(p))
                        )
                    ).ToList();
                }

                viewer.TriggerSpawn();
            }
        }

        /// <summary>
        /// Load background image settings.
        /// </summary>
        /// <returns></returns>
        private async UniTask LoadBackgroundSettings()
        {
            m_BackgroundImageInput.text = NikkeSettings.BackgroundImage;
            m_BackgroundImage.transform.localScale = Vector3.one * NikkeSettings.BackgroundScale;
            m_BackgroundImage.rectTransform.anchoredPosition = new Vector2(NikkeSettings.BackgroundPanX, NikkeSettings.BackgroundPanY);
            m_BackgroundImage.sprite = !string.IsNullOrEmpty(NikkeSettings.BackgroundImage)
                ? await LoadSprite(NikkeSettings.BackgroundImage)
                : null;

            m_BackgroundMusicInput.text = NikkeSettings.BackgroundMusic;
            m_BackgroundMusicAudio.clip = !string.IsNullOrEmpty(NikkeSettings.BackgroundMusic)
                ? await WebRequestHelper.GetAudioClip(NikkeSettings.BackgroundMusic)
                : null;
            m_BackgroundMusicAudio.loop = true;

            m_BackgroundMusicVolumeSlider.value = m_BackgroundMusicAudio.volume =
                NikkeSettings.BackgroundMusicVolume;
            if (NikkeSettings.BackgroundMusicPlaying && m_BackgroundMusicAudio.clip != null)
            {
                m_BackgroundMusicAudio.Play();
            }
            m_BackgroundMusicStateToggle.isOn = NikkeSettings.BackgroundMusicPlaying;
            TogglePauseBGM(NikkeSettings.BackgroundMusicPlaying);
        }
        #endregion

        #region Runtime
        public void ApplySettings() => ApplySettingsAsync().Forget();

        private async UniTaskVoid ApplySettingsAsync()
        {
            // Set background image
            m_BackgroundImage.sprite = File.Exists(m_BackgroundImageInput.text)
                ? await LoadSprite(m_BackgroundImageInput.text)
                : null;
            NikkeSettings.BackgroundImage = m_BackgroundImageInput.text;
            m_BackgroundImage.transform.localScale = Vector3.one * NikkeSettings.BackgroundScale;
            m_BackgroundImage.rectTransform.anchoredPosition = new Vector2(NikkeSettings.BackgroundPanX, NikkeSettings.BackgroundPanY);

            // Set background music
            if (
                File.Exists(m_BackgroundMusicInput.text)
                && m_BackgroundMusicInput.text != NikkeSettings.BackgroundMusic
            )
            {
                NikkeSettings.BackgroundMusic = m_BackgroundMusicInput.text;
                NikkeSettings.BackgroundMusicPlaying = true;
                m_BackgroundMusicAudio.clip = await WebRequestHelper.GetAudioClip(
                    NikkeSettings.BackgroundMusic
                );
                m_BackgroundMusicAudio.Play();
            }
            Debug.Log("Settings applied.");
        }

        /// <summary>
        /// Update <paramref name="item"/> UI data from <paramref name="nikkeData"/>.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="nikkeData"></param>
        private void UpdateNikkeListItemUI(NikkeListItem item, Nikke nikkeData)
        {
            item.NikkeNameText.text = nikkeData.NikkeName;
            item.SkelPathText.text = nikkeData.SkelPath;
            item.AtlasPathText.text = nikkeData.AtlasPath;
            item.TexturesPathText.text = string.Join(", ", nikkeData.TexturesPath);
            item.VoicesSourceText.text = string.Join(", ", nikkeData.VoicesSource);
        }

        /// <summary>
        /// Update <paramref name="item"/> viewer data from <paramref name="nikkeData"/>.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="nikkeData"></param>
        /// <returns></returns>
        private async UniTask InitializeViewerData(NikkeListItem item, Nikke nikkeData)
        {
            NikkeViewerBase viewer = await mainControl.InstantiateViewer(nikkeData.SkelPath);
            item.Viewer = viewer;
            viewer.NikkeData = nikkeData;
            viewer.gameObject.transform.position = nikkeData.Position;
            viewer.gameObject.transform.localScale = nikkeData.Scale;

            viewer.TouchVoices = (
                await UniTask.WhenAll(
                    nikkeData.VoicesPath.Select(async path => await LoadAudioClip(path))
                )
            ).ToList();

            item.ItemCanvasGroup.interactable = !nikkeData.Lock;
            item.LockButtonToggle.isOn = nikkeData.Lock;
            item.name = viewer.name = string.IsNullOrEmpty(nikkeData.NikkeName)
                ? Path.GetFileNameWithoutExtension(nikkeData.SkelPath)
                : nikkeData.NikkeName;
            item.ScaleValueText.text = $"{nikkeData.Scale.x:#0.0}x";
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        private async UniTask PopulateSkinDropdowns()
        {
            foreach (
                NikkeListItem item in mainControl.NikkeListContent.GetComponentsInChildren<NikkeListItem>()
            )
            {
                await UniTask.WaitUntil(() => item.Viewer.Skins != null);
                item.SkinDropdown.options = item
                    .Viewer.Skins.Select(skin => new TMP_Dropdown.OptionData(skin))
                    .ToList();

                for (int i = 0; i < item.Viewer.Skins.Length; i++)
                {
                    if (item.SkinDropdown.options[i].text == item.Viewer.NikkeData.Skin)
                    {
                        item.Viewer.InvokeChangeSkin(i);
                        item.SkinDropdown.value = i;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Toggle pause the background music.
        /// </summary>
        /// <param name="pause"></param>
        private void TogglePauseBGM(bool pause)
        {
            m_BackgroundMusicStateToggle.isOn = pause;
            m_BackgroundMusicStateToggle.targetGraphic.enabled = !pause;
            NikkeSettings.BackgroundMusicPlaying = pause;
            switch (pause)
            {
                case true:
                    m_BackgroundMusicAudio.UnPause();
                    Debug.Log("BGM Resumed.");
                    break;
                case false:
                    m_BackgroundMusicAudio.Pause();
                    Debug.Log("BGM Paused.");
                    break;
            }
        }
        #endregion

        #region Save & Load Settings
        /// <summary>
        /// Save settings to disk.
        /// </summary>
        /// <returns></returns>
        public async UniTask<string> SaveSettings()
        {
            try
            {
                string settings = JsonUtility.ToJson(NikkeSettings);
                await UniTask.RunOnThreadPool(() =>
                {
                    using FileStream fs =
                        new(
                            settingsFilePath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.ReadWrite
                        );
                    using StreamWriter writer = new(fs);
                    writer.Write(settings);
                });
                return settings;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save the settings at {settingsFilePath}! {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load settings from disk.
        /// </summary>
        /// <returns></returns>
        private async UniTask<NikkeSettings> LoadSettings()
        {
            try
            {
                return JsonUtility.FromJson<NikkeSettings>(
                    await WebRequestHelper.GetTextData(settingsFilePath)
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to get the settings at {settingsFilePath}! {ex.Message}");
                return null;
            }
        }
        #endregion

        #region Utils
        /// <summary>
        /// Load image as Sprite.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private async UniTask<Sprite> LoadSprite(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            Texture2D texture2D = new(1, 1);
            texture2D.LoadImage(await WebRequestHelper.GetBinaryData(path));
            return Sprite.Create(
                texture2D,
                new Rect(0, 0, texture2D.width, texture2D.height),
                new Vector2(0.5f, 0.5f)
            );
        }

        /// <summary>
        /// Set max framerate.
        /// </summary>
        /// <param name="fps"></param>
        /// <returns></returns>
        private void SetAppFrameRate(int fps)
        {
            Application.targetFrameRate = fps;
            Debug.Log($"Set Framerate: {fps}");
        }

        private void SetFrameRate(int dropdownIndex)
        {
            string dropdownValue = m_FPSDropdown.options[dropdownIndex].text;
            int fps = dropdownValue.Equals("Unlimited") ? -1 : Convert.ToInt32(dropdownValue);
            NikkeSettings.FPS = dropdownValue;
            SetAppFrameRate(fps);
        }

        /// <summary>
        /// Load audio clip.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private async UniTask<AudioClip> LoadAudioClip(string path)
        {
            return await WebRequestHelper.GetAudioClip(
                WebRequestHelper.IsHttp(path)
                    ? await WebRequestHelper.CacheAsset(
                        path,
                        Path.Combine(
                            CachedDataDirectory,
                            Path.GetFileName(new Uri(path).AbsolutePath)
                        )
                    )
                    : path
            );
        }

        /// <summary>
        /// Open File Dialog for image & music assets.
        /// </summary>
        /// <param name="inputField"></param>
        /// <returns></returns>
        public void OpenSettingsAssetDialog(TMP_InputField inputField) =>
            OpenSettingsAssetDialogAsync(inputField).Forget();

        private async UniTaskVoid OpenSettingsAssetDialogAsync(TMP_InputField inputField)
        {
            FileBrowser.SetFilters(
                false,
                new FileBrowser.Filter("Assets", ".png", ".jpg", ".mp3", ".ogg", ".wav", ".flac")
            );
            string[] paths = await StorageHelper.OpenFileDialog(
                inputField,
                "Load Assets",
                initialPath: NikkeSettings.LastOpenedDirectory
            );
            if (paths.Length > 0)
            {
                NikkeSettings.LastOpenedDirectory = Path.GetDirectoryName(paths[0]);
                await SaveSettings();
            }
        }
        #endregion
    }
}
