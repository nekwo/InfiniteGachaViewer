using System;
using System.Collections.Generic;
using UnityEngine;

namespace NikkeViewerEX.Serialization
{
    [Serializable]
    public enum NikkePoseType
    {
        Base,
        Cover,
        Aim
    }

    [Serializable]
    public class NikkePose
    {
        public NikkePoseType PoseType;
        public string SkelPath;
        public string AtlasPath;
        public List<string> TexturesPath = new();
    }


    [Serializable]
    public class NikkePreset
    {
        public string Name;
        public List<Nikke> NikkeList = new();
        public List<AzurLaneCharacter> AzurLaneList = new();
        public string BackgroundImage;
        public float BackgroundScale = 1f;
        public float BackgroundPanX;
        public float BackgroundPanY;
        public string BackgroundMusic;
        public float BackgroundMusicVolume = 0.5f;
        public bool BackgroundMusicPlaying = true;
    }

    [Serializable]
    public class NikkeSettings
    {
        public bool IsFirstTime = true;
        public string LastOpenedDirectory;
        public bool HideUI;
        public string FPS = "60";
        public string BackgroundImage;
        public float BackgroundScale = 1f;
        public float BackgroundPanX = 0f;
        public float BackgroundPanY = 0f;
        public string BackgroundMusic;
        public float BackgroundMusicVolume = 0.5f;
        public bool BackgroundMusicPlaying = true;
        public string DatabaseJsonPath;
        public string AssetsFolder;
        public string ThumbnailsFolder;
        public string BackgroundsFolder;
        public string BgmFolder;
        public string AzurLaneDatabaseJsonPath;
        public string AzurLaneAssetsFolder;
        public List<Nikke> NikkeList = new();
        public List<AzurLaneCharacter> AzurLaneList = new();
        public List<NikkePreset> Presets = new();
    }

    [Serializable]
    public class ParameterOverride
    {
        public string Id;
        public float Value;
    }

    [Serializable]
    public class PhysicsScaleOverride
    {
        public string SubRigName;
        public float Ratio = 1f;
    }

    [Serializable]
    public class PartOpacityOverride
    {
        public string PartId;
        public float Opacity;
    }

    [Serializable]
    public class DrawableOpacityOverride
    {
        public string DrawableId;
        public float Opacity;
    }

    [Serializable]
    public class AzurLaneCharacter
    {
        public int InstanceId;
        public string DisplayName;
        public string AssetName;
        public string Model3JsonPath;
        public List<string> VoicesPath = new();
        public Vector3 Scale = Vector3.one;
        public Vector2 Position;
        public bool Lock;
        public bool HideName = true;
        public float Brightness = 1f;
        public float ShadowBrightness = 0f;
        public List<ParameterOverride> ParameterOverrides = new();
        public List<PhysicsScaleOverride> PhysicsScaleOverrides = new();
        public List<PartOpacityOverride> PartOpacityOverrides = new();
        public List<DrawableOpacityOverride> DrawableOpacityOverrides = new();
    }

    [Serializable]
    public class Nikke
    {
        public int InstanceId;
        public string NikkeName;
        public string AssetName;
        public string SkelPath;
        public string AtlasPath;
        public List<string> TexturesPath = new();
        public List<string> VoicesSource = new();
        public List<string> VoicesPath = new();
        public string Skin = "default";
        public Vector3 Scale = Vector3.one;
        public Vector2 Position;
        public bool Lock;
        public bool HideName = true;
        public List<NikkePose> Poses = new();
        public NikkePoseType ActivePose = NikkePoseType.Base;
        public float Brightness = 1f;
        public float ShadowBrightness = 0f;
    }
}
