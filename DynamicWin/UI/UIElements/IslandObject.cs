using DynamicWin.Main;
using DynamicWin.UI.Menu;
using DynamicWin.Utils;
using SkiaSharp;

namespace DynamicWin.UI.UIElements
{
    public class IslandObject : UIObject
    {
        public float topOffset = -2.5f;

        public SecondOrder scaleSecondOrder;

        public float[] secondOrderValuesExpand = [2.5f, 0.6f, 0.1f];
        public float[] secondOrderValuesContract = [3f, 0.9f, 0.1f];

        public bool hidden = false;

        // Set by the active menu while playback events should hold the
        // island open, even when the cursor is not hovering it.
        public bool autoExpanded = false;
        
        public Vec2 currSize;

        float dropShadowStrength = 0f;
        float dropShadowSize = 0f;

        Col borderCol = Col.Transparent;

        public IslandObject() : base(null, Vec2.zero, new Vec2(250, 50), UIAlignment.TopCenter)
        {
            currSize = Size;

            Anchor = new Vec2(0.5f, 0f);

            roundRadius = 35f;

            LocalPosition = new Vec2(0, topOffset);

            scaleSecondOrder = new SecondOrder(Size, secondOrderValuesExpand[0], secondOrderValuesExpand[1], secondOrderValuesExpand[2]);
            expandInteractionRect = 20;

            maskInToIsland = false;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            bool expanded = IsHovering || autoExpanded;

            if (!hidden)
            {
                if (expanded)
                {
                    scaleSecondOrder.SetValues(secondOrderValuesExpand[0], secondOrderValuesExpand[1], secondOrderValuesExpand[2]);
                    currSize = MenuManager.Instance.ActiveMenu.IslandSizeBig();
                }
                else
                {
                    scaleSecondOrder.SetValues(secondOrderValuesContract[0], secondOrderValuesContract[1], secondOrderValuesContract[2]);
                    currSize = MenuManager.Instance.ActiveMenu.IslandSize();
                }

                Size = scaleSecondOrder.Update(deltaTime, currSize);
                //Size = Vec2.lerp(Size, currSize, deltaTime * 15f);

                LocalPosition.Y = Mathf.Lerp(LocalPosition.Y, topOffset, 15f * deltaTime);
            }
            else
            {
                scaleSecondOrder.SetValues(secondOrderValuesContract[0], secondOrderValuesContract[1], secondOrderValuesContract[2]);

                Size = scaleSecondOrder.Update(deltaTime, new Vec2(500, 15));
                //Size = Vec2.lerp(Size, new Vec2(500, 10), deltaTime * 15f);

                LocalPosition.Y = Mathf.Lerp(LocalPosition.Y, -Size.Y / 1.5f, 25f * deltaTime);
            }

            MainForm.Instance.Opacity = hidden ? 0.85f : 1f;

            dropShadowStrength = Mathf.Lerp(dropShadowStrength, expanded ? 0.75f : 0.25f, 10f * deltaTime);
            dropShadowSize = Mathf.Lerp(dropShadowSize, expanded ? 35f : 7.5f, 10f * deltaTime);

            borderCol = Col.Lerp(borderCol, MenuManager.Instance.ActiveMenu.IslandBorderColor(), 10f  * deltaTime);
        }

        public override void Draw(SKCanvas canvas)
        {
            var paint = GetPaint();
            paint.IsAntialias = Settings.AntiAliasing;

            paint.Color = Theme.IslandBackground.Value();

            if (!hidden)
            {
                paint.ImageFilter = SKImageFilter.CreateDropShadow(1, 1, dropShadowSize, dropShadowSize, new Col(0, 0, 0).Override(a: dropShadowStrength).Value());
            }

            // Border
            var paint2 = GetPaint();
            paint2.Color = borderCol.Override(a: borderCol.a * 0.35f).Value();
            paint2.IsStroke = true;
            paint2.StrokeWidth = 2.5f;

            if (!hidden)
            {
                // One continuous silhouette: square corners at the screen edge
                // and a fully rounded bottom, so the sides flow into the pill
                // without a visible seam.
                var notchRect = GetNotchRect();

                canvas.DrawRoundRect(notchRect, paint2);

                paint.ImageFilter = null;
                canvas.DrawRoundRect(notchRect, paint);

                return;
            }

            var rect = GetRect();
            rect.Inflate(2.5f / 2, 2.5f / 2);
            canvas.DrawRoundRect(rect, paint2);

            paint.ImageFilter = null;
            canvas.DrawRoundRect(GetRect(), paint);
        }

        SKRoundRect GetNotchRect()
        {
            var rect = SKRect.Create((int)Position.X, (int)Position.Y, (int)Size.X, (int)Size.Y);
            float radius = Math.Min(roundRadius, Math.Min(rect.Width, rect.Height) / 2f);
            float top = Math.Min(rect.Top, 0f);

            var notchRect = new SKRoundRect();
            notchRect.SetRectRadii(
                SKRect.Create(rect.Left, top, rect.Width, rect.Bottom - top),
                new SKPoint[]
                {
                    new SKPoint(0, 0),
                    new SKPoint(0, 0),
                    new SKPoint(radius, radius),
                    new SKPoint(radius, radius)
                });

            return notchRect;
        }

        public override SKRoundRect GetInteractionRect()
        {
            var rect = SKRect.Create(Position.X, Position.Y, Size.X, Size.Y);

            if(IsHovering)
                rect.Inflate(expandInteractionRect + 5, expandInteractionRect + 5);

            rect.Inflate(expandInteractionRect, expandInteractionRect);
            var r = new SKRoundRect(rect, roundRadius);

            return r;
        }

        public override SKRoundRect GetRect()
        {
            var rect = base.GetRect();
            return rect;
        }
    }
}
