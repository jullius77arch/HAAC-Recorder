
<div align="center">

# 🎙️ HAAC Recorder

**Raw, unprocessed PCM audio recording for Windows Phone 8.1**

![Platform](https://img.shields.io/badge/platform-Windows%20Phone%208.1-0078D7?logo=windows&logoColor=white)
![Language](https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white)
![IDE](https://img.shields.io/badge/built%20with-Visual%20Studio%202013-5C2D91?logo=visualstudio&logoColor=white)
![License](https://img.shields.io/badge/license-unlicensed-lightgrey)

</div>

---

Most phone recording apps run audio through AGC, echo cancellation, and noise suppression before saving it. **HAAC Recorder skips all of that.** It taps directly into the raw audio pipeline, capturing the true, unprocessed signal from the HAAC (High Amplitude Audio Capture) mics on select Nokia Lumia phones — ideal for loud sources like concerts or instruments where normal processing would clip or distort.

## ✨ Features

- 🎚️ **Raw capture** — `AudioProcessing.Raw` disables AGC/AEC/noise suppression and engages the high-amplitude mic path on supported hardware
- ▶️ **One-tap start/stop** with large, simple controls
- 🔒 **Lock screen while recording** — slide-to-unlock overlay + suppressed Back button so nothing interrupts a take
- 💾 **Suspend-safe** — recordings are finalized properly if the phone suspends mid-capture
- 📊 **Live free-space estimate** based on the fixed recording bitrate
- 🔄 **Portrait & landscape** layouts

## 📁 Output Format

| Property      | Value                        |
|---------------|-------------------------------|
| Container     | `.wav`                        |
| Sample rate   | 48 kHz                        |
| Channels      | Stereo                        |
| Bit depth     | 16-bit PCM                    |
| Save location | `Music\recordings\` on device |

## 🛠️ Build & Run

1. Open `HaacRecorder.sln` in **Visual Studio 2013 (Update 5)** with the WP8.1 SDK installed.
2. Pick a target platform (`x86`, `ARM`, or `Any CPU`).
3. Deploy to a physical Lumia device (ARM), or run in the WP8.1 emulator.

## 📱 Usage

**Start Recording** → **Lock Screen** (optional) → **Stop Recording** → find your `.wav` in the Music library.

## 🔧 Notes

- WinRT (XAML) app, not Silverlight.
- `MediaCategory.Other` is used instead of `Communications` to avoid voice-call-style processing on some Lumia firmware.

---

<div align="center">
<sub>Built with the help of <a href="https://claude.ai">Claude</a> 🤖</sub>
</div>
