using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace NikkeViewerEX.UI
{
    public partial class NikkeBrowserPanel
    {
        static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };
        const string ThumbCacheFolder = ".thumbcache";
        const int ThumbWidth = 152;
        const int ThumbHeight = 86;

        // Persists between tab switches; cleared when folder changes
        readonly Dictionary<string, Texture2D> bgThumbnailCache = new();
        readonly SemaphoreSlim bgLoadSemaphore = new(4, 4);
        CancellationTokenSource bgLoadCts;
        string bgLastFolder;

        Label bgCount;
        ScrollView bgList;
        VisualElement bgEmpty;

        void QueryBackgroundElements()
        {
            bgCount = root.Q<Label>("bg-count");
            bgList = root.Q<ScrollView>("bg-list");
            bgEmpty = root.Q("bg-empty");
        }

        void BindBackgroundEvents()
        {
            root.Q<Button>("bg-clear-button").clicked += ClearBackground;
        }

        void RefreshBackgroundList()
        {
            // Cancel any in-progress loads from a previous open
            bgLoadCts?.Cancel();
            bgLoadCts?.Dispose();
            bgLoadCts = new CancellationTokenSource();

            bgList.Clear();

            string folder = settingsManager.NikkeSettings.BackgroundsFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                bgEmpty.style.display = DisplayStyle.Flex;
                bgList.style.display = DisplayStyle.None;
                bgCount.text = "0 backgrounds";
                return;
            }

            // If folder changed, drop the old memory cache
            if (!string.Equals(folder, bgLastFolder, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var tex in bgThumbnailCache.Values)
                    if (tex != null) Destroy(tex);
                bgThumbnailCache.Clear();
                bgLastFolder = folder;
            }

            string[] files = Array.FindAll(
                Directory.GetFiles(folder),
                f => Array.Exists(ImageExtensions, ext =>
                    string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase))
            );

            if (files.Length == 0)
            {
                bgEmpty.style.display = DisplayStyle.Flex;
                bgList.style.display = DisplayStyle.None;
                bgCount.text = "0 backgrounds";
                return;
            }

            bgEmpty.style.display = DisplayStyle.None;
            bgList.style.display = DisplayStyle.Flex;
            bgCount.text = $"{files.Length} background{(files.Length != 1 ? "s" : "")}";

            string activePath = settingsManager.NikkeSettings.BackgroundImage;
            var ct = bgLoadCts.Token;

            foreach (string file in files)
            {
                var card = new VisualElement();
                card.AddToClassList("bg-card");

                var thumb = new VisualElement();
                thumb.AddToClassList("bg-thumbnail");
                thumb.AddToClassList("bg-thumbnail-missing");

                var label = new Label(Path.GetFileName(file));
                label.AddToClassList("bg-filename");

                card.Add(thumb);
                card.Add(label);

                if (string.Equals(file, activePath, StringComparison.OrdinalIgnoreCase))
                    card.AddToClassList("bg-active");

                string captured = file;
                card.RegisterCallback<PointerDownEvent>(_ => SetBackground(captured));

                bgList.Add(card);
                LoadBgThumbnail(thumb, file, ct).Forget();
            }
        }

        async UniTask LoadBgThumbnail(VisualElement thumb, string path, CancellationToken ct)
        {
            // Memory cache hit — apply instantly, no disk or semaphore needed
            if (bgThumbnailCache.TryGetValue(path, out Texture2D cached))
            {
                ApplyThumb(thumb, cached);
                return;
            }

            bool acquired = false;
            try
            {
                await bgLoadSemaphore.WaitAsync(ct);
                acquired = true;

                if (ct.IsCancellationRequested) return;

                // Another concurrent task may have just loaded it
                if (bgThumbnailCache.TryGetValue(path, out cached))
                {
                    ApplyThumb(thumb, cached);
                    return;
                }

                string cacheDir = Path.Combine(bgLastFolder, ThumbCacheFolder);
                string thumbFile = Path.Combine(cacheDir, Path.GetFileName(path) + ".thumb.png");

                Texture2D tex;
                if (File.Exists(thumbFile))
                {
                    // Load pre-generated thumbnail from disk cache
                    byte[] data = await File.ReadAllBytesAsync(thumbFile);
                    if (ct.IsCancellationRequested) return;

                    tex = new Texture2D(2, 2);
                    tex.LoadImage(data);
                }
                else
                {
                    // Load full image, generate thumbnail, save to disk
                    byte[] data = await File.ReadAllBytesAsync(path);
                    if (ct.IsCancellationRequested) return;

                    Texture2D full = new Texture2D(2, 2);
                    full.LoadImage(data);

                    tex = ResizeTexture(full, ThumbWidth, ThumbHeight);
                    Destroy(full);

                    if (!Directory.Exists(cacheDir))
                        Directory.CreateDirectory(cacheDir);

                    byte[] pngData = tex.EncodeToPNG();
                    await File.WriteAllBytesAsync(thumbFile, pngData);
                }

                if (ct.IsCancellationRequested)
                {
                    Destroy(tex);
                    return;
                }

                bgThumbnailCache[path] = tex;
                ApplyThumb(thumb, tex);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not load background thumbnail '{path}': {ex.Message}");
            }
            finally
            {
                if (acquired)
                    bgLoadSemaphore.Release();
            }
        }

        static void ApplyThumb(VisualElement thumb, Texture2D tex)
        {
            thumb.style.backgroundImage = new StyleBackground(tex);
            thumb.RemoveFromClassList("bg-thumbnail-missing");
        }

        static Texture2D ResizeTexture(Texture2D source, int width, int height)
        {
            RenderTexture rt = RenderTexture.GetTemporary(width, height);
            Graphics.Blit(source, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D result = new Texture2D(width, height, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        void SetBackground(string path)
        {
            settingsManager.BackgroundImageInput.text = path;
            settingsManager.ApplySettings();
            settingsManager.SaveSettings().Forget();

            foreach (var card in bgList.Children())
            {
                var label = card.Q<Label>(className: "bg-filename");
                if (label == null) continue;
                string cardPath = Path.Combine(
                    settingsManager.NikkeSettings.BackgroundsFolder,
                    label.text);

                bool isActive = string.Equals(cardPath, path, StringComparison.OrdinalIgnoreCase);
                if (isActive) card.AddToClassList("bg-active");
                else card.RemoveFromClassList("bg-active");
            }
        }

        void ClearBackground()
        {
            settingsManager.BackgroundImageInput.text = "";
            settingsManager.ApplySettings();
            settingsManager.SaveSettings().Forget();

            foreach (var card in bgList.Children())
                card.RemoveFromClassList("bg-active");
        }
    }
}
