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
                string normalizedJsonPath = model3JsonPath.Replace('\\', '/');
                fileCache[normalizedJsonPath] = File.ReadAllText(model3JsonPath);

                // Parse enough to discover referenced paths
                string json = (string)fileCache[normalizedJsonPath];
                var stub = JsonUtility.FromJson<Model3JsonStub>(json);

                if (stub == null) return;

                // moc3
                if (!string.IsNullOrEmpty(stub.FileReferences?.Moc))
                    CacheBytes(fileCache, Path.Combine(modelDir, stub.FileReferences.Moc));

                // physics3.json
                if (!string.IsNullOrEmpty(stub.FileReferences?.Physics))
                    CacheText(fileCache, Path.Combine(modelDir, stub.FileReferences.Physics));

                // pose3.json
                if (!string.IsNullOrEmpty(stub.FileReferences?.Pose))
                    CacheText(fileCache, Path.Combine(modelDir, stub.FileReferences.Pose));

                // cdi3.json (display info — parameter/part names, combined parameters)
                if (!string.IsNullOrEmpty(stub.FileReferences?.DisplayInfo))
                    CacheText(fileCache, Path.Combine(modelDir, stub.FileReferences.DisplayInfo));

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

            // ── Step 2b: Initialize pose parts from pose3.json ───────────────────────
            InitializePoseParts(model, model3Json);

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

            // ── Step 4: Convert motion3.json → AnimationClip ───────────
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

    #if UNITY_EDITOR
                    var events = UnityEditor.AnimationUtility.GetAnimationEvents(clip);
                    if (events != null && events.Length > 0)
                    {
                        UnityEditor.AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                    }
    #endif

                    result.MotionJsonStrings[motionName] = kvp.Value;
                    result.AllMotions.Add(clip);

                    string baseName = Path.GetFileNameWithoutExtension(motionName);
                    
                    if (baseName.StartsWith("touch_", StringComparison.OrdinalIgnoreCase))
                        result.TouchMotions.Add(clip);
                    else if (baseName == "idle")
                        result.IdleMotions.Add(clip);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CubismHelper] Failed to load motion {motionName}: {ex.Message}");
                }
            }

            return result;
        }

        // ── Pose initialization (mirrors editor-only CubismPoseMotionImporter) ────────

        static void InitializePoseParts(CubismModel model, CubismModel3Json model3Json)
        {
            var pose3Json = model3Json.Pose3Json;
            if (pose3Json?.Groups == null) return;

            var parts = model.Parts;
            if (parts == null) return;

            for (int groupIndex = 0; groupIndex < pose3Json.Groups.Length; groupIndex++)
            {
                var group = pose3Json.Groups[groupIndex];
                if (group == null) continue;

                for (int partIndex = 0; partIndex < group.Length; partIndex++)
                {
                    var part = parts.FindById(group[partIndex].Id);
                    if (part == null) continue;

                    var posePart = part.gameObject.GetComponent<Live2D.Cubism.Framework.Pose.CubismPosePart>();
                    if (posePart == null)
                        posePart = part.gameObject.AddComponent<Live2D.Cubism.Framework.Pose.CubismPosePart>();

                    posePart.GroupIndex = groupIndex;
                    posePart.PartIndex = partIndex;
                    posePart.Link = group[partIndex].Link;
                }
            }

            // Re-initialize pose controller now that CubismPosePart tags exist
            var poseController = model.GetComponent<Live2D.Cubism.Framework.Pose.CubismPoseController>();
            if (poseController != null)
                poseController.Refresh();

            Debug.Log($"[CubismHelper] Initialized pose parts from pose3.json ({pose3Json.Groups.Length} groups)");
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────

        static void CacheBytes(Dictionary<string, object> cache, string path)
        {
            string key = path.Replace('\\', '/');
            if (!cache.ContainsKey(key) && File.Exists(path))
                cache[key] = File.ReadAllBytes(path);
        }

        static void CacheText(Dictionary<string, object> cache, string path)
        {
            string key = path.Replace('\\', '/');
            if (!cache.ContainsKey(key) && File.Exists(path))
                cache[key] = File.ReadAllText(path);
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
                else
                    Debug.LogWarning($"[CubismHelper] Texture file not found: {path}");

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
                public string Pose;
                public string DisplayInfo;
                public string[] Textures;
            }
        }
    }
}
