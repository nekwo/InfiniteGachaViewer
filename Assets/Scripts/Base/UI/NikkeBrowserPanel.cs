using Cysharp.Threading.Tasks;
using NikkeViewerEX.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NikkeViewerEX.UI
{
    public enum BrowserTab
    {
        Config,
        Browser,
        Active,
        Debug,
        Presets,
        Backgrounds,
        Music,
    }

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
            mainControl = MainControl.Instance
                ?? FindObjectsByType<MainControl>(FindObjectsSortMode.None)[0];
            settingsManager = mainControl.GetComponent<SettingsManager>();
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

            tabConfigBtn.clicked += () => SwitchTab(BrowserTab.Config);
            tabBrowserBtn.clicked += () => SwitchTab(BrowserTab.Browser);
            tabActiveBtn.clicked += () => SwitchTab(BrowserTab.Active);
            tabDebugBtn.clicked += () => SwitchTab(BrowserTab.Debug);
            tabPresetsBtn.clicked += () => SwitchTab(BrowserTab.Presets);
            tabBackgroundsBtn.clicked += () => SwitchTab(BrowserTab.Backgrounds);
            tabMusicBtn.clicked += () => SwitchTab(BrowserTab.Music);

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
        (Button btn, VisualElement content)[] _tabs;

        (Button btn, VisualElement content)[] Tabs => _tabs ??= new[]
        {
            (tabConfigBtn,      contentConfig),
            (tabBrowserBtn,     contentBrowser),
            (tabActiveBtn,      contentActive),
            (tabDebugBtn,       contentDebug),
            (tabPresetsBtn,     contentPresets),
            (tabBackgroundsBtn, contentBackgrounds),
            (tabMusicBtn,       contentMusic),
        };

        void SwitchTab(BrowserTab tab)
        {
            foreach (var (btn, content) in Tabs)
            {
                btn.RemoveFromClassList("tab-active");
                content.RemoveFromClassList("tab-visible");
            }

            var (activeBtn, activeContent) = Tabs[(int)tab];
            activeBtn.AddToClassList("tab-active");
            activeContent.AddToClassList("tab-visible");

            switch (tab)
            {
                case BrowserTab.Browser:     ApplyBrowserFilters(); break;
                case BrowserTab.Active:      RefreshActiveList();   break;
                case BrowserTab.Debug:       RefreshDebugList();    break;
                case BrowserTab.Presets:     RefreshPresetList();   break;
                case BrowserTab.Backgrounds: RefreshBackgroundList(); break;
                case BrowserTab.Music:       RefreshMusicList();    break;
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
