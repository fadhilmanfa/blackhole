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
        const float CardTopMargin = 8f;
        const float CardTrayGap = 12f;
        const float CardSideMargin = 8f;

        // Collapsed "playing" layout: album art + equalizer, centered as a
        // pair with a visible gap so the bars don't crowd the artwork.
        const float CollapseArtSize = 28f;
        const float CollapseArtX = -44f;
        const float CollapseEqX = 29f;

        // Empty room above and below the album art. The island height is
        // derived from the art size so the content never hugs the pill
        // edge, while staying centered vertically.
        const float CollapseContentPadding = 8f;
        const float CollapseIslandHeight = CollapseArtSize + CollapseContentPadding * 2f;

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

        float CurrentTrayHeight => tray != null && tray.HasFiles ? TrayHeight : 0f;
        float CurrentCardTrayGap => CurrentTrayHeight > 0 ? CardTrayGap : 0f;
        float CurrentCardY => -(CardBottomMargin + CurrentTrayHeight + CurrentCardTrayGap);

        public override Vec2 IslandSize()
        {
            // Idle and playing share the same compact pill size; idle simply
            // has no content inside it.
            return new Vec2(200, CollapseIslandHeight);
        }

        public override Vec2 IslandSizeBig()
        {
            float cardW = Math.Max(calendarCard.GetWidgetSize().X, mediaCard.GetWidgetSize().X);
            float cardH = MediaState.IsPlaying
                ? mediaCard.GetWidgetSize().Y
                : calendarCard.GetWidgetSize().Y;
            return new Vec2(cardW + CardSideMargin * 2f, cardH + CurrentTrayHeight + CardBottomMargin + CurrentCardTrayGap + CardTopMargin);
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
                roundRadius = 8f
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
            bool playing = MediaState.IsPlaying;

            if (hover && !wasHovering)
                tray.RefreshFiles();
            wasHovering = hover;

            eqWidget.SetActive(!hover && playing);
            collapseArt.SetActive(!hover && playing);

            if (playing)
            {
                int version = MediaState.AlbumArtVersion;
                if (version != shownArtVersion)
                {
                    shownArtVersion = version;
                    var art = MediaState.GetAlbumArtCopy();

                    if (!ReferenceEquals(art, shownArtBitmap))
                    {
                        // Only dispose our own copy, never the Res.Spotify placeholder.
                        shownArtBitmap?.Dispose();
                        shownArtBitmap = art;
                        collapseArt.Image = art ?? Res.Spotify;
                    }
                }
            }

            calendarCard.SetActive(hover && !playing);
            mediaCard.SetActive(hover && playing);
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
