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
        private static bool isMediaOpen;
        private static bool mediaDismissed;
        private static int attentionVersion;
        private static string trackKey = "";
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

        // Sticky session tracking. Windows' "current session" follows
        // whichever app last changed playback, so opening Instagram/CapCut
        // can steal the island from the music the user is listening to.
        // Keep the chosen session until it closes or stops; paused media is
        // pinned by design in this app.
        private static GlobalSystemMediaTransportControlsSession? trackedSession;
        private static string trackedAppId = "";
        private static string trackedTrackKey = "";

        // Serializes transport commands so a fast double click cannot run
        // play and pause out of order.
        private static readonly SemaphoreSlim transportLock = new SemaphoreSlim(1, 1);

        private static float audioLevel;
        private static WasapiLoopbackCapture? capture;

        private const float AudioThreshold = 0.03f;
        private const int PositiveNeeded = 2;  // ~1s to confirm playing
        private const int NegativeNeeded = 4;  // ~2s to confirm stopped

        public static bool IsPlaying { get { lock (stateLock) return isPlaying; } }

        /// <summary>
        /// True while a media session is still open, including when playback
        /// is paused. The island keeps the now-playing layout until the
        /// player/tab is closed or the user dismisses it.
        /// </summary>
        public static bool IsMediaVisible
        {
            get { lock (stateLock) return isMediaOpen && !mediaDismissed; }
        }

        /// <summary>
        /// Hides the current now-playing state without stopping playback.
        /// It comes back when playback resumes or the track changes.
        /// </summary>
        public static void DismissCurrentMedia()
        {
            lock (stateLock)
            {
                if (!isMediaOpen) return;
                mediaDismissed = true;
            }
        }

        /// <summary>
        /// Sends play/pause directly to the tracked session. Media keys are
        /// deliberately avoided: Windows broadcasts them to whichever session
        /// it considers current, which may be another app that just made a
        /// sound (Instagram, CapCut, a browser tab).
        /// </summary>
        public static void SetPlaying(bool playing)
        {
            var session = GetTrackedSession();
            if (session == null) return;
            _ = SetPlayingAsync(session, playing);
        }

        public static void SkipNext()
        {
            var session = GetTrackedSession();
            if (session == null) return;
            _ = SkipAsync(session, next: true);
        }

        public static void SkipPrevious()
        {
            var session = GetTrackedSession();
            if (session == null) return;
            _ = SkipAsync(session, next: false);
        }

        /// <summary>
        /// Fresh play/pause state straight from the tracked session. The
        /// debounced <see cref="IsPlaying"/> can lag briefly, and a click must
        /// target the real state instead of toggling twice.
        /// </summary>
        public static bool GetLivePlaying()
        {
            var session = GetTrackedSession();
            var status = GetPlaybackStatus(session);

            if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return true;
            if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                return false;

            return IsPlaying;
        }

        /// <summary>
        /// Bumped when the island should open on its own: playback starts
        /// while no media session was open, or the track changes during
        /// playback. Resume-after-pause does not bump it.
        /// </summary>
        public static int AttentionVersion { get { lock (stateLock) return attentionVersion; } }

        private static GlobalSystemMediaTransportControlsSession? GetTrackedSession()
        {
            lock (stateLock) return trackedSession;
        }

        private static async Task SetPlayingAsync(
            GlobalSystemMediaTransportControlsSession session, bool playing)
        {
            await transportLock.WaitAsync();
            try
            {
                var status = GetPlaybackStatus(session);

                // Already in the requested state (the UI may be one poll
                // behind): don't toggle it the wrong way.
                if (playing &&
                    status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return;
                if (!playing &&
                    status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                    return;

                if (playing)
                    await session.TryPlayAsync();
                else
                    await session.TryPauseAsync();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Media command failed: " + e.Message);
            }
            finally
            {
                transportLock.Release();
            }
        }

        private static async Task SkipAsync(
            GlobalSystemMediaTransportControlsSession session, bool next)
        {
            try
            {
                if (next)
                    await session.TrySkipNextAsync();
                else
                    await session.TrySkipPreviousAsync();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Media skip failed: " + e.Message);
            }
        }

        public static string Title { get { lock (stateLock) return title; } }
        public static string Artist { get { lock (stateLock) return artist; } }
        public static SKBitmap? AlbumArt { get { lock (stateLock) return albumArt; } }

        /// <summary>
        /// AppUserModelId of the tracked player. The now-playing card uses
        /// this to bring the source application back to the foreground.
        /// Empty when playback comes from raw audio with no SMTC session.
        /// </summary>
        public static string SourceAppId { get { lock (stateLock) return trackedAppId; } }

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
            bool smtcPaused = false;
            bool smtcChanging = false;
            bool hasSmtcSession = false;
            bool sessionExists = false;
            string newTitle = "";
            string newArtist = "";
            string newArtKey = "";
            GlobalSystemMediaTransportControlsSessionMediaProperties? mediaProps = null;

            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = await ResolveSessionAsync(manager, manager.GetSessions());

                if (session != null)
                {
                    var status = GetPlaybackStatus(session);
                    smtcPlaying = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    smtcPaused = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
                    smtcChanging = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing;

                    // Playing, paused, or changing means the media is still
                    // open. A stopped/closed session should not keep stale
                    // music on the island forever.
                    hasSmtcSession = smtcPlaying || smtcPaused || smtcChanging;
                    sessionExists = hasSmtcSession;

                    if (smtcPlaying || smtcPaused)
                    {
                        mediaProps = await TryGetMediaPropertiesAsync(session);
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
                // Transient SMTC failure: keep the tracked session so the
                // next poll can recover, but treat this snapshot as unknown.
                smtcPlaying = false;
                smtcPaused = false;
                smtcChanging = false;
                hasSmtcSession = false;
                sessionExists = false;
                mediaProps = null;
            }

            bool audioActive;
            lock (stateLock)
            {
                audioActive = audioLevel > AudioThreshold;
                audioLevel *= 0.5f; // decay, silence fades the level out
            }

            // A tracked SMTC session is authoritative. Loopback audio is only
            // a fallback for games/apps with no media session; otherwise any
            // notification or video in another app would make a paused player
            // look like it is still playing.
            bool active = hasSmtcSession ? (smtcPlaying || smtcChanging) : audioActive;

            bool becamePlaying = false;
            bool attention = false;
            lock (stateLock)
            {
                bool wasMediaOpen = isMediaOpen;

                if (active)
                {
                    positiveStreak++;
                    negativeStreak = 0;

                    // An explicit SMTC state is reliable; only the audio-level
                    // fallback needs the longer confirmation streak.
                    int needed = (smtcPlaying || smtcChanging) ? 1 : PositiveNeeded;
                    if (!isPlaying && positiveStreak >= needed)
                    {
                        isPlaying = true;
                        becamePlaying = true;
                    }
                }
                else
                {
                    negativeStreak++;
                    positiveStreak = 0;

                    int needed = smtcPaused ? 1 : NegativeNeeded;
                    if (isPlaying && negativeStreak >= needed)
                        isPlaying = false;
                }

                // Paused sessions stay visible until the player/tab closes.
                isMediaOpen = hasSmtcSession || isPlaying;

                if (becamePlaying)
                {
                    // Resuming after the user dismissed the status brings it
                    // back. Dismissing while playing stays dismissed because
                    // there is no positive transition.
                    mediaDismissed = false;

                    // Only a playback that starts from a closed/idle media
                    // state should open the island. Resuming a paused player
                    // leaves wasMediaOpen true and stays collapsed.
                    if (!wasMediaOpen && newArtKey != "")
                        attention = true;
                }

                if (newArtKey != "")
                {
                    title = newTitle;
                    artist = newArtist;
                    trackedTrackKey = newArtKey;

                    if (trackKey != newArtKey)
                    {
                        trackKey = newArtKey;

                        // A genuinely new track should reappear even if the
                        // previous one was dismissed.
                        mediaDismissed = false;

                        // Track changes while playing should open the island;
                        // skipping while paused does not.
                        if (isPlaying && newArtKey != "")
                            attention = true;

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
                }
                else if (!sessionExists)
                {
                    // No usable session: audio-only playback (game, app
                    // without SMTC) or the player closed. Never keep showing
                    // metadata/cover from a source that already left.
                    title = "";
                    artist = "";
                    trackKey = "";
                    trackedTrackKey = "";

                    if (albumArt != null)
                    {
                        albumArt.Dispose();
                        albumArt = null;
                        artKey = "";
                        artHash = "";
                        artVersion++;

                        accentPrimary = null;
                        accentSecondary = null;
                        accentVersion++;
                    }
                }

                if (!isMediaOpen)
                    mediaDismissed = false;

                if (attention)
                    attentionVersion++;
            }

            // Fetch the cover right away on a track change, then re-check
            // the same track at a slower cadence: browsers (Chrome/YouTube)
            // publish the media session before the real thumbnail exists, so
            // the first fetch can land on a placeholder or browser icon.
            if (IsMediaVisible && newArtKey != "" && mediaProps?.Thumbnail != null)
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
        }

        /// <summary>
        /// Resolves which session the island should follow.
        ///
        /// The chosen session is sticky: opening Instagram, CapCut, or any
        /// other noisy app must not steal the now-playing card while the
        /// tracked player is playing or paused. A new session is only picked
        /// after the tracked one closes or stops.
        /// </summary>
        private static async Task<GlobalSystemMediaTransportControlsSession?> ResolveSessionAsync(
            GlobalSystemMediaTransportControlsSessionManager manager,
            IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
        {
            var tracked = await FindTrackedSessionAsync(sessions);
            if (tracked != null && IsUsableStatus(GetPlaybackStatus(tracked)))
                return tracked;

            // The tracked session closed or stopped. Pick a fresh one.
            SetTrackedSession(null);

            var next = ChooseSession(manager, sessions);
            if (next != null)
                SetTrackedSession(next);

            return next;
        }

        /// <summary>
        /// Finds the sticky session in the current session list. WinRT may
        /// hand out fresh wrappers between polls, so fall back to the app id;
        /// browsers expose several tabs under one app, hence the track key
        /// check when that is ambiguous.
        /// </summary>
        private static async Task<GlobalSystemMediaTransportControlsSession?> FindTrackedSessionAsync(
            IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
        {
            GlobalSystemMediaTransportControlsSession? tracked;
            string appId;
            string key;
            lock (stateLock)
            {
                tracked = trackedSession;
                appId = trackedAppId;
                key = trackedTrackKey;
            }

            if (tracked == null) return null;

            // Prefer the exact same WinRT session object.
            foreach (var session in sessions)
            {
                if (ReferenceEquals(session, tracked))
                    return session;
            }

            if (string.IsNullOrEmpty(appId)) return null;

            var sameApp = new List<GlobalSystemMediaTransportControlsSession>();
            foreach (var session in sessions)
            {
                if (GetAppId(session) == appId)
                    sameApp.Add(session);
            }

            if (sameApp.Count == 0) return null;
            if (sameApp.Count == 1)
                return IsUsableStatus(GetPlaybackStatus(sameApp[0])) ? sameApp[0] : null;

            // One app, multiple sessions (browser tabs): match the track
            // that was last shown before accepting any of them.
            if (key != "")
            {
                foreach (var session in sameApp)
                {
                    var props = await TryGetMediaPropertiesAsync(session);
                    if (props != null &&
                        BuildKey(props.Title ?? "", props.Artist ?? "") == key)
                        return session;
                }
            }

            // Track key was not found (the tab may have navigated): prefer
            // the same app's playing session over an arbitrary paused tab.
            GlobalSystemMediaTransportControlsSession? firstUsable = null;
            foreach (var session in sameApp)
            {
                var status = GetPlaybackStatus(session);
                if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return session;
                if (firstUsable == null && IsUsableStatus(status))
                    firstUsable = session;
            }

            return firstUsable;
        }

        /// <summary>
        /// First-run selection only. Windows' current session is preferred
        /// while it is playing; otherwise the first playing session wins,
        /// then a paused/opened one.
        /// </summary>
        private static GlobalSystemMediaTransportControlsSession? ChooseSession(
            GlobalSystemMediaTransportControlsSessionManager manager,
            IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
        {
            GlobalSystemMediaTransportControlsSession? current = null;
            try { current = manager.GetCurrentSession(); } catch { }

            if (IsSessionPlaying(current)) return current;

            foreach (var session in sessions)
            {
                if (IsSessionPlaying(session)) return session;
            }

            if (IsUsableStatus(GetPlaybackStatus(current))) return current;

            foreach (var session in sessions)
            {
                if (IsUsableStatus(GetPlaybackStatus(session)))
                    return session;
            }

            return null;
        }

        private static bool IsSessionPlaying(
            GlobalSystemMediaTransportControlsSession? session)
        {
            return GetPlaybackStatus(session) ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }

        private static bool IsUsableStatus(
            GlobalSystemMediaTransportControlsSessionPlaybackStatus? status)
        {
            return status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ||
                   status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused ||
                   status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing;
        }

        private static GlobalSystemMediaTransportControlsSessionPlaybackStatus? GetPlaybackStatus(
            GlobalSystemMediaTransportControlsSession? session)
        {
            if (session == null) return null;
            try { return session.GetPlaybackInfo()?.PlaybackStatus; }
            catch { return null; }
        }

        private static string GetAppId(GlobalSystemMediaTransportControlsSession session)
        {
            try { return session.SourceAppUserModelId ?? ""; }
            catch { return ""; }
        }

        private static async Task<GlobalSystemMediaTransportControlsSessionMediaProperties?>
            TryGetMediaPropertiesAsync(GlobalSystemMediaTransportControlsSession session)
        {
            try { return await session.TryGetMediaPropertiesAsync(); }
            catch { return null; }
        }

        private static void SetTrackedSession(
            GlobalSystemMediaTransportControlsSession? session)
        {
            // Read the app id outside the lock: WinRT calls must never block
            // UI threads that read IsPlaying/Title.
            string appId = session != null ? GetAppId(session) : "";

            lock (stateLock)
            {
                trackedSession = session;
                trackedAppId = appId;
                trackedTrackKey = "";
            }
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
