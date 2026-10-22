# FFmpeg 9.0.2: binaries and corresponding source

This directory is the build support for the separate FFmpeg command-line programs.
The build support is supplied as part of FFmpeg's corresponding source under
GPL-3.0-or-later. This does not choose a license for the KeepClip application.

The build enables H.264 libx264 (CPU), NVENC, AMF and oneVPL/QSV, native AAC,
gdigrab, ddagrab, CUDA decoding/scaling and the native demuxers/decoders/filters.
GPU encoders require the matching installed vendor GPU and driver; vendor driver
implementations are loaded from the operating system and are not redistributed.
Optional libraries are disabled by `--disable-autodetect`. No nonfree code is enabled.

The supplied source archives are the exact unmodified inputs consumed by this
recipe: FFmpeg, x264, nv-codec-headers, AMD's AMF header snapshot, oneVPL and
the MinGW CRT/winpthreads upstream source plus Debian packaging/patches. See
`sources.lock.json` for versions, official download locations and SHA-256 hashes.
The GPL, dependency copyrights and notices accompany the source archives.
The GCC runtime license and Runtime Library Exception are in `GCC-COPYRIGHT`;
compiler tools are not themselves distributed as part of the application.

AMF headers and their MIT license are vendored under `vendor/` and checked against
the same SHA-256 lock as downloaded inputs. All 57 headers were verified byte for
byte against `amf/public/include/` at upstream commit
`8c648005e07d4309033282bfd9947df2c7e76104`. `Version.h` identifies them as 1.5.2;
the archive's top-level directory has a historical v1.5.3 name. The old v1.5.3
release URL serves a different snapshot and must not replace these tested inputs.
The upstream license is preserved unchanged alongside the archive. No AMD driver
binary is included. Git attributes preserve exact archive and license bytes.

To build on Windows with Docker Desktop in Linux-container mode and Node 20+,
or on an Ubuntu GitHub Actions runner with Docker and Node 20+:

```
node build/ffmpeg/build.mjs --output /absolute/path/to/an/empty/artifact-directory
```

The script uses Docker Desktop from the Windows Program Files directory, or
`/usr/bin/docker`, `/usr/local/bin/docker`, or `/opt/homebrew/bin/docker` on Unix.
It does not search `PATH` or the working directory for executables. For a custom
installation, set `KEEPCLIP_DOCKER` to the absolute path of the trusted Docker
executable. The `docker-cli.mjs` helper must accompany `build.mjs` in source packages.

To rebuild directly from an installed source package, copy the files from
`licenses/ffmpeg-build/build/` into a working directory, copy the installed
`sources/` and `AMF-LICENSE.txt` into an input directory, then run:

```
node build.mjs --sources /absolute/path/to/input-directory --output /absolute/path/to/empty/output-directory
```

`--sources` uses the bundled source archives without downloading them. In a repository
checkout, AMF inputs fall back to `vendor/`; source-package rebuilds must supply
both AMF files in the input directory. Other missing inputs are fetched from the
pinned URLs and every input is checked against its
locked SHA-256. No upstream code patches are applied. FFmpeg configuration and
the exact toolchain package list are included under `build/`. Toolchain packages
and container identity are recorded; the recipe does not promise byte-identical
output across different toolchains. Sources, source checksums, recipe and output
binary checksums travel together in `provenance.json`.

Setup copies `bin/` into `tools/bin/` and `notices/` into `tools/ffmpeg-notices/`.
Publish installs all the notices and source archives under `licenses/ffmpeg-build/`.
The installer includes those files. Release CI builds this same source package
before the Windows application, and uploads a separate source archive alongside
the installer. Preserve both when redistributing; installed users also have a
complete local copy of the source archives and build support.

This replaces unknown older FFmpeg binaries prospectively. It does not supply
corresponding source for earlier public KeepClip installers.
