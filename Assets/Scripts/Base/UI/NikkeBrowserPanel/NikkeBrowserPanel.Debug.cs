using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NikkeViewerEX.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NikkeViewerEX.UI
{
    public partial class NikkeBrowserPanel
    {
        // Debug tab elements
        Label debugCount;
        ScrollView debugList;
        VisualElement debugEmpty;

        /// <summary>Natural sort key: splits "ArtMesh42" into ("ArtMesh", 42).</summary>
        static (string prefix, int number) NaturalSortKey(string id)
        {
            var m = Regex.Match(id, @"^(.*?)(\d+)$");
            if (m.Success)
                return (m.Groups[1].Value, int.Parse(m.Groups[2].Value));
            return (id, 0);
        }

        /// <summary>Adds a search TextField that filters child rows by their name userData.</summary>
        static TextField AddSearchField(VisualElement parent, VisualElement content)
        {
            var search = new TextField();
            search.style.marginLeft = 8;
            search.style.marginRight = 8;
            search.style.marginTop = 4;
            search.style.marginBottom = 4;
            // Use the value property as placeholder hint
            search.value = "";
            search.RegisterValueChangedCallback(evt =>
            {
                string filter = evt.newValue?.Trim().ToLower() ?? "";
                foreach (var child in content.Children())
                {
                    if (child.userData is string name)
                        child.style.display = (filter == "" || name.ToLower().Contains(filter))
                            ? DisplayStyle.Flex : DisplayStyle.None;
                }
            });
            parent.Add(search);
            return search;
        }

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
                        var animItem = new VisualElement();
                        animItem.Add(new Label($"  {anim}"));
                        animItem.AddToClassList("debug-anim-item");
                        if (anim == pose.CurrentAnimation)
                            animItem.AddToClassList("debug-anim-current");

                        // Make clickable to play animation
                        animItem.AddManipulator(new Clickable(() =>
                        {
                            viewer.PlayAnimationByName(anim);
                        }));

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

                // Physics sub-rig sliders for Live2D viewers
                var physicsSubRigs = viewer.GetPhysicsSubRigDebugInfo();
                if (physicsSubRigs.Count > 0)
                {
                    var physSection = new VisualElement();
                    physSection.AddToClassList("debug-pose-section");

                    var physHeaderRow = new VisualElement();
                    physHeaderRow.style.flexDirection = FlexDirection.Row;
                    physHeaderRow.style.alignItems = Align.Center;

                    var physCollapseBtn = new Button { text = "▶" };
                    physCollapseBtn.AddToClassList("debug-collapse-btn");

                    var physHeader = new Label($"Physics ({physicsSubRigs.Count} sub-rigs)");
                    physHeader.AddToClassList("debug-field-label");
                    physHeader.style.flexGrow = 1;

                    var physResetAllBtn = new Button { text = "Reset All" };
                    physResetAllBtn.AddToClassList("debug-param-reset-all-btn");

                    var physEnableToggle = new Toggle();
                    physEnableToggle.value = viewer.PhysicsOverridesEnabled;
                    physEnableToggle.style.marginRight = 4;
                    var viewerForPhysToggle = viewer;
                    physEnableToggle.RegisterValueChangedCallback(evt =>
                    {
                        viewerForPhysToggle.PhysicsOverridesEnabled = evt.newValue;
                    });

                    physHeaderRow.Add(physCollapseBtn);
                    physHeaderRow.Add(physEnableToggle);
                    physHeaderRow.Add(physHeader);
                    physHeaderRow.Add(physResetAllBtn);
                    physSection.Add(physHeaderRow);

                    var physContent = new VisualElement();
                    AddSearchField(physContent, physContent);
                    var physSliderEntries = new List<(string name, Slider slider, Label label)>();
                    var sortedPhysics = physicsSubRigs.OrderBy(s => NaturalSortKey(s.Name).prefix)
                        .ThenBy(s => NaturalSortKey(s.Name).number).ToList();

                    foreach (var subRig in sortedPhysics)
                    {
                        var row = new VisualElement();
                        row.style.flexDirection = FlexDirection.Row;
                        row.style.alignItems = Align.Center;
                        row.style.marginLeft = 8;
                        row.style.marginTop = 2;
                        row.style.marginBottom = 2;

                        var nameLabel = new Label(subRig.Name);
                        nameLabel.AddToClassList("debug-field");
                        nameLabel.style.minWidth = 140;
                        nameLabel.style.width = 140;
                        row.Add(nameLabel);

                        var slider = new Slider(0f, 5f);
                        slider.value = subRig.AngleScaleRatio;
                        slider.style.flexGrow = 1;
                        slider.style.minWidth = 100;

                        var valueLabel = new Label(subRig.AngleScaleRatio.ToString("F2"));
                        valueLabel.AddToClassList("debug-field");
                        valueLabel.style.minWidth = 50;
                        valueLabel.style.unityTextAlign = UnityEngine.TextAnchor.MiddleRight;

                        string rigName = subRig.Name;
                        var viewerRef = viewer;
                        slider.RegisterValueChangedCallback(evt =>
                        {
                            viewerRef.SetPhysicsSubRigScale(rigName, evt.newValue);
                            viewerRef.OnNikkeDataChanged();
                            valueLabel.text = evt.newValue.ToString("F2");
                            SaveSettingsDebounced();
                        });

                        var resetBtn = new Button { text = "R" };
                        resetBtn.AddToClassList("debug-param-reset-btn");
                        resetBtn.clicked += () =>
                        {
                            viewerRef.SetPhysicsSubRigScale(rigName, 1f);
                            viewerRef.OnNikkeDataChanged();
                            slider.SetValueWithoutNotify(1f);
                            valueLabel.text = "1.00";
                            SaveSettingsDebounced();
                        };

                        row.Add(slider);
                        row.Add(valueLabel);
                        row.Add(resetBtn);
                        row.userData = rigName;
                        physContent.Add(row);
                        physSliderEntries.Add((rigName, slider, valueLabel));
                    }

                    var viewerForPhysReset = viewer;
                    physResetAllBtn.clicked += () =>
                    {
                        foreach (var (name, s, lbl) in physSliderEntries)
                        {
                            viewerForPhysReset.SetPhysicsSubRigScale(name, 1f);
                            s.SetValueWithoutNotify(1f);
                            lbl.text = "1.00";
                        }
                        viewerForPhysReset.OnNikkeDataChanged();
                        SaveSettingsDebounced();
                    };

                    physSection.Add(physContent);

                    bool physCollapsed = true;
                    physContent.style.display = DisplayStyle.None;
                    physCollapseBtn.clicked += () =>
                    {
                        physCollapsed = !physCollapsed;
                        physContent.style.display = physCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
                        physCollapseBtn.text = physCollapsed ? "▶" : "▼";
                    };

                    card.Add(physSection);
                }

                // Part opacity sliders for Live2D viewers
                var partOpacities = viewer.GetPartOpacityDebugInfo();
                if (partOpacities.Count > 0)
                {
                    var partSection = new VisualElement();
                    partSection.AddToClassList("debug-pose-section");

                    var partHeaderRow = new VisualElement();
                    partHeaderRow.style.flexDirection = FlexDirection.Row;
                    partHeaderRow.style.alignItems = Align.Center;

                    var partCollapseBtn = new Button { text = "▶" };
                    partCollapseBtn.AddToClassList("debug-collapse-btn");

                    var partHeader = new Label($"Parts ({partOpacities.Count})");
                    partHeader.AddToClassList("debug-field-label");
                    partHeader.style.flexGrow = 1;

                    var partResetAllBtn = new Button { text = "Reset All" };
                    partResetAllBtn.AddToClassList("debug-param-reset-all-btn");

                    var partEnableToggle = new Toggle();
                    partEnableToggle.value = viewer.PartOverridesEnabled;
                    partEnableToggle.style.marginRight = 4;
                    var viewerForPartToggle = viewer;
                    partEnableToggle.RegisterValueChangedCallback(evt =>
                    {
                        viewerForPartToggle.PartOverridesEnabled = evt.newValue;
                    });

                    partHeaderRow.Add(partCollapseBtn);
                    partHeaderRow.Add(partEnableToggle);
                    partHeaderRow.Add(partHeader);
                    partHeaderRow.Add(partResetAllBtn);
                    partSection.Add(partHeaderRow);

                    var partContent = new VisualElement();
                    AddSearchField(partContent, partContent);
                    var partSliderEntries = new List<(string id, float defaultOpacity, Slider slider, Label label)>();
                    var sortedParts = partOpacities.OrderBy(p => NaturalSortKey(p.Id).prefix)
                        .ThenBy(p => NaturalSortKey(p.Id).number).ToList();

                    foreach (var part in sortedParts)
                    {
                        var row = new VisualElement();
                        row.style.flexDirection = FlexDirection.Row;
                        row.style.alignItems = Align.Center;
                        row.style.marginLeft = 8;
                        row.style.marginTop = 2;
                        row.style.marginBottom = 2;

                        var nameLabel = new Label(part.Id);
                        nameLabel.AddToClassList("debug-field");
                        nameLabel.style.minWidth = 140;
                        nameLabel.style.width = 140;
                        row.Add(nameLabel);

                        var slider = new Slider(0f, 1f);
                        slider.value = part.Opacity;
                        slider.style.flexGrow = 1;
                        slider.style.minWidth = 100;

                        var valueLabel = new Label(part.Opacity.ToString("F2"));
                        valueLabel.AddToClassList("debug-field");
                        valueLabel.style.minWidth = 50;
                        valueLabel.style.unityTextAlign = UnityEngine.TextAnchor.MiddleRight;

                        string partId = part.Id;
                        float defaultOpacity = part.DefaultOpacity;
                        var viewerRef = viewer;
                        slider.RegisterValueChangedCallback(evt =>
                        {
                            viewerRef.SetPartOpacity(partId, evt.newValue);
                            viewerRef.OnNikkeDataChanged();
                            valueLabel.text = evt.newValue.ToString("F2");
                            SaveSettingsDebounced();
                        });

                        var resetBtn = new Button { text = "R" };
                        resetBtn.AddToClassList("debug-param-reset-btn");
                        resetBtn.clicked += () =>
                        {
                            viewerRef.ClearPartOpacityOverride(partId);
                            viewerRef.OnNikkeDataChanged();
                            slider.SetValueWithoutNotify(defaultOpacity);
                            valueLabel.text = defaultOpacity.ToString("F2");
                            SaveSettingsDebounced();
                        };

                        row.Add(slider);
                        row.Add(valueLabel);
                        row.Add(resetBtn);
                        row.userData = partId;
                        partContent.Add(row);
                        partSliderEntries.Add((partId, defaultOpacity, slider, valueLabel));
                    }

                    var viewerForPartReset = viewer;
                    partResetAllBtn.clicked += () =>
                    {
                        foreach (var (id, def, s, lbl) in partSliderEntries)
                        {
                            viewerForPartReset.ClearPartOpacityOverride(id);
                            s.SetValueWithoutNotify(def);
                            lbl.text = def.ToString("F2");
                        }
                        viewerForPartReset.OnNikkeDataChanged();
                        SaveSettingsDebounced();
                    };

                    partSection.Add(partContent);

                    bool partCollapsed = true;
                    partContent.style.display = DisplayStyle.None;
                    partCollapseBtn.clicked += () =>
                    {
                        partCollapsed = !partCollapsed;
                        partContent.style.display = partCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
                        partCollapseBtn.text = partCollapsed ? "▶" : "▼";
                    };

                    card.Add(partSection);
                }

                // Drawable opacity sliders for Live2D viewers
                var drawableOpacities = viewer.GetDrawableOpacityDebugInfo();
                if (drawableOpacities.Count > 0)
                {
                    var drawSection = new VisualElement();
                    drawSection.AddToClassList("debug-pose-section");

                    var drawHeaderRow = new VisualElement();
                    drawHeaderRow.style.flexDirection = FlexDirection.Row;
                    drawHeaderRow.style.alignItems = Align.Center;

                    var drawCollapseBtn = new Button { text = "▶" };
                    drawCollapseBtn.AddToClassList("debug-collapse-btn");

                    var drawHeader = new Label($"Drawables ({drawableOpacities.Count})");
                    drawHeader.AddToClassList("debug-field-label");
                    drawHeader.style.flexGrow = 1;

                    var drawResetAllBtn = new Button { text = "Reset All" };
                    drawResetAllBtn.AddToClassList("debug-param-reset-all-btn");

                    var drawEnableToggle = new Toggle();
                    drawEnableToggle.value = viewer.DrawableOverridesEnabled;
                    drawEnableToggle.style.marginRight = 4;
                    var viewerForDrawToggle = viewer;
                    drawEnableToggle.RegisterValueChangedCallback(evt =>
                    {
                        viewerForDrawToggle.DrawableOverridesEnabled = evt.newValue;
                    });

                    drawHeaderRow.Add(drawCollapseBtn);
                    drawHeaderRow.Add(drawEnableToggle);
                    drawHeaderRow.Add(drawHeader);
                    drawHeaderRow.Add(drawResetAllBtn);
                    drawSection.Add(drawHeaderRow);

                    // Pick mode: hover to identify drawables
                    var pickRow = new VisualElement();
                    pickRow.style.flexDirection = FlexDirection.Row;
                    pickRow.style.alignItems = Align.Center;
                    pickRow.style.marginLeft = 8;
                    pickRow.style.marginTop = 4;
                    pickRow.style.marginBottom = 4;

                    var pickBtn = new Button { text = "Pick" };
                    pickBtn.AddToClassList("debug-collapse-btn");
                    pickBtn.style.width = 40;
                    pickBtn.style.minWidth = 40;

                    var pickLabel = new Label("Hover over model...");
                    pickLabel.AddToClassList("debug-field");
                    pickLabel.style.flexGrow = 1;
                    pickLabel.style.whiteSpace = WhiteSpace.Normal;
                    pickLabel.style.display = DisplayStyle.None;

                    pickRow.Add(pickBtn);
                    pickRow.Add(pickLabel);
                    drawSection.Add(pickRow);

                    bool pickActive = false;
                    var viewerForPick = viewer;
                    IVisualElementScheduledItem pickSchedule = null;

                    pickBtn.clicked += () =>
                    {
                        pickActive = !pickActive;
                        pickBtn.text = pickActive ? "Stop" : "Pick";
                        pickLabel.style.display = pickActive ? DisplayStyle.Flex : DisplayStyle.None;
                        if (pickActive)
                        {
                            pickLabel.text = "Hover over model...";
                            pickSchedule = pickBtn.schedule.Execute(() =>
                            {
                                if (!pickActive) return;
                                var mousePos = Pointer.current?.position.ReadValue() ?? Vector2.zero;
                                var hits = viewerForPick.GetDrawablesAtScreenPosition(mousePos);
                                pickLabel.text = hits.Count > 0
                                    ? string.Join("\n", hits)
                                    : "—";
                            }).Every(50);
                        }
                        else
                        {
                            pickSchedule?.Pause();
                            pickSchedule = null;
                        }
                    };

                    var drawContent = new VisualElement();
                    AddSearchField(drawContent, drawContent);
                    var drawSliderEntries = new List<(string id, float defaultOpacity, Slider slider, Label label)>();
                    var sortedDrawables = drawableOpacities.OrderBy(d => NaturalSortKey(d.Id).prefix)
                        .ThenBy(d => NaturalSortKey(d.Id).number).ToList();

                    foreach (var draw in sortedDrawables)
                    {
                        var row = new VisualElement();
                        row.style.flexDirection = FlexDirection.Row;
                        row.style.alignItems = Align.Center;
                        row.style.marginLeft = 8;
                        row.style.marginTop = 2;
                        row.style.marginBottom = 2;

                        var nameLabel = new Label(draw.Id);
                        nameLabel.AddToClassList("debug-field");
                        nameLabel.style.minWidth = 140;
                        nameLabel.style.width = 140;
                        row.Add(nameLabel);

                        var slider = new Slider(0f, 1f);
                        slider.value = draw.Opacity;
                        slider.style.flexGrow = 1;
                        slider.style.minWidth = 100;

                        var valueLabel = new Label(draw.Opacity.ToString("F2"));
                        valueLabel.AddToClassList("debug-field");
                        valueLabel.style.minWidth = 50;
                        valueLabel.style.unityTextAlign = UnityEngine.TextAnchor.MiddleRight;

                        string drawId = draw.Id;
                        float defaultOpacity = draw.DefaultOpacity;
                        var viewerRef = viewer;
                        slider.RegisterValueChangedCallback(evt =>
                        {
                            viewerRef.SetDrawableOpacity(drawId, evt.newValue);
                            viewerRef.OnNikkeDataChanged();
                            valueLabel.text = evt.newValue.ToString("F2");
                            SaveSettingsDebounced();
                        });

                        var resetBtn = new Button { text = "R" };
                        resetBtn.AddToClassList("debug-param-reset-btn");
                        resetBtn.clicked += () =>
                        {
                            viewerRef.ClearDrawableOpacityOverride(drawId);
                            viewerRef.OnNikkeDataChanged();
                            slider.SetValueWithoutNotify(defaultOpacity);
                            valueLabel.text = defaultOpacity.ToString("F2");
                            SaveSettingsDebounced();
                        };

                        row.Add(slider);
                        row.Add(valueLabel);
                        row.Add(resetBtn);
                        row.userData = drawId;
                        drawContent.Add(row);
                        drawSliderEntries.Add((drawId, defaultOpacity, slider, valueLabel));
                    }

                    var viewerForDrawReset = viewer;
                    drawResetAllBtn.clicked += () =>
                    {
                        foreach (var (id, def, s, lbl) in drawSliderEntries)
                        {
                            viewerForDrawReset.ClearDrawableOpacityOverride(id);
                            s.SetValueWithoutNotify(def);
                            lbl.text = def.ToString("F2");
                        }
                        viewerForDrawReset.OnNikkeDataChanged();
                        SaveSettingsDebounced();
                    };

                    drawSection.Add(drawContent);

                    bool drawCollapsed = true;
                    drawContent.style.display = DisplayStyle.None;
                    drawCollapseBtn.clicked += () =>
                    {
                        drawCollapsed = !drawCollapsed;
                        drawContent.style.display = drawCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
                        drawCollapseBtn.text = drawCollapsed ? "▶" : "▼";
                    };

                    card.Add(drawSection);
                }

                // Parameter sliders for Live2D viewers
                var parameters = viewer.GetParameterDebugInfo();
                if (parameters.Count > 0)
                {
                    var paramSection = new VisualElement();
                    paramSection.AddToClassList("debug-pose-section");

                    // Collapsible header row
                    var paramHeaderRow = new VisualElement();
                    paramHeaderRow.style.flexDirection = FlexDirection.Row;
                    paramHeaderRow.style.alignItems = Align.Center;

                    var collapseBtn = new Button { text = "▼" };
                    collapseBtn.AddToClassList("debug-collapse-btn");

                    var paramHeader = new Label($"Parameters ({parameters.Count})");
                    paramHeader.AddToClassList("debug-field-label");
                    paramHeader.style.flexGrow = 1;

                    var resetAllBtn = new Button { text = "Reset All" };
                    resetAllBtn.AddToClassList("debug-param-reset-all-btn");

                    var paramEnableToggle = new Toggle();
                    paramEnableToggle.value = viewer.ParameterOverridesEnabled;
                    paramEnableToggle.style.marginRight = 4;
                    var viewerForParamToggle = viewer;
                    paramEnableToggle.RegisterValueChangedCallback(evt =>
                    {
                        viewerForParamToggle.ParameterOverridesEnabled = evt.newValue;
                    });

                    paramHeaderRow.Add(collapseBtn);
                    paramHeaderRow.Add(paramEnableToggle);
                    paramHeaderRow.Add(paramHeader);
                    paramHeaderRow.Add(resetAllBtn);
                    paramSection.Add(paramHeaderRow);

                    var paramContent = new VisualElement();
                    AddSearchField(paramContent, paramContent);
                    var sliderEntries = new List<(string id, float defaultVal, Slider slider, Label label)>();
                    var sortedParams = parameters.OrderBy(p => NaturalSortKey(p.Id).prefix)
                        .ThenBy(p => NaturalSortKey(p.Id).number).ToList();

                    foreach (var param in sortedParams)
                    {
                        var row = new VisualElement();
                        row.style.flexDirection = FlexDirection.Row;
                        row.style.alignItems = Align.Center;
                        row.style.marginLeft = 8;
                        row.style.marginTop = 2;
                        row.style.marginBottom = 2;

                        var nameLabel = new Label(param.Id);
                        nameLabel.AddToClassList("debug-field");
                        nameLabel.style.minWidth = 140;
                        nameLabel.style.width = 140;
                        row.Add(nameLabel);

                        var slider = new Slider(param.MinValue, param.MaxValue);
                        slider.value = param.Value;
                        slider.style.flexGrow = 1;
                        slider.style.minWidth = 100;

                        var valueLabel = new Label(param.Value.ToString("F2"));
                        valueLabel.AddToClassList("debug-field");
                        valueLabel.style.minWidth = 50;
                        valueLabel.style.unityTextAlign = UnityEngine.TextAnchor.MiddleRight;

                        string paramId = param.Id;
                        float defaultVal = param.DefaultValue;
                        var viewerRef = viewer;
                        slider.RegisterValueChangedCallback(evt =>
                        {
                            viewerRef.SetParameterValue(paramId, evt.newValue);
                            viewerRef.OnNikkeDataChanged();
                            valueLabel.text = evt.newValue.ToString("F2");
                            SaveSettingsDebounced();
                        });

                        // Reset button — clears the override so animation drives it again
                        var resetBtn = new Button { text = "R" };
                        resetBtn.AddToClassList("debug-param-reset-btn");
                        resetBtn.clicked += () =>
                        {
                            viewerRef.ClearParameterOverride(paramId);
                            viewerRef.OnNikkeDataChanged();
                            slider.SetValueWithoutNotify(defaultVal);
                            valueLabel.text = defaultVal.ToString("F2");
                            SaveSettingsDebounced();
                        };

                        row.Add(slider);
                        row.Add(valueLabel);
                        row.Add(resetBtn);
                        row.userData = paramId;
                        paramContent.Add(row);
                        sliderEntries.Add((paramId, defaultVal, slider, valueLabel));
                    }

                    // Wire up Reset All
                    var viewerForResetAll = viewer;
                    resetAllBtn.clicked += () =>
                    {
                        foreach (var (id, def, s, lbl) in sliderEntries)
                        {
                            viewerForResetAll.ClearParameterOverride(id);
                            s.SetValueWithoutNotify(def);
                            lbl.text = def.ToString("F2");
                        }
                        viewerForResetAll.OnNikkeDataChanged();
                        SaveSettingsDebounced();
                    };

                    paramSection.Add(paramContent);

                    // Toggle collapse
                    bool collapsed = true;
                    paramContent.style.display = DisplayStyle.None;
                    collapseBtn.text = "▶";
                    collapseBtn.clicked += () =>
                    {
                        collapsed = !collapsed;
                        paramContent.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
                        collapseBtn.text = collapsed ? "▶" : "▼";
                    };

                    card.Add(paramSection);
                }

                debugList.Add(card);
            }
        }
    }
}
