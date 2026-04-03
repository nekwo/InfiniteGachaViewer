using System;
using UnityEngine;

namespace NikkeViewerEX.Serialization
{
    [Serializable]
    public class NikkeDatabaseEntry
    {
        public string name;
        public string id;
        public float version;

        /// <summary>
        /// Returns the effective Spine version string.
        /// Defaults to "4.0" if version is not specified in JSON.
        /// </summary>
        public string VersionLabel =>
            version >= 4.09f ? version.ToString("0.0") : "4.0";
    }

    [Serializable]
    public class NikkeDatabaseWrapper
    {
        public NikkeDatabaseEntry[] items;
    }

    public static class NikkeDatabaseParser
    {
        /// <summary>
        /// Parse a JSON array of NikkeDatabaseEntry objects.
        /// Unity's JsonUtility can't deserialize top-level arrays,
        /// so we wrap it in an object first.
        /// </summary>
        public static NikkeDatabaseEntry[] Parse(string json)
        {
            string wrapped = $"{{\"items\":{json}}}";
            return JsonUtility.FromJson<NikkeDatabaseWrapper>(wrapped).items;
        }
    }
}
