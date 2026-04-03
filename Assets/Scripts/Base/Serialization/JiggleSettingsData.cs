using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NikkeViewerEX.Serialization
{
    [Serializable]
    public class JiggleBoneSettings
    {
        public string BoneName;
        public float Stiffness = 30f;
        public float Damping = 4f;
        public float ForceFactor = 3f;
        public float MaxRotDisplacement = 12f;
        public float PosStiffness = 25f;
        public float PosDamping = 3.5f;
        public float PosForceFactor = 2f;
        public float MaxPosDisplacement = 4f;
    }

    [Serializable]
    public class JigglePattern
    {
        public string Keyword;
        public float Stiffness;
        public float Damping;
        public float ForceFactor;
        public float MaxRotDisplacement;
        public float PosStiffness;
        public float PosDamping;
        public float PosForceFactor;
        public float MaxPosDisplacement;
    }

    [Serializable]
    public class JiggleCharacterSettings
    {
        public string AssetName;
        public bool Enabled = true;
        public List<JiggleBoneSettings> Bones = new();
        public List<JigglePattern> AutoPatterns = new();
    }

    [Serializable]
    public class JiggleSettingsFile
    {
        public bool GlobalEnabled = true;
        public List<JigglePattern> DefaultPatterns = new()
        {
            new() { Keyword = "thigh", Stiffness = 18f, Damping = 0.5f, ForceFactor = 10f, MaxRotDisplacement = 30f, PosStiffness = 60f, PosDamping = 0.1f, PosForceFactor = 4f, MaxPosDisplacement = 5f },
            new() { Keyword = "hip", Stiffness = 20f, Damping = 0.6f, ForceFactor = 9f, MaxRotDisplacement = 25f, PosStiffness = 65f, PosDamping = 0.1f, PosForceFactor = 3.5f, MaxPosDisplacement = 4f },
            new() { Keyword = "skirt", Stiffness = 25f, Damping = 2.5f, ForceFactor = 5f, MaxRotDisplacement = 8f, PosStiffness = 50f, PosDamping = 4f, PosForceFactor = 1.5f, MaxPosDisplacement = 1f },
        };
    }

    public static class JiggleSettingsManager
    {
        const string GlobalFileName = "jiggle.json";
        const string CharacterFileName = "physics.json";
        static string GlobalFilePath => Path.Combine(Application.dataPath, "..", GlobalFileName);
        static JiggleSettingsFile cachedGlobal;
        static readonly Dictionary<string, JiggleCharacterSettings> characterCache = new();

        public static JiggleSettingsFile Load()
        {
            if (cachedGlobal != null) return cachedGlobal;
            try
            {
                string path = GlobalFilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    cachedGlobal = JsonUtility.FromJson<JiggleSettingsFile>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Jiggle] Failed to load {GlobalFileName}: {ex.Message}");
            }
            cachedGlobal ??= new JiggleSettingsFile();
            return cachedGlobal;
        }

        public static JiggleCharacterSettings GetForCharacter(string characterFolder)
        {
            if (characterCache.TryGetValue(characterFolder, out var cached))
                return cached;
            try
            {
                string path = Path.Combine(characterFolder, CharacterFileName);
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonUtility.FromJson<JiggleCharacterSettings>(json);
                    characterCache[characterFolder] = settings;
                    return settings;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Jiggle] Failed to load {CharacterFileName} for {characterFolder}: {ex.Message}");
            }
            characterCache[characterFolder] = null;
            return null;
        }

        public static List<JigglePattern> GetPatterns(JiggleCharacterSettings character)
        {
            var file = Load();
            if (character != null && character.AutoPatterns.Count > 0)
                return character.AutoPatterns;
            return file.DefaultPatterns;
        }
    }
}
