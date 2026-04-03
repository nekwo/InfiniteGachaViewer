using System.Collections.Generic;
using System.IO;
using System.Linq;
using NikkeViewerEX.Serialization;

namespace NikkeViewerEX.Utils
{
    /// <summary>
    /// Asset paths for a single pose (base, cover, or aim).
    /// </summary>
    public class PoseAssetInfo
    {
        public string SkelPath;
        public string AtlasPath;

        /// <summary>
        /// Each entry is a texture variation (a list of PNGs for that variation).
        /// Variation 0 matches the atlas name, others are alternatives.
        /// </summary>
        public List<List<string>> TextureVariations = new();

        public bool IsValid => !string.IsNullOrEmpty(SkelPath)
            && !string.IsNullOrEmpty(AtlasPath)
            && TextureVariations.Count > 0;

        public List<string> GetTextures(int variationIndex)
        {
            if (TextureVariations.Count == 0) return new();
            int idx = variationIndex % TextureVariations.Count;
            return TextureVariations[idx];
        }

        public int VariationCount => TextureVariations.Count;
    }

    /// <summary>
    /// Resolved asset paths for a character folder, including all available poses.
    /// </summary>
    public class CharacterAssetInfo
    {
        /// <summary>
        /// All poses keyed by type. Base pose is always present if the character has assets.
        /// </summary>
        public Dictionary<NikkePoseType, PoseAssetInfo> Poses = new();

        // Convenience shortcuts to the base pose (backward compatibility)
        PoseAssetInfo BasePose =>
            Poses.TryGetValue(NikkePoseType.Base, out var p) ? p : null;

        public string SkelPath => BasePose?.SkelPath;
        public string AtlasPath => BasePose?.AtlasPath;
        public List<List<string>> TextureVariations =>
            BasePose?.TextureVariations ?? new();

        public bool IsValid => BasePose is { IsValid: true };

        public List<string> GetTextures(int variationIndex) =>
            BasePose?.GetTextures(variationIndex) ?? new();

        public int VariationCount => BasePose?.VariationCount ?? 0;

        public bool HasPose(NikkePoseType type) =>
            Poses.ContainsKey(type) && Poses[type].IsValid;

        public IEnumerable<NikkePoseType> AvailablePoses =>
            Poses.Where(kv => kv.Value.IsValid).Select(kv => kv.Key);
    }

    /// <summary>
    /// Resolved asset paths for a Live2D Cubism character.
    /// All content references in the model3.json are relative to its directory,
    /// so only the manifest path is needed at this level.
    /// </summary>
    public class AzurLaneAssetInfo
    {
        public string Model3JsonPath;
        public bool IsValid => !string.IsNullOrEmpty(Model3JsonPath) && File.Exists(Model3JsonPath);
    }

    public static class CharacterAssetResolver
    {
        /// <summary>
        /// Resolve all asset paths for a character by scanning its folder,
        /// including cover/ and aim/ subfolders.
        /// </summary>
        public static CharacterAssetInfo Resolve(string assetsFolder, string characterId)
        {
            var info = new CharacterAssetInfo();
            string charFolder = Path.Combine(assetsFolder, characterId);

            if (!Directory.Exists(charFolder))
                return info;

            // Resolve base pose
            var basePose = ResolvePose(charFolder, characterId);
            if (basePose.IsValid)
                info.Poses[NikkePoseType.Base] = basePose;

            // Resolve cover pose
            string coverFolder = Path.Combine(charFolder, "cover");
            if (Directory.Exists(coverFolder))
            {
                var coverPose = ResolvePose(coverFolder, $"{characterId}_cover");
                if (coverPose.IsValid)
                    info.Poses[NikkePoseType.Cover] = coverPose;
            }

            // Resolve aim pose
            string aimFolder = Path.Combine(charFolder, "aim");
            if (Directory.Exists(aimFolder))
            {
                var aimPose = ResolvePose(aimFolder, $"{characterId}_aim");
                if (aimPose.IsValid)
                    info.Poses[NikkePoseType.Aim] = aimPose;
            }

            return info;
        }

        /// <summary>
        /// Resolve skeleton, atlas, and texture assets within a single folder.
        /// </summary>
        static PoseAssetInfo ResolvePose(string folder, string prefix)
        {
            var pose = new PoseAssetInfo();

            pose.SkelPath = FindFile(folder, $"{prefix}_00.skel")
                ?? FindFile(folder, $"{prefix}.skel")
                ?? FindFirstByExtension(folder, "*.skel");

            pose.AtlasPath = FindFile(folder, $"{prefix}_00.atlas")
                ?? FindFile(folder, $"{prefix}.atlas")
                ?? FindFirstByExtension(folder, "*.atlas");

            pose.TextureVariations = ResolveTextureVariations(folder, prefix, pose.AtlasPath);

            return pose;
        }

        /// <summary>
        /// Group PNG files into texture variations.
        /// Base textures share the atlas file's base name (e.g. c010_00.png, c010_00_2.png).
        /// Variation textures have different suffixes (e.g. c010_01.png).
        /// </summary>
        static List<List<string>> ResolveTextureVariations(
            string charFolder,
            string characterId,
            string atlasPath
        )
        {
            var variations = new List<List<string>>();

            string[] allPngs = Directory.GetFiles(charFolder, "*.png");
            if (allPngs.Length == 0)
                return variations;

            // Determine the base name from the atlas (e.g. "c010_00" from "c010_00.atlas")
            string atlasBase = !string.IsNullOrEmpty(atlasPath)
                ? Path.GetFileNameWithoutExtension(atlasPath)
                : null;

            // Group PNGs: files starting with the same prefix pattern
            // e.g. "c010_00.png" and "c010_00_2.png" are one group (multi-page atlas)
            // "c010_01.png" is a separate variation
            var groups = new SortedDictionary<string, List<string>>();

            foreach (string png in allPngs.OrderBy(f => f))
            {
                string fileName = Path.GetFileNameWithoutExtension(png);
                string groupKey = GetVariationGroupKey(fileName, characterId);
                if (!groups.ContainsKey(groupKey))
                    groups[groupKey] = new List<string>();
                groups[groupKey].Add(png);
            }

            // Put the atlas-matching group first
            if (atlasBase != null && groups.ContainsKey(atlasBase))
            {
                variations.Add(groups[atlasBase]);
                foreach (var kvp in groups)
                {
                    if (kvp.Key != atlasBase)
                        variations.Add(kvp.Value);
                }
            }
            else
            {
                // No atlas match, just add in order
                foreach (var kvp in groups)
                    variations.Add(kvp.Value);
            }

            return variations;
        }

        /// <summary>
        /// Determine the group key for a PNG filename.
        /// "c010_00" and "c010_00_2" both group under "c010_00".
        /// "c010_01" groups under "c010_01".
        /// </summary>
        static string GetVariationGroupKey(string fileName, string characterId)
        {
            if (!fileName.StartsWith(characterId))
                return fileName;

            string suffix = fileName.Substring(characterId.Length);

            // suffix might be "_00", "_00_2", "_01", etc.
            // We want the first segment: "_00" or "_01"
            if (suffix.Length >= 3 && suffix[0] == '_')
            {
                int i = 1;
                while (i < suffix.Length && char.IsDigit(suffix[i]))
                    i++;

                return characterId + suffix.Substring(0, i);
            }

            return fileName;
        }

        /// <summary>
        /// Find all touch sounds for a character (lobby_touch and outpost_touch).
        /// Looks in {assetsFolder}/{characterId}/sounds/.
        /// Returns lobby_touch files first (sorted), then outpost_touch files (sorted).
        /// </summary>
        public static List<string> FindTouchSounds(string assetsFolder, string characterId)
        {
            string soundsFolder = Path.Combine(assetsFolder, characterId, "sounds");
            if (!Directory.Exists(soundsFolder))
                return new List<string>();

            var lobby = Directory.GetFiles(soundsFolder, $"{characterId}_lobby_touch*.wav")
                .OrderBy(f => f).ToList();
            var outpost = Directory.GetFiles(soundsFolder, $"{characterId}_outpost_touch*.wav")
                .OrderBy(f => f).ToList();

            lobby.AddRange(outpost);
            return lobby;
        }

        /// <summary>
        /// Find a thumbnail for a character in the thumbnails folder.
        /// Thumbnails are named like: si_{characterId}*.png
        /// </summary>
        public static string FindThumbnail(string thumbnailsFolder, string characterId)
        {
            if (string.IsNullOrEmpty(thumbnailsFolder) || !Directory.Exists(thumbnailsFolder))
                return null;

            string[] matches = Directory.GetFiles(thumbnailsFolder, $"si_{characterId}*.png");
            return matches.Length > 0 ? matches.OrderBy(f => f).First() : null;
        }

        static string FindFile(string folder, string fileName)
        {
            string path = Path.Combine(folder, fileName);
            return File.Exists(path) ? path : null;
        }

        static string FindFirstByExtension(string folder, string pattern)
        {
            string[] files = Directory.GetFiles(folder, pattern);
            return files.Length > 0 ? files.OrderBy(f => f).First() : null;
        }

        /// <summary>
        /// Resolve the .model3.json path for an Azur Lane Live2D character.
        /// Expects layout: {assetsFolder}/{characterId}/{characterId}.model3.json
        /// </summary>
        public static AzurLaneAssetInfo ResolveAzurLane(string assetsFolder, string characterId)
        {
            var info = new AzurLaneAssetInfo();
            string charFolder = Path.Combine(assetsFolder, characterId);

            if (!Directory.Exists(charFolder))
                return info;

            // Try canonical name first, then any .model3.json in the folder
            string canonical = Path.Combine(charFolder, $"{characterId}.model3.json");
            if (File.Exists(canonical))
                info.Model3JsonPath = canonical;
            else
            {
                string[] found = Directory.GetFiles(charFolder, "*.model3.json");
                if (found.Length > 0)
                    info.Model3JsonPath = found[0];
            }

            return info;
        }

        /// <summary>
        /// Scan the Azur Lane assets folder for all character subfolders that contain
        /// a .model3.json, returning id → AzurLaneAssetInfo for each valid entry.
        /// </summary>
        public static Dictionary<string, AzurLaneAssetInfo> ScanAzurLaneFolder(string assetsFolder)
        {
            var results = new Dictionary<string, AzurLaneAssetInfo>();

            if (string.IsNullOrEmpty(assetsFolder) || !Directory.Exists(assetsFolder))
                return results;

            foreach (string dir in Directory.GetDirectories(assetsFolder))
            {
                string id = Path.GetFileName(dir);
                var info = ResolveAzurLane(assetsFolder, id);
                if (info.IsValid)
                    results[id] = info;
            }

            return results;
        }
    }
}
