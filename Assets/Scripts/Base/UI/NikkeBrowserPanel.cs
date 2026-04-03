using Cysharp.Threading.Tasks;
using NikkeViewerEX.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NikkeViewerEX.UI
{
    [AddComponentMenu("Nikke Viewer EX/UI/Nikke Browser Panel")]
    [RequireComponent(typeof(UIDocument))]
    public partial class NikkeBrowserPanel : MonoBehaviour
    {
        [Header("Templates")]
        [SerializeField]
        VisualTreeAsset m_BrowserItemTemplate;

        [SerializeField]
        VisualTreeAsset m_ActiveItemTemplate;

        // UI root references
        VisualElement root;
        VisualElement panel;
        VisualElement hoverZone;
        Toggle hideUiToggle;

        // Hover-based UI state
        bool isHoverModeEnabled;
        bool isUiVisible = true;

        // Drag state
        bool dragging;
        Vector2 dragStartPointer;
        Vector2 dragStartPanelPos;

        // Tab buttons
        Button tabConfigBtn;
        Button tabBrowserBtn;
        Button tabActiveBtn;
        Button tabDebugBtn;
        Button tabPresetsBtn;
        Button tabBackgroundsBtn;
        Button tabMusicBtn;

        // Tab content panels
        VisualElement contentConfig;
        VisualElement contentBrowser;
        VisualElement contentActive;
        VisualElement contentDebug;
        VisualElement contentBackgrounds;
        VisualElement contentPresets;
        VisualElement contentMusic;

        #region Lifecycle
        void Awake()
        {
            mainControl = FindObjectsByType<MainControl>(FindObjectsSortMode.None)[0];
            settingsManager = FindObjectsByType<SettingsManager>(FindObjectsSortMode.None)[0];
        }

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            root = doc.rootVisualElement;
            QueryElements();
            BindEvents();
            RestoreConfig();
            RebuildActiveViewers();
            filterHasAssetsBtn.EnableInClassList("filter-active", true);
            filterFullBtn.EnableInClassList("filter-active", true);
            ApplyBrowserFilters();
        }

        void OnDisable()
        {
            UnbindEvents();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                TogglePanel();
        }
        #endregion

        #region UI Queries
        void QueryElements()
        {
            panel = root.Q("browser-panel");
            hoverZone = root.Q("hover-zone");
            hideUiToggle = root.Q<Toggle>("hide-ui-toggle");

            tabConfigBtn = root.Q<Button>("tab-config");
            tabBrowserBtn = root.Q<Button>("tab-browser");
            tabActiveBtn = root.Q<Button>("tab-active");
            tabDebugBtn = root.Q<Button>("tab-debug");
            tabPresetsBtn = root.Q<Button>("tab-presets");
            tabBackgroundsBtn = root.Q<Button>("tab-backgrounds");
            tabMusicBtn = root.Q<Button>("tab-music");

            contentConfig = root.Q("content-config");
            contentBrowser = root.Q("content-browser");
            contentActive = root.Q("content-active");
            contentDebug = root.Q("content-debug");
            contentBackgrounds = root.Q("content-backgrounds");
            contentPresets = root.Q("content-presets");
            contentMusic = root.Q("content-music");

            QueryConfigElements();
            QueryBrowserElements();
            QueryAzurLaneElements();
            QueryActiveElements();
            QueryDebugElements();
            QueryBackgroundElements();
            QueryPresetElements();
            QueryMusicElements();
        }
        #endregion

        #region Event Binding
        void BindEvents()
        {
            var header = root.Q("header");
            header.pickingMode = PickingMode.Position;
            header.RegisterCallback<PointerDownEvent>(OnHeaderPointerDown);
            header.RegisterCallback<PointerMoveEvent>(OnHeaderPointerMove);
            header.RegisterCallback<PointerUpEvent>(OnHeaderPointerUp);

            hoverZone.RegisterCallback<PointerEnterEvent>(OnHoverZoneEnter);
            hoverZone.RegisterCallback<PointerDownEvent>(OnHoverZoneClick);
            panel.RegisterCallback<PointerLeaveEvent>(OnPanelPointerLeave);

            hideUiToggle.RegisterValueChangedCallback(OnHideUiToggleChanged);

            tabConfigBtn.clicked += () => SwitchTab(0);
            tabBrowserBtn.clicked += () => SwitchTab(1);
            tabActiveBtn.clicked += () => SwitchTab(2);
            tabDebugBtn.clicked += () => SwitchTab(3);
            tabPresetsBtn.clicked += () => SwitchTab(4);
            tabBackgroundsBtn.clicked += () => SwitchTab(5);
            tabMusicBtn.clicked += () => SwitchTab(6);

            BindConfigEvents();
            BindBrowserEvents();
            BindAzurLaneEvents();
            BindActiveEvents();
            BindBackgroundEvents();
            BindPresetEvents();
            BindMusicEvents();
        }

        void UnbindEvents()
        {
            var header = root.Q("header");
            header.UnregisterCallback<PointerDownEvent>(OnHeaderPointerDown);
            header.UnregisterCallback<PointerMoveEvent>(OnHeaderPointerMove);
            header.UnregisterCallback<PointerUpEvent>(OnHeaderPointerUp);

            hoverZone.UnregisterCallback<PointerEnterEvent>(OnHoverZoneEnter);
            hoverZone.UnregisterCallback<PointerDownEvent>(OnHoverZoneClick);
            panel.UnregisterCallback<PointerLeaveEvent>(OnPanelPointerLeave);
            hideUiToggle.UnregisterValueChangedCallback(OnHideUiToggleChanged);

            UnbindBrowserEvents();
            UnbindAzurLaneEvents();
        }

        void OnHoverZoneEnter(PointerEnterEvent evt)
        {
            hoverZone.style.opacity = 1;
        }

        void OnPanelPointerLeave(PointerLeaveEvent evt)
        {
            if (isHoverModeEnabled)
            {
                Hide();
                hoverZone.style.opacity = 0;
            }
        }

        void OnHoverZoneClick(PointerDownEvent evt)
        {
            if (evt.button == 0)
            {
                TogglePanel();
                hoverZone.style.opacity = isUiVisible ? 1f : 0f;
            }
        }

        void OnHideUiToggleChanged(ChangeEvent<bool> evt)
        {
            isHoverModeEnabled = evt.newValue;
            settingsManager.NikkeSettings.HideUI = evt.newValue;
            settingsManager.SaveSettings().Forget();
            if (isHoverModeEnabled)
            {
                Hide();
                hoverZone.style.opacity = 0;
            }
            else
            {
                Show();
                hoverZone.style.opacity = 1;
            }
        }

        void OnHeaderPointerDown(PointerDownEvent evt)
        {
            dragging = true;
            dragStartPointer = evt.position;
            dragStartPanelPos = new Vector2(panel.resolvedStyle.left, panel.resolvedStyle.top);
            (evt.target as VisualElement)?.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnHeaderPointerMove(PointerMoveEvent evt)
        {
            if (!dragging) return;
            Vector2 delta = (Vector2)evt.position - dragStartPointer;
            panel.style.left = dragStartPanelPos.x + delta.x;
            panel.style.top  = dragStartPanelPos.y + delta.y;
            evt.StopPropagation();
        }

        void OnHeaderPointerUp(PointerUpEvent evt)
        {
            if (!dragging) return;
            dragging = false;
            (evt.target as VisualElement)?.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }
        #endregion

        #region Tab Switching
        void SwitchTab(int index)
        {
            tabConfigBtn.RemoveFromClassList("tab-active");
            tabBrowserBtn.RemoveFromClassList("tab-active");
            tabActiveBtn.RemoveFromClassList("tab-active");
            tabDebugBtn.RemoveFromClassList("tab-active");
            tabPresetsBtn.RemoveFromClassList("tab-active");
            tabBackgroundsBtn.RemoveFromClassList("tab-active");
            tabMusicBtn.RemoveFromClassList("tab-active");

            contentConfig.RemoveFromClassList("tab-visible");
            contentBrowser.RemoveFromClassList("tab-visible");
            contentActive.RemoveFromClassList("tab-visible");
            contentDebug.RemoveFromClassList("tab-visible");
            contentBackgrounds.RemoveFromClassList("tab-visible");
            contentPresets.RemoveFromClassList("tab-visible");
            contentMusic.RemoveFromClassList("tab-visible");

            switch (index)
            {
                case 0:
                    tabConfigBtn.AddToClassList("tab-active");
                    contentConfig.AddToClassList("tab-visible");
                    break;
                case 1:
                    tabBrowserBtn.AddToClassList("tab-active");
                    contentBrowser.AddToClassList("tab-visible");
                    ApplyBrowserFilters();
                    break;
                case 2:
                    tabActiveBtn.AddToClassList("tab-active");
                    contentActive.AddToClassList("tab-visible");
                    RefreshActiveList();
                    break;
                case 3:
                    tabDebugBtn.AddToClassList("tab-active");
                    contentDebug.AddToClassList("tab-visible");
                    RefreshDebugList();
                    break;
                case 4:
                    tabPresetsBtn.AddToClassList("tab-active");
                    contentPresets.AddToClassList("tab-visible");
                    RefreshPresetList();
                    break;
                case 5:
                    tabBackgroundsBtn.AddToClassList("tab-active");
                    contentBackgrounds.AddToClassList("tab-visible");
                    RefreshBackgroundList();
                    break;
                case 6:
                    tabMusicBtn.AddToClassList("tab-active");
                    contentMusic.AddToClassList("tab-visible");
                    RefreshMusicList();
                    break;
            }
        }
        #endregion

        #region Public API
        public void TogglePanel()
        {
            if (isUiVisible)
                Hide();
            else
                Show();
        }

        public void Show()
        {
            panel.style.display = DisplayStyle.Flex;
            isUiVisible = true;
        }

        public void Hide()
        {
            panel.style.display = DisplayStyle.None;
            isUiVisible = false;
        }
        #endregion
    }
}
