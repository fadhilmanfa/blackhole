using DynamicWin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicWin.UI.Widgets.Small
{
    public class SmallVisualizerWidget : SmallWidgetBase
    {
        AudioVisualizer audioVisualizer;

        // "Now playing" palette sourced from the album art. Kept separately
        // from the theme so we can cross-fade when the track changes.
        int lastAccentVersion = -1;
        Col currentPrimary;
        Col currentSecondary;
        Col targetPrimary;
        Col targetSecondary;

        public SmallVisualizerWidget(UIObject? parent, Vec2 position, UIAlignment alignment = UIAlignment.TopCenter) : base(parent, position, alignment)
        {
            // 16 / (16/9) = 9 bar pas, tidak overflow.
            // Offset +1.25 = setengah gap kanan (2.5px) supaya bar rata tengah.
            audioVisualizer = new AudioVisualizer(this, new Vec2(1.25f, 0), new Vec2(GetWidgetSize().X, GetWidgetSize().Y), UIAlignment.Center, length: 16, 16)
            {
                divisor = 16f / 9f,
                barDownSmoothing = 10,
                barUpSmoothing = 20
            };
            AddLocalObject(audioVisualizer);

            currentPrimary = targetPrimary = audioVisualizer.Primary ?? Col.White;
            currentSecondary = targetSecondary = audioVisualizer.Secondary ?? Col.White.Override(a: 0.5f);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // Keep the bars tied to the tracked player. Loopback also hears
            // Instagram/CapCut, so without this the paused music island would
            // still dance whenever another app makes noise.
            audioVisualizer.AudioGain = MediaState.IsPlaying ? 1f : 0f;

            int version = MediaState.AccentVersion;
            if (version != lastAccentVersion)
            {
                lastAccentVersion = version;

                targetPrimary = MediaState.AccentPrimary ?? Theme.Primary ?? Col.White;
                targetSecondary = MediaState.AccentSecondary
                    ?? Theme.Secondary?.Override(a: 0.5f)
                    ?? Col.White.Override(a: 0.5f);
            }

            // Ease towards the current palette so track switches fade
            // instead of snapping between two unrelated colours.
            float t = Mathf.Clamp(deltaTime * 5f, 0f, 1f);
            currentPrimary = Col.Lerp(currentPrimary, targetPrimary, t);
            currentSecondary = Col.Lerp(currentSecondary, targetSecondary, t);

            audioVisualizer.Primary = currentPrimary;
            audioVisualizer.Secondary = currentSecondary;
        }

        protected override float GetWidgetWidth()
        {
            return base.GetWidgetWidth() * 2;
        }
    }
}
