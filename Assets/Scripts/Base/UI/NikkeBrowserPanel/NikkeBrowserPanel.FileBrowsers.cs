using Cysharp.Threading.Tasks;
using NikkeViewerEX.Utils;

namespace NikkeViewerEX.UI
{
    public partial class NikkeBrowserPanel
    {
        async UniTask<string> OpenFileBrowser(string title, string[] extensions)
        {
            SimpleFileBrowser.FileBrowser.SetFilters(
                false,
                new SimpleFileBrowser.FileBrowser.Filter("Files", extensions)
            );

            UniTaskCompletionSource<string> tcs = new();
            SimpleFileBrowser.FileBrowser.ShowLoadDialog(
                paths => tcs.TrySetResult(paths.Length > 0 ? paths[0] : null),
                () => tcs.TrySetResult(null),
                SimpleFileBrowser.FileBrowser.PickMode.Files,
                false,
                settingsManager.NikkeSettings.LastOpenedDirectory
                    ?? StorageHelper.GetApplicationPath(),
                null,
                title
            );

            string result = await tcs.Task;
            if (!string.IsNullOrEmpty(result))
                settingsManager.NikkeSettings.LastOpenedDirectory =
                    System.IO.Path.GetDirectoryName(result);
            return result;
        }

        async UniTask<string> OpenFolderBrowser(string title)
        {
            UniTaskCompletionSource<string> tcs = new();
            SimpleFileBrowser.FileBrowser.ShowLoadDialog(
                paths => tcs.TrySetResult(paths.Length > 0 ? paths[0] : null),
                () => tcs.TrySetResult(null),
                SimpleFileBrowser.FileBrowser.PickMode.Folders,
                false,
                settingsManager.NikkeSettings.LastOpenedDirectory
                    ?? StorageHelper.GetApplicationPath(),
                null,
                title
            );

            string result = await tcs.Task;
            if (!string.IsNullOrEmpty(result))
                settingsManager.NikkeSettings.LastOpenedDirectory = result;
            return result;
        }
    }
}
