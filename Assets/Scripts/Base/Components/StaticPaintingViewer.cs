using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NikkeViewerEX.Core;
using NikkeViewerEX.Serialization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NikkeViewerEX.Components
{
    /// <summary>
    /// Viewer for static Azur Lane paintings (non-L2D/Spine).
    /// Loads a pre-restored painting texture and overlays swappable face expressions.
    /// Self-contained: creates its own SpriteRenderers at runtime — no prefab wiring needed.
    /// </summary>
    [AddComponentMenu("Nikke Viewer EX/Components/Static Painting Viewer")]
    public class StaticPaintingViewer : NikkeViewerBase
    {
        public override AzurLaneCharacter AlCharacterData { get; set; } = new();

        SpriteRenderer bodyRenderer;
        SpriteRenderer faceRenderer;
        AudioSource audioSource;

        Texture2D bodyTexture;
        readonly List<Texture2D> faceTextures = new();
        int currentExpression;

        // Face rect in pixels (relative to painting image)
        float faceX, faceY, faceW, faceH;
        int paintingW, paintingH;
        bool hasFaces;

        public int ExpressionCount => faceTextures.Count;
        public int CurrentExpression => currentExpression;

        public override void OnEnable()
        {
            base.OnEnable();
            InputManager.PointerClick.performed += OnPointerClick;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            InputManager.PointerClick.performed -= OnPointerClick;

            if (bodyTexture != null) Destroy(bodyTexture);
            foreach (var tex in faceTextures)
                if (tex != null) Destroy(tex);
        }

        public override void OnNikkeDataChanged()
        {
            AlCharacterData.Position = NikkeData.Position;
            AlCharacterData.Scale = NikkeData.Scale;
            AlCharacterData.Lock = NikkeData.Lock;
            AlCharacterData.HideName = NikkeData.HideName;
        }

        public override void TriggerSpawn()
        {
            // Sync NikkeData from saved AlCharacterData
            NikkeData.Lock = AlCharacterData.Lock;
            NikkeData.HideName = AlCharacterData.HideName;

            // Ensure AudioSource for voice playback
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            EnsureRenderers();
            LoadStaticPainting().Forget();
        }

        void EnsureRenderers()
        {
            if (bodyRenderer == null)
            {
                var bodyGo = new GameObject("Body");
                bodyGo.transform.SetParent(transform, false);
                bodyRenderer = bodyGo.AddComponent<SpriteRenderer>();
                bodyRenderer.sortingOrder = 0;
            }

            if (faceRenderer == null)
            {
                var faceGo = new GameObject("Face");
                faceGo.transform.SetParent(transform, false);
                faceRenderer = faceGo.AddComponent<SpriteRenderer>();
                faceRenderer.sortingOrder = 1;
                faceGo.SetActive(false);
            }
        }

        async UniTaskVoid LoadStaticPainting()
        {
            string assetsFolder = !string.IsNullOrEmpty(SettingsManager.NikkeSettings.StaticPaintingAssetsFolder)
                ? SettingsManager.NikkeSettings.StaticPaintingAssetsFolder
                : SettingsManager.NikkeSettings.AzurLaneAssetsFolder;
            string charId = AlCharacterData.AssetName;
            string charFolder = Path.Combine(assetsFolder, charId);

            if (!Directory.Exists(charFolder))
            {
                Debug.LogError($"[StaticPainting] Folder not found: {charFolder}");
                return;
            }

            // Load main painting texture
            string paintingPath = Path.Combine(charFolder, $"{charId}.png");
            if (!File.Exists(paintingPath))
            {
                Debug.LogError($"[StaticPainting] Painting not found: {paintingPath}");
                return;
            }

            byte[] paintingData = await File.ReadAllBytesAsync(paintingPath);
            bodyTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            bodyTexture.LoadImage(paintingData);
            paintingW = bodyTexture.width;
            paintingH = bodyTexture.height;

            // Create body sprite (pivot at center)
            float ppu = 100f;
            Sprite bodySprite = Sprite.Create(
                bodyTexture,
                new Rect(0, 0, paintingW, paintingH),
                new Vector2(0.5f, 0.5f),
                ppu
            );
            bodyRenderer.sprite = bodySprite;

            // Add collider for drag & click detection
            var collider = bodyRenderer.gameObject.GetComponent<BoxCollider>();
            if (collider == null)
                collider = bodyRenderer.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(paintingW / ppu, paintingH / ppu, 0.1f);

            // Load face coordinates
            await LoadFaceData(charFolder);

            // Load face textures
            string facesFolder = Path.Combine(charFolder, "faces");
            if (Directory.Exists(facesFolder))
            {
                var faceFiles = Directory.GetFiles(facesFolder, "face_*.png")
                    .OrderBy(f => ExtractFaceIndex(f))
                    .ToList();

                foreach (string facePath in faceFiles)
                {
                    byte[] fData = await File.ReadAllBytesAsync(facePath);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    tex.LoadImage(fData);
                    faceTextures.Add(tex);
                }
            }

            hasFaces = faceTextures.Count > 0 && faceW > 0 && faceH > 0;

            if (hasFaces)
            {
                SetupFaceRenderer();
                SetExpression(0);
            }

            // Scale to reasonable world size
            float worldHeight = 10f;
            float scale = worldHeight / (paintingH / ppu);
            transform.localScale = Vector3.one * scale;

            // Apply saved transforms
            if (AlCharacterData.Scale != Vector3.one)
                transform.localScale = AlCharacterData.Scale;
            if (AlCharacterData.Position != Vector2.zero)
                transform.position = new Vector3(
                    AlCharacterData.Position.x,
                    AlCharacterData.Position.y,
                    transform.position.z
                );
        }

        void SetupFaceRenderer()
        {
            if (!hasFaces) return;

            float ppu = 100f;

            // Convert pixel coords to local space offset from body center
            // faceX/faceY are top-left in image space (Y-down)
            // Unity sprites have origin at center, Y-up
            float faceCenterX = faceX + faceW / 2f;
            float faceCenterY = faceY + faceH / 2f;

            float offsetX = (faceCenterX - paintingW / 2f) / ppu;
            float offsetY = (paintingH / 2f - faceCenterY) / ppu; // flip Y

            faceRenderer.transform.localPosition = new Vector3(offsetX, offsetY, -0.01f);
            faceRenderer.gameObject.SetActive(true);
        }

        /// <summary>
        /// Switch to a specific face expression by index.
        /// </summary>
        public void SetExpression(int index)
        {
            if (!hasFaces || faceTextures.Count == 0) return;

            currentExpression = Mathf.Clamp(index, 0, faceTextures.Count - 1);
            var tex = faceTextures[currentExpression];

            Sprite faceSprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f
            );
            faceRenderer.sprite = faceSprite;
        }

        /// <summary>
        /// Cycle to the next face expression.
        /// </summary>
        public void NextExpression()
        {
            if (!hasFaces) return;
            SetExpression((currentExpression + 1) % faceTextures.Count);
        }

        /// <summary>
        /// Cycle to the previous face expression.
        /// </summary>
        public void PrevExpression()
        {
            if (!hasFaces) return;
            SetExpression((currentExpression - 1 + faceTextures.Count) % faceTextures.Count);
        }

        async UniTask LoadFaceData(string charFolder)
        {
            string faceJsonPath = Path.Combine(charFolder, "face.json");
            if (File.Exists(faceJsonPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(faceJsonPath);
                    var data = JsonUtility.FromJson<FaceRect>(json);
                    faceX = data.x;
                    faceY = data.y;
                    faceW = data.w;
                    faceH = data.h;
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[StaticPainting] Failed to parse face.json: {ex.Message}");
                }
            }

            faceW = 0;
            faceH = 0;
        }

        static int ExtractFaceIndex(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string numPart = name.Replace("face_", "");
            return int.TryParse(numPart, out int val) ? val : 0;
        }

        void OnPointerClick(InputAction.CallbackContext ctx)
        {
            if (!AllowInteraction || InputManager.IsPointerOverUI()) return;

            Camera cam = CachedCamera;
            if (cam == null) return;

            Vector2 mousePos = Pointer.current.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(mousePos);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                var viewer = hit.collider.GetComponentInParent<NikkeViewerBase>();
                if (viewer != this) return;

                if (hasFaces)
                    NextExpression();

                if (TouchVoices.Count > 0 && audioSource != null)
                {
                    audioSource.Stop();
                    audioSource.clip = TouchVoices[TouchVoiceIndex % TouchVoices.Count];
                    audioSource.Play();
                    TouchVoiceIndex++;
                }
            }
        }

        [Serializable]
        class FaceRect
        {
            public float x, y, w, h;
        }
    }
}
