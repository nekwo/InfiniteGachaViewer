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
        VisualElement header;

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
            AddDebugCorners();
        }

        void OnDisable()
        {
            UnbindEvents();
        }

        void Update()
        {
            // F1 key toggle disabled
        }

        void AddDebugCorners()
        {
            if (panel == null) return;
            
            var topLeft = new Button { name = "debug-tl", text = "" };
            var topRight = new Button { name = "debug-tr", text = "" };
            var bottomLeft = new Button { name = "debug-bl", text = "" };
            var bottomRight = new Button { name = "debug-br", text = "" };
            
            topLeft.style.position = Position.Absolute;
            topLeft.style.left = 0;
            topLeft.style.top = 0;
            topLeft.style.width = 30;
            topLeft.style.height = 30;
            topLeft.style.opacity = 0;
            
            topRight.style.position = Position.Absolute;
            topRight.style.right = 0;
            topRight.style.top = 0;
            topRight.style.width = 30;
            topRight.style.height = 30;
            topRight.style.opacity = 0;
            
            bottomLeft.style.position = Position.Absolute;
            bottomLeft.style.left = 0;
            bottomLeft.style.bottom = 0;
            bottomLeft.style.width = 30;
            bottomLeft.style.height = 30;
            bottomLeft.style.opacity = 0;
            
            bottomRight.style.position = Position.Absolute;
            bottomRight.style.right = 0;
            bottomRight.style.bottom = 0;
            bottomRight.style.width = 30;
            bottomRight.style.height = 30;
            bottomRight.style.opacity = 0;
            
            topLeft.clicked += () => Debug.Log($"[Debug] TL clicked - button: {topLeft.worldBound}, mouse: {Mouse.current.position.ReadValue()}");
            topRight.clicked += () => Debug.Log($"[Debug] TR clicked - button: {topRight.worldBound}, mouse: {Mouse.current.position.ReadValue()}");
            bottomLeft.clicked += () => Debug.Log($"[Debug] BL clicked - button: {bottomLeft.worldBound}, mouse: {Mouse.current.position.ReadValue()}");
            bottomRight.clicked += () => Debug.Log($"[Debug] BR clicked - button: {bottomRight.worldBound}, mouse: {Mouse.current.position.ReadValue()}");
            
            panel.Add(topLeft);
            panel.Add(topRight);
            panel.Add(bottomLeft);
            panel.Add(bottomRight);
        }

        public Vector2 GetPanelPosition() => panel?.worldBound.position ?? Vector2.zero;
        public Vector2 GetPanelSize() => panel?.worldBound.size ?? Vector2.zero;
        
        public Rect GetPanelBounds()
        {
            if (panel == null) return Rect.zero;
            
            if (root?.panel == null) return Rect.zero;
            
            panel.MarkDirtyRepaint();
            
            float scaleFactor = root.panel.scaledPixelsPerPoint;
            
            float x = panel.resolvedStyle.left;
            float y = panel.resolvedStyle.top;
            float width = panel.resolvedStyle.width;
            float height = panel.resolvedStyle.height;
            
            if (width <= 0 || height <= 0 || float.IsNaN(width) || float.IsNaN(height))
            {
                Rect layoutRect = panel.layout;
                if (layoutRect.width > 0 && layoutRect.height > 0)
                {
                    x = layoutRect.x;
                    y = layoutRect.y;
                    width = layoutRect.width;
                    height = layoutRect.height;
                }
            }
            
            if (width <= 0 || height <= 0)
            {
                Rect worldBounds = panel.worldBound;
                if (worldBounds.width > 0 && worldBounds.height > 0)
                {
                    x = worldBounds.x;
                    y = Screen.height - worldBounds.y - worldBounds.height;
                    width = worldBounds.width;
                    height = worldBounds.height;
                }
            }
            
            if (width <= 0 || height <= 0) return Rect.zero;
            
            Vector2 screenPos = new Vector2(x, y);
            screenPos.y = Screen.height - screenPos.y - (height * scaleFactor);
            
            float extraHeight = 100f;
            return new Rect(screenPos.x, screenPos.y - extraHeight, width * scaleFactor, (height * scaleFactor) + extraHeight);
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
            header = root.Q("header");
            header.pickingMode = PickingMode.Position;
            header.RegisterCallback<PointerDownEvent>(OnHeaderPointerDown);
            header.RegisterCallback<PointerMoveEvent>(OnHeaderPointerMove);
            header.RegisterCallback<PointerUpEvent>(OnHeaderPointerUp);
            header.RegisterCallback<PointerCaptureOutEvent>(OnHeaderPointerCaptureOut);

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
            header.UnregisterCallback<PointerDownEvent>(OnHeaderPointerDown);
            header.UnregisterCallback<PointerMoveEvent>(OnHeaderPointerMove);
            header.UnregisterCallback<PointerUpEvent>(OnHeaderPointerUp);
            header.UnregisterCallback<PointerCaptureOutEvent>(OnHeaderPointerCaptureOut);

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
            // Hover mode disabled - panel won't hide on mouse leave
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
            header.CapturePointer(evt.pointerId);
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
            header.ReleasePointer(evt.pointerId);
            panel.MarkDirtyRepaint();
            evt.StopPropagation();
        }

        void OnHeaderPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            // Safety net: if the capture is lost (e.g. pointer left the window),
            // reset drag state so the panel doesn't get stuck unresponsive.
            dragging = false;
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
