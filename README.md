# Spindle

**English** · [Türkçe](README.tr.md)

**A 100% local, offline file converter for Windows. Drag, press a key, drop.**

While dragging a file in File Explorer, press **Shift**: a ring of keys opens around the cursor.
Drop the file on the format you want; the converted copy is saved next to the original.
Everything happens on your computer; no file is ever uploaded.

**[⬇ Download Spindle.exe](https://github.com/Omrndr/spindle/releases/latest/download/Spindle.exe)** · Windows 10/11 · 52 MB · [installation and requirements](#installation) · [website](https://korayakdogan.me/spindle/)

<p align="center"><img src="docs/tanitim-donustur.gif" width="720" alt="rapor.docx is dragged, Shift is pressed, and it is dropped on the PDF key"></p>

The interface is inspired by [Tangerine](https://tangerineformac.com) for macOS.
Spindle's interface is in Turkish for now, as you can see in the animations above.

## 100% local

- **Your files never leave your computer.** Every conversion runs on this PC, using Office, Magick.NET, ffmpeg and
  Windows' own PDF engine. Nothing is uploaded to a server.
- **No internet needed.** Spindle never connects anywhere on its own: no account, no ads, no usage data.
  The only exception is the optional "Install ffmpeg" button in Settings, which runs winget when you click it.
- **Open source.** All the code is here; you can read it and build it yourself.

## How to use

The first time Spindle starts, it shows the three short animations below. You can open them again from the tray icon
menu or from the **Nasıl kullanılır?** (How to use) link in Settings.

### 1. Drag + Shift: convert

Press **Shift** while dragging a file and drop it on one of the keys. The key under the cursor goes down instantly and
takes the color of the target format; the round key in the middle says what will happen (`PDF · rapor.docx · → belge`).
A small card in the corner shows progress and opens the new file with **Göster** (Show) when it's done.

### 2. Drag + Ctrl: tools

<p align="center"><img src="docs/tanitim-araclar.gif" width="720" alt="foto.jpg is dragged, Ctrl is pressed, and it is dropped on the compress key"></p>

**Ctrl** opens the tools: compress, shrink, rotate, strip metadata, extract audio… The result is again saved next to the
original under a new name (`foto (sıkıştırılmış).jpg`, "compressed").

### 3. Right-click menu

<p align="center"><img src="docs/tanitim-sag-tik.gif" width="720" alt="klip.mp4 is right-clicked, Spindle, Convert is chosen, and the MP3 key is clicked"></p>

If you'd rather not drag, right-click a file, choose **Spindle ▸ Dönüştür…** (Convert) or **Araçlar…** (Tools), and
click a key. The menu is turned on in Settings (or on the last page of the intro). On Windows 11 it's under
"Show more options".

The trigger keys can be changed in Settings (Shift, Ctrl, Alt). If you select several files, the wheel only shows the
options they all share. Outputs never overwrite an existing file (they get a new name such as `rapor 2.pdf`).

## Supported formats

### Convert (Shift)

The source's own format is not shown on the wheel (for example, a DOCX file has no DOCX key).

| Type | Opens | Converts to | Needs |
|---|---|---|---|
| Word document | doc, docx, docm, dot, dotx, rtf, odt, txt | PDF, DOCX, ODT, RTF, TXT, HTML, DOC | Microsoft Office |
| Excel spreadsheet | xls, xlsx, xlsm, ods, csv | PDF, XLSX, CSV, ODS, XLS | Microsoft Office |
| PowerPoint presentation | ppt, pptx, pps, ppsx, odp | PDF, PPTX, PNG (one per slide), ODP, PPT | Microsoft Office |
| PDF | pdf | DOCX (needs Office), PNG and JPG (one per page) | — |
| Image | jpg, jpeg, jfif, png, webp, heic, heif, avif, bmp, tif, tiff, gif, ico, svg, psd, tga | PNG, JPG, WEBP, PDF, AVIF, TIFF, BMP, GIF, ICO | — |
| Camera RAW | raf (Fujifilm), raw (Panasonic/Leica), dng, cr2, cr3 (Canon), nef (Nikon), arw (Sony), orf (Olympus), rw2 (Panasonic), pef (Pentax), srw (Samsung) | PNG, JPG, WEBP, PDF, AVIF, TIFF, BMP, GIF, ICO | — |
| Audio | mp3, m4a, wav, flac, ogg, oga, opus, aiff, aif, wma, aac | MP3, M4A, WAV, FLAC, OGG, OPUS, AIFF, AAC | ffmpeg |
| Video | mp4, m4v, mov, mkv, webm, avi, wmv, flv, mpg, mpeg, 3gp, ts | MP4, MOV, MKV, WEBM, GIF, AVI; audio only as MP3 or M4A | ffmpeg |
| Archive | zip, tar, tar.gz, tgz | ZIP | — |

### Tools (Ctrl)

Output names come from the (Turkish) interface; the English meaning is in parentheses.

| Type | Tool | Output |
|---|---|---|
| Image | Compress | `foto (sıkıştırılmış).jpg` (compressed) |
| | Strip metadata (location, camera info…) | `foto (temiz).jpg` (clean) |
| | Shrink by 50% | `foto (küçük).jpg` (small) |
| | Rotate 90° left / right | `foto (döndürülmüş).jpg` (rotated) |
| | Black and white | `foto (siyah-beyaz).jpg` |
| Video | Compress | `klip (sıkıştırılmış).mp4` |
| | Downscale to 720p | `klip (720p).mp4` |
| | Remove audio | `klip (sessiz).mp4` (muted) |
| | Extract audio as MP3 | `klip.mp3` |
| Audio | Compress | `müzik (sıkıştırılmış).mp3` |
| | Normalize loudness | `müzik (dengelenmiş).mp3` (normalized) |
| Archive | Extract to folder | an `arşiv` folder |
| Any file or folder | Zip, copy path | `rapor.zip`, file path on the clipboard |

## Look and feel

<p align="center">
  <img src="docs/cark-donustur.png" width="260" alt="Convert wheel">
  <img src="docs/cark-araclar.png" width="260" alt="Tools wheel">
  <img src="docs/cark-koyu.png" width="260" alt="Video wheel in dark mode">
</p>

- **Every key shows the family of the target file:** documents blue (PDF red), spreadsheets and images green,
  video purple, audio orange. The tools wheel uses a single color (blue). It follows Windows' light/dark theme.
- **If Office or ffmpeg is missing,** the related keys are dimmed; hovering shows "Office gerekli" / "ffmpeg gerekli"
  (Office / ffmpeg required) in the middle, and the file can't be dropped there.
- **Sounds:** a short, soft click when moving between keys; a short, soft chime when the queue is done.
  They can be turned off in Settings. There are no sound files; both sounds are generated in memory on first use (~32 KB).
- **Notification card:** nothing running in the background is visible (Word, Excel, PowerPoint and ffmpeg run hidden);
  only the small card in the corner shows. The logo's segments light up in turn and the bar fills by percentage.

<p align="center">
  <img src="docs/kart-yukleniyor.png" width="340" alt="Loading">
  <img src="docs/kart-tamam.png" width="340" alt="Done">
</p>

## Installation

**[Download Spindle.exe](https://github.com/Omrndr/spindle/releases/latest/download/Spindle.exe)** (Windows, 52 MB) and run it.

There is no installer: on first launch Spindle copies itself to `%LOCALAPPDATA%\Programs\Spindle`, adds itself to the
Start menu and to Windows' **Apps** list, then shows the intro. No administrator rights are needed.

- Because the app isn't code-signed, Windows SmartScreen may say "Windows protected your PC":
  click **More info → Run anyway**.
- To update, download the new `Spindle.exe` and run it; the running version closes and your settings are kept.
- To uninstall: Windows Settings → **Apps** → Spindle → **Uninstall**. The right-click menu, autostart and settings are
  removed too.
- The right-click menu, "start with Windows" and sounds are turned on and off in Spindle's Settings.

<p align="center"><img src="docs/ayarlar.png" width="420" alt="Settings"></p>

### Requirements

- Windows 10 (2004) or Windows 11 and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
  (if it's missing, Spindle shows a download link when it starts)
- **Microsoft Office** (including Microsoft 365) for Word/Excel/PowerPoint conversions and PDF → Word
- **ffmpeg** for audio and video. The "Install ffmpeg" button in Settings runs `winget install Gyan.FFmpeg`.
- Images, PDF pages and archives need nothing else.

### Building from source

```powershell
git clone https://github.com/Omrndr/spindle.git
cd spindle
powershell -File scripts\install.ps1
```

The script builds the single-file `Spindle.exe` and runs it; the exe installs itself as described above.
`scripts\uninstall.ps1` removes everything without asking.

## Why does Word → PDF work?

This project started because [File Converter](https://github.com/Tichau/FileConverter) 2.2 can't convert Word to PDF.
File Converter 2.2 switched to extra DLLs called NetOffice to talk to Office, but those DLLs were left out of the
installer; even with Office installed, the conversion crashes with `FileNotFoundException`
([Tichau/FileConverter#721](https://github.com/Tichau/FileConverter/issues/721)).

Spindle talks to Office directly through Windows' COM infrastructure (`Word.Application`), without any extra DLLs. Also:

- Office runs invisibly. If it opens an unexpected dialog, Spindle makes it transparent, moves it off-screen before it
  appears, and answers it. The separate `PDFREFLOW.EXE` process that shows Word's PDF warning is watched too.
- If Office (or helpers like PDFREFLOW) sits without using any CPU for a long time, it is treated as stuck: helper
  processes are closed first, and if Office still doesn't respond, only the Office instance Spindle started is closed.
- If Spindle attaches to a Word/Excel you already have open, it leaves your documents and settings (such as visibility)
  alone.

## Speed

Compared with File Converter 2.2 on the same PC with the same files (12 cores, Microsoft 365):

| Scenario | File Converter 2.2 | Spindle |
|---|---|---|
| Word → PDF (small document) | crashes (missing DLL) | 4.5 s; 1.4 s when Word was pre-started while the wheel was open |
| 3 small Word documents in a row | crashes | 2.6 s (one Word instance) |
| 10 JPGs (6 MP) → PNG | 7.4 s | 2.8 s |
| WAV (60 s) → MP3 | 1.4 s | 0.8 s |
| MP4 1080p (15 s) → WEBM | not done after 4 minutes | 20 s |

- **Word is kept warm.** Starting Word alone takes ~3.5 s. As soon as the wheel opens for an Office document, Spindle
  starts Word in the background, reuses the same Word for consecutive files, and closes it a few seconds after the job.
- **Multi-file jobs run in parallel:** images by core count (up to 6), audio/video two at a time.
  Office files are converted one by one in a single Word instance.
- **WEBM** is encoded with VP9's fast quality setting (`-cpu-used 4`): ~3.4× faster than the default, with a quality
  difference too small to measure (SSIM 0.9975 → 0.9968).

For large Word documents with many text boxes and equations, the time is set by Word's own PDF rendering
(~2 minutes for a document with 85 pages, 870 text boxes and 324 equations). That step happens inside Word,
so it's the same for every converter that uses Word.

## Resource usage

| | File Converter 2.2 | Spindle |
|---|---|---|
| Install size | 147 MB | 52 MB, single file (including the three intro GIFs, ~540 KB total) |
| Idle | no background process (the right-click extension loads inside Explorer) | waits in the tray: ~7 MB RAM, ~0 CPU |
| After a window was opened and closed | — | back to ~12 MB RAM within 30 s |
| Peak memory during 6 MP JPG → PNG | 180 MB | 119 MB |
| Peak memory during WAV → MP3 | 100 MB (+ ffmpeg 18 MB) | 48 MB (+ ffmpeg 18 MB) |
| Word conversion | crashes | Word's own memory (~250 MB) during the job and for at most 8 s after (25 s if pre-started while the wheel was open) |

- **The mouse isn't tracked.** There is only a keyboard hook, and it ignores every key except Shift/Ctrl/Alt/Esc.
  When the trigger key is pressed, Spindle checks once whether the left mouse button is down. The rest of the drag is
  only followed while the wheel is open, every 50 ms.
- **Idle memory is returned:** after 30 s with no window open and no conversion running, unused memory is given back
  to Windows.
- Whether Office/ffmpeg is installed is cached for 30 s; the registry and disk aren't scanned every time the wheel opens.
- The **notification card** isn't a transparent (layered) window; Windows draws the shadow and rounded corners, and the
  card updates 8 times a second. While "loading" it uses ~1.8% of one core (24.4% in the first version, which had a
  spinning-arrow, transparent window).
- The **intro GIFs** are decoded once on the first loop; later loops only write the small regions that change
  (~0.8% of one core). Playback stops when the window is hidden.

## Known limitations

- The interface is in Turkish only for now.
- **PDF → Word** uses Word's own PDF converter: the text becomes editable, but pages with many images or complex
  layouts may not look exactly like the original PDF. Scanned (image-only) PDFs come through as images, not text.
  If Office previously hung on a PDF, Word asks whether to open it again ("caused a serious error"); Spindle answers
  that safety question with "No" and the conversion is cancelled.
- HEIC and RAW files can be read but not saved in the same format; tools save their results as JPG for these files
  (`DSCF0065 (sıkıştırılmış).jpg`). RAW files are opened with the white balance recorded by the camera; a 26 MP Fujifilm
  RAF converts to JPG in ~9 s and uses ~570 MB of memory while doing so, so at most two RAW files are converted at once.
  PNG output is 8-bit, TIFF output 16-bit.
- By default the drag + key gesture only works in File Explorer and on the desktop (this can be changed in Settings).

## Development

```powershell
dotnet build
dotnet test       # also runs real conversions if Office and ffmpeg are installed
dotnet run --project src\Spindle
```

| Folder | Contents |
|---|---|
| `src/Spindle.Core` | Format catalog and conversion engines (Office, Magick.NET, ffmpeg, Windows PDF, archives) |
| `src/Spindle` | WPF app: wheel, notification card, settings, intro, tray icon, drag detection, sounds |
| `src/Spindle/Tour` | The scene that draws the intro GIFs (with the app's real wheel and card, without opening windows on screen) |
| `tests/Spindle.Tests` | Catalog tests and conversion tests with real files |
| `docs` | README images and intro GIFs (the GIFs are also embedded in the app) |

Built app: `src\Spindle\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Spindle.exe`. Command line:

| Command | What it does |
|---|---|
| `--convert to:pdf rapor.docx` | Converts without UI; results go to `%LOCALAPPDATA%\Spindle\son-donusum.log` |
| `--wheel file` / `--tools file` | Opens the wheel next to the cursor (the right-click menu uses this) |
| `--tour` / `--settings` | Opens the intro or Settings |
| `--uninstall` | Removes Spindle (the Uninstall button in the Apps list calls this) |
| `--render-tour docs` | Regenerates the three intro GIFs |
| `--snapshot out.png convert 0 file` | Draws the wheel to an image (`--dark`, `--no-office` can be added) |
| `--snapshot-ui folder` | Draws the card, Settings and intro window images |
| `--render-icon spindle.ico` | Generates the app icon from the logo |
| `--export-sounds folder` | Writes the click and chime sounds as WAV |
