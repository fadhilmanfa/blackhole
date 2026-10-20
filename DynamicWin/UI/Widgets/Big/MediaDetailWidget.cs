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

        // Short fade/pop when the cover changes, so skipping tracks does
        // not hard-cut between two unrelated images.
        float artSwapAnim = 1f;
        const float ArtSwapFadeSpeed = 12f;

        // Play/pause button: shows the action it will perform. MediaState is
        // debounced (up to ~2 s), so clicks flip the glyph immediately and a
        // short override keeps it from bouncing while the state settles.
        bool? iconShowsPlaying;
        long iconOverrideUntil;
        const int IconOverrideMs = 2500;

        public MediaDetailWidget(UIObject? parent, Vec2 position, UIAlignment alignment = UIAlignment.TopCenter) : base(parent, position, alignment)
        {
            controller = new MediaController();

            artImage = new DWImage(this, Res.Spotify, new Vec2(12, 24), new Vec2(84, 84), UIAlignment.TopLeft, maskOwnRect: true)
            {
                allowIconThemeColor = false,
                Color = Col.White,
                roundRadius = 12f,
                // Covers come in many aspect ratios: crop-to-fill keeps the
                // square shape instead of squashing portrait/wide artwork.
                Fit = DWImage.ImageFit.Cover
            };
            artImage.Anchor = new Vec2(0, 0);
            AddLocalObject(artImage);

            titleText = new DWText(this, "Title", new Vec2(108, 48), UIAlignment.TopLeft)
            {
                Font = Res.InterBold,
                TextSize = 15,
                Color = Theme.TextMain,
                marquee = true,
                marqueeMaxWidth = 144
            };
            titleText.Anchor = new Vec2(0, 0);
            AddLocalObject(titleText);

            artistText = new DWText(this, "Artist", new Vec2(108, 66), UIAlignment.TopLeft)
            {
                Font = Res.InterRegular,
                TextSize = 12,
                Color = Theme.TextThird,
                marquee = true,
                marqueeMaxWidth = 144
            };
            artistText.Anchor = new Vec2(0, 0);
            AddLocalObject(artistText);

            // Controls sit on the same left edge as the title (x=108) and a
            // little higher, so the block reads as one left-aligned column.
            prevBtn = new DWImageButton(this, Res.Previous, new Vec2(108, 84), new Vec2(28, 28), () => controller.Previous(), UIAlignment.TopLeft)
            {
                roundRadius = 25,
                normalColor = Col.Transparent,
                hoverColor = Col.White.Override(a: 0.1f),
                clickColor = Col.White.Override(a: 0.25f),
                imageScale = 0.65f
            };
            prevBtn.Anchor = new Vec2(0, 0);
            AddLocalObject(prevBtn);

            playBtn = new DWImageButton(this, Res.Play, new Vec2(145, 83), new Vec2(30, 30), TogglePlayPause, UIAlignment.TopLeft)
            {
                roundRadius = 25,
                normalColor = Col.Transparent,
                hoverColor = Col.White.Override(a: 0.1f),
                clickColor = Col.White.Override(a: 0.25f),
                imageScale = 0.8f
            };
            playBtn.Anchor = new Vec2(0, 0);
            playBtn.Image.Image = MediaState.IsPlaying ? Res.Pause : Res.Play;
            AddLocalObject(playBtn);

            nextBtn = new DWImageButton(this, Res.Next, new Vec2(184, 84), new Vec2(28, 28), () => controller.Next(), UIAlignment.TopLeft)
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

            UpdatePlayIcon();

            artSwapAnim = Mathf.Lerp(artSwapAnim, 1f, ArtSwapFadeSpeed * deltaTime);
            artImage.Alpha = artSwapAnim;

            if (cycle++ % 16 != 0) return;

            string t = MediaState.Title;
            string a = MediaState.Artist;

            titleText.SetText(string.IsNullOrEmpty(t) ? "Audio Playing" : t);
            artistText.SetText(a ?? "");

            int version = MediaState.AlbumArtVersion;
            if (version != lastArtVersion)
            {
                lastArtVersion = version;
                var art = MediaState.GetAlbumArtCopy();

                if (!ReferenceEquals(art, shownArtBitmap))
                {
                    if (art != null)
                    {
                        // Only dispose our own copy, never the Res.Spotify placeholder.
                        shownArtBitmap?.Dispose();
                        shownArtBitmap = art;
                        artImage.Image = art;
                        artSwapAnim = 0f;
                    }
                    else if (shownArtBitmap == null)
                    {
                        artImage.Image = Res.Spotify;
                    }
                    // Between tracks, keep the previous cover visible until
                    // the new fetch lands instead of flashing the placeholder.
                }
            }
        }

        void TogglePlayPause()
        {
            bool target = !(iconShowsPlaying ?? MediaState.IsPlaying);
            iconShowsPlaying = target;
            iconOverrideUntil = Environment.TickCount64 + IconOverrideMs;
            UpdatePlayIcon();
            controller.PlayPause();
        }

        void UpdatePlayIcon()
        {
            bool actual = MediaState.IsPlaying;

            if (iconShowsPlaying.HasValue &&
                (actual == iconShowsPlaying.Value || Environment.TickCount64 > iconOverrideUntil))
            {
                // Real playback state caught up, or the key press never took
                // effect: stop overriding and follow the state again.
                iconShowsPlaying = null;
            }

            var glyph = (iconShowsPlaying ?? actual) ? Res.Pause : Res.Play;
            if (!ReferenceEquals(playBtn.Image.Image, glyph))
                playBtn.Image.Image = glyph;
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
