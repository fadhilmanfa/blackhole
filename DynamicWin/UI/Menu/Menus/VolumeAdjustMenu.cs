using DynamicWin.Main;
using DynamicWin.Resources;
using DynamicWin.UI.UIElements;
using DynamicWin.UI.UIElements.Custom;
using DynamicWin.Utils;
using NAudio.CoreAudioApi;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DynamicWin.UI.Menu.Menus
{
    internal class VolumeAdjustMenu : BaseMenu
    {
        // P/Invoke to call the Windows API function
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int waveOutGetVolume(IntPtr hwo, out uint dwVolume);

        double GetVolumePercent()
        {
            var volume = (DynamicWinMain.defaultDevice == null) ? 1f : DynamicWinMain.defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar;

            return volume * 100;
        }

        bool IsMuted()
        {
            return (DynamicWinMain.defaultDevice == null) ? true : DynamicWinMain.defaultDevice.AudioEndpointVolume.Mute;
        }

        DWImage volumeImage;
        UIObject mutedBg;
        DWText muteText;
        DWProgressBar volume;

        static VolumeAdjustMenu instance;

        float islandScale = 1.25f;

        // Directional jelly: fresh presses build a sideways pull, the island
        // stays pulled while the keys are active, then springs back with a
        // little overshoot once the presses stop.
        float jellyX;
        float jellyVelocity;
        float jellyTarget;
        float jellyHold;
        bool wasVolumeUpDown;
        bool wasVolumeDownDown;

        const float JellyPullStep = 15f;
        const float JellyMaxPull = 22f;
        const float JellyMaxSpeed = 600f;
        const float JellyStiffness = 260f;
        const float JellyDamping = 12f;
        const float JellyHoldTime = 0.22f;
        const float JellyReleaseSpeed = 7f;

        // Matches the idle HomeMenu pill (120x38) in height so switching
        // between idle and the volume popup never changes the silhouette.
        const float IslandHeight = 38f;

        public VolumeAdjustMenu()
        {
            instance = this;
            timerUntilClose = 0f;

            // The key that opened the popup is still held; pull immediately
            // and seed the edge trackers so the first Update doesn't pull twice.
            wasVolumeUpDown = KeyHandler.keyDown.Contains(System.Windows.Forms.Keys.VolumeUp);
            wasVolumeDownDown = KeyHandler.keyDown.Contains(System.Windows.Forms.Keys.VolumeDown);

            if (wasVolumeUpDown)
                NudgeJelly(1f);
            if (wasVolumeDownDown)
                NudgeJelly(-1f);
        }

        void NudgeJelly(float direction)
        {
            // A reversal should answer at once instead of slowly unwinding
            // the pull from the opposite side.
            if (jellyTarget != 0f && Math.Sign(jellyTarget) != Math.Sign(direction))
                jellyTarget = 0f;

            jellyTarget = Mathf.Clamp(jellyTarget + direction * JellyPullStep, -JellyMaxPull, JellyMaxPull);
            jellyHold = 0f;
        }

        public override List<UIObject> InitializeMenu(IslandObject island)
        {
            var objects = base.InitializeMenu(island);

            mutedBg = new UIObject(island, new Vec2(10, 0), new Vec2(40, 20), UIAlignment.MiddleLeft);
            mutedBg.Color = Theme.Error;
            mutedBg.roundRadius = 15;
            mutedBg.Anchor.X = 0;
            objects.Add(mutedBg);

            volumeImage = new DWImage(island, Res.VolumeOn, new Vec2(20, 0), new Vec2(20, 20), UIAlignment.MiddleLeft);
            volumeImage.Anchor.X = 0;
            objects.Add(volumeImage);

            muteText = new DWText(island, "Silent", new Vec2(-15, 0), UIAlignment.MiddleRight);
            muteText.Anchor.X = 1;
            muteText.TextSize = 15;
            muteText.Font = Res.InterBold;
            muteText.Color = Theme.Error;
            muteText.CenterInkVertically();
            objects.Add(muteText);

            volume = new DWProgressBar(island, new Vec2(-20, 0), new Vec2(150, 5f), UIAlignment.MiddleRight)
            {
                elastic = true
            };
            volume.Anchor.X = 1;
            objects.Add(volume);

            return objects;
        }

        public static float timerUntilClose = 0f;

        bool mute = false;

        public override void Update()
        {
            base.Update();

            float dt = RendererMain.Instance.DeltaTime;

            if (timerUntilClose > 2.75f) MenuManager.CloseOverlay();

            islandScale = Mathf.Lerp(islandScale, 1f, 5f * dt);

            var volume = GetVolumePercent();
            var isMuted = IsMuted();

            // Each fresh press adds another step of pull (capped), while a
            // held key keeps the current pull alive. Spamming therefore feels
            // like one continuous tug instead of separate kicks.
            bool volumeUpDown = KeyHandler.keyDown.Contains(System.Windows.Forms.Keys.VolumeUp);
            bool volumeDownDown = KeyHandler.keyDown.Contains(System.Windows.Forms.Keys.VolumeDown);

            if (volumeUpDown && !wasVolumeUpDown)
                NudgeJelly(1f);
            if (volumeDownDown && !wasVolumeDownDown)
                NudgeJelly(-1f);

            wasVolumeUpDown = volumeUpDown;
            wasVolumeDownDown = volumeDownDown;

            if (volumeUpDown || volumeDownDown)
            {
                jellyHold = 0f;
            }
            else
            {
                jellyHold += dt;
                if (jellyHold > JellyHoldTime)
                    jellyTarget = Mathf.Lerp(jellyTarget, 0f, JellyReleaseSpeed * dt);
            }

            if (volume <= 0f || isMuted)
            {
                if (!mute)
                {
                    mute = true;
                    islandScale = 1.05f;
                }

                volumeImage.Image = Res.VolumeOff;

                muteText.SetActive(true);
                mutedBg.SetActive(true);
                this.volume.SetActive(false);
            }
            else if (volume >= 0f && !isMuted)
            {
                if (mute)
                {
                    islandScale = 1.25f;
                    mute = false;
                }

                volumeImage.Image = Res.VolumeOn;

                muteText.SetActive(false);
                mutedBg.SetActive(false);
                this.volume.SetActive(true);
            }

            // Damped spring for the sideways jelly. Substep so the motion
            // stays consistent whether the renderer ticks at 30 or 60 fps.
            const float JellyStep = 1f / 240f;
            int jellySteps = Math.Max(1, (int)Math.Ceiling(dt / JellyStep));
            float jellyDt = dt / jellySteps;

            for (int i = 0; i < jellySteps; i++)
            {
                jellyVelocity += ((jellyTarget - jellyX) * JellyStiffness - JellyDamping * jellyVelocity) * jellyDt;
                jellyVelocity = Mathf.Clamp(jellyVelocity, -JellyMaxSpeed, JellyMaxSpeed);
                jellyX += jellyVelocity * jellyDt;
            }

            RendererMain.Instance.MainIsland.LocalPosition.X = jellyX;

            timerUntilClose += dt;

            this.volume.value = (float)volume / 100f;

            var volXOffset = KeyHandler.keyDown.Contains(System.Windows.Forms.Keys.VolumeUp) ? 2f :
                KeyHandler.keyDown.Contains(System.Windows.Forms.Keys.VolumeDown) ? -2f : 0;

            this.volume.LocalPosition.X = Mathf.Lerp(this.volume.LocalPosition.X, volXOffset, 
                (Math.Abs(volXOffset) > Math.Abs(this.volume.LocalPosition.X) ? 4.5f : 2.5f) * RendererMain.Instance.DeltaTime);
        }

        public override Vec2 IslandSize()
        {
            // The intro "pop" only stretches the width; a tiny extra stretch
            // follows the jelly so the pill feels elastic instead of sliding.
            float jellyStretch = 1f + Math.Min(Math.Abs(jellyX) / 400f, 0.03f);
            return new Vec2(250f * islandScale * jellyStretch, IslandHeight);
        }

        public override Vec2 IslandSizeBig()
        {
            // Overlay popups should not resize on hover.
            return IslandSize();
        }

        public override Col IslandBorderColor()
        {
            // Silent state drops the outline entirely; the red label and
            // mute pill already carry the state.
            return IsMuted() ? Col.Transparent : new Col(0.5f, 0.5f, 0.5f);
        }
    }
}
