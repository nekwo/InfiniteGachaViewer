using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NikkeViewerEX.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace NikkeViewerEX.UI
{
    public partial class NikkeBrowserPanel
    {
        static readonly string[] AudioExtensions = { ".mp3", ".ogg", ".wav", ".flac" };

        Label musicCount;
        ScrollView musicList;
        VisualElement musicEmpty;
        string musicLastFolder;

        void QueryMusicElements()
        {
            musicCount = root.Q<Label>("music-count");
            musicList = root.Q<ScrollView>("music-list");
            musicEmpty = root.Q("music-empty");
        }

        void BindMusicEvents()
        {
            root.Q<Button>("music-clear-button").clicked += ClearMusic;
        }

        void RefreshMusicList()
        {
            musicList.Clear();

            string folder = settingsManager.NikkeSettings.BgmFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                musicEmpty.style.display = DisplayStyle.Flex;
                musicList.style.display = DisplayStyle.None;
                musicCount.text = "0 tracks";
                return;
            }

            musicLastFolder = folder;

            string[] files = Directory.GetFiles(folder)
                .Where(f => Array.Exists(AudioExtensions, ext =>
                    string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                musicEmpty.style.display = DisplayStyle.Flex;
                musicList.style.display = DisplayStyle.None;
                musicCount.text = "0 tracks";
                return;
            }

            musicEmpty.style.display = DisplayStyle.None;
            musicList.style.display = DisplayStyle.Flex;
            musicCount.text = $"{files.Length} track{(files.Length != 1 ? "s" : "")}";

            string activePath = settingsManager.NikkeSettings.BackgroundMusic;

            foreach (string file in files)
            {
                var item = new VisualElement();
                item.AddToClassList("music-item");

                var icon = new Label("\u266B");
                icon.AddToClassList("music-icon");

                var info = new VisualElement();
                info.AddToClassList("music-info");

                var nameLabel = new Label(Path.GetFileNameWithoutExtension(file));
                nameLabel.AddToClassList("music-name");

                var extLabel = new Label(Path.GetExtension(file).ToUpperInvariant());
                extLabel.AddToClassList("music-ext");

                info.Add(nameLabel);
                info.Add(extLabel);

                item.Add(icon);
                item.Add(info);

                if (string.Equals(file, activePath, StringComparison.OrdinalIgnoreCase))
                    item.AddToClassList("music-active");

                string captured = file;
                item.RegisterCallback<PointerDownEvent>(_ => SelectMusic(captured));

                musicList.Add(item);
            }
        }

        void SelectMusic(string path)
        {
            settingsManager.NikkeSettings.BackgroundMusic = path;
            settingsManager.NikkeSettings.BackgroundMusicPlaying = true;
            settingsManager.BackgroundMusicInput.text = path;
            LoadAndPlayMusic(path).Forget();
            settingsManager.SaveSettings().Forget();

            foreach (var item in musicList.Children())
            {
                var nameLabel = item.Q<Label>(className: "music-name");
                if (nameLabel == null) continue;
                string itemPath = Path.Combine(musicLastFolder,
                    nameLabel.text + item.Q<Label>(className: "music-ext").text.ToLowerInvariant());
                bool isActive = string.Equals(itemPath, path, StringComparison.OrdinalIgnoreCase);
                item.EnableInClassList("music-active", isActive);
            }

            RefreshActiveMusicInfo();
        }

        async UniTask LoadAndPlayMusic(string path)
        {
            var clip = await WebRequestHelper.GetAudioClip(path);
            if (clip == null) return;
            settingsManager.BackgroundMusicAudio.clip = clip;
            settingsManager.BackgroundMusicAudio.Play();
        }

        void ClearMusic()
        {
            settingsManager.NikkeSettings.BackgroundMusic = "";
            settingsManager.NikkeSettings.BackgroundMusicPlaying = false;
            settingsManager.BackgroundMusicInput.text = "";
            settingsManager.BackgroundMusicAudio.Stop();
            settingsManager.BackgroundMusicAudio.clip = null;
            settingsManager.SaveSettings().Forget();

            foreach (var item in musicList.Children())
                item.RemoveFromClassList("music-active");

            RefreshActiveMusicInfo();
        }
    }
}
