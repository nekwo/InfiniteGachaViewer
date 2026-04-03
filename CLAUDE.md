# NikkeViewerEX

A Unity-based Spine character viewer supporting Live2D portraits with Base/Cover/Aim poses, touch interactions, and dynamic asset loading.

## Quick Reference

- **Engine**: Unity 6 (6000.0.23f1), URP 17.0.3
- **Platform**: Windows (primary)
- **Language**: C# (.NET Standard)
- **Async**: UniTask everywhere — no coroutines
- **License**: MIT

## Architecture Overview

```
Assets/Scripts/
├── Base/                          # Shared across Spine versions
│   ├── Core/
│   │   ├── MainControl.cs         # Central orchestrator, viewer instantiation, event hub
│   │   ├── SettingsManager.cs     # JSON persistence, background media, FPS control
│   │   └── InputManager.cs        # Input System wrapper (5 actions)
│   ├── Components/
│   │   └── NikkeViewerBase.cs     # Abstract base: drag/drop, scaling, touch voice cycling
│   ├── Serialization/
│   │   ├── NikkeSettings.cs       # Data models: NikkeSettings, Nikke, NikkePose
│   │   └── NikkeDatabase.cs       # JSON parser for character database
│   ├── UI/
│   │   ├── NikkeBrowserPanel.cs   # UI Toolkit panel (Config/Browser/Active/Debug tabs)
│   │   └── NikkeListItem.cs       # Legacy UGUI list item (manual path input)
│   └── Utils/
│       ├── CharacterAssetResolver.cs  # Folder scanner: discovers poses + texture variations
│       ├── SpineHelperBase.cs     # .skel version detection (reads bytes 9-11)
│       ├── WebRequestHelper.cs    # HTTP + caching (./cache/ directory)
│       ├── StorageHelper.cs       # File browser wrapper
│       └── AudioHelper.cs         # Audio pitch utilities
├── Nikke4.0/                      # Spine 4.0 implementation
│   ├── Components/NikkeViewer.cs  # Pose management, skin changing, interactions
│   └── Utils/SpineHelper.cs       # Runtime SkeletonDataAsset creation via reflection
└── Nikke4.1/                      # Spine 4.1 implementation (mirrors 4.0 structure)
    ├── Components/NikkeViewer.cs
    └── Utils/SpineHelper.cs
```

## Key Systems

### Spine Version Handling
- `MainControl.InstantiateViewer()` reads .skel header to detect version ("4.0" or "4.1")
- Instantiates correct prefab from `SerializableDictionary<string, NikkeViewerBase>`
- 4.0 and 4.1 NikkeViewer classes are structurally identical, differ only in shader path
- Both versions can coexist in the same scene

### Viewer Lifecycle
1. `SettingsManager` loads settings → creates `NikkeListItem` per saved Nikke → calls `MainControl.InstantiateViewer()`
2. Viewer's `OnEnable` subscribes to `MainControl.OnSettingsApplied` and `SettingsManager.OnSettingsLoaded`
3. Either event triggers `SpawnNikke()` which:
   - Spawns child GameObjects per pose (Base/Cover/Aim)
   - Calls `SpineHelper.InstantiateSpine()` async for each
   - Adds MeshColliders, hides non-active poses (renderer/collider toggle, not SetActive)
   - Extracts skins and touch animations from active skeleton
4. Guard: `if (poseInstances.Count > 0) return;` prevents double-spawning

### Pose System
- **3 pose types**: `NikkePoseType.Base`, `Cover`, `Aim`
- All poses stay as active GameObjects (animations keep running), visibility toggled via renderer/collider
- **Middle-click** on character: toggle Base ↔ Cover, sets `Possessed`
- **Right-click hold**: switches Possessed to Aim; release → Cover
- `SetActivePose()` swaps visibility and updates Skins/TouchAnimations from new skeleton

### Dual UI Systems
| | NikkeBrowserPanel (UI Toolkit) | NikkeListItem (UGUI) |
|---|---|---|
| **How** | JSON database + auto folder scanning | Manual path input |
| **Tech** | UXML/USS, `UIDocument` | Canvas, `TMP_InputField` |
| **Features** | Search, thumbnails, texture variations, pose buttons | Basic path fields, skin dropdown, scale slider |
| **Status** | Modern, primary approach | Legacy, still functional |

Both add to the same `NikkeSettings.NikkeList` and share the viewer lifecycle.

### NikkeBrowserPanel Details
- Toggle with **F1**
- `activeViewers` dictionary maps `AssetName → NikkeViewerBase` — must be rebuilt from scene on app restart (not persisted)
- `CharacterAssetResolver.Resolve()` scans `{assetsFolder}/{id}/` for .skel/.atlas/.png + `cover/` and `aim/` subfolders
- Texture variations detected by grouping PNGs (e.g., `c010_00.png`, `c010_01.png`)

### Settings Persistence
- `settings.json` at application root, serialized via `JsonUtility`
- File I/O on thread pool: `UniTask.RunOnThreadPool()`
- `NikkeSettings` contains: UI state, BGM config, FPS, `DatabaseJsonPath`, `AssetsFolder`, `ThumbnailsFolder`, `List<Nikke>`
- Each `Nikke` stores: name, asset paths, voice paths, skin, position, scale, lock, `List<NikkePose>`, `ActivePose`

### Input System
- 5 actions defined in `InputSettings.inputactions`:
  - `PointerClick` (left click) → touch interaction
  - `PointerHold` (left hold) → drag
  - `MiddleClick` → toggle cover pose
  - `RightClick` → aim (hold) / cover (release)
  - `ToggleUI` → hide UI elements

### Asset Resolution (CharacterAssetResolver)
```
assetsFolder/{id}/
├── {id}_00.skel, .atlas, .png     → Base pose
├── {id}_01.png                    → Base texture variation
├── cover/
│   └── {id}_cover_00.skel/atlas/png → Cover pose
└── aim/
    └── {id}_aim_00.skel/atlas/png   → Aim pose
```

## Dependencies

| Package | Purpose |
|---------|---------|
| UniTask (git) | Async/await, no coroutines |
| Spine Unity 4.0 & 4.1 (included) | Spine rendering runtime |
| Input System 1.11.2 | Modern input handling |
| URP 17.0.3 | Rendering pipeline + Spine shaders |
| SimpleFileBrowser (git) | Runtime file/folder dialogs |
| SerializableCollections (git) | Dictionary serialization |
| InGameDebugConsole (git) | Runtime debug console |
| TextMeshPro (built-in) | Text rendering |

## Conventions

- **Commits**: Imperative present tense, no prefix convention. e.g., "Fix touch voices not updated if the voices directory changed"
- **Async**: Always UniTask, never coroutines. Use `.Forget()` for fire-and-forget calls
- **Events**: `OnEnable` subscribes, `OnDestroy` unsubscribes (note: not `OnDisable`)
- **Raycasting**: All character interactions use Physics.Raycast + `GetComponentInParent<NikkeViewer>()`
- **Pose visibility**: Toggle `MeshRenderer.enabled` + `MeshCollider.enabled`, never `SetActive` (keeps Spine animations running)
- **Spine loading**: Runtime `SkeletonDataAsset` creation via reflection (no editor import pipeline)

## Common Pitfalls

- `NikkeBrowserPanel.activeViewers` is runtime-only (not persisted). Must be rebuilt from scene viewers after restart — see `RebuildActiveViewers()`
- `SpawnNikke()` guards with `poseInstances.Count > 0` — won't re-run if poses already exist
- MeshColliders are set once at spawn from Spine's generated mesh; they don't auto-update with animation
- Both `OnSettingsApplied` and `OnSettingsLoaded` can trigger `SpawnNikke()` — the guard prevents double-spawn
- `Possessed` is a static field on `NikkeViewerBase` — shared across all viewers, set by middle-click interaction
