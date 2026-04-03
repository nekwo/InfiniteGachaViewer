using UnityEngine;
using UnityEngine.UI;

namespace NikkeViewerEX.UI
{
    /// <summary>
    /// Procedural aim reticle — four bars that spread outward from center.
    /// Call Expand() to burst open (e.g. on shoot); it shrinks back automatically.
    /// </summary>
    public class AimReticle : MonoBehaviour
    {
        [SerializeField] float m_BarLength = 16f;
        [SerializeField] float m_BarThickness = 2f;
        [SerializeField] float m_BaseSpread = 6f;
        [SerializeField] float m_ExpandedSpread = 20f;
        [SerializeField] float m_ShrinkSpeed = 8f;
        [SerializeField] Color m_Color = Color.black;

        float currentSpread;
        RectTransform[] bars;

        void Awake()
        {
            currentSpread = m_BaseSpread;
            BuildReticle();
        }

        void BuildReticle()
        {
            bars = new RectTransform[4];

            // top, bottom, left, right
            (string name, bool vertical)[] defs =
            {
                ("Top",    true),
                ("Bottom", true),
                ("Left",   false),
                ("Right",  false),
            };

            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject(defs[i].name);
                go.transform.SetParent(transform, false);
                var img = go.AddComponent<Image>();
                img.color = m_Color;
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = defs[i].vertical
                    ? new Vector2(m_BarThickness, m_BarLength)
                    : new Vector2(m_BarLength, m_BarThickness);
                bars[i] = rt;
            }

            // center dot
            var dotGO = new GameObject("Center");
            dotGO.transform.SetParent(transform, false);
            var dotImg = dotGO.AddComponent<Image>();
            dotImg.color = m_Color;
            var dotRt = dotGO.GetComponent<RectTransform>();
            dotRt.sizeDelta = new Vector2(m_BarThickness, m_BarThickness);

            UpdateBarPositions();
        }

        void Update()
        {
            currentSpread = Mathf.Lerp(currentSpread, m_BaseSpread, Time.deltaTime * m_ShrinkSpeed);
            UpdateBarPositions();
        }

        /// <summary>
        /// Burst the reticle open — call this on shoot.
        /// </summary>
        public void Expand()
        {
            currentSpread = m_ExpandedSpread;
        }

        void UpdateBarPositions()
        {
            float offset = currentSpread + m_BarLength * 0.5f;
            bars[0].anchoredPosition = new Vector2(0,       offset);   // top
            bars[1].anchoredPosition = new Vector2(0,      -offset);   // bottom
            bars[2].anchoredPosition = new Vector2(-offset,  0);       // left
            bars[3].anchoredPosition = new Vector2( offset,  0);       // right
        }
    }
}
