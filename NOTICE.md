# Attribution and distribution

Quiet Home is a native Tizen .NET/NUI implementation. Its layout measurements,
palette, dock proportions and focus treatment adapt
[Free My TV](https://github.com/simbasjungles/free-my-tv/tree/73409804e5085e720dccea9ab95078a515cae098),
commit `73409804e5085e720dccea9ab95078a515cae098`, copyright 2026 FreeMyTV
contributors, under the MIT license in `licenses/FreeMyTV-MIT.txt`.
The Android APK is not used.

The bundled Quiet Inter fonts are static derivatives of Inter, copyright
2016 The Inter Project Authors, renamed after conversion. Medium: weight 500,
optical size 14; Semibold: weight 600, optical size 14; Display: weight 600,
optical size 32. See `licenses/Inter-OFL.txt`. These font files retain the
SIL Open Font License; the repository's MIT license does not replace it.

The Material Symbols PNGs come through Free My TV and retain Apache 2.0:
`licenses/Material-Symbols-Apache-2.txt`. Console illustrations, gradient
cards and text-only app placeholders were produced for this launcher.

The source subset in `vendor/samsung_tv_root/` and `vendor/payload-source/`
comes from [samsung-tv-root](https://github.com/chris-ritsen/samsung-tv-root/tree/4ad2c4d3b0d0e0da4b5b7ebe4a787e94596f416b),
commit `4ad2c4d3b0d0e0da4b5b7ebe4a787e94596f416b`, under the
Unlicense in `licenses/samsung-tv-root-Unlicense.txt`. The local runtime
fallback is described in [provenance](docs/provenance.md).

No Samsung SDB executable, firmware, SDK/reference assembly, stock app
binary, account database or private backup is distributed.

Publisher streaming-app artwork is not bundled. `launcher/artwork-sources.json`
records optional sources and transformations; `tools/fetch_artwork.py` downloads
selected artwork into ignored local files. Those assets and associated
trademarks remain subject to their owners' terms and are not relicensed by
this project. Source availability does not imply permission for every reuse.
The repository and its contributors are not affiliated with Samsung, Sony,
Nintendo, Microsoft, Amazon or any streaming service.

## PS2-style theme (this fork)

The PS2-style interface imitates the look of Sony's PlayStation 2 system menu.
No Sony code or assets are distributed. The PS2 font, menu sounds, boot and
menu videos and ambience are generated locally from the user's own BIOS dump
(`tools/ps2_assets.py`) or recorded by the user, and are excluded by
`.gitignore`. `launcher/src/Ps2FontMetrics.cs` holds only glyph widths and the
atlas layout produced by that tool. The console, PC, Apps and Settings icons,
the selection bar and the remote-button glyphs in `launcher/assets/ps2/` were
drawn for this project. PlayStation is a trademark of Sony Interactive
Entertainment; this project is not affiliated with or endorsed by Sony.
