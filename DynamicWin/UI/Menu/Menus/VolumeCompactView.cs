using DynamicWin.Main;
using DynamicWin.Resources;
using DynamicWin.UI;
using DynamicWin.UI.UIElements;
using DynamicWin.UI.UIElements.Custom;
using DynamicWin.Utils;
using System;
using System.Collections.Generic;
using Forms = System.Windows.Forms;

namespace DynamicWin.UI.Menu.Menus
{
    /// <summary>
    /// Compact volume/mute content. This used to be a standalone menu; it is
    /// now one of the activities rendered by HomeMenu.
    ///
    /// The view owns only its own visuals and physics. Ordering, lifetime and
    /// hand-off to other activities belong to HomeMenu.Activities.
    /// </summary>
    internal sealed class VolumeCompactView
    {
        const float IslandHeight = 38f;
        const float BaseWidth = 250f;

        const float JellyPullStep = 15f;
        const float JellyMaxPull = 22f;
        const float JellyMaxSpeed = 600f;
        const float JellyStiffness = 260f;
        const float JellyDamping = 12f;
        const float JellyHoldTime = 0.22f;
        const float JellyReleaseSpeed = 7f;

        readonly IslandObject island;
        readonly UIObject mutedBg;
        readonly DWImage volumeImage;
        readonly DWText muteText;
        readonly DWProgressBar volume;

        public List<UIObject> Objects { get; } = new();

        float visibility;
        float muteVisibility;
        float islandScale = 1f;

        float jellyX;
        float jellyVelocity;
        float jellyTarget;
        float jellyHold;

        bool wasVolumeUpDown;
        bool wasVolumeDownDown;
        bool mute;

        Vec2 cursorOnOpen = Vec2.zero;
        bool wasHovering;

        public VolumeCompactView(IslandObject island)
        {
            this.island = island;

            mutedBg = new UIObject(island, new Vec2(10, 0), new Vec2(40, 20), UIAlignment.MiddleLeft)
            {
                roundRadius = 15,
                Color = Theme.Error
            };
            mutedBg.Anchor.X = 0;
            Objects.Add(mutedBg);

            volumeImage = new DWImage(
                island,
                Res.VolumeOn,
                new Vec2(20, 0),
                new Vec2(20, 20),
                UIAlignment.MiddleLeft);
            volumeImage.Anchor.X = 0;
            Objects.Add(volumeImage);

            muteText = new DWText(island, "Silent", new Vec2(-15, 0), UIAlignment.MiddleRight)
            {
                TextSize = 15,
                Font = Res.InterBold,
                Color = Theme.Error
            };
            muteText.Anchor.X = 1;
            muteText.CenterInkVertically();
            Objects.Add(muteText);

            volume = new DWProgressBar(
                island,
                new Vec2(-20, 0),
                new Vec2(150, 5f),
                UIAlignment.MiddleRight)
            {
                elastic = true
            };
            volume.Anchor.X = 1;
            Objects.Add(volume);

            ApplyVisibility();
        }

        public Vec2 Size
        {
            get
            {
                float jellyStretch = 1f + Math.Min(Math.Abs(jellyX) / 400f, 0.03f);
                return new Vec2(BaseWidth * islandScale * jellyStretch, IslandHeight);
            }
        }

        public Col BorderColor => IsMuted() ? Col.Transparent : new Col(0.5f, 0.5f, 0.5f);

        public float Visibility => visibility;

        /// <summary>Fresh volume-key press or a promotion from the bubble.</summary>
        public void Show()
        {
            islandScale = 1.25f;
            cursorOnOpen = RendererMain.CursorPosition;
            wasHovering = island.IsHovering;

            // Seed the edge trackers so the key that opened the view is not
            // applied twice on the next Update.
            wasVolumeUpDown = KeyHandler.keyDown.Contains(Forms.Keys.VolumeUp);
            wasVolumeDownDown = KeyHandler.keyDown.Contains(Forms.Keys.VolumeDown);

            if (wasVolumeUpDown) NudgeJelly(1f);
            if (wasVolumeDownDown) NudgeJelly(-1f);
        }

        public void Hide()
        {
            islandScale = 1f;
            jellyX = 0f;
            jellyVelocity = 0f;
            jellyTarget = 0f;
            jellyHold = 0f;
            island.LocalPosition.X = 0f;
        }

        public void HideImmediate()
        {
            visibility = 0f;
            muteVisibility = 0f;
            islandScale = 1f;
            jellyX = 0f;
            jellyVelocity = 0f;
            jellyTarget = 0f;
            jellyHold = 0f;
            wasVolumeUpDown = false;
            wasVolumeDownDown = false;
            wasHovering = false;
            mute = IsMuted();
            island.LocalPosition.X = 0f;
            ApplyVisibility();
        }

        /// <summary>
        /// The old volume menu closed when the cursor deliberately moved onto
        /// the island. Keep that rule, including the "already hovering when
        /// it opened" guard.
        /// </summary>
        public bool ShouldYieldToHover()
        {
            bool hovering = island.IsHovering;
            bool yield = hovering &&
                !wasHovering &&
                Vec2.Distance(RendererMain.CursorPosition, cursorOnOpen) > 6f;

            wasHovering = hovering;
            return yield;
        }

        public void Update(float deltaTime, bool primary)
        {
            ApplyTheme();

            visibility = Mathf.Lerp(visibility, primary ? 1f : 0f, 14f * deltaTime);

            if (!primary && visibility < 0.001f)
            {
                visibility = 0f;
                ApplyVisibility();
                return;
            }

            islandScale = Mathf.Lerp(islandScale, 1f, 5f * deltaTime);

            if (primary)
            {
                UpdateVolume(deltaTime);
                UpdateJelly(deltaTime);
            }
            else
            {
                island.LocalPosition.X = Mathf.Lerp(island.LocalPosition.X, 0f, 14f * deltaTime);
            }

            bool silent = IsMuted() || GetVolumePercent() <= 0.0;
            muteVisibility = Mathf.Lerp(
                muteVisibility,
                silent ? 1f : 0f,
                12f * deltaTime);

            ApplyVisibility();
        }

        void UpdateVolume(float deltaTime)
        {
            double volumePercent = GetVolumePercent();
            bool isMuted = IsMuted();

            if (volumePercent <= 0 || isMuted)
            {
                if (!mute)
                {
                    mute = true;
                    islandScale = 1.05f;
                }

                volumeImage.Image = Res.VolumeOff;
            }
            else
            {
                if (mute)
                {
                    mute = false;
                    islandScale = 1.25f;
                }

                volumeImage.Image = Res.VolumeOn;
            }

            volume.value = (float)(volumePercent / 100.0);

            bool volumeUpDown = KeyHandler.keyDown.Contains(Forms.Keys.VolumeUp);
            bool volumeDownDown = KeyHandler.keyDown.Contains(Forms.Keys.VolumeDown);

            if (volumeUpDown && !wasVolumeUpDown) NudgeJelly(1f);
            if (volumeDownDown && !wasVolumeDownDown) NudgeJelly(-1f);

            wasVolumeUpDown = volumeUpDown;
            wasVolumeDownDown = volumeDownDown;

            float volXOffset = volumeUpDown ? 2f : volumeDownDown ? -2f : 0f;
            volume.LocalPosition.X = Mathf.Lerp(
                volume.LocalPosition.X,
                volXOffset,
                (Math.Abs(volXOffset) > Math.Abs(volume.LocalPosition.X) ? 4.5f : 2.5f) * deltaTime);
        }

        void UpdateJelly(float deltaTime)
        {
            bool volumeUpDown = KeyHandler.keyDown.Contains(Forms.Keys.VolumeUp);
            bool volumeDownDown = KeyHandler.keyDown.Contains(Forms.Keys.VolumeDown);

            if (volumeUpDown || volumeDownDown)
            {
                jellyHold = 0f;
            }
            else
            {
                jellyHold += deltaTime;
                if (jellyHold > JellyHoldTime)
                    jellyTarget = Mathf.Lerp(jellyTarget, 0f, JellyReleaseSpeed * deltaTime);
            }

            // Damped spring for the sideways jelly. Substep so the motion
            // stays consistent whether the renderer ticks at 30 or 60 fps.
            const float JellyStep = 1f / 240f;
            int jellySteps = Math.Max(1, (int)Math.Ceiling(deltaTime / JellyStep));
            float jellyDt = deltaTime / jellySteps;

            for (int i = 0; i < jellySteps; i++)
            {
                jellyVelocity += ((jellyTarget - jellyX) * JellyStiffness - JellyDamping * jellyVelocity) * jellyDt;
                jellyVelocity = Mathf.Clamp(jellyVelocity, -JellyMaxSpeed, JellyMaxSpeed);
                jellyX += jellyVelocity * jellyDt;
            }

            island.LocalPosition.X = jellyX;
        }

        void NudgeJelly(float direction)
        {
            // A reversal should answer at once instead of slowly unwinding
            // the pull from the opposite side.
            if (jellyTarget != 0f && Math.Sign(jellyTarget) != Math.Sign(direction))
                jellyTarget = 0f;

            jellyTarget = Mathf.Clamp(
                jellyTarget + direction * JellyPullStep,
                -JellyMaxPull,
                JellyMaxPull);
            jellyHold = 0f;
        }

        void ApplyVisibility()
        {
            float visible = Mathf.Clamp(visibility, 0f, 1f);
            float muted = Mathf.Clamp(muteVisibility, 0f, 1f);

            mutedBg.Alpha = visible * muted;
            muteText.Alpha = visible * muted;
            volume.Alpha = visible * (1f - muted);
            volumeImage.Alpha = visible;
        }

        void ApplyTheme()
        {
            var icon = Theme.IconColor ?? Col.White;
            var error = Theme.Error ?? new Col(0.85f, 0.27f, 0.27f);

            mutedBg.Color = error;
            muteText.Color = error;
            volumeImage.Color = icon;
            volume.Color = icon.Override(a: 0.1f);
            volume.contentColor = icon;
        }

        double GetVolumePercent()
        {
            var device = DynamicWinMain.defaultDevice;
            double volume = device == null ? 1f : device.AudioEndpointVolume.MasterVolumeLevelScalar;
            return volume * 100.0;
        }

        bool IsMuted()
        {
            var device = DynamicWinMain.defaultDevice;
            return device == null || device.AudioEndpointVolume.Mute;
        }
    }
}
