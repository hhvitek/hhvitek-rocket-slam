using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSlam.Menus
{
    /// <summary>A five-pointed star drawn with Painter2D (no font glyph needed): gold when earned, a dim outline when not.</summary>
    public class StarElement : VisualElement
    {
        public static readonly Color Gold = new Color(1f, 0.82f, 0.24f);
        static readonly Color GoldEdge = new Color(0.75f, 0.5f, 0.08f);
        static readonly Color EmptyFill = new Color(1f, 1f, 1f, 0.07f);
        static readonly Color EmptyEdge = new Color(1f, 1f, 1f, 0.32f);

        bool filled;

        public bool Filled
        {
            get => filled;
            set
            {
                if (filled == value) return;
                filled = value;
                MarkDirtyRepaint();
            }
        }

        public StarElement()
        {
            AddToClassList("star");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            float size = Mathf.Min(r.width, r.height);
            if (size <= 1f) return;
            Vector2 c = r.center + new Vector2(0f, size * 0.04f);
            float outer = size * 0.48f, inner = outer * 0.45f;
            Painter2D p = ctx.painter2D;
            p.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float a = (-90f + i * 36f) * Mathf.Deg2Rad;
                float rad = (i & 1) == 0 ? outer : inner;
                Vector2 pt = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.fillColor = filled ? Gold : EmptyFill;
            p.Fill();
            p.strokeColor = filled ? GoldEdge : EmptyEdge;
            p.lineWidth = Mathf.Max(1.5f, size * 0.035f);
            p.lineJoin = LineJoin.Round;
            p.Stroke();
        }
    }
}
