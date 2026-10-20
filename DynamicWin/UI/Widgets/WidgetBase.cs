using DynamicWin.Main;
using DynamicWin.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicWin.UI.Widgets
{
    public class WidgetBase : UIObject
    {
        protected bool isSmallWidget = false;
        public bool IsSmallWidget { get { return isSmallWidget; } }

        //DWText widgetName;

        public WidgetBase(UIObject? parent, Vec2 position, UIAlignment alignment = UIAlignment.TopCenter) : base(parent, position, Vec2.zero, alignment)
        {
            Size = GetWidgetSize();

            var objs = InitializeWidget();
            objs.ForEach(obj => AddLocalObject(obj));

            roundRadius = 15f;

            /*widgetName = new DWText(this, GetWidgetName(), Vec2.zero, UIAlignment.Center)
            {
                Font = Res.InterBold,
                textSize = 20
            };*/
        }

        public Vec2 GetWidgetSize() { return new Vec2(GetWidgetWidth(), GetWidgetHeight()); }

        protected virtual float GetWidgetHeight() { return 100; }
        protected virtual float GetWidgetWidth() { return 200; }

        public List<UIObject> InitializeWidget()
        {
            return new List<UIObject>();
        }

        float hoverProgress = 0f;

        public override void Draw(SKCanvas canvas)
        {
            Size = GetWidgetSize();

            hoverProgress = Mathf.Lerp(hoverProgress, IsHovering ? 1f : 0f, 10f * RendererMain.Instance.DeltaTime);

            if(hoverProgress > 0.025f)
            {
                var paint = GetPaint();
                paint.ImageFilter = SKImageFilter.CreateDropShadowOnly(0, 0, hoverProgress * 10, hoverProgress * 10, Theme.WidgetBackground.Override(a: hoverProgress / 10).Value());

                int ogC = canvas.Save();

                var p = Position + Size / 2;
                canvas.Scale(1 + hoverProgress / 60, 1 + hoverProgress / 60, p.X, p.Y);

                int sc = canvas.Save();
                canvas.ClipRoundRect(GetRect(), SKClipOperation.Difference, antialias: true);
                canvas.DrawRoundRect(GetRect(), paint);
                canvas.RestoreToCount(sc);
            }


            /*if (!isEditMode || isSmallWidget)
            {
                drawLocalObjects = true; */
            DrawWidget(canvas);
            /*            }
                        else
                        {
                            widgetName.blurAmount = GetBlur();
                            widgetName.DrawCall(canvas);
                            drawLocalObjects = false;
                        }*/


            /*if (!IsSmallWidget)
            {
                var bPaint = GetPaint();
                bPaint.ImageFilter = SKImageFilter.CreateBlur(100, 100);
                bPaint.BlendMode = SKBlendMode.SrcOver;
                bPaint.Color = Col.White.Override(a: 0.4f).Value();

                int canvasSave = canvas.Save();
                canvas.ClipRoundRect(GetRect(), antialias: true);
                canvas.DrawCircle(RendererMain.CursorPosition.X + 12.5f, RendererMain.CursorPosition.Y + 20, 35, bPaint);

                canvas.RestoreToCount(canvasSave);
            }*/

            //canvas.RestoreToCount(ogC);
        }

        public virtual void DrawWidget(SKCanvas canvas) { }
    }
}
