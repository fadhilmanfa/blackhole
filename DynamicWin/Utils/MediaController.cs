namespace DynamicWin.Utils
{
    /// <summary>
    /// Sends transport commands to the session tracked by <see cref="MediaState"/>.
    /// Media keys are intentionally not used: Windows broadcasts them to
    /// whichever session it considers current, which may be another app that
    /// only made a sound (Instagram, CapCut, a browser tab).
    /// </summary>
    public class MediaController
    {
        public void PlayPause()
        {
            MediaState.SetPlaying(!MediaState.GetLivePlaying());
        }

        public void SetPlaying(bool playing)
        {
            MediaState.SetPlaying(playing);
        }

        public void Next()
        {
            MediaState.SkipNext();
        }

        public void Previous()
        {
            MediaState.SkipPrevious();
        }
    }
}
