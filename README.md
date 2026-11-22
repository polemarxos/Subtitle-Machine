# Subtitle Machine

Windows app that creates translated subtitles for your videos, **fully offline and local**:

1. **Extracts** text subtitles from the video (MKV/MP4/…), or
2. **Reads image subtitles** (Blu-ray PGS / DVD VobSub) with the OCR built into Windows, or
3. **Transcribes the audio** with [whisper.cpp](https://github.com/ggml-org/whisper.cpp) (also for audio files: mp3, wav, m4a, flac…),
4. **Translates** with Google's **TranslateGemma** (4B / 12B / 27B), running locally with [llama.cpp](https://github.com/ggml-org/llama.cpp). No Ollama, no cloud.
5. Saves `Movie.el.srt`, `Movie.de.srt`, … (one per target language), or replaces the original subtitle file if you choose that.

Everything it needs is **downloaded automatically from the official sources** and can be **updated from inside the app**. The app **updates itself** from GitHub Releases.

## Features

- Drag & drop files or whole folders; batch processing with per-file status
- **Create from:** embedded subtitles (otherwise audio) · embedded only · **audio only**
- **Several target languages in one run**
- **Context-aware translation:** lines are translated in groups of 4, so names, gender and tone stay consistent. It falls back to line-by-line automatically.
- **Voice detection (Silero VAD):** Whisper skips silence, so fewer invented lines
- **OCR** for PGS and VobSub image subtitles
- **Readable output:** lines re-wrapped to a maximum length (default 42 characters), minimum display time of 1 second, no overlaps, very short lines merged
- TranslateGemma size chosen **automatically from your GPU memory**, or manually
- NVIDIA (CUDA), AMD / Intel (Vulkan) or CPU, detected automatically
- Reads non-UTF-8 subtitle files (e.g. Windows-1253 Greek) and writes UTF-8
- **Video output:** add the new subtitles into an MKV copy as extra tracks (no re-encoding, language tags set) and/or make an MP4 copy with the subtitles **burned into the picture** (uses the GPU's video encoder when available)
- **Glossary:** names and terms that must always be translated the same way, or never translated (*Components & settings › Edit glossary*). A starter glossary for English ↔ Greek (about 250 entries: agencies and brands kept as they are, ranks, courtroom and police terms, holidays, common first names in both directions) is created on first start.
- **Resume:** after Stop, a crash or a power cut, the next Start continues where the translation stopped
- **Auto-sync of downloaded subtitles:** a subtitle file next to a video with the same name (`Movie.en.srt` + `Movie.mkv`) is matched to the speech in the video's audio. Fixes subtitles that are early or late and the common frame-rate mismatch (a 25 fps subtitle on a 23.976 fps video that drifts more and more). The timing is only changed when the match is clear – subtitles for a different cut are left alone. About 1–2 seconds per hour of video.
- **Subtitle editor:** double-click a finished file to review original and translation side by side, fix text or times, re-translate selected lines and save
  - **Spell check** with red underlines, suggestions on right-click, *Add to dictionary* and F7 for the next mistake (LibreOffice Hunspell dictionaries for Greek, English and 18 more languages, downloaded on first use; names from the glossary are never marked)
  - **Sync to video:** match the timing of any subtitle file to the video with one click
- **Compare subtitles:** two files side by side, matched by time, with changed words marked, timing differences in orange, and lines that exist in only one file in red/green; *Show only differences* and *Next difference* (F3)
- **When finished:** do nothing, sleep, hibernate or shut down (with a 60-second countdown you can cancel)
- **Hearing-impaired cleanup:** removes [MUSIC], (laughs), speaker names like `JOHN:` and ♪ song lyrics ♪ before translating (on by default)
- **Greek or English interface**, chosen automatically from Windows or in *Components & settings*
- **Light, dark or Windows theme**, and a Windows notification with sound when a batch is done

## Download

From [Releases](../../releases):

| File | |
|---|---|
| `SubtitleMachine-standalone.exe` | Works on any Windows 10/11 x64 PC, nothing else to install |
| `SubtitleMachine.exe` | Smaller; needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

Each edition updates itself with the newest file of the same edition.

## Components (downloaded automatically)

| Component | Source | Size |
|---|---|---|
| FFmpeg / FFprobe / FFplay | [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) release essentials (mirror: GyanD/codexffmpeg) | ~115 MB |
| whisper.cpp (CUDA 12 or CPU build) | [ggml-org/whisper.cpp releases](https://github.com/ggml-org/whisper.cpp/releases) | 16–460 MB |
| Whisper model (default `large-v3-turbo`) | [ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp) | 0.15–3.1 GB |
| Silero VAD model | [ggml-org/whisper-vad](https://huggingface.co/ggml-org/whisper-vad) | 0.9 MB |
| llama.cpp (CUDA / Vulkan / CPU build) | [ggml-org/llama.cpp releases](https://github.com/ggml-org/llama.cpp/releases) | 30–600 MB |
| TranslateGemma 4B / 12B / 27B (GGUF Q4_K_M) | [mradermacher/translategemma-*-it-GGUF](https://huggingface.co/mradermacher/translategemma-12b-it-GGUF) (fallback: anilb/translategemma-gguf) | 2.5 / 7.3 / 16.5 GB |

Downloads resume after interruptions and are verified with SHA-256.
If the newest whisper.cpp / llama.cpp release has no Windows build, the newest release that has one is used.
**Components & settings** shows the installed and latest versions and installs updates.

### Automatic model choice

| GPU memory (VRAM) | Model |
|---|---|
| 22 GB or more | TranslateGemma 27B |
| 10–22 GB | TranslateGemma 12B |
| less than 10 GB / no GPU | TranslateGemma 4B |

You can always choose manually. A model larger than your VRAM still works; part of it runs on the CPU, which is slower.

### OCR languages

Image subtitles are read with Windows' own OCR. Windows needs the OCR pack of the subtitle language. If it is missing, the app tells you: add the language in *Settings › Time & language › Language & region* with *Optical character recognition* ticked.

## Settings (`config.json`)

Stored next to the exe (or in `%LocalAppData%\Subtitle Machine` if that folder is read-only).

| Key | Default | Meaning |
|---|---|---|
| `TargetLanguages` | `["el"]` | Target languages (ISO 639-1) |
| `TranscribeLanguage` | `auto` | Source language for audio / SRT |
| `SubtitleSource` | `auto` | `auto`, `embedded`, `audio` |
| `TranslateModelSize` | `auto` | `auto`, `4b`, `12b`, `27b` |
| `GpuBackend` | `auto` | `auto`, `cuda`, `vulkan`, `cpu` |
| `WhisperModel` | `large-v3-turbo` | whisper.cpp model |
| `UseVad` | `true` | Voice detection for Whisper |
| `OcrImageSubtitles` | `true` | OCR for PGS / VobSub |
| `ContextTranslation` | `true` | Translate lines in context |
| `FixFormatting`, `MaxLineLength` | `true`, `42` | Line length & timing fixes |
| `VideoOutput` | `none` | `none`, `mkv` (subtitles added to `Movie.subtitled.mkv`), `burn` (`Movie.el.hardsub.mp4`), `both` |
| `KeepSrtFiles` | `true` | Keep the .srt files after adding them into the MKV |
| `Theme` | `system` | `system`, `dark`, `light` |
| `NotifyWhenDone` | `true` | Notification and sound when a batch is finished |
| `ReplaceOriginal` | `true` | Write the translation into the original subtitle file (one target language only) |
| `OverwriteExisting` | `false` | *Redo files that are already finished.* Off: files made in an earlier run (e.g. `Movie.el.srt`) are kept and skipped. On: everything is made again and the old files are replaced – useful after changing the model or glossary |
| `RemoveHearingImpaired` | `true` | Remove [music], (laughs), JOHN:, ♪ lyrics ♪ before translating |
| `AutoSyncSubtitles` | `true` | Match the timing of subtitle files to the speech of the video next to them |
| `SpellCheck` | `true` | Mark spelling mistakes in the subtitle editor |
| `UiLanguage` | `auto` | Interface language: `auto` (like Windows), `en`, `el` |
| `AutoUpdateApp` | `true` | Update the app automatically from GitHub |
| `CheckComponentUpdates` | `true` | Check component updates on startup |

The glossary is stored in `glossary.txt` next to `config.json`:

```
Nick = Nick            # all languages
Winterfell =           # empty → keep the word untranslated
[el]                   # only for Greek
Nick = Νίκος
Ministry of Magic = Υπουργείο Μαγείας
```

Start with `-debug` to open a debug console. A log is always written to `subtitle_machine.log`.

## Building

```
git clone https://github.com/polemarxos/Subtitle-Machine.git
cd Subtitle-Machine
dotnet test tests/SubtitleMachine.Tests/SubtitleMachine.Tests.vbproj
dotnet publish "Subtitle Machine.vbproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Or open `Subtitle Machine.slnx` in Visual Studio 2026.

## Releasing

Push a tag like `v1.3.0`. GitHub Actions runs the tests, builds both exes and creates the release.
Running copies find it on their next start, download it in the background, and install it when closed (or immediately with *Restart now*).

> Self-update reads the GitHub Releases without logging in, so **the repository must be public**. For a private repository the update check simply finds nothing.

## Third-party licences

Downloaded at runtime, not bundled: FFmpeg (GPL v3 for the gyan.dev builds), whisper.cpp (MIT), llama.cpp (MIT), Whisper & Silero models (MIT),
TranslateGemma: [Gemma Terms of Use](https://ai.google.dev/gemma/terms).
