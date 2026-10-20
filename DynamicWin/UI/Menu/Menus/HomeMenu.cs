using DynamicWin.Main;
using DynamicWin.Resources;
using DynamicWin.UI.UIElements;
using DynamicWin.UI.UIElements.Custom;
using DynamicWin.UI.Widgets.Big;
using DynamicWin.UI.Widgets.Small;
using DynamicWin.Utils;
using SkiaSharp;

namespace DynamicWin.UI.Menu.Menus
{
    /// <summary>
    /// Fixed layout, no dynamic widgets, no editor.
    /// Collapsed: empty black pill when idle, album art + equalizer
    /// when audio is playing.
    /// Expanded: iOS-style calendar when idle, now-playing card
    /// when audio is playing, file tray always at the bottom.
    /// </summary>
    public class HomeMenu : BaseMenu
    {
        const float TrayHeight = 140f;
        const float CardBottomMargin = 8f;
        const float CardTrayGap = 12f;

        // Collapsed "playing" layout: album art + equalizer, centered as a
        // pair with a visible gap so the bars don't crowd the artwork.
        // Art 24px + gap 16px + EQ 70px = 110px total, centered on screen:
        // artMid = -55+12 = -43, eqMid = +55-35 = +20.
        const float CollapseArtSize = 24f;
        const float CollapseArtX = -43f;
        const float CollapseEqX = 20f;

        // ── Island sizes ────────────────────────────────────────────────
        // Every state declares its own size instead of inheriting one
        // shared pill, so the idle ("diam") island and the music island
        // can be tuned independently.
        //
        // Collapsed pill widths are independent: idle is an empty capsule
        // and can be the narrowest, while music needs room for its content
        // (art -55..-31 + EQ -15..+55 = 110px, so 145 leaves ~17px of air).
        //
        // Collapsed pill height, shared by the idle and music states.
        // CollapseArtSize (24) + 14 keeps ~7px of air above and below, so
        // the artwork and EQ keep their size while the capsule gets slimmer.
        const float CollapsedHeight = 38f;

        static readonly Vec2 IdleIslandSize = new Vec2(120, CollapsedHeight);
        static readonly Vec2 MusicIslandSize = new Vec2(145, CollapsedHeight);

        // Expanded: the tray is added on top when files are present.
        // Width must stay >= the card width + 16 (280) or the now-playing
        // card / calendar gets clipped by the island mask.
        // Height = card height + 8 top + 8 bottom:
        // idle calendar 208 + 16 = 224, music card 132 + 16 = 148.
        static readonly Vec2 IdleExpandedSize = new Vec2(280, 224);
        static readonly Vec2 MusicExpandedSize = new Vec2(280, 148);

        SmallVisualizerWidget eqWidget;
        DWImage collapseArt;

        // Bumped whenever MediaState swaps the bitmap, so late-arriving
        // album art swaps in without waiting for a title change.
        int shownArtVersion = -1;
        SKBitmap? shownArtBitmap;

        BigCalendarWidget calendarCard;
        MediaDetailWidget mediaCard;

        Tray tray;
        bool wasHovering;

        // Collapsed music content (art + EQ) fades and pops in when playback
        // starts, and out when it stops or the island expands on hover.
        float musicContentAnim;
        float artSwapAnim = 1f;

        const float MusicContentFadeSpeed = 9f;
        const float ArtSwapFadeSpeed = 12f;

        float CurrentTrayHeight => tray != null && tray.HasFiles ? TrayHeight : 0f;
        float CurrentCardTrayGap => CurrentTrayHeight > 0 ? CardTrayGap : 0f;
        float CurrentCardY => -(CardBottomMargin + CurrentTrayHeight + CurrentCardTrayGap);

        public override Vec2 IslandSize()
        {
            // Idle ("diam") and music have their own declared pill sizes.
            return MediaState.IsMediaVisible ? MusicIslandSize : IdleIslandSize;
        }

        public override Vec2 IslandSizeBig()
        {
            var baseSize = MediaState.IsMediaVisible ? MusicExpandedSize : IdleExpandedSize;

            // The tray grows the island downwards only while it holds files;
            // the gap keeps the card from touching the tray icons.
            return new Vec2(baseSize.X, baseSize.Y + CurrentTrayHeight + CurrentCardTrayGap);
        }

        public override List<UIObject> InitializeMenu(IslandObject island)
        {
            var objects = base.InitializeMenu(island);

            MediaState.EnsureStarted();

            eqWidget = new SmallVisualizerWidget(island, new Vec2(CollapseEqX, 0), UIAlignment.Center);
            objects.Add(eqWidget);

            collapseArt = new DWImage(island, Res.Spotify, new Vec2(CollapseArtX, 0), new Vec2(CollapseArtSize, CollapseArtSize), UIAlignment.Center, maskOwnRect: true)
            {
                allowIconThemeColor = false,
                Color = Col.White,
                roundRadius = 8f,
                // Crop-to-fill so non-square covers still sit perfectly in
                // the small square slot instead of being stretched.
                Fit = DWImage.ImageFit.Cover
            };
            objects.Add(collapseArt);

            // Create the tray first so its cached file count can determine
            // whether the expanded island needs room for it.
            tray = new Tray(island, new Vec2(0, -CardBottomMargin), Vec2.zero, UIAlignment.BottomCenter)
            {
                Anchor = new Vec2(0.5f, 1f),
                ShowEmptyPlaceholder = false
            };
            tray.SilentSetActive(false);

            float cardY = CurrentCardY;

            calendarCard = new BigCalendarWidget(island, new Vec2(0, cardY), UIAlignment.BottomCenter);
            calendarCard.Anchor = new Vec2(0.5f, 1f);
            calendarCard.SilentSetActive(false);
            objects.Add(calendarCard);

            mediaCard = new MediaDetailWidget(island, new Vec2(0, cardY), UIAlignment.BottomCenter);
            mediaCard.Anchor = new Vec2(0.5f, 1f);
            mediaCard.SilentSetActive(false);
            objects.Add(mediaCard);

            objects.Add(tray);

            return objects;
        }

        public override void Update()
        {
            bool hover = RendererMain.Instance.MainIsland.IsHovering;
            bool mediaVisible = MediaState.IsMediaVisible;

            if (hover && !wasHovering)
                tray.RefreshFiles();
            wasHovering = hover;

            float dt = RendererMain.Instance.DeltaTime;
            bool showMusic = !hover && mediaVisible;

            // Shared 0..1 progress for the collapsed cover + EQ pair.
            musicContentAnim = Mathf.Lerp(musicContentAnim, showMusic ? 1f : 0f, MusicContentFadeSpeed * dt);
            artSwapAnim = Mathf.Lerp(artSwapAnim, 1f, ArtSwapFadeSpeed * dt);

            float reveal = Easings.EaseOutCubic(musicContentAnim);
            bool contentVisible = reveal > 0.01f;

            eqWidget.SilentSetActive(contentVisible);
            collapseArt.SilentSetActive(contentVisible);

            // Slide up a touch while fading, and let the cover pop in from
            // 75% so the pair feels like it grows out of the pill.
            float lift = (1f - reveal) * 4f;
            collapseArt.LocalPosition = new Vec2(0, lift);
            eqWidget.LocalPosition = new Vec2(0, lift);

            collapseArt.Alpha = Math.Min(reveal, artSwapAnim);
            eqWidget.Alpha = reveal;

            float artScale = (0.75f + 0.25f * reveal) * (0.88f + 0.12f * artSwapAnim);
            collapseArt.Size = new Vec2(CollapseArtSize * artScale, CollapseArtSize * artScale);

            if (mediaVisible)
            {
                int version = MediaState.AlbumArtVersion;
                if (version != shownArtVersion)
                {
                    shownArtVersion = version;
                    var art = MediaState.GetAlbumArtCopy();

                    if (!ReferenceEquals(art, shownArtBitmap))
                    {
                        if (art != null)
                        {
                            // Only dispose our own copy, never the Res.Spotify placeholder.
                            shownArtBitmap?.Dispose();
                            shownArtBitmap = art;
                            collapseArt.Image = art;

                            // New cover during playback: pop it in instead of
                            // hard-cutting between two unrelated images.
                            if (reveal > 0.5f) artSwapAnim = 0f;
                        }
                        else if (shownArtBitmap == null)
                        {
                            // First track without artwork: show the placeholder.
                            collapseArt.Image = Res.Spotify;
                        }
                        // If artwork briefly disappears between tracks, keep
                        // the previous cover until the new fetch lands.
                    }
                }
            }

            calendarCard.SetActive(hover && !mediaVisible);
            mediaCard.SetActive(hover && mediaVisible);
            tray.SetActive(hover);

            float cardY = CurrentCardY;
            calendarCard.Position = new Vec2(0, cardY);
            mediaCard.Position = new Vec2(0, cardY);

            float trayHeight = CurrentTrayHeight;
            tray.Size = new Vec2(IslandSizeBig().X - 30, trayHeight);
        }

        public override void OnDeload()
        {
            wasHovering = false;
            base.OnDeload();
        }
    }
}
