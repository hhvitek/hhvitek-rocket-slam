using System.Collections.Generic;
using RocketSlam.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSlam.Menus
{
    /// <summary>
    /// Results-screen graph of one attempt, drawn with Painter2D: altitude, fall speed and thrust over time. Each line has
    /// its own scale (0 at the bottom, the series maximum at the top; the legend states the ranges); the x axis is time
    /// with second ticks. <see cref="Reveal"/> (0..1) draws the lines progressively for the intro animation.
    /// </summary>
    public class TelemetryGraph : VisualElement
    {
        public static readonly Color AltitudeColor = new Color(0.36f, 0.78f, 1f);
        public static readonly Color FallColor = new Color(1f, 0.42f, 0.36f);
        public static readonly Color ThrustColor = new Color(1f, 0.78f, 0.2f);
        static readonly Color GridColor = new Color(1f, 1f, 1f, 0.09f);
        static readonly Color AxisColor = new Color(1f, 1f, 1f, 0.45f);

        /// <summary>Series maxima sit a little below the top edge (a line along the frame reads as a border).</summary>
        const float Headroom = 0.92f;
        const float PadLeft = 14f, PadRight = 14f, PadTop = 12f, PadBottom = 34f;

        readonly List<TelemetrySample> data = new List<TelemetrySample>();
        readonly List<Label> tickLabels = new List<Label>();
        float maxAlt = 1f, maxFall = 1f, duration = 1f, reveal = 1f;

        public int SampleCount => data.Count;
        public float MaxAltitude => maxAlt;
        public float MaxFall => maxFall;
        public float Duration => duration;

        public float Reveal
        {
            get => reveal;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, reveal)) return;
                reveal = value;
                MarkDirtyRepaint();
            }
        }

        public TelemetryGraph()
        {
            AddToClassList("telemetry-graph");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => LayoutTicks());
        }

        public void SetData(IReadOnlyList<TelemetrySample> samples)
        {
            data.Clear();
            maxAlt = 1f;
            maxFall = 1f;
            for (int i = 0; i < samples.Count; i++)
            {
                TelemetrySample s = samples[i];
                data.Add(s);
                maxAlt = Mathf.Max(maxAlt, s.altitude);
                maxFall = Mathf.Max(maxFall, -s.verticalSpeed);
            }
            duration = data.Count > 1 ? Mathf.Max(1f, data[data.Count - 1].time) : 1f;
            LayoutTicks();
            MarkDirtyRepaint();
        }

        /// <summary>Nice tick step (seconds) for about 6-10 ticks.</summary>
        public static float TickStep(float seconds)
        {
            foreach (float step in new[] { 1f, 2f, 5f, 10f, 15f, 20f, 30f, 60f, 120f })
                if (seconds / step <= 10f) return step;
            return 300f;
        }

        Rect Plot
        {
            get
            {
                Rect r = contentRect;
                return new Rect(r.x + PadLeft, r.y + PadTop, Mathf.Max(1f, r.width - PadLeft - PadRight), Mathf.Max(1f, r.height - PadTop - PadBottom));
            }
        }

        void LayoutTicks()
        {
            Rect plot = Plot;
            if (float.IsNaN(plot.width) || plot.width <= 1f) return;
            float step = TickStep(duration);
            int count = Mathf.FloorToInt(duration / step) + 1;
            while (tickLabels.Count < count)
            {
                var l = new Label();
                l.AddToClassList("graph-tick");
                l.pickingMode = PickingMode.Ignore;
                Add(l);
                tickLabels.Add(l);
            }
            for (int i = 0; i < tickLabels.Count; i++)
            {
                Label l = tickLabels[i];
                bool show = i < count;
                l.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                float t = i * step;
                l.text = $"{t:0} s";
                l.style.left = plot.x + plot.width * (t / duration) - 30f;
                l.style.top = plot.yMax + 6f;
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect plot = Plot;
            if (plot.width <= 2f || plot.height <= 2f) return;
            Painter2D p = ctx.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;

            // grid: quarters horizontally, ticks vertically
            p.strokeColor = GridColor;
            p.lineWidth = 1f;
            for (int i = 1; i <= 4; i++)
            {
                float y = plot.yMax - plot.height * i / 4f;
                p.BeginPath();
                p.MoveTo(new Vector2(plot.x, y));
                p.LineTo(new Vector2(plot.xMax, y));
                p.Stroke();
            }
            float step = TickStep(duration);
            for (float t = step; t <= duration + 1e-3f; t += step)
            {
                float x = plot.x + plot.width * (t / duration);
                p.BeginPath();
                p.MoveTo(new Vector2(x, plot.y));
                p.LineTo(new Vector2(x, plot.yMax));
                p.Stroke();
            }

            // axes
            p.strokeColor = AxisColor;
            p.lineWidth = 2f;
            p.BeginPath();
            p.MoveTo(new Vector2(plot.x, plot.y));
            p.LineTo(new Vector2(plot.x, plot.yMax));
            p.LineTo(new Vector2(plot.xMax, plot.yMax));
            p.Stroke();

            if (data.Count < 2) return;
            float tEnd = duration * reveal;
            Line(p, plot, tEnd, ThrustColor, 3f, s => s.thrust);
            Line(p, plot, tEnd, FallColor, 3f, s => Mathf.Max(0f, -s.verticalSpeed) / maxFall);
            Line(p, plot, tEnd, AltitudeColor, 3.5f, s => s.altitude / maxAlt);
        }

        void Line(Painter2D p, Rect plot, float tEnd, Color color, float width, System.Func<TelemetrySample, float> value)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            bool started = false;
            for (int i = 0; i < data.Count; i++)
            {
                TelemetrySample s = data[i];
                if (s.time > tEnd && started) break;
                Vector2 pt = new Vector2(plot.x + plot.width * (s.time / duration), plot.yMax - plot.height * Headroom * Mathf.Clamp01(value(s)));
                if (!started) { p.MoveTo(pt); started = true; }
                else p.LineTo(pt);
            }
            p.Stroke();
        }
    }
}
