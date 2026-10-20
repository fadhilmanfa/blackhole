using NAudio.Wave;
using SkiaSharp;
using System;
using System.IO;
using System.Security.Cryptography;
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
        private static string artHash = "";
        private static int artVersion;

        // Same-track artwork is re-read occasionally: browsers publish the
        // media session before the real thumbnail is ready, so the first
        // fetch often lands on a placeholder or the browser icon.
        private static string lastArtAttemptKey = "";
        private static long lastArtCheckMs;
        private const int ArtRecheckMs = 2000;

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
            bool hasSmtcSession = false;
            string newTitle = "";
            string newArtist = "";
            string newArtKey = "";
            GlobalSystemMediaTransportControlsSessionMediaProperties? mediaProps = null;

            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = PickSession(manager);
                if (session != null)
                {
                    hasSmtcSession = true;
                    var status = session.GetPlaybackInfo()?.PlaybackStatus;
                    smtcPlaying = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    if (smtcPlaying || status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                    {
                        mediaProps = await session.TryGetMediaPropertiesAsync();
                        if (mediaProps != null)
                        {
                            newTitle = mediaProps.Title ?? "";
                            newArtist = mediaProps.Artist ?? "";
                            newArtKey = BuildKey(newTitle, newArtist);
                        }
                    }
                }
            }
            catch
            {
                smtcPlaying = false;
                mediaProps = null;
            }

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

                if (isPlaying && newArtKey != "")
                {
                    title = newTitle;
                    artist = newArtist;

                    // New or unknown track: drop stale art so the UI stops
                    // showing the previous cover. The palette follows the art.
                    if (artKey != newArtKey && albumArt != null)
                    {
                        albumArt.Dispose();
                        albumArt = null;
                        artHash = "";
                        artVersion++;

                        accentPrimary = null;
                        accentSecondary = null;
                        accentVersion++;
                    }
                }
                else if (isPlaying && !hasSmtcSession)
                {
                    // Audio-only playback (game, app without SMTC): don't
                    // keep showing metadata from a player that already left.
                    title = "";
                    artist = "";
                }
            }

            // Fetch the cover right away on a track change, then re-check
            // the same track at a slower cadence: browsers (Chrome/YouTube)
            // publish the media session before the real thumbnail exists, so
            // the first fetch can land on a placeholder or browser icon.
            if (IsPlaying && newArtKey != "" && mediaProps?.Thumbnail != null)
            {
                long now = Environment.TickCount64;
                bool newTrack = newArtKey != lastArtAttemptKey;
                if (newTrack || now - lastArtCheckMs >= ArtRecheckMs)
                {
                    lastArtCheckMs = now;
                    lastArtAttemptKey = newArtKey;
                    await RefreshArtAsync(newArtKey, mediaProps);
                }
            }

            if (becameIdle)
            {
                lock (stateLock)
                {
                    title = "";
                    artist = "";
                }
            }
        }

        /// <summary>
        /// Picks the session that is actually playing. Browsers can expose
        /// several sessions (one per tab or window) and GetCurrentSession()
        /// is not guaranteed to be the one producing audio.
        /// </summary>
        private static GlobalSystemMediaTransportControlsSession? PickSession(
            GlobalSystemMediaTransportControlsSessionManager manager)
        {
            try
            {
                var sessions = manager.GetSessions();
                var current = manager.GetCurrentSession();

                // The session Windows considers current is usually the right
                // one; trust it while it is playing.
                if (current != null &&
                    current.GetPlaybackInfo()?.PlaybackStatus ==
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return current;

                // Otherwise look for another session that is playing. This
                // covers browsers exposing several tab sessions where the
                // OS-current one sits paused while another tab plays.
                foreach (var session in sessions)
                {
                    if (session.GetPlaybackInfo()?.PlaybackStatus ==
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        return session;
                }

                if (current != null) return current;
                if (sessions.Count > 0) return sessions[0];
            }
            catch { }

            return manager.GetCurrentSession();
        }

        /// <summary>
        /// Track identity used to decide when artwork belongs to a new song.
        /// Returns "" when the session has no usable metadata at all.
        /// </summary>
        private static string BuildKey(string newTitle, string newArtist)
        {
            if (string.IsNullOrWhiteSpace(newTitle) && string.IsNullOrWhiteSpace(newArtist))
                return "";

            return newTitle + "\n" + newArtist;
        }

        /// <summary>
        /// Reads the artwork carried by the given media-properties snapshot
        /// and publishes it when the bytes differ from the cover on screen.
        /// Hash comparison makes the periodic re-check cheap and prevents
        /// re-triggering the UI crossfade for an unchanged image.
        /// </summary>
        private static async Task RefreshArtAsync(
            string key,
            GlobalSystemMediaTransportControlsSessionMediaProperties props)
        {
            byte[] bytes;
            try
            {
                using var stream = await props.Thumbnail.OpenReadAsync();
                using var input = stream.AsStreamForRead();
                using var ms = new MemoryStream();
                await input.CopyToAsync(ms);
                bytes = ms.ToArray();
            }
            catch (Exception e)
            {
                // Transient: the session may have moved on while opening the
                // stream. The next poll retries.
                System.Diagnostics.Debug.WriteLine("Album art load failed: " + e.Message);
                return;
            }

            string hash = Convert.ToHexString(SHA256.HashData(bytes));

            lock (stateLock)
            {
                if (albumArt != null && key == artKey && hash == artHash)
                    return;
            }

            SKBitmap? art = null;
            try
            {
                art = SKBitmap.Decode(bytes);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Album art decode failed: " + e.Message);
            }

            if (art == null) return;

            // Extract outside the lock: sampling is CPU work and must not
            // stall UI threads that read IsPlaying/Title every frame.
            var palette = AlbumArtColor.Extract(art);

            lock (stateLock)
            {
                if (albumArt != null && key == artKey && hash == artHash)
                {
                    art.Dispose();
                    return;
                }

                // Publish the new bitmap and only then mark the key, so a
                // failed decode above is retried instead of silently cached.
                albumArt?.Dispose();
                albumArt = art;
                artKey = key;
                artHash = hash;
                artVersion++;

                accentPrimary = palette?.primary;
                accentSecondary = palette?.secondary;
                accentVersion++;
            }
        }
    }
}
