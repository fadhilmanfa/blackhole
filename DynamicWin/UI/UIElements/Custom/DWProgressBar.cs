using DynamicWin.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicWin.UI.UIElements.Custom
{
    public class DWProgressBar : UIObject
    {
        public float value = 1f;
        public float vaueSmoothing = 15f;

        // Optional springy fill: overshoots the new value a little before
        // settling, so volume steps read as a small jelly pop.
        public bool elastic = false;

        const float ElasticStiffness = 190f;
        const float ElasticDamping = 15f;

        float velocity;

        public Col contentColor;

        public DWProgressBar(UIObject? parent, Vec2 position, Vec2 size, UIAlignment alignment = UIAlignment.TopCenter) : base(parent, position, size, alignment)
        {
            roundRadius = 15f;

            Color = Theme.IconColor.Override(a: 0.1f);
            contentColor = Theme.IconColor.Override(a: 1f);

            displayValue = value;
        }

        float displayValue = 1f;

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (elastic)
            {
                velocity += (value - displayValue) * ElasticStiffness * deltaTime;
                velocity *= Mathf.Clamp(1f - ElasticDamping * deltaTime, 0f, 1f);
                displayValue += velocity * deltaTime;

                // Keep the fill inside the track; stop it from clipping out.
                if (displayValue <= 0f)
                {
                    displayValue = 0f;
                    if (velocity < 0f) velocity = 0f;
                }
                else if (displayValue >= 1f)
                {
                    displayValue = 1f;
                    if (velocity > 0f) velocity = 0f;
                }
            }
            else
            {
                displayValue = Mathf.Lerp(displayValue, value, vaueSmoothing * deltaTime);
            }
        }

        public override void Draw(SKCanvas canvas)
        {
            var paint = GetPaint();

            var bgSize = Size;
            var bgP = RawPosition + LocalPosition;
                bgP.X += bgSize.X * (displayValue) + 3f;
            var bgRectPos = GetScreenPosFromRawPosition(bgP, bgSize);
            var bgRect = SKRect.Create(bgRectPos.X, bgRectPos.Y, bgSize.X * (1f - displayValue), bgSize.Y);
            var rBgRect = new SKRoundRect(bgRect, roundRadius);

            var fillSize = Size;
            var fillRectPos = GetScreenPosFromRawPosition(RawPosition + LocalPosition, fillSize);
            var fillRect = SKRect.Create(fillRectPos.X, fillRectPos.Y, fillSize.X * displayValue, fillSize.Y);
            var rFillRect = new SKRoundRect(fillRect, roundRadius);

            canvas.DrawRoundRect(rBgRect, paint);

            paint.Color = GetColor(contentColor).Value();
            canvas.DrawRoundRect(rFillRect, paint);
        }
    }
}
