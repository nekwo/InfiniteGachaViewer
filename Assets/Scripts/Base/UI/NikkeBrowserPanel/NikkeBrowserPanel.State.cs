using System.Collections.Generic;
using NikkeViewerEX.Components;
using NikkeViewerEX.Core;
using NikkeViewerEX.Serialization;
using NikkeViewerEX.Utils;

namespace NikkeViewerEX.UI
{
    public partial class NikkeBrowserPanel
    {
        // Managers
        MainControl mainControl;
        SettingsManager settingsManager;

        // Data
        NikkeDatabaseEntry[] database;
        readonly Dictionary<int, NikkeViewerBase> activeViewers = new();
        readonly Dictionary<string, CharacterAssetInfo> resolvedAssets = new();
        readonly Dictionary<string, int> currentVariation = new();
        static int nextInstanceId = 1;
    }
}
