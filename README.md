
<div align="center">

# 🎙️ HAAC Recorder

**Raw, unprocessed PCM audio recording for Windows Phone 8.1**

![Platform](https://img.shields.io/badge/platform-Windows%20Phone%208.1-0078D7?logo=windows&logoColor=white)
![Language](https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white)
![IDE](https://img.shields.io/badge/built%20with-Visual%20Studio%202013-5C2D91?logo=visualstudio&logoColor=white)
![License](https://img.shields.io/badge/license-GPLv3-blue)

</div>

---

Most phone recording apps run audio through AGC, echo cancellation, and noise suppression before saving it. **HAAC Recorder tries to bypass all of that**, requesting raw, unprocessed audio straight from the driver — useful for loud sources like concerts or instruments, where normal processing would pump, clip, or duck the signal.

Hardware support varies, so the app doesn't assume anything. At the moment you hit Start it negotiates with the device, working down a preference list until something is accepted, and tells you what it settled on.

## ✨ Features

- 🎚️ **Raw capture where the hardware allows it** — requests `AudioProcessing.Raw` to disable AGC/AEC/noise suppression, and degrades gracefully when the driver won't allow it
- 🥇 **Quality-first negotiation** — stereo is preferred over raw, so you never lose a channel to gain a processing mode
- 🔎 **Honest status line** — the subtitle always shows the mode actually in use, not the one that was requested
- ▶️ **One-tap start/stop** with large, simple controls
- 🔒 **Lock screen while recording** — slide-to-unlock overlay + suppressed Back button so nothing interrupts a take
- 💾 **Suspend-safe** — recordings are finalized properly if the phone suspends mid-capture
- ⏱️ **Live elapsed-time display** driven off wall-clock time, so it can't drift
- 📊 **Free-space estimate** based on the current recording bitrate
- 🔄 **Portrait & landscape** layouts

## 🎯 Capture Negotiation

Channel count and processing mode are independent, and not every combination exists on every phone. The app tries them in this order and keeps the first one that works:

| Priority | Channels | Processing | Result |
|----------|----------|------------|--------|
| 1 | Stereo | Raw | Best case — two channels, no processing |
| 2 | Stereo | Standard | Two channels, driver applies its own processing |
| 3 | Mono | Raw | One channel, no processing |
| 4 | Mono | Standard | Fallback of last resort |

The ordering is deliberate: **stereo is preferred over raw**. A second channel is unrecoverable once lost, whereas processing artifacts are at least partly a matter of degree. A phone that can only do raw in mono will therefore record in stereo standard rather than mono raw.

If the driver doesn't advertise raw support at all, priorities 1 and 3 are skipped outright, and the device goes straight to stereo standard, then mono standard.

### Why each step can fail

- **Raw processing** is set on `MediaCaptureInitializationSettings`, so it's fixed when the `MediaCapture` is created. A driver that doesn't implement the raw signal-processing DDI throws `ArgumentException` from `InitializeAsync`. Support is checked in advance via the documented `System.Devices.AudioDevice.RawProcessingSupported` property key, but the check is treated as a hint rather than a guarantee — the initialization is still wrapped, because some drivers advertise support and then reject it.
- **Channel count** is set later, at `StartRecordToStorageFileAsync`. A device that won't do the requested count throws `ArgumentException` there instead. Some Lumia 92x firmware exposes only a mono raw path despite having multiple HAAC mics physically present.

Because the two settings are applied at different stages, each row in the table above needs its own initialize-and-start cycle. Moving from raw to standard means disposing the `MediaCapture` and building a new one — it isn't a parameter that can be changed in place.

## 📁 Output Format

| Property      | Value                                    |
|---------------|------------------------------------------|
| Container     | `.wav`                                   |
| Sample rate   | 48 kHz                                   |
| Channels      | Stereo, or mono if the device requires it |
| Bit depth     | 16-bit PCM                               |
| Save location | `Music\recordings\` on device            |

Files are named `HaacRecording-yyyy-MM-dd-HHmmss.wav`.

## 📱 Usage

**Start Recording** → check the subtitle to see what was negotiated → **Lock Screen** (optional) → **Stop Recording** → find your `.wav` in the Music library.

While recording, the subtitle reads as one of:

```
16bit Stereo PCM 48kHz Audio — Raw
16bit Stereo PCM 48kHz Audio — Standard processing
16bit Mono PCM 48kHz Audio — Raw
16bit Mono PCM 48kHz Audio — Standard processing
```

It reverts to the generic `16bit Stereo PCM 48kHz Audio` once recording stops, since nothing has been negotiated at that point.

## 🛠️ Build & Run

1. Open `HaacRecorder.sln` in **Visual Studio 2013 (Update 5)** with the WP8.1 SDK installed.
2. Pick a target platform (`x86`, `ARM`, or `Any CPU`).
3. Deploy to a physical Lumia device (ARM), or run in the WP8.1 emulator.

The emulator has no HAAC hardware and no raw driver support, so it will always land on stereo standard — useful for exercising the fallback path, not for judging audio quality.

## 🔧 Notes

- WinRT (XAML) app, not Silverlight.
- `MediaCategory.Other` is used instead of `Communications`, which forces voice-call-style processing on some Lumia firmware.
- Targets C# 5 (VS2013's default compiler) — no expression-bodied members, and no `await` inside `catch`/`finally`.
- The free-space estimate assumes stereo until a recording has actually been negotiated. On a mono-only device the figure shown before the first recording is pessimistic by roughly half, and corrects itself afterwards.
- HAAC's anti-clipping behavior happens in a hardware preamp stage, so standard processing still handles loud sources reasonably well on phones that lack raw driver support.

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

---

<div align="center">
<sub>Built with the help of <a href="https://claude.ai">Claude</a> 🤖</sub>
</div>
