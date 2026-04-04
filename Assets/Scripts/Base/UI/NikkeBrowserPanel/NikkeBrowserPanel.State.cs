using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
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

        // Debounce
        CancellationTokenSource _saveDebounceCts;

        /// <summary>
        /// Debounced save: waits 300ms after the last call before actually writing to disk.
        /// Safe to call rapidly (e.g. on every slider tick).
        /// </summary>
        void SaveSettingsDebounced()
        {
            _saveDebounceCts?.Cancel();
            _saveDebounceCts = new CancellationTokenSource();
            SaveSettingsDebouncedAsync(_saveDebounceCts.Token).Forget();
        }

        async UniTaskVoid SaveSettingsDebouncedAsync(CancellationToken ct)
        {
            await UniTask.Delay(300, cancellationToken: ct);
            await settingsManager.SaveSettings();
        }
    }
}
