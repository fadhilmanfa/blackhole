using DynamicWin.Main;
using DynamicWin.Resources;
using DynamicWin.UI;
using DynamicWin.UI.UIElements;
using DynamicWin.Utils;
using SkiaSharp;
using System;

namespace DynamicWin.UI.Menu.Menus
{
    /// <summary>
    /// One suspended activity, drawn as a circular bubble next to the island.
    ///
    /// Bubbles are only interactive in the compact state. When the island is
    /// expanded the bubble fades out but the activity stays alive in
    /// HomeMenu's activity list.
    /// </summary>
    internal sealed class ActivityBubble : UIObject
    {
        public const float Diameter = 32f;
        public const float Radius = Diameter / 2f;
        public const float Gap = 10f;

        const float CenterY = 19f;
        const float MergeDuration = 0.34f;

        enum BubblePhase
        {
            Hidden,
            Idle,
            Detaching,
            Merging
        }

        readonly IslandObject island;
        readonly Action<IslandActivityKind> onClick;
        readonly DWImage art;
        readonly DWImage icon;

        IslandActivityKind? kind;
        BubblePhase phase = BubblePhase.Hidden;

        Vec2 center;
        Vec2 velocity = Vec2.zero;
        Vec2 targetCenter;

        Vec2 mergeStart = Vec2.zero;
        Vec2 mergeTarget = Vec2.zero;
        float mergeT;

        float visibility;
        float contentAnim = 1f;
        float hover;
        float scale = 1f;

        bool visibleRequested;
        bool clearOnFade;
        bool interactionEnabled = true;

        public IslandActivityKind? Kind => kind;
        public bool IsMerging => phase == BubblePhase.Merging;
        public bool IsShown => kind != null && phase != BubblePhase.Hidden && visibleRequested;

        public ActivityBubble(IslandObject island, Action<IslandActivityKind> onClick)
            : base(island, Vec2.zero, new Vec2(Diameter, Diameter), UIAlignment.TopCenter)
        {
            this.island = island;
            this.onClick = onClick;

            Anchor = new Vec2(0.5f, 0.5f);
            maskInToIsland = false;
            roundRadius = Radius;
            expandInteractionRect = 4;

            targetCenter = GetTargetCenter(island);
            center = targetCenter;

            art = new DWImage(
                this,
                Res.Spotify,
                Vec2.zero,
                new Vec2(Diameter, Diameter),
                UIAlignment.Center,
                maskOwnRect: true)
            {
                allowIconThemeColor = false,
                Color = Col.White,
                roundRadius = Radius,
                Fit = DWImage.ImageFit.Cover
            };
            AddLocalObject(art);

            icon = new DWImage(
                this,
                Res.VolumeOff,
                Vec2.zero,
                new Vec2(16, 16),
                UIAlignment.Center)
            {
                allowIconThemeColor = true,
                Color = Theme.IconColor ?? Col.White
            };
            AddLocalObject(icon);

            art.SilentSetActive(false);
            icon.SilentSetActive(false);

            Alpha = 0f;
            UpdateLayout();
        }

        public static Vec2 GetTargetCenter(IslandObject island)
        {
            return new Vec2(
                island.Position.X - Gap - Radius,
                island.Position.Y + CenterY);
        }

        public void SetInteractive(bool enabled)
        {
            interactionEnabled = enabled;
        }

        public void SetContent(IslandActivityKind newKind, SKBitmap? mediaArt)
        {
            if (kind != newKind)
            {
                kind = newKind;
                contentAnim = 0f;

                if (phase == BubblePhase.Hidden)
                    phase = BubblePhase.Idle;
            }

            if (newKind == IslandActivityKind.Media)
            {
                SetMediaArt(mediaArt);
                art.SilentSetActive(true);
                icon.SilentSetActive(false);
            }
            else
            {
                icon.SilentSetActive(true);
                art.SilentSetActive(false);
            }
        }

        public void SetMediaArt(SKBitmap? mediaArt)
        {
            var bitmap = mediaArt ?? Res.Spotify;
            if (!ReferenceEquals(art.Image, bitmap))
                art.Image = bitmap;
        }

        public void SetTarget(Vec2 target)
        {
            targetCenter = target;
        }

        public void SetVisible(bool visible)
        {
            if (phase == BubblePhase.Merging) return;

            visibleRequested = visible;

            if (visible && kind != null && phase == BubblePhase.Hidden)
            {
                phase = BubblePhase.Idle;
                center = targetCenter;
                velocity = Vec2.zero;
                visibility = 0f;
            }
        }

        public void SnapToTarget()
        {
            center = targetCenter;
            velocity = Vec2.zero;

            if (kind != null && phase == BubblePhase.Hidden)
                phase = BubblePhase.Idle;

            UpdateLayout();
        }

        public void ShowIdle()
        {
            if (kind == null) return;

            phase = BubblePhase.Idle;
            center = targetCenter;
            velocity = Vec2.zero;
            visibility = 0f;
            contentAnim = 1f;
            visibleRequested = true;
            interactionEnabled = true;
        }

        public void BeginDetach(Vec2 startCenter, float startScale)
        {
            if (kind == null) return;

            phase = BubblePhase.Detaching;
            center = startCenter;
            velocity = Vec2.zero;
            visibility = 0f;
            scale = Mathf.Clamp(startScale, 0.4f, 1f);
            contentAnim = 1f;
            visibleRequested = true;
            interactionEnabled = true;
        }

        public void BeginMerge(Vec2 target)
        {
            if (kind == null) return;

            mergeStart = center;
            mergeTarget = target;
            mergeT = 0f;
            phase = BubblePhase.Merging;
            visibleRequested = true;
            interactionEnabled = false;

            // The return trip slides under the island instead of pulsing on
            // top of it.
            drawBehindIsland = true;
        }

        public void CancelMerge()
        {
            if (phase != BubblePhase.Merging) return;

            phase = BubblePhase.Idle;
            mergeT = 0f;
            center = targetCenter;
            velocity = Vec2.zero;
            visibility = 1f;
            visibleRequested = true;
            interactionEnabled = true;
            drawBehindIsland = false;
        }

        public void ClearActivity()
        {
            if (kind == null) return;

            visibleRequested = false;
            clearOnFade = true;
        }

        public void HideImmediate()
        {
            kind = null;
            phase = BubblePhase.Hidden;
            visibility = 0f;
            contentAnim = 1f;
            hover = 0f;
            scale = 1f;
            clearOnFade = false;
            visibleRequested = false;
            interactionEnabled = true;
            drawBehindIsland = false;
            center = targetCenter;
            velocity = Vec2.zero;
            art.SilentSetActive(false);
            icon.SilentSetActive(false);
            Alpha = 0f;
            UpdateLayout();
        }

        public override void Update(float deltaTime)
        {
            if (kind == null)
            {
                Alpha = 0f;
                return;
            }

            contentAnim = Mathf.Lerp(contentAnim, 1f, 12f * deltaTime);

            if (phase == BubblePhase.Merging)
            {
                UpdateMerge(deltaTime);
                Alpha = visibility * contentAnim;
                UpdateLayout();
                return;
            }

            hover = Mathf.Lerp(hover, IsHovering ? 1f : 0f, 14f * deltaTime);

            if (!Settings.AllowAnimation)
            {
                center = targetCenter;
                velocity = Vec2.zero;
                visibility = visibleRequested ? 1f : 0f;
                scale = 1f + hover * 0.08f;

                if (!visibleRequested && clearOnFade)
                {
                    HideImmediate();
                    return;
                }

                phase = BubblePhase.Idle;
            }
            else
            {
                SpringTo(targetCenter, deltaTime);
                visibility = Mathf.Lerp(visibility, visibleRequested ? 1f : 0f, 14f * deltaTime);

                if (!visibleRequested && visibility < 0.01f)
                {
                    visibility = 0f;

                    if (clearOnFade)
                    {
                        HideImmediate();
                        return;
                    }
                }

                if (phase == BubblePhase.Detaching &&
                    Vec2.Distance(center, targetCenter) < 1.5f)
                {
                    phase = BubblePhase.Idle;
                }

                scale = Mathf.Lerp(scale, 1f + hover * 0.08f, 14f * deltaTime);
            }

            Alpha = visibility * contentAnim;
            icon.Color = Theme.IconColor ?? Col.White;
            UpdateLayout();
        }

        void UpdateMerge(float deltaTime)
        {
            if (!Settings.AllowAnimation)
            {
                center = mergeTarget;
                FinishMerge();
                return;
            }

            mergeT += deltaTime / MergeDuration;

            if (mergeT >= 1f)
            {
                center = mergeTarget;
                FinishMerge();
                return;
            }

            float t = Easings.EaseInSin(mergeT);
            center = Vec2.lerp(mergeStart, mergeTarget, t);
            scale = Mathf.Lerp(scale, 1f, 12f * deltaTime);
            visibility = 1f;
        }

        void FinishMerge()
        {
            kind = null;
            phase = BubblePhase.Hidden;
            visibility = 0f;
            contentAnim = 1f;
            hover = 0f;
            scale = 1f;
            clearOnFade = false;
            visibleRequested = false;
            interactionEnabled = true;
            drawBehindIsland = false;
            art.SilentSetActive(false);
            icon.SilentSetActive(false);
            Alpha = 0f;
            UpdateLayout();
        }

        void SpringTo(Vec2 target, float deltaTime)
        {
            const float stiffness = 220f;
            const float damping = 20f;

            int steps = Math.Max(1, (int)Math.Ceiling(deltaTime / (1f / 120f)));
            float step = deltaTime / steps;

            for (int i = 0; i < steps; i++)
            {
                velocity += (target - center) * (stiffness * step);
                velocity *= Mathf.Clamp(1f - damping * step, 0f, 1f);
                center += velocity * step;
            }
        }

        void UpdateLayout()
        {
            LocalPosition = new Vec2(
                center.X - (island.Position.X + island.Size.X * 0.5f),
                center.Y - island.Position.Y);

            float visualSize = Diameter * scale;
            art.Size = new Vec2(visualSize, visualSize);
            art.roundRadius = Radius * scale;
            icon.Size = new Vec2(16f * scale, 16f * scale);
        }

        public override void Draw(SKCanvas canvas)
        {
            if (kind == null || Alpha <= 0.01f) return;

            var bubbleCenter = Position + Size / 2f;
            float radius = Radius * scale;

            var paint = GetPaint();
            var background = Theme.IslandBackground ?? new Col(0, 0, 0);
            paint.Color = background.Override(a: Alpha).Value();
            paint.ImageFilter = SKImageFilter.CreateDropShadow(
                0,
                2,
                8,
                8,
                new Col(0, 0, 0).Override(a: 0.35f * Alpha).Value());
            canvas.DrawCircle(bubbleCenter.X, bubbleCenter.Y, radius, paint);

            paint.ImageFilter = null;
            paint.Color = (Theme.TextThird ?? Col.White).Override(a: 0.5f * Alpha).Value();
            paint.IsStroke = true;
            paint.StrokeWidth = 1.2f;
            canvas.DrawCircle(bubbleCenter.X, bubbleCenter.Y, radius, paint);
        }

        public override SKRoundRect GetInteractionRect()
        {
            if (!IsInteractive)
                return new SKRoundRect(SKRect.Create(-1000, -1000, 1, 1), 0);

            var rect = SKRect.Create(
                Position.X - 4,
                Position.Y - 4,
                Size.X + 8,
                Size.Y + 8);

            return new SKRoundRect(rect, Radius + 4);
        }

        bool IsInteractive =>
            kind != null &&
            interactionEnabled &&
            visibleRequested &&
            phase != BubblePhase.Hidden &&
            phase != BubblePhase.Merging &&
            Alpha > 0.5f;

        public override void OnMouseUp()
        {
            base.OnMouseUp();

            if (kind.HasValue && IsInteractive)
                onClick?.Invoke(kind.Value);
        }
    }
}
