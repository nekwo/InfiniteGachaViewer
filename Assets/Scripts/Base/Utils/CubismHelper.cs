using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework.Json;
using UnityEngine;
using UnityEditor;

namespace NikkeViewerEX.Utils
{
    /// <summary>
    /// Holds a loaded CubismModel and its categorised motion clips.
    /// </summary>
    public class CubismModelData
    {
        public CubismModel Model;
        /// <summary>Motions whose filename starts with "touch_".</summary>
        public List<AnimationClip> TouchMotions = new();
        /// <summary>Motions named "idle" or "home".</summary>
        public List<AnimationClip> IdleMotions = new();
        /// <summary>Motion JSON strings keyed by motion name (without extension) for fade data creation.</summary>
        public Dictionary<string, string> MotionJsonStrings = new();
        /// <summary>All animation clips with their instance IDs for fade list matching.</summary>
        public List<AnimationClip> AllMotions = new();
    }

    /// <summary>
    /// Runtime loader for Live2D Cubism models from arbitrary filesystem paths.
    /// All file I/O is performed on the thread pool; Unity API calls happen on the main thread.
    /// </summary>
    public static class CubismHelper
    {
        /// <summary>
        /// Asynchronously load a Cubism model and its motions from a .model3.json path.
        /// Returns null if loading fails.
        /// </summary>
        public static async UniTask<CubismModelData> LoadModelAsync(string model3JsonPath)
        {
            if (string.IsNullOrEmpty(model3JsonPath) || !File.Exists(model3JsonPath))
            {
                Debug.LogError($"[CubismHelper] model3.json not found: {model3JsonPath}");
                return null;
            }

            string modelDir = Path.GetDirectoryName(model3JsonPath);

            // ── Step 1: Pre-read all files on thread pool ─────────────────────────────
            var fileCache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            await UniTask.RunOnThreadPool(() =>
            {
                // model3.json itself
                fileCache[model3JsonPath] = File.ReadAllText(model3JsonPath);

                // Parse enough to discover referenced paths
                string json = (string)fileCache[model3JsonPath];
                var stub = JsonUtility.FromJson<Model3JsonStub>(json);

                if (stub == null) return;

                // moc3
                if (!string.IsNullOrEmpty(stub.FileReferences?.Moc))
                    CacheBytes(fileCache, Path.Combine(modelDir, stub.FileReferences.Moc));

                // physics3.json
                if (!string.IsNullOrEmpty(stub.FileReferences?.Physics))
                    CacheText(fileCache, Path.Combine(modelDir, stub.FileReferences.Physics));

                // textures
                if (stub.FileReferences?.Textures != null)
                {
                    foreach (string tex in stub.FileReferences.Textures)
                        CacheBytes(fileCache, Path.Combine(modelDir, tex));
                }
            });

            // ── Step 2: Instantiate model on main thread ───────────────────────────────
            CubismModel3Json model3Json = CubismModel3Json.LoadAtPath(
                model3JsonPath,
                (type, path) => LoadFromCache(fileCache, type, path)
            );

            if (model3Json == null)
            {
                Debug.LogError($"[CubismHelper] Failed to parse model3.json: {model3JsonPath}");
                return null;
            }

            CubismModel model = model3Json.ToModel(shouldImportAsOriginalWorkflow: true);
            if (model == null)
            {
                Debug.LogError($"[CubismHelper] ToModel() returned null for: {model3JsonPath}");
                return null;
            }

            // ── Step 3: Load motion AnimationClips on thread pool ─────────────────────
            var motionPaths = CollectMotionPaths(model3Json, modelDir);
            var motionTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await UniTask.RunOnThreadPool(() =>
            {
                foreach (string mp in motionPaths)
                {
                    if (File.Exists(mp))
                        motionTexts[mp] = File.ReadAllText(mp);
                }
            });

            // ── Step 4: Convert motion3.json → AnimationClip on main thread ───────────
            var result = new CubismModelData { Model = model };

            foreach (var kvp in motionTexts)
            {
                string motionName = Path.GetFileNameWithoutExtension(kvp.Key);
                
                try
                {
                    var motion3Json = CubismMotion3Json.LoadFrom(kvp.Value, shouldCheckMotionConsistency: false);
                    if (motion3Json == null) continue;

                    AnimationClip clip = motion3Json.ToAnimationClip();
                    if (clip == null) continue;
                    clip.name = motionName;

                    // Strip animation events that have no function name (causes warnings)
    #if UNITY_EDITOR
                    var events = UnityEditor.AnimationUtility.GetAnimationEvents(clip);
                    if (events != null && events.Length > 0)
                    {
                        UnityEditor.AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                    }
    #endif

                    // Store motion JSON for fade data creation later
                    result.MotionJsonStrings[motionName] = kvp.Value;
                    result.AllMotions.Add(clip);

                    string baseName = Path.GetFileNameWithoutExtension(motionName);
                    
                    if (baseName.StartsWith("touch_", StringComparison.OrdinalIgnoreCase))
                        result.TouchMotions.Add(clip);
                    else if (baseName == "idle" || baseName == "home")
                        result.IdleMotions.Add(clip);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CubismHelper] Failed to load motion {motionName}: {ex.Message}");
                }
            }

            return result;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────

        static void CacheBytes(Dictionary<string, object> cache, string path)
        {
            if (!cache.ContainsKey(path) && File.Exists(path))
                cache[path] = File.ReadAllBytes(path);
        }

        static void CacheText(Dictionary<string, object> cache, string path)
        {
            if (!cache.ContainsKey(path) && File.Exists(path))
                cache[path] = File.ReadAllText(path);
        }

        static object LoadFromCache(Dictionary<string, object> cache, Type assetType, string path)
        {
            // Normalise path separators
            path = path.Replace('\\', '/');

            if (assetType == typeof(byte[]))
            {
                if (cache.TryGetValue(path, out object bytes) && bytes is byte[]) return bytes;
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }

            if (assetType == typeof(string))
            {
                if (cache.TryGetValue(path, out object text) && text is string) return text;
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }

            if (assetType == typeof(Texture2D))
            {
                byte[] data = null;
                if (cache.TryGetValue(path, out object cached) && cached is byte[] b)
                    data = b;
                else if (File.Exists(path))
                    data = File.ReadAllBytes(path);

                if (data == null) return null;

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                
                // LoadImage only works for JPEG/PNG - this will silently fail for other formats
                if (!tex.LoadImage(data))
                {
                    Debug.LogWarning($"[CubismHelper] LoadImage failed for {path}. Expected JPEG/PNG format. Texture may appear blank.");
                }
                
                tex.name = Path.GetFileNameWithoutExtension(path);
                
                // Required settings for Cubism rendering
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.Apply(false, true);
                
                return tex;
            }

            return null;
        }

        static List<string> CollectMotionPaths(CubismModel3Json model3Json, string modelDir)
        {
            var paths = new List<string>();
            var motions = model3Json.FileReferences.Motions;
            if (motions.Motions == null) return paths;

            foreach (var group in motions.Motions)
            {
                if (group == null) continue;
                foreach (var m in group)
                {
                    if (!string.IsNullOrEmpty(m.File))
                        paths.Add(Path.Combine(modelDir, m.File));
                }
            }

            // Also scan the motions folder for any additional .motion3.json files not in the model3.json
            string motionsFolder = Path.Combine(modelDir, "motions");
            if (Directory.Exists(motionsFolder))
            {
                var allMotionFiles = Directory.GetFiles(motionsFolder, "*.motion3.json", SearchOption.AllDirectories);
                foreach (var motionFile in allMotionFiles)
                {
                    if (!paths.Exists(p => Path.GetFileName(p).Equals(Path.GetFileName(motionFile), StringComparison.OrdinalIgnoreCase)))
                    {
                        paths.Add(motionFile);
                    }
                }
            }

            // Deduplicate (AL model3.json has many duplicates)
            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ── Minimal stub for pre-reading file references before the full parse ─────────
        [Serializable]
        class Model3JsonStub
        {
            public FileRefsStub FileReferences;

            [Serializable]
            public class FileRefsStub
            {
                public string Moc;
                public string Physics;
                public string[] Textures;
            }
        }
    }
}
