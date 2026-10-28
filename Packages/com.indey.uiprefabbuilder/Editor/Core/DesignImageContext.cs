using System;
using System.Collections.Generic;

namespace Indey.UIPrefabBuilder.Core
{
    /// <summary>
    /// Holds the current task's design mockup image as an Assets-relative path so that
    /// tools (e.g. crop_design_image, match_sprite_by_region, map_design_rect) can access
    /// the raw reference image without needing the LLM to smuggle image bytes through a
    /// tool-call parameter.
    /// </summary>
    public static class DesignImageContext
    {
        /// <summary>Assets-relative path (e.g. "Assets/Screenshots/design_reference_foo.png") of the
        /// design mockup attached to the current task, or null if none.</summary>
        public static string CurrentAssetPath { get; set; }

        /// <summary>
        /// How many mockup regions have been converted to Canvas rects for the current task.
        /// Zero means nothing has been measured yet, so any build would be based on eyeballed sizes.
        /// </summary>
        public static int MappedRegionCount { get; private set; }

        /// <summary>Fit of the mockup into the canvas, recorded by the last successful map.</summary>
        public static float LastScale { get; private set; }
        public static float LastCanvasWidth { get; private set; }
        public static float LastCanvasHeight { get; private set; }
        public static float LastImageWidth { get; private set; }
        public static float LastImageHeight { get; private set; }

        private static readonly List<MappedRect> MappedRects = new List<MappedRect>();

        public struct MappedRect
        {
            public string Label;
            public float X, Y, W, H;
            public float AnchoredX, AnchoredY, SizeW, SizeH;
        }

        /// <summary>
        /// True while the current task is "reproduce this freshly attached mockup", which is when
        /// building before measuring is a real mistake. A follow-up text-only turn keeps the image
        /// available to the tools but must not be blocked again.
        /// </summary>
        public static bool RequiresMeasurement { get; private set; }

        public static void BeginImageTask()
        {
            MappedRegionCount = 0;
            RequiresMeasurement = true;
            LastScale = 0;
            LastCanvasWidth = 0;
            LastCanvasHeight = 0;
            LastImageWidth = 0;
            LastImageHeight = 0;
            MappedRects.Clear();
        }

        public static void BeginTextTask()
        {
            RequiresMeasurement = false;
        }

        public static void RegisterMappedRegions(int count)
        {
            if (count > 0) MappedRegionCount += count;
        }

        public static void NoteFit(float scale, float canvasW, float canvasH, float imageW, float imageH)
        {
            LastScale = scale;
            LastCanvasWidth = canvasW;
            LastCanvasHeight = canvasH;
            LastImageWidth = imageW;
            LastImageHeight = imageH;
        }

        /// <summary>True once a design rect has been fitted into the canvas, so mockup boxes can be placed on a screenshot.</summary>
        public static bool HasFit =>
            LastScale > 0f
            && LastCanvasWidth > 0f
            && LastCanvasHeight > 0f
            && LastImageWidth > 0f
            && LastImageHeight > 0f;

        public static List<MappedRect> CopyMappedRects()
        {
            return new List<MappedRect>(MappedRects);
        }

        /// <summary>
        /// Replaces the in-memory measurements. An empty list clears them.
        /// Used when a session is loaded, so a resumed task still knows what was measured.
        /// </summary>
        public static void ReplaceMeasurements(
            float scale, float canvasW, float canvasH, float imageW, float imageH,
            IList<MappedRect> rects)
        {
            MappedRects.Clear();
            LastScale = 0;
            LastCanvasWidth = 0;
            LastCanvasHeight = 0;
            LastImageWidth = 0;
            LastImageHeight = 0;
            MappedRegionCount = 0;
            if (rects == null || rects.Count == 0) return;
            if (scale > 0f && canvasW > 0f && canvasH > 0f && imageW > 0f && imageH > 0f)
                NoteFit(scale, canvasW, canvasH, imageW, imageH);
            for (int i = 0; i < rects.Count; i++)
            {
                var entry = rects[i];
                Remember(entry.Label, entry.X, entry.Y, entry.W, entry.H,
                    entry.AnchoredX, entry.AnchoredY, entry.SizeW, entry.SizeH);
            }
            MappedRegionCount = MappedRects.Count;
        }

        /// <summary>
        /// True when a measured region with this label strictly contains another measured region.
        /// The crop of that region is the parts inside it, not a sprite of the region itself.
        /// </summary>
        public static bool IsContainerLabel(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            for (int i = 0; i < MappedRects.Count; i++)
            {
                if (!SameLabel(MappedRects[i].Label, name)) continue;
                for (int j = 0; j < MappedRects.Count; j++)
                {
                    if (i == j) continue;
                    if (ContainsStrictly(MappedRects[i], MappedRects[j])) return true;
                }
            }
            return false;
        }

        /// <summary>True when <paramref name="inner"/> sits strictly inside <paramref name="outer"/> and is smaller.</summary>
        public static bool ContainsStrictly(MappedRect outer, MappedRect inner)
        {
            const float slop = 0.002f;
            if (inner.W <= 0f || inner.H <= 0f || outer.W <= 0f || outer.H <= 0f) return false;
            float innerArea = inner.W * inner.H;
            float outerArea = outer.W * outer.H;
            if (innerArea >= outerArea * 0.98f) return false;
            return inner.X >= outer.X - slop
                && inner.Y >= outer.Y - slop
                && inner.X + inner.W <= outer.X + outer.W + slop
                && inner.Y + inner.H <= outer.Y + outer.H + slop;
        }

        public static bool TryGetMapped(string label, float x, float y, float w, float h, out MappedRect hit)
        {
            for (int i = 0; i < MappedRects.Count; i++)
            {
                var entry = MappedRects[i];
                if (!SameLabel(entry.Label, label)) continue;
                if (!Near(entry.X, x) || !Near(entry.Y, y) || !Near(entry.W, w) || !Near(entry.H, h)) continue;
                hit = entry;
                return true;
            }
            hit = default;
            return false;
        }

        public static void Remember(string label, float x, float y, float w, float h,
            float anchoredX, float anchoredY, float sizeW, float sizeH)
        {
            var entry = new MappedRect
            {
                Label = label,
                X = x, Y = y, W = w, H = h,
                AnchoredX = anchoredX, AnchoredY = anchoredY, SizeW = sizeW, SizeH = sizeH
            };
            for (int i = 0; i < MappedRects.Count; i++)
            {
                var existing = MappedRects[i];
                if (!SameLabel(existing.Label, label)) continue;
                if (!Near(existing.X, x) || !Near(existing.Y, y) || !Near(existing.W, w) || !Near(existing.H, h)) continue;
                MappedRects[i] = entry;
                return;
            }
            MappedRects.Add(entry);
        }

        private static bool SameLabel(string left, string right)
        {
            return string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Near(float left, float right)
        {
            return Math.Abs(left - right) <= 0.0005f;
        }

        /// <summary>Pixel width of the design mockup, or 0 if unknown.</summary>
        public static int Width { get; set; }

        /// <summary>Pixel height of the design mockup, or 0 if unknown.</summary>
        public static int Height { get; set; }

        public static bool HasSize => Width > 0 && Height > 0;

        public static void Clear()
        {
            CurrentAssetPath = null;
            Width = 0;
            Height = 0;
            MappedRegionCount = 0;
            RequiresMeasurement = false;
            LastScale = 0;
            LastCanvasWidth = 0;
            LastCanvasHeight = 0;
            LastImageWidth = 0;
            LastImageHeight = 0;
            MappedRects.Clear();
        }

        public static void SetFromTexture(string assetPath, UnityEngine.Texture2D tex)
        {
            CurrentAssetPath = assetPath;
            if (tex != null)
            {
                Width = tex.width;
                Height = tex.height;
            }
            else
            {
                Width = 0;
                Height = 0;
            }
        }
    }
}
