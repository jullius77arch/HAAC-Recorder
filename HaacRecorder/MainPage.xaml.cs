using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Devices.Enumeration;
using Windows.Graphics.Display;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.System.Display;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace HaacRecorder
{
    public sealed partial class MainPage : Page
    {
        private MediaCapture _mediaCapture;
        private StorageFolder _recordingFolder;
        private StorageFile _recordingFile;
        private DisplayRequest _displayRequest;
        private bool _isRecording;
        private bool _suspendedWhileRecording;
        private DisplayInformation _displayInformation;
        private DateTime _recordingStartTime;
        private DispatcherTimer _recordingTimer;

        // These describe the fixed raw PCM format used everywhere in this
        // app (see TryStartRecordingAsync). Kept in one place so the
        // free-space estimate always matches what we actually record.
        private const int SampleRateHz = 48000;

        // Set during capture negotiation in StartButton_Click. Defaults to
        // stereo so the free-space estimate shown before the first recording
        // assumes the best case; it corrects itself after a recording has
        // actually established what this device supports.
        private int _channels = 2;

        private const int BytesPerSample = 2; // 16-bit

        // A regular get-only property (not an expression-bodied member —
        // that's C# 6 syntax, and this project builds with VS2013's
        // default C# 5 compiler) so it always reflects the current
        // _channels value after a mono fallback.
        private long BytesPerSecond
        {
            get { return SampleRateHz * _channels * BytesPerSample; }
        }

        // Whether AudioProcessing.Raw was actually granted for the current
        // recording. Set during negotiation alongside _channels, since the
        // two are decided together rather than independently.
        private bool _rawProcessingUsed;

        // Repo URL for the Info overlay.
        private const string GitHubRepoUrl = "https://github.com/jullius77arch/HAAC-Recorder";

        // Feedback address for the Info overlay. Kept as a plain mailto:
        // link (rather than requiring a GitHub account) so anyone can send
        // a bug report, a note on whether they find the app useful, or
        // their phone model and whether it supports RAW capture.
        private const string FeedbackEmail = "start-07axed@icloud.com";

        public MainPage()
        {
            this.InitializeComponent();

            // If the app gets suspended (screen manually locked, low battery,
            // whatever), finalize the current recording instead of letting it
            // get torn down mid-write and leaving a corrupted WAV file.
            Application.Current.Suspending += App_Suspending;
            Application.Current.Resuming += App_Resuming;

            // Populate the recording-time estimate as soon as the page loads,
            // start the timer to dismiss the splash overlay (which is visible
            // by default in XAML), and lay out for whatever orientation the
            // phone is already in.
            // Explicitly request all rotations at runtime via this API rather
            // than a manifest declaration — the manifest's "Supported
            // Rotations" schema for this project type only accepts the
            // Windows 8.1 tablet/desktop namespace, not this phone project's
            // VisualElements block, so this is the correct place to set it.
            DisplayInformation.AutoRotationPreferences =
                DisplayOrientations.Portrait | DisplayOrientations.Landscape | DisplayOrientations.LandscapeFlipped;

            _displayInformation = DisplayInformation.GetForCurrentView();
            _displayInformation.OrientationChanged += DisplayInformation_OrientationChanged;

            this.Loaded += async (s, e) =>
            {
                await UpdateEstimatedRecordingTimeAsync();
                StartSplashHideTimer();
                ApplyOrientationLayout(_displayInformation.CurrentOrientation);
            };
        }

        #region Capture negotiation

        /// <summary>
        /// One capture configuration to try, in preference order.
        /// </summary>
        private sealed class CaptureAttempt
        {
            public readonly int Channels;
            public readonly bool Raw;

            public CaptureAttempt(int channels, bool raw)
            {
                this.Channels = channels;
                this.Raw = raw;
            }
        }

        /// <summary>
        /// Preference order: stereo raw, stereo standard, mono raw, mono
        /// standard.
        ///
        /// Stereo is deliberately ranked above raw. A second channel can't be
        /// recovered after the fact, while processing artifacts are a matter
        /// of degree — so a phone that only offers raw in mono records in
        /// stereo standard instead.
        ///
        /// When the driver doesn't advertise raw support at all, the raw
        /// entries are omitted entirely rather than attempted and failed,
        /// which saves two pointless MediaCapture initializations on the
        /// majority of devices.
        /// </summary>
        private static CaptureAttempt[] BuildAttemptOrder(bool rawSupported)
        {
            if (rawSupported)
            {
                return new[]
                {
                    new CaptureAttempt(2, true),   // 1. stereo raw
                    new CaptureAttempt(2, false),  // 2. stereo standard
                    new CaptureAttempt(1, true),   // 3. mono raw
                    new CaptureAttempt(1, false)   // 4. mono standard
                };
            }

            return new[]
            {
                new CaptureAttempt(2, false),
                new CaptureAttempt(1, false)
            };
        }

        /// <summary>
        /// Creates and initializes _mediaCapture with the given processing
        /// mode. Returns false (instead of throwing) when the device rejects
        /// that mode, so the caller can move on to the next attempt in the
        /// preference list. Anything else — mic already in use, permission
        /// denied — is left to propagate to StartButton_Click's catch, since
        /// retrying a different channel count wouldn't help with those.
        /// </summary>
        private async Task<bool> TryInitializeCaptureAsync(bool useRawProcessing)
        {
            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio,

                // WP8.1's MediaCategory enum only has Other/Communications (the
                // Media/Speech/etc. members were added later, in Windows 10).
                // Other is the right choice here — Communications tends to force
                // voice-call-style processing on some Lumia firmware.
                MediaCategory = MediaCategory.Other,

                AudioProcessing = useRawProcessing
                    ? Windows.Media.AudioProcessing.Raw
                    : Windows.Media.AudioProcessing.Default
            };

            var capture = new MediaCapture();

            try
            {
                await capture.InitializeAsync(settings);
            }
            catch (ArgumentException)
            {
                // "Value does not fall within the expected range" — the
                // driver claimed raw support but rejected it in practice.
                capture.Dispose();
                return false;
            }
            catch (NotSupportedException)
            {
                capture.Dispose();
                return false;
            }

            _mediaCapture = capture;
            return true;
        }

        /// <summary>
        /// Attempts to start recording with the given channel count. Returns
        /// false (instead of throwing) specifically when the device rejects
        /// that channel count, so the caller can move on to the next attempt.
        /// Any other kind of failure is rethrown to the existing top-level
        /// catch in StartButton_Click.
        /// </summary>
        private async Task<bool> TryStartRecordingAsync(int channels)
        {
            // 16-bit, 48kHz PCM inside a WAV container.
            // We build the profile with CreateWav() to get the right container,
            // then overwrite the Audio properties with the exact raw PCM spec.
            var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
            profile.Audio = AudioEncodingProperties.CreatePcm(
                SampleRateHz, (uint)channels, BytesPerSample * 8);

            try
            {
                await _mediaCapture.StartRecordToStorageFileAsync(profile, _recordingFile);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks whether the phone's default audio capture device
        /// advertises support for "raw" signal processing mode, per
        /// Microsoft's documented System.Devices.AudioDevice.RawProcessingSupported
        /// property key. Returns false (rather than throwing) on any
        /// failure, since the safe default is to not request Raw.
        ///
        /// Treated as a hint rather than a guarantee — TryInitializeCaptureAsync
        /// still guards the initialization, because some drivers advertise
        /// support here and then reject it anyway.
        /// </summary>
        private async Task<bool> IsRawProcessingSupportedAsync()
        {
            try
            {
                var deviceId = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default);
                if (string.IsNullOrEmpty(deviceId))
                {
                    return false;
                }

                var extraProperties = new[] { "System.Devices.AudioDevice.RawProcessingSupported" };
                var deviceInfo = await DeviceInformation.CreateFromIdAsync(deviceId, extraProperties);

                object supported;
                if (deviceInfo.Properties.TryGetValue(
                        "System.Devices.AudioDevice.RawProcessingSupported", out supported)
                    && supported is bool)
                {
                    return (bool)supported;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void DisposeMediaCapture()
        {
            if (_mediaCapture != null)
            {
                _mediaCapture.Dispose();
                _mediaCapture = null;
            }
        }

        #endregion

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;
            Exception failure = null;

            try
            {
                // Save inside Music\recordings so it's easy to find and pull off
                // the phone later — plug in over USB and it shows up as a normal
                // folder in File Explorer under the phone's Music library.
                _recordingFolder = await KnownFolders.MusicLibrary.CreateFolderAsync(
                    "recordings", CreationCollisionOption.OpenIfExists);

                var fileName = string.Format(
                    "HaacRecording-{0}.wav", DateTime.Now.ToString("yyyy-MM-dd-HHmmss"));

                _recordingFile = await _recordingFolder.CreateFileAsync(
                    fileName, CreationCollisionOption.GenerateUniqueName);

                // Walk the preference list until something sticks. Each attempt
                // needs its own MediaCapture because AudioProcessing is fixed at
                // initialization time — switching raw/standard means a full
                // dispose and re-init, not just a different
                // StartRecordToStorageFileAsync call. Channel count, by contrast,
                // is only rejected at start time, which is why the two failure
                // points are handled separately.
                //
                // Reusing the same StorageFile across attempts is safe: a failed
                // StartRecordToStorageFileAsync doesn't write to it.
                bool rawSupported = await IsRawProcessingSupportedAsync();
                var attempts = BuildAttemptOrder(rawSupported);

                bool started = false;

                foreach (var attempt in attempts)
                {
                    if (!(await TryInitializeCaptureAsync(attempt.Raw)))
                    {
                        // This processing mode isn't usable at all — every
                        // channel count under it will fail the same way.
                        continue;
                    }

                    if (await TryStartRecordingAsync(attempt.Channels))
                    {
                        _channels = attempt.Channels;
                        _rawProcessingUsed = attempt.Raw;
                        started = true;
                        break;
                    }

                    // That channel count wasn't accepted under this processing
                    // mode — tear the capture down so the next attempt starts
                    // clean and the handle on _recordingFile is released.
                    DisposeMediaCapture();
                }

                if (!started)
                {
                    try { await _recordingFile.DeleteAsync(); } catch { /* best effort */ }
                    throw new InvalidOperationException(
                        "This device didn't accept raw or standard PCM capture in stereo or mono.");
                }

                _isRecording = true;
                StopButton.IsEnabled = true;
                LockButton.IsEnabled = true;

                // Info opens an overlay with no connection to recording state,
                // but disabling it here keeps the user from wandering into it
                // (and having Back suppressed on top of an overlay) mid-take.
                InfoButton.IsEnabled = false;

                // Report what was actually negotiated, not what was requested.
                SubtitleText.Text = string.Format(
                    "16bit {0} PCM 48kHz Audio — {1}",
                    _channels == 2 ? "Stereo" : "Mono",
                    _rawProcessingUsed ? "Raw" : "Standard processing");

                // Live "RECORDING hh:mm:ss" display. Driven off wall-clock time
                // (not a tick counter) so it can't drift, and it only ever
                // touches this one text label — no connection to _mediaCapture
                // or the recording file, so it can't affect recording stability.
                _recordingStartTime = DateTime.Now;
                UpdateRecordingElapsedText();
                _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _recordingTimer.Tick += (s, args) => UpdateRecordingElapsedText();
                _recordingTimer.Start();

                // Swallow the hardware Back button for the whole recording,
                // not just while locked — a stray Back tap should never be
                // able to exit or navigate away mid-recording. (Start and
                // Search can't be suppressed this way; see LockButton_Click.)
                HardwareButtons.BackPressed += HardwareButtons_BackPressed;

                // Keep the screen from auto-locking on its own while a recording
                // is running. Note this only stops the *idle timeout* from firing —
                // if the user manually presses the physical Power button, the phone
                // still locks and the app still gets suspended regardless of this.
                _displayRequest = new DisplayRequest();
                _displayRequest.RequestActive();
            }
            catch (Exception ex)
            {
                // C# 5 (VS2013's default compiler) doesn't allow await inside a catch
                // block, so we just capture the exception here and handle it below.
                failure = ex;
            }

            if (failure != null)
            {
                StatusText.Text = "Failed to start: " + failure.Message;
                StartButton.IsEnabled = true;
                CleanupCapture();
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new MessageDialog("Are you sure you want to stop recording?");
            var yesCommand = new UICommand("Yes");
            var noCommand = new UICommand("No");
            dialog.Commands.Add(yesCommand);
            dialog.Commands.Add(noCommand);
            dialog.DefaultCommandIndex = 1; // Enter/back defaults to "No"
            dialog.CancelCommandIndex = 1;

            var result = await dialog.ShowAsync();
            if (result != yesCommand)
            {
                // User backed out — keep recording, don't touch any state.
                return;
            }

            StopButton.IsEnabled = false;
            Exception failure = null;

            try
            {
                if (_isRecording)
                {
                    await _mediaCapture.StopRecordAsync();
                    _isRecording = false;
                }

                var props = await _recordingFile.GetBasicPropertiesAsync();
                var sizeMb = props.Size / (1024.0 * 1024.0);
                StatusText.Text = "Stopped.";
                FileInfoText.Text = string.Format(
                    "Saved to:  \\Music\\recordings\\\n{0}\nSize: {1:N2} MB",
                    _recordingFile.Name, sizeMb);

                // Free space just changed, so refresh the estimate. This now
                // also reflects the negotiated channel count, so on a
                // mono-only device the figure gets more accurate after the
                // first recording.
                await UpdateEstimatedRecordingTimeAsync();
            }
            catch (Exception ex)
            {
                // Same reason as above: no await allowed in catch/finally under C# 5,
                // so cleanup happens unconditionally right after this block instead.
                failure = ex;
            }

            if (failure != null)
            {
                StatusText.Text = "Failed to stop: " + failure.Message;
            }

            CleanupCapture();
            StartButton.IsEnabled = true;
            LockButton.IsEnabled = false;
            InfoButton.IsEnabled = true;

            // Nothing is negotiated while idle, so drop back to the generic
            // format line rather than leaving the last session's result up.
            SubtitleText.Text = "16bit Stereo PCM 48kHz Audio";
        }

        private async void LockButton_Click(object sender, RoutedEventArgs e)
        {
            UnlockSlider.Value = 0;
            LockOverlay.Visibility = Visibility.Visible;

            // Back is already suppressed for the whole recording (see
            // StartButton_Click) — locking just adds the black overlay and
            // hides the status bar on top of that.
            await StatusBar.GetForCurrentView().HideAsync();
        }

        private async void UnlockSlider_ValueChanged(
            object sender, RangeBaseValueChangedEventArgs e)
        {
            if (e.NewValue < 95)
            {
                return;
            }

            LockOverlay.Visibility = Visibility.Collapsed;
            UnlockSlider.Value = 0;
            await StatusBar.GetForCurrentView().ShowAsync();
        }

        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            var version = Package.Current.Id.Version;
            InfoVersionText.Text = string.Format(
                "Version {0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision);
            InfoOverlay.Visibility = Visibility.Visible;
        }

        private void InfoCloseButton_Click(object sender, RoutedEventArgs e)
        {
            InfoOverlay.Visibility = Visibility.Collapsed;
        }

        private async void GitHubLink_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(GitHubRepoUrl));
        }

        /// <summary>
        /// Opens the default mail app with a blank message addressed to the
        /// feedback inbox. Exists for people who hit a bug but don't have (or
        /// don't want to make) a GitHub account, and doubles as a channel for
        /// general "is this useful" and phone-model/RAW-support feedback.
        /// </summary>
        private async void FeedbackEmailLink_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("mailto:" + FeedbackEmail));
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            e.Handled = true;
        }

        private async void App_Suspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            var wasRecording = _isRecording;

            try
            {
                if (_isRecording && _mediaCapture != null)
                {
                    await _mediaCapture.StopRecordAsync();
                    _isRecording = false;
                }
            }
            catch
            {
                // Best effort during suspension — no await allowed in a catch
                // block under C# 5, and there's little more we could do here
                // in the last moments before the OS suspends us anyway.
            }

            _suspendedWhileRecording = wasRecording;

            CleanupCapture();
            deferral.Complete();
        }

        private async void App_Resuming(object sender, object e)
        {
            // Windows Phone doesn't re-run App.OnLaunched when the app is
            // merely resumed from a suspended state (only when it's a true
            // cold start or relaunch after termination) — this Resuming
            // event is the only thing guaranteed to fire every time the
            // user reopens the app, so the splash overlay gets shown here
            // too rather than relying solely on the initial page load.
            SplashOverlay.Visibility = Visibility.Visible;
            StartSplashHideTimer();

            // The previous MediaCapture/recording was already torn down before
            // suspension, so we don't try to resume it — the user starts a new
            // segment instead.
            if (!_isRecording)
            {
                StopButton.IsEnabled = false;
                StartButton.IsEnabled = true;
                LockButton.IsEnabled = false;
                InfoButton.IsEnabled = true;
                SubtitleText.Text = "16bit Stereo PCM 48kHz Audio";
            }

            if (_suspendedWhileRecording)
            {
                StatusText.Text = "Previous recording ended — screen was locked. Saved and ready to start a new one.";
                _suspendedWhileRecording = false;

                // A recording just finished, so free space changed — refresh the estimate.
                await UpdateEstimatedRecordingTimeAsync();
            }

            // Defensively unwind the lock overlay in case suspension happened
            // while it was showing, so we don't come back to a stuck black
            // screen with a hidden status bar.
            if (LockOverlay.Visibility == Visibility.Visible)
            {
                LockOverlay.Visibility = Visibility.Collapsed;
                UnlockSlider.Value = 0;
                await StatusBar.GetForCurrentView().ShowAsync();
            }

            // Same defensive cleanup for the Info overlay — nothing bad happens
            // if it's left open across a suspend, but there's no reason to let
            // it linger over a freshly-resumed session either.
            if (InfoOverlay.Visibility == Visibility.Visible)
            {
                InfoOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task UpdateEstimatedRecordingTimeAsync()
        {
            try
            {
                // There's no DriveInfo in a WinRT app — querying this "extra
                // property" on a storage folder is the standard way to find
                // out how much free space is on the drive it lives on.
                var properties = await KnownFolders.MusicLibrary.Properties.RetrievePropertiesAsync(
                    new[] { "System.FreeSpace" });

                var freeSpaceBytes = (ulong)properties["System.FreeSpace"];
                var totalSeconds = freeSpaceBytes / (ulong)BytesPerSecond;

                var hours = totalSeconds / 3600;
                var minutes = (totalSeconds % 3600) / 60;

                FreeSpaceText.Text = string.Format(
                    "Recording time available: {0}h {1:00}m", hours, minutes);
            }
            catch (Exception)
            {
                // Free-space lookup is a nice-to-have, not essential — if it
                // fails for any reason, just leave the field blank rather
                // than blocking the rest of the UI.
                FreeSpaceText.Text = string.Empty;
            }
        }

        private void StartSplashHideTimer()
        {
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };

            timer.Tick += (s, args) =>
            {
                timer.Stop();
                SplashOverlay.Visibility = Visibility.Collapsed;
            };

            timer.Start();
        }

        private void DisplayInformation_OrientationChanged(DisplayInformation sender, object args)
        {
            ApplyOrientationLayout(sender.CurrentOrientation);
        }

        private void ApplyOrientationLayout(DisplayOrientations orientation)
        {
            // This only ever repositions the existing controls (via Grid.Row/
            // Grid.Column/Margin) — it never touches _mediaCapture, the
            // recording file, or _isRecording, so a recording in progress is
            // completely unaffected by rotating the phone.
            var isLandscape = orientation == DisplayOrientations.Landscape ||
                               orientation == DisplayOrientations.LandscapeFlipped;

            if (isLandscape)
            {
                // Tighten the header so the button row and status text below
                // it have room to fit within the shorter landscape height.
                HeaderPanel.Margin = new Thickness(20, 15, 20, 10);
                TitleText.FontSize = 26;
                SubtitleText.FontSize = 14;

                // Info button goes back to being a small utility button in
                // the header's top-right corner, same as before this button
                // grew a text label — there's no cramped-reach problem in
                // landscape, so no need to drop it down next to Lock Screen.
                Grid.SetRow(InfoButton, 0);
                InfoButton.Width = 70;
                InfoButton.Height = 40;
                InfoButton.FontSize = 16;
                InfoButton.Margin = new Thickness(0, 15, 20, 0);

                // All three buttons side-by-side in one row instead of stacked.
                Grid.SetRow(StartButton, 0);
                Grid.SetColumn(StartButton, 0);
                Grid.SetColumnSpan(StartButton, 1);
                StartButton.Margin = new Thickness(0, 0, 5, 0);

                Grid.SetRow(StopButton, 0);
                Grid.SetColumn(StopButton, 1);
                Grid.SetColumnSpan(StopButton, 1);
                StopButton.Margin = new Thickness(5, 0, 5, 0);

                Grid.SetRow(LockButton, 0);
                Grid.SetColumn(LockButton, 2);
                Grid.SetColumnSpan(LockButton, 1);
                LockButton.Margin = new Thickness(5, 0, 0, 0);

                StatusText.Margin = new Thickness(0, 15, 0, 0);
                FreeSpaceText.Margin = new Thickness(0, 8, 0, 0);
                FileInfoText.Margin = new Thickness(0, 10, 0, 0);
            }
            else
            {
                // Portrait — the original stacked layout.
                HeaderPanel.Margin = new Thickness(20, 40, 20, 20);
                TitleText.FontSize = 32;
                SubtitleText.FontSize = 16;

                Grid.SetRow(StartButton, 0);
                Grid.SetColumn(StartButton, 0);
                Grid.SetColumnSpan(StartButton, 3);
                StartButton.Margin = new Thickness(0, 0, 0, 10);

                Grid.SetRow(StopButton, 1);
                Grid.SetColumn(StopButton, 0);
                Grid.SetColumnSpan(StopButton, 3);
                StopButton.Margin = new Thickness(0, 0, 0, 10);

                // Narrowed from a full-width span (3 columns) to 2, leaving
                // the rightmost column open for the Info button below.
                Grid.SetRow(LockButton, 2);
                Grid.SetColumn(LockButton, 0);
                Grid.SetColumnSpan(LockButton, 2);
                LockButton.Margin = new Thickness(0, 0, 10, 0);

                // Info button drops down to sit beside Lock Screen instead of
                // hugging the header, since that corner is an awkward reach
                // on small displays like the Lumia 928. It isn't actually a
                // member of the ButtonRow grid, so its position is worked out
                // by hand rather than by column: it lives in the same outer
                // Grid.Row as the StackPanel ("1"), and its top margin is the
                // StackPanel's own top margin (20) plus the combined height of
                // the Start and Stop rows above Lock Screen (80 + 10 each =
                // 180), landing its top edge flush with Lock Screen's. Height
                // matches Lock Screen's 80 so the two bottoms align too, and
                // the right margin matches the StackPanel's so its right edge
                // sits flush with the page's content edge.
                Grid.SetRow(InfoButton, 1);
                InfoButton.Width = 90;
                InfoButton.Height = 80;
                InfoButton.FontSize = 20;
                InfoButton.Margin = new Thickness(0, 200, 20, 0);

                StatusText.Margin = new Thickness(0, 30, 0, 0);
                FreeSpaceText.Margin = new Thickness(0, 10, 0, 0);
                FileInfoText.Margin = new Thickness(0, 20, 0, 0);
            }
        }

        private void UpdateRecordingElapsedText()
        {
            var elapsed = DateTime.Now - _recordingStartTime;
            StatusText.Text = string.Format(
                "RECORDING  {0:00}:{1:00}:{2:00}",
                (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
        }

        private void CleanupCapture()
        {
            // Recording has ended one way or another — stop swallowing Back
            // and stop the elapsed-time stopwatch.
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;

            if (_recordingTimer != null)
            {
                _recordingTimer.Stop();
                _recordingTimer = null;
            }

            if (_displayRequest != null)
            {
                _displayRequest.RequestRelease();
                _displayRequest = null;
            }

            DisposeMediaCapture();
        }
    }
}
