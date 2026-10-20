using DynamicWin.Resources;
using DynamicWin.UI.UIElements;
using DynamicWin.Utils;
using SkiaSharp;
using System;

namespace DynamicWin.UI.Widgets.Big
{
    /// <summary>
    /// Now-playing card shown when the island is expanded while audio
    /// is playing: album art, title/artist, prev/play/next, mini EQ.
    /// </summary>
    public class MediaDetailWidget : WidgetBase
    {
        const float Width = 264;
        const float Height = 132;

        MediaController controller;

        DWImage artImage;
        DWText titleText;
        DWText artistText;

        DWImageButton prevBtn;
        DWImageButton playBtn;
        DWImageButton nextBtn;

        // Bumped whenever MediaState swaps the bitmap, so late-arriving
        // album art swaps in even if the title never changed between polls.
        int lastArtVersion = -1;
        SKBitmap? shownArtBitmap;
        int cycle;

        public MediaDetailWidget(UIObject? parent, Vec2 position, UIAlignment alignment = UIAlignment.TopCenter) : base(parent, position, alignment)
        {
            controller = new MediaController();

            artImage = new DWImage(this, Res.Spotify, new Vec2(12, 24), new Vec2(84, 84), UIAlignment.TopLeft, maskOwnRect: true)
            {
                allowIconThemeColor = false,
                Color = Col.White,
                roundRadius = 12f
            };
            artImage.Anchor = new Vec2(0, 0);
            AddLocalObject(artImage);

            titleText = new DWText(this, "Title", new Vec2(108, 48), UIAlignment.TopLeft)
            {
                Font = Res.InterBold,
                TextSize = 15,
                Color = Theme.TextMain
            };
            titleText.Anchor = new Vec2(0, 0);
            AddLocalObject(titleText);

            artistText = new DWText(this, "Artist", new Vec2(108, 66), UIAlignment.TopLeft)
            {
                Font = Res.InterRegular,
                TextSize = 12,
                Color = Theme.TextThird
            };
            artistText.Anchor = new Vec2(0, 0);
            AddLocalObject(artistText);

            prevBtn = new DWImageButton(this, Res.Previous, new Vec2(128, 92), new Vec2(28, 28), () => controller.Previous(), UIAlignment.TopLeft)
            {
                roundRadius = 25,
                normalColor = Col.Transparent,
                hoverColor = Col.White.Override(a: 0.1f),
                clickColor = Col.White.Override(a: 0.25f),
                imageScale = 0.65f
            };
            prevBtn.Anchor = new Vec2(0, 0);
            AddLocalObject(prevBtn);

            playBtn = new DWImageButton(this, Res.PlayPause, new Vec2(165, 91), new Vec2(30, 30), () => controller.PlayPause(), UIAlignment.TopLeft)
            {
                roundRadius = 25,
                normalColor = Col.Transparent,
                hoverColor = Col.White.Override(a: 0.1f),
                clickColor = Col.White.Override(a: 0.25f),
                imageScale = 0.8f
            };
            playBtn.Anchor = new Vec2(0, 0);
            AddLocalObject(playBtn);

            nextBtn = new DWImageButton(this, Res.Next, new Vec2(204, 92), new Vec2(28, 28), () => controller.Next(), UIAlignment.TopLeft)
            {
                roundRadius = 25,
                normalColor = Col.Transparent,
                hoverColor = Col.White.Override(a: 0.1f),
                clickColor = Col.White.Override(a: 0.25f),
                imageScale = 0.65f
            };
            nextBtn.Anchor = new Vec2(0, 0);
            AddLocalObject(nextBtn);

        }

        protected override float GetWidgetWidth() { return Width; }
        protected override float GetWidgetHeight() { return Height; }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (cycle++ % 16 != 0) return;

            string t = MediaState.Title;
            string a = MediaState.Artist;

            titleText.SetText(string.IsNullOrEmpty(t) ? "Audio Playing" : DWText.Truncate(t, 22));
            artistText.SetText(string.IsNullOrEmpty(a) ? "" : DWText.Truncate(a, 26));

            int version = MediaState.AlbumArtVersion;
            if (version != lastArtVersion)
            {
                lastArtVersion = version;
                var art = MediaState.GetAlbumArtCopy();

                if (!ReferenceEquals(art, shownArtBitmap))
                {
                    // Only dispose our own copy, never the Res.Spotify placeholder.
                    shownArtBitmap?.Dispose();
                    shownArtBitmap = art;
                    artImage.Image = art ?? Res.Spotify;
                }
            }
        }

        public override void OnDestroy()
        {
            shownArtBitmap?.Dispose();
            shownArtBitmap = null;
            base.OnDestroy();
        }

        public override void DrawWidget(SKCanvas canvas)
        {
            var paint = GetPaint();
            paint.Color = GetColor(Theme.WidgetBackground).Value();
            canvas.DrawRoundRect(GetRect(), paint);
        }
    }
}
