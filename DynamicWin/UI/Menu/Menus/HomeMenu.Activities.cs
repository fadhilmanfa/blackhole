using DynamicWin.Main;
using DynamicWin.Resources;
using DynamicWin.UI;
using DynamicWin.UI.UIElements;
using DynamicWin.Utils;
using System;
using System.Collections.Generic;

namespace DynamicWin.UI.Menu.Menus
{
    internal enum IslandActivityKind
    {
        Media,
        Silent
    }

    internal sealed class IslandActivity
    {
        public IslandActivityKind Kind { get; }
        public long ExpiresAt { get; set; }

        public IslandActivity(IslandActivityKind kind, long expiresAt = 0)
        {
            Kind = kind;
            ExpiresAt = expiresAt;
        }
    }

    public partial class HomeMenu
    {
        // The volume popup used to live for 2.75 s before closing itself.
        // The activity keeps exactly the same lifetime.
        const long SilentTtlMs = 2750;
        const long PromotedExpandMs = 4000;
        const long CompactReturnResetTimeoutMs = 600;

        enum CompactReturnPhase
        {
            None,
            ResetToDefault,
            CoverEntering
        }

        readonly List<IslandActivity> activities = new();

        VolumeCompactView volumeView = null!;
        ActivityBubble bubble = null!;

        CompactReturnPhase compactReturnPhase;
        long compactReturnStartedAt;

        internal bool IsCompactReturning => compactReturnPhase != CompactReturnPhase.None;

        IslandActivity? PrimaryActivity => activities.Count > 0 ? activities[0] : null;
        IslandActivity? SecondaryActivity => activities.Count > 1 ? activities[1] : null;

        bool IsPrimaryMedia => PrimaryActivity?.Kind == IslandActivityKind.Media;
        bool IsPrimarySilent => PrimaryActivity?.Kind == IslandActivityKind.Silent;

        void InitializeActivities(IslandObject island, List<UIObject> objects)
        {
            volumeView = new VolumeCompactView(island);
            objects.AddRange(volumeView.Objects);

            bubble = new ActivityBubble(island, Promote);
            objects.Add(bubble);
        }

        /// <summary>
        /// Called by the volume-key handler in RendererMain. Volume keys are
        /// also the entry point for the "Silent" activity.
        /// </summary>
        public void NotifyVolumeKeys()
        {
            // A fresh volume press cancels the staged return.
            if (IsCompactReturning)
                CancelCompactReturn();

            long now = Environment.TickCount64;
            var silent = activities.Find(a => a.Kind == IslandActivityKind.Silent);

            if (silent == null)
            {
                silent = new IslandActivity(IslandActivityKind.Silent, now + SilentTtlMs);
                activities.Insert(0, silent);
                volumeView.Show();
                DetachMediaBubble();
                return;
            }

            silent.ExpiresAt = now + SilentTtlMs;

            if (activities[0] == silent) return;

            // The key revived a suspended volume activity: promote it and
            // let media take the now-free bubble slot.
            activities.Remove(silent);
            activities.Insert(0, silent);
            volumeView.Show();

            if (bubble.Kind != IslandActivityKind.Silent)
                DetachMediaBubble();
        }

        void Promote(IslandActivityKind kind)
        {
            if (IsCompactReturning)
                CancelCompactReturn();

            int index = activities.FindIndex(a => a.Kind == kind);
            if (index <= 0) return;

            var activity = activities[index];
            activities.RemoveAt(index);
            activities.Insert(0, activity);

            if (kind == IslandActivityKind.Silent)
            {
                activity.ExpiresAt = Environment.TickCount64 + SilentTtlMs;
                autoExpandUntil = 0;
                volumeView.Show();

                // The clicked silent bubble now represents the suspended
                // media activity; swap its content in place.
                bubble.SetContent(IslandActivityKind.Media, collapseArt.Image);
                bubble.SetTarget(BubbleTargetCenter());
                bubble.SnapToTarget();
                return;
            }

            // Media takes the pill back and grows immediately. The volume
            // activity continues its lifetime as the hidden bubble until it
            // expires (bubbles are only drawn in the compact state).
            autoExpandUntil = Environment.TickCount64 + PromotedExpandMs;
            volumeView.Hide();
            bubble.SetTarget(BubbleTargetCenter());
            bubble.BeginMerge(CompactArtCenter());
        }

        void SyncMediaActivity()
        {
            int index = activities.FindIndex(a => a.Kind == IslandActivityKind.Media);
            bool visible = MediaState.IsMediaVisible;

            if (visible && index < 0)
            {
                activities.Add(new IslandActivity(IslandActivityKind.Media));
            }
            else if (!visible && index >= 0)
            {
                bool wasPrimary = index == 0;
                activities.RemoveAt(index);

                if (IsCompactReturning)
                    CancelCompactReturn();

                // Media disappearing can expose a suspended volume pill.
                // Refresh its hover guard so an old cursor position cannot
                // dismiss it immediately.
                if (wasPrimary && IsPrimarySilent)
                    volumeView.Show();
            }
        }

        void TickActivities(long now)
        {
            for (int i = activities.Count - 1; i >= 0; i--)
            {
                var activity = activities[i];
                if (activity.Kind != IslandActivityKind.Silent ||
                    activity.ExpiresAt == 0 ||
                    now < activity.ExpiresAt)
                {
                    continue;
                }

                bool wasPrimary = i == 0;
                activities.RemoveAt(i);

                if (!wasPrimary) continue;

                volumeView.Hide();

                // The suspended media activity takes the pill back. This is
                // staged: reset the pill to the default compact size first,
                // then let the cover slide behind it, then reveal the song.
                if (bubble.Kind == IslandActivityKind.Media)
                    BeginCompactReturn(now);
            }
        }

        void DismissSilent()
        {
            int index = activities.FindIndex(a => a.Kind == IslandActivityKind.Silent);
            if (index < 0) return;

            activities.RemoveAt(index);
            volumeView.Hide();

            if (bubble.Kind == IslandActivityKind.Media)
                bubble.BeginMerge(CompactArtCenter());
        }

        void ResetActivities()
        {
            compactReturnPhase = CompactReturnPhase.None;
            activities.Clear();
            volumeView.HideImmediate();
            bubble.HideImmediate();
            SyncMediaActivity();
        }

        void ClearActivities()
        {
            compactReturnPhase = CompactReturnPhase.None;
            activities.Clear();
            volumeView.HideImmediate();
            bubble.HideImmediate();
        }

        void BeginCompactReturn(long now)
        {
            compactReturnPhase = CompactReturnPhase.ResetToDefault;
            compactReturnStartedAt = now;

            volumeView.Hide();
            bubble.SetInteractive(false);
        }

        void CancelCompactReturn()
        {
            if (!IsCompactReturning) return;

            compactReturnPhase = CompactReturnPhase.None;

            if (bubble.IsMerging)
                bubble.CancelMerge();

            bubble.SetInteractive(true);
        }

        void UpdateCompactReturn(long now)
        {
            if (compactReturnPhase == CompactReturnPhase.ResetToDefault)
            {
                bool timedOut = now - compactReturnStartedAt >= CompactReturnResetTimeoutMs;
                bool settled = RendererMain.Instance.MainIsland.SizeSettled &&
                    volumeView.Visibility <= 0.05f;

                if (!settled && !timedOut) return;

                compactReturnPhase = CompactReturnPhase.CoverEntering;
                bubble.BeginMerge(CompactArtCenter());
                return;
            }

            if (compactReturnPhase == CompactReturnPhase.CoverEntering && !bubble.IsMerging)
            {
                compactReturnPhase = CompactReturnPhase.None;
                bubble.SetInteractive(true);
            }
        }

        void UpdateBubble(bool expanded)
        {
            // While the pill resets to its default size, media is already
            // primary but its old bubble still waits outside. Keep it there
            // and non-interactive until the cover phase starts.
            if (compactReturnPhase == CompactReturnPhase.ResetToDefault)
            {
                if (bubble.Kind != IslandActivityKind.Media)
                    bubble.SetContent(IslandActivityKind.Media, collapseArt.Image);
                else
                    bubble.SetMediaArt(collapseArt.Image);

                bubble.SetTarget(BubbleTargetCenter());
                bubble.SetVisible(true);
                bubble.SetInteractive(false);

                RendererMain.Instance.MainIsland.expandInteractionRect = 20;
                return;
            }

            if (bubble.IsMerging)
            {
                RendererMain.Instance.MainIsland.expandInteractionRect = 20;
                return;
            }

            var secondary = SecondaryActivity;

            if (secondary == null)
            {
                bubble.ClearActivity();
                RendererMain.Instance.MainIsland.expandInteractionRect = 20;
                return;
            }

            bubble.SetTarget(BubbleTargetCenter());

            if (bubble.Kind != secondary.Kind)
            {
                bubble.SetContent(
                    secondary.Kind,
                    secondary.Kind == IslandActivityKind.Media ? collapseArt.Image : Res.VolumeOff);
                bubble.SnapToTarget();
            }
            else if (secondary.Kind == IslandActivityKind.Media)
            {
                bubble.SetMediaArt(collapseArt.Image);
            }

            bubble.SetInteractive(true);
            bubble.SetVisible(!expanded);

            // Keep the island's hover area from swallowing the bubble.
            RendererMain.Instance.MainIsland.expandInteractionRect = expanded ? 20 : 6;
        }

        void DetachMediaBubble()
        {
            if (SecondaryActivity?.Kind != IslandActivityKind.Media) return;

            bubble.SetContent(IslandActivityKind.Media, collapseArt.Image);
            bubble.SetTarget(BubbleTargetCenter());
            bubble.SetInteractive(true);

            // A cancelled return already has its cover bubble outside; keep
            // it where it is instead of restarting the detach animation.
            if (bubble.IsShown)
            {
                bubble.SetVisible(true);
                return;
            }

            if (collapseArt.IsEnabled && collapseArt.Size.X > 0f)
            {
                bubble.BeginDetach(
                    CompactArtCenter(),
                    collapseArt.Size.X / ActivityBubble.Diameter);
            }
            else
            {
                bubble.ShowIdle();
            }

            bubble.SetVisible(true);
        }

        Vec2 BubbleTargetCenter() => ActivityBubble.GetTargetCenter(RendererMain.Instance.MainIsland);

        Vec2 CompactArtCenter() => collapseArt.Position +
            new Vec2(collapseArt.Size.X * 0.5f, collapseArt.Size.Y * 0.5f);
    }
}
