using DynamicWin.Resources;
using DynamicWin.UI.UIElements;
using DynamicWin.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace DynamicWin.UI.Widgets.Big
{
    /// <summary>
    /// iOS-style month calendar card shown when the island is expanded
    /// and nothing is playing. Static current month, today highlighted.
    /// </summary>
    public class BigCalendarWidget : WidgetBase
    {
        static readonly string[] MonthNames = new string[]
        {
            "Januari", "Februari", "Maret", "April", "Mei", "Juni",
            "Juli", "Agustus", "September", "Oktober", "November", "Desember"
        };

        static readonly string[] Weekdays = new string[] { "MIN", "SEN", "SEL", "RAB", "KAM", "JUM", "SAB" };

        const float Width = 264;
        const float Height = 208;
        const float PadX = 18;
        const float HeaderTop = 12;
        const float WeekdayY = 56;
        const float GridY = 68;
        const float CellH = 20;

        DWText headerText;
        List<DWText> weekdayCells = new List<DWText>();
        List<DayCell> dayCells = new List<DayCell>();

        int builtYear = -1;
        int builtMonth = -1;
        int dayOffset;
        int daysInMonth;
        int today;

        float cellW;

        class DayCell
        {
            public DWText text;
            public int col;
            public int row;
        }

        public BigCalendarWidget(UIObject? parent, Vec2 position, UIAlignment alignment = UIAlignment.TopCenter) : base(parent, position, alignment)
        {
            cellW = (Width - PadX * 2) / 7f;

            headerText = new DWText(this, "", Vec2.zero, UIAlignment.TopLeft)
            {
                Font = Res.InterBold,
                TextSize = 17,
                Color = Theme.TextMain
            };
            headerText.Anchor = Vec2.zero;
            AddLocalObject(headerText);

            for (int i = 0; i < 7; i++)
            {
                var wd = new DWText(this, Weekdays[i], Vec2.zero, UIAlignment.TopLeft)
                {
                    Font = Res.InterBold,
                    TextSize = 11,
                    Color = i == 0 ? Theme.Error : Theme.TextThird
                };
                wd.Anchor = Vec2.zero;
                AddLocalObject(wd);
                weekdayCells.Add(wd);
            }

            Rebuild(DateTime.Now);
        }

        protected override float GetWidgetWidth() { return Width; }
        protected override float GetWidgetHeight() { return Height; }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            var now = DateTime.Now;
            if (now.Year != builtYear || now.Month != builtMonth || now.Day != today)
                Rebuild(now);
        }

        void Layout()
        {
            // DWText draws from the blob origin (baseline start), not the visible
            // top-left, so center on the measured glyph bounds to keep every
            // column symmetric with the today circle.
            headerText.SetTopCenterPosition(Width / 2f, HeaderTop);

            for (int i = 0; i < 7; i++)
            {
                float cx = PadX + i * cellW + cellW / 2;
                weekdayCells[i].SetCenterPosition(new Vec2(cx, WeekdayY));
            }

            foreach (var day in dayCells)
                day.text.SetCenterPosition(GetCellCenter(day.col, day.row));
        }

        Vec2 GetCellCenter(int col, int row)
        {
            return new Vec2(
                PadX + col * cellW + cellW / 2f,
                GridY + row * CellH + CellH / 2f);
        }

        void Rebuild(DateTime now)
        {
            builtYear = now.Year;
            builtMonth = now.Month;
            today = now.Day;

            dayOffset = (int)new DateTime(builtYear, builtMonth, 1).DayOfWeek;
            daysInMonth = DateTime.DaysInMonth(builtYear, builtMonth);

            foreach (var day in dayCells)
                DestroyLocalObject(day.text);
            dayCells.Clear();

            string header = MonthNames[builtMonth - 1] + " " + builtYear;
            headerText.SilentSetText(header);

            for (int d = 1; d <= daysInMonth; d++)
            {
                int idx = dayOffset + d - 1;
                int col = idx % 7;
                int row = idx / 7;

                string label = d.ToString();
                bool isToday = d == today;
                var cell = new DWText(this, label, Vec2.zero, UIAlignment.TopLeft)
                {
                    Font = isToday ? Res.InterBold : Res.InterRegular,
                    TextSize = isToday ? 9 : 13,
                    Color = isToday ? Col.White : (col == 0 ? Theme.Error : Theme.TextMain)
                };
                cell.Anchor = Vec2.zero;
                AddLocalObject(cell);
                dayCells.Add(new DayCell
                {
                    text = cell,
                    col = col,
                    row = row
                });
            }

            Layout();
        }

        public override void DrawWidget(SKCanvas canvas)
        {
            var paint = GetPaint();
            paint.Color = GetColor(Theme.WidgetBackground).Value();
            canvas.DrawRoundRect(GetRect(), paint);

            // Red circle behind today (drawn before/under the day texts).
            int idx = dayOffset + today - 1;
            if (today >= 1 && today <= daysInMonth)
            {
                int col = idx % 7;
                int row = idx / 7;
                Vec2 center = GetCellCenter(col, row);
                float cx = Position.X + center.X;
                float cy = Position.Y + center.Y;

                var circlePaint = GetPaint();
                circlePaint.Color = GetColor(Col.FromHex("#ff3b30")).Value();
                canvas.DrawCircle(cx, cy, 10f, circlePaint);
            }
        }
    }
}
