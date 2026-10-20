using NAudio.Wave;
using SkiaSharp;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace DynamicWin.Utils
{
    /// <summary>
    /// Single source of truth for "is something playing".
    /// Combines Windows SMTC (Spotify, browser, any player + album art)
    /// with the raw loopback audio level (games, apps without SMTC).
    /// Debounced so the island does not flicker when switching tracks.
    /// </summary>
    public static class MediaState
    {
        private static readonly object stateLock = new object();

        private static bool isPlaying;
        private static string title = "";
        private static string artist = "";
        private static SKBitmap? albumArt;
        private static string artKey = "";
        private static int artVersion;

        // Palette derived from the current cover; null means "no art" and
        // the UI falls back to the active theme colours.
        private static Col? accentPrimary;
        private static Col? accentSecondary;
        private static int accentVersion;

        private static int positiveStreak;
        private static int negativeStreak;

        private static float audioLevel;
        private static WasapiLoopbackCapture? capture;

        private const float AudioThreshold = 0.03f;
        private const int PositiveNeeded = 2;  // ~1s to confirm playing
        private const int NegativeNeeded = 4;  // ~2s to confirm stopped

        public static bool IsPlaying { get { lock (stateLock) return isPlaying; } }
        public static string Title { get { lock (stateLock) return title; } }
        public static string Artist { get { lock (stateLock) return artist; } }
        public static SKBitmap? AlbumArt { get { lock (stateLock) return albumArt; } }

        /// <summary>
        /// Bumped every time the stored album art bitmap is replaced.
        /// UI caches by this version so late-arriving fetches are picked up.
        /// </summary>
        public static int AlbumArtVersion { get { lock (stateLock) return artVersion; } }

        /// <summary>Bright accent colour sampled from the current album art.</summary>
        public static Col? AccentPrimary { get { lock (stateLock) return accentPrimary; } }

        /// <summary>Dimmed companion colour for gradients on the same art.</summary>
        public static Col? AccentSecondary { get { lock (stateLock) return accentSecondary; } }

        /// <summary>Bumped whenever the extracted accent palette changes.</summary>
        public static int AccentVersion { get { lock (stateLock) return accentVersion; } }

        /// <summary>
        /// Returns a private copy of the album art (taken under the lock), so the
        /// caller can safely keep/draw/dispose it even while MediaState
        /// swaps or disposes its own bitmap on a background thread.
        /// </summary>
        public static SKBitmap? GetAlbumArtCopy()
        {
            lock (stateLock) return albumArt?.Copy();
        }

        static MediaState()
        {
            StartAudioCapture();
            Task.Run(PollLoop);
        }

        /// <summary>Touch to force the static constructor to run early.</summary>
        public static void EnsureStarted() { }

        private static void StartAudioCapture()
        {
            try
            {
                if (DynamicWinMain.defaultDevice == null) return;
                capture = new WasapiLoopbackCapture(DynamicWinMain.defaultDevice);
                capture.DataAvailable += OnAudioData;
                capture.StartRecording();
            }
            catch
            {
                capture = null;
            }
        }

        private static void OnAudioData(object? sender, WaveInEventArgs e)
        {
            try
            {
                float peak = 0f;
                // 32-bit float samples, sample every 8th for speed
                for (int i = 0; i + 3 < e.BytesRecorded; i += 32)
                {
                    float s = Math.Abs(BitConverter.ToSingle(e.Buffer, i));
                    if (s > peak) peak = s;
                }
                if (peak > audioLevel) audioLevel = peak;
            }
            catch { }
        }

        private static async Task PollLoop()
        {
            while (true)
            {
                try { await PollOnce(); } catch { }
                await Task.Delay(500);
            }
        }

        private static async Task PollOnce()
        {
            bool smtcPlaying = false;
            string newTitle = "";
            string newArtist = "";
            string newArtKey = "";

            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = manager.GetCurrentSession();
                if (session != null)
                {
                    var status = session.GetPlaybackInfo()?.PlaybackStatus;
                    smtcPlaying = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    if (smtcPlaying || status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                    {
                        var props = await session.TryGetMediaPropertiesAsync();
                        if (props != null)
                        {
                            newTitle = props.Title ?? "";
                            newArtist = props.Artist ?? "";
                            newArtKey = newTitle + "\n" + newArtist;
                        }
                    }
                }
            }
            catch { smtcPlaying = false; }

            bool audioActive;
            lock (stateLock)
            {
                audioActive = audioLevel > AudioThreshold;
                audioLevel *= 0.5f; // decay, silence fades the level out
            }

            bool active = smtcPlaying || audioActive;

            bool becamePlaying = false;
            bool becameIdle = false;
            lock (stateLock)
            {
                if (active)
                {
                    positiveStreak++;
                    negativeStreak = 0;
                    if (!isPlaying && positiveStreak >= PositiveNeeded)
                    {
                        isPlaying = true;
                        becamePlaying = true;
                    }
                }
                else
                {
                    negativeStreak++;
                    positiveStreak = 0;
                    if (isPlaying && negativeStreak >= NegativeNeeded)
                    {
                        isPlaying = false;
                        becameIdle = true;
                    }
                }

                if (isPlaying)
                {
                    title = newTitle;
                    artist = newArtist;

                    // New or unknown track: drop stale art so the UI falls
                    // back to the placeholder until the new fetch lands.
                    if (artKey != newArtKey && albumArt != null)
                    {
                        albumArt.Dispose();
                        albumArt = null;
                        artVersion++;

                        // Clear the palette with the art so accents never
                        // outlive the cover they came from.
                        accentPrimary = null;
                        accentSecondary = null;
                        accentVersion++;
                    }
                }
            }

            // Refetch only when the art we have does not match the current
            // track yet (covers both the first fetch and track changes).
            if (IsPlaying && newArtKey != "" && newArtKey != artKey)
                await RefreshArtAsync(newArtKey);

            if (becameIdle)
            {
                lock (stateLock)
                {
                    title = "";
                    artist = "";
                }
            }
        }

        private static async Task RefreshArtAsync(string key)
        {
            SKBitmap? art = null;
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = manager.GetCurrentSession();
                if (session != null)
                {
                    var props = await session.TryGetMediaPropertiesAsync();
                    var thumbRef = props?.Thumbnail;
                    if (thumbRef != null)
                    {
                        using var stream = await thumbRef.OpenReadAsync();
                        using var input = stream.AsStreamForRead();
                        using var ms = new MemoryStream();
                        await input.CopyToAsync(ms);
                        art = SKBitmap.Decode(ms.ToArray());
                    }
                }
            }
            catch (Exception e)
            {
                art = null;
                System.Diagnostics.Debug.WriteLine("Album art load failed: " + e.Message);
            }

            // Extract outside the lock: sampling is CPU work and must not
            // stall UI threads that read IsPlaying/Title every frame.
            var palette = art == null ? null : AlbumArtColor.Extract(art);

            lock (stateLock)
            {
                // Drop the fetch if the track already changed again.
                string currentKey = title + "\n" + artist;
                if (key != "" && key != currentKey && currentKey != "")
                {
                    art?.Dispose();
                    return;
                }

                // Publish the new bitmap (or the lack of one) and only mark the
                // key when it succeeded, so failed loads are retried next poll.
                if (!ReferenceEquals(albumArt, art))
                {
                    albumArt?.Dispose();
                    albumArt = art;
                    artVersion++;

                    accentPrimary = palette?.primary;
                    accentSecondary = palette?.secondary;
                    accentVersion++;
                }
                artKey = art == null ? "" : key;
            }
        }
    }
}
