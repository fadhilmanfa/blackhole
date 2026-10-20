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

        // Smooth "now playing" marquee. Off by default so existing labels
        // keep rendering as plain text. When enabled and the text is wider
        // than marqueeMaxWidth, it pauses at each end and glides between
        // them (ping-pong) instead of a stiff constant scroll.
        public bool marquee = false;
        public float marqueeMaxWidth = 0f;

        float marqueeElapsed;
        float marqueeScroll;
        const float MarqueePause = 1.6f;
        const float MarqueeSpeed = 30f;

        SKShader? marqueeShader;
        SKColor marqueeShaderColor;
        float marqueeShaderWidth;
        bool marqueeShaderLeftFade;

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
            float textHeight = fontMetrics.Descent - fontMetrics.Ascent;

            bool scrolling = marquee && marqueeMaxWidth > 0f && advance > marqueeMaxWidth + 0.5f;

            Size = new Vec2(scrolling ? marqueeMaxWidth : advance, textHeight);
            textBounds = new Vec2(advance, textHeight);

            using var skFont = new SKFont(paint.Typeface, textSize);
            using var blob = SKTextBlob.Create(text, skFont);

            if (blob == null) return;

            if (!scrolling)
            {
                canvas.DrawText(blob, Position.X, Position.Y, paint);
                return;
            }

            // Clip to the slot, then fade the cut edges so glyphs dissolve
            // into the card instead of being chopped in half.
            float width = marqueeMaxWidth;
            var clip = SKRect.Create(Position.X, Position.Y + fontMetrics.Ascent, width, textHeight);

            float fade = Math.Min(10f, width * 0.2f);
            float leftFade = marqueeScroll > 0.5f ? fade : 0f;

            var color = Color.Value();

            // Cache the gradient: it only depends on colour, slot width and
            // whether the left edge is currently faded, not on scroll px.
            bool hasLeftFade = leftFade > 0f;
            if (marqueeShader == null || marqueeShaderColor != color ||
                Math.Abs(marqueeShaderWidth - width) > 0.01f ||
                marqueeShaderLeftFade != hasLeftFade)
            {
                marqueeShader?.Dispose();
                marqueeShader = SKShader.CreateLinearGradient(
                    new SKPoint(clip.Left, 0),
                    new SKPoint(clip.Right, 0),
                    new[]
                    {
                        color.WithAlpha(0),
                        color,
                        color,
                        color.WithAlpha(0)
                    },
                    new[]
                    {
                        0f,
                        Math.Min(leftFade / width, 0.49f),
                        1f - Math.Min(fade / width, 0.49f),
                        1f
                    },
                    SKShaderTileMode.Clamp);

                marqueeShaderColor = color;
                marqueeShaderWidth = width;
                marqueeShaderLeftFade = hasLeftFade;
            }

            paint.Shader = marqueeShader;

            int save = canvas.Save();
            canvas.ClipRect(clip);
            canvas.DrawText(blob, Position.X - marqueeScroll, Position.Y, paint);
            canvas.RestoreToCount(save);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!marquee || marqueeMaxWidth <= 0f)
            {
                marqueeElapsed = 0f;
                marqueeScroll = 0f;
                return;
            }

            float textWidth = GetBoundsForString(text).X;
            if (textWidth <= marqueeMaxWidth + 0.5f)
            {
                marqueeElapsed = 0f;
                marqueeScroll = 0f;
                return;
            }

            marqueeElapsed += deltaTime;
            marqueeScroll = GetMarqueeOffset(textWidth);
        }

        float GetMarqueeOffset(float textWidth)
        {
            float distance = textWidth - marqueeMaxWidth;
            float scrollTime = Mathf.Clamp(distance / MarqueeSpeed, 1.2f, 5.5f);
            float cycle = (MarqueePause + scrollTime) * 2f;
            float t = marqueeElapsed % cycle;

            if (t < MarqueePause) return 0f;              // hold at the start
            t -= MarqueePause;

            if (t < scrollTime)                           // glide to the end
                return distance * EaseInOutCubic(t / scrollTime);
            t -= scrollTime;

            if (t < MarqueePause) return distance;        // hold at the end
            t -= MarqueePause;

            return distance * (1f - EaseInOutCubic(Math.Min(t / scrollTime, 1f)));
        }

        static float EaseInOutCubic(float x)
        {
            return x < 0.5f
                ? 4f * x * x * x
                : 1f - (float)Math.Pow(-2f * x + 2f, 3f) / 2f;
        }

        public override void OnDestroy()
        {
            marqueeShader?.Dispose();
            marqueeShader = null;
            base.OnDestroy();
        }

        public Vec2 GetBoundsForString(string text)
        {
            using var paint = new SKPaint { Typeface = Font, TextSize = textSize };
            var metrics = paint.FontMetrics;
            return new Vec2(paint.MeasureText(text), metrics.Descent - metrics.Ascent);
        }

        /// <summary>
        /// Middle-aligned labels anchor on the full line box, which leaves
        /// the glyphs sitting high because drawing starts at the baseline.
        /// Call after setting Font/TextSize to nudge the baseline down so
        /// the ink itself is vertically centred.
        /// </summary>
        public void CenterInkVertically()
        {
            using var paint = new SKPaint { Typeface = font, TextSize = textSize };
            // (descent - ascent) / 2 - (ascent + descent) / 2 == -ascent
            LocalPosition.Y += -paint.FontMetrics.Ascent;
        }

        /// <summary>
        /// Sets the baseline nudge (instead of adding to it) so the visible
        /// glyphs are centred inside a centre/middle aligned parent. Safe to
        /// call again after changing <see cref="TextSize"/>.
        /// </summary>
        public void SetInkVerticalOffset()
        {
            using var paint = new SKPaint { Typeface = font, TextSize = textSize };
            LocalPosition.Y = -paint.FontMetrics.Ascent;
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
            ResetMarquee();
        }

        void ResetMarquee()
        {
            marqueeElapsed = 0f;
            marqueeScroll = 0f;
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
                    ResetMarquee();

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
                ResetMarquee();
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
