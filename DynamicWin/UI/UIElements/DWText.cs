using DynamicWin.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicWin.UI.UIElements
{
    public class DWText : UIObject
    {
        private string text = "";
        public string Text { get { return text; } set { SetText(value); } }

        private float textSize = 24;
        public float TextSize { get => textSize; set => textSize = value; }

        private Vec2 textBounds;

        private SKTypeface font;
        public SKTypeface Font { get => font; set => font = value; }

        public Vec2 TextBounds
        {
            get
            {
                if (textBounds == null) return Vec2.zero;
                return textBounds;
            }
        }

        public DWText(UIObject? parent, string text, Vec2 position, UIAlignment alignment = UIAlignment.TopCenter) : base(parent, position, Vec2.zero, alignment)
        {
            this.text = text;
            Color = Theme.TextMain;
            font = Resources.Res.InterRegular;
        }

        public override void Draw(SKCanvas canvas)
        {
            var paint = GetPaint();
            paint.Color = Color.Value();
            paint.TextSize = textSize;
            paint.Typeface = font;

            // Advance width + font metrics: SKTextBlob.Bounds is unreliable
            // in this SkiaSharp version (inflated, shifted), so never use it
            // for layout. Ink starts at the draw origin and spans ~advance.
            float advance = paint.MeasureText(text);
            var fontMetrics = paint.FontMetrics;
            Size = new Vec2(advance, fontMetrics.Descent - fontMetrics.Ascent);
            textBounds = new Vec2(advance, fontMetrics.Descent - fontMetrics.Ascent);

            using var skFont = new SKFont(paint.Typeface, textSize);
            using var blob = SKTextBlob.Create(text, skFont);

            if (blob != null)
                canvas.DrawText(blob, Position.X, Position.Y, paint);

            //canvas.DrawRoundRect(GetRect(), paint);
        }

        public Vec2 GetBoundsForString(string text)
        {
            using var paint = new SKPaint { Typeface = Font, TextSize = textSize };
            var metrics = paint.FontMetrics;
            return new Vec2(paint.MeasureText(text), metrics.Descent - metrics.Ascent);
        }

        /// <summary>
        /// Positions this text so its visible glyphs are centered on
        /// <paramref name="center"/>. Ink starts at the draw origin and spans
        /// roughly the advance width, so centering on the advance (never on
        /// SKTextBlob.Bounds, which is unreliable) keeps every cell symmetric.
        /// </summary>
        public void SetCenterPosition(Vec2 center)
        {
            using var paint = new SKPaint { Typeface = Font, TextSize = textSize };
            var metrics = paint.FontMetrics;
            Position = new Vec2(
                center.X - paint.MeasureText(text) / 2f,
                center.Y - (metrics.Ascent + metrics.Descent) / 2f);
        }

        /// <summary>
        /// Positions this text so its visible glyphs sit top-centered at
        /// (<paramref name="centerX"/>, <paramref name="top"/>).
        /// </summary>
        public void SetTopCenterPosition(float centerX, float top)
        {
            using var paint = new SKPaint { Typeface = Font, TextSize = textSize };
            Position = new Vec2(
                centerX - paint.MeasureText(text) / 2f,
                top - paint.FontMetrics.Ascent);
        }

        Animator changeTextAnim;

        public void SilentSetText(string text)
        {
            this.text = text;
        }

        public void SetText(string text)
        {
            if (this.text == text) return;
            if (changeTextAnim != null && changeTextAnim.IsRunning) return;

            float ogTextSize = textSize;

            changeTextAnim = new Animator(350, 1);

            changeTextAnim.onAnimationUpdate += (x) =>
            {
                if(x <= 0.5f)
                {
                    float t = Easings.EaseInQuint(x * 2);

                    textSize = Mathf.Lerp(ogTextSize, ogTextSize / 1.5f, t);
                    localBlurAmount = Mathf.Lerp(0, 10, t);
                    Alpha = Mathf.Lerp(1, 0, x);
                }
                else
                {
                    this.text = text;

                    float t = Easings.EaseOutQuint((x - 0.5f) * 2);

                    textSize = Mathf.Lerp(ogTextSize / 2.5f, ogTextSize, t);
                    localBlurAmount = Mathf.Lerp(10, 0, t);
                    Alpha = Mathf.Lerp(0, 1, x);
                }
            };

            AddLocalObject(changeTextAnim);
            changeTextAnim.Start();
            changeTextAnim.onAnimationEnd += () =>
            {
                this.text = text;
                textSize = ogTextSize;
                DestroyLocalObject(changeTextAnim);
            };
        }

        public static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
        }
    }
}
