using System.Linq;
using NikkeViewerEX.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace NikkeViewerEX.UI
{
    public partial class NikkeBrowserPanel
    {
        // Debug tab elements
        Label debugCount;
        ScrollView debugList;
        VisualElement debugEmpty;

        void QueryDebugElements()
        {
            debugCount = root.Q<Label>("debug-count");
            debugList = root.Q<ScrollView>("debug-list");
            debugEmpty = root.Q("debug-empty");
        }

        void RefreshDebugList()
        {
            debugList.Clear();
            var viewers = FindObjectsByType<NikkeViewerBase>(FindObjectsSortMode.None).ToList();

            if (viewers.Count == 0)
            {
                debugEmpty.style.display = DisplayStyle.Flex;
                debugList.style.display = DisplayStyle.None;
                debugCount.text = "0 viewers active";
                return;
            }

            debugEmpty.style.display = DisplayStyle.None;
            debugList.style.display = DisplayStyle.Flex;
            debugCount.text = $"{viewers.Count} viewers active";

            foreach (var viewer in viewers)
            {
                var card = new VisualElement();
                card.AddToClassList("debug-viewer-card");

                string headerName = !string.IsNullOrEmpty(viewer.NikkeData.NikkeName)
                    ? viewer.NikkeData.NikkeName
                    : viewer.NikkeData.AssetName;

                var header = new Label($"{headerName} ({viewer.NikkeData.AssetName})");
                header.AddToClassList("debug-viewer-header");
                card.Add(header);

                var info = new Label(
                    $"Position: {viewer.transform.position}  |  " +
                    $"Scale: {viewer.transform.localScale}  |  " +
                    $"Lock: {viewer.NikkeData.Lock}"
                );
                info.AddToClassList("debug-field");
                card.Add(info);

                var poses = viewer.GetPoseDebugInfo();
                if (poses.Count == 0)
                {
                    var noPose = new Label("(no poses loaded)");
                    noPose.AddToClassList("debug-field");
                    card.Add(noPose);
                }

                foreach (var pose in poses)
                {
                    var section = new VisualElement();
                    section.AddToClassList("debug-pose-section");
                    if (pose.IsActive)
                        section.AddToClassList("debug-pose-active");

                    string activeTag = pose.IsActive ? "  [ACTIVE]" : "";
                    var poseHeader = new Label($"{pose.PoseType}{activeTag}");
                    poseHeader.AddToClassList("debug-pose-header");
                    section.Add(poseHeader);

                    var currentAnim = new Label($"Current Animation: {pose.CurrentAnimation}");
                    currentAnim.AddToClassList("debug-field");
                    section.Add(currentAnim);

                    var currentSkin = new Label($"Current Skin: {pose.CurrentSkin}");
                    currentSkin.AddToClassList("debug-field");
                    section.Add(currentSkin);

                    var animLabel = new Label($"Animations ({pose.Animations.Length}):");
                    animLabel.AddToClassList("debug-field-label");
                    section.Add(animLabel);

                    foreach (string anim in pose.Animations)
                    {
                        var animItem = new Label($"  {anim}");
                        animItem.AddToClassList("debug-anim-item");
                        if (anim == pose.CurrentAnimation)
                            animItem.AddToClassList("debug-anim-current");
                        section.Add(animItem);
                    }

                    var skinLabel = new Label($"Skins ({pose.SkinNames.Length}):");
                    skinLabel.AddToClassList("debug-field-label");
                    section.Add(skinLabel);

                    foreach (string skin in pose.SkinNames)
                    {
                        var skinItem = new Label($"  {skin}");
                        skinItem.AddToClassList("debug-anim-item");
                        if (skin == pose.CurrentSkin)
                            skinItem.AddToClassList("debug-anim-current");
                        section.Add(skinItem);
                    }

                    card.Add(section);
                }

                debugList.Add(card);
            }
        }
    }
}
