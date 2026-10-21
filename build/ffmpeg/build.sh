#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
set -euo pipefail
trap 'test ! -f /work/ffmpeg/ffbuild/config.log || tail -100 /work/ffmpeg/ffbuild/config.log' ERR
export LC_ALL=C SOURCE_DATE_EPOCH=1789776000
export CC=x86_64-w64-mingw32-gcc CXX=x86_64-w64-mingw32-g++
export PKG_CONFIG_LIBDIR=/prefix/lib/pkgconfig
export CFLAGS='-O2' CXXFLAGS='-O2' LDFLAGS='-static -static-libgcc -static-libstdc++'
mkdir -p /work /prefix /out/bin /out/notices/sources /out/notices/build
for component in ffmpeg x264 nv-codec-headers amf libvpl; do
    mkdir -p "/work/$component"
    archive=$(find /sources -maxdepth 1 -name "$component*.tar.*" | head -1)
    tar -xf "$archive" -C "/work/$component" --strip-components=1
    cp "$archive" /out/notices/sources/
done
cp /recipe/build.sh /out/notices/build/
dpkg-query -W -f='${Package}\t${Version}\n' > /out/notices/build/toolchain-packages.tsv
cd /work/nv-codec-headers
make PREFIX=/prefix install
mkdir -p /prefix/include/AMF
cp -r /work/amf/AMF/* /prefix/include/AMF/
cp /sources/AMF-LICENSE.txt /out/notices/
cd /work/x264
./configure --host=x86_64-w64-mingw32 --cross-prefix=x86_64-w64-mingw32- --prefix=/prefix \
    --enable-static --disable-cli --disable-opencl --extra-ldflags='-static'
make -j4
make install
cp config.log /out/notices/build/x264-config.log
cmake -S /work/libvpl -B /work/libvpl-build -G Ninja \
    -DCMAKE_SYSTEM_NAME=Windows -DCMAKE_C_COMPILER="$CC" -DCMAKE_CXX_COMPILER="$CXX" \
    -DCMAKE_INSTALL_PREFIX=/prefix -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTS=OFF \
    -DBUILD_EXAMPLES=OFF -DINSTALL_EXAMPLES=OFF -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_CXX_FLAGS='-O2 -static -static-libgcc -static-libstdc++'
cmake --build /work/libvpl-build -j4
cmake --install /work/libvpl-build
cd /work/ffmpeg
./configure --prefix=/prefix --target-os=mingw32 --arch=x86_64 \
    --cross-prefix=x86_64-w64-mingw32- --enable-cross-compile \
    --disable-autodetect --enable-gpl --enable-version3 --disable-nonfree \
    --disable-doc --disable-ffplay --disable-debug --disable-shared --enable-static \
    --enable-libx264 --enable-libvpl --enable-amf --enable-ffnvcodec --enable-nvenc --enable-nvdec \
    --enable-cuda-llvm --enable-d3d11va --enable-dxva2 --enable-schannel \
    --pkg-config=pkg-config --pkg-config-flags=--static --extra-cflags=-I/prefix/include \
    --extra-ldflags='-L/prefix/lib -static -static-libgcc -static-libstdc++' --extra-libs='-lstdc++ -lole32 -lcfgmgr32' \
    --extra-version=KeepClip-source-build-1
make -j4
cp ffmpeg.exe ffprobe.exe /out/bin/
cp COPYING* LICENSE.md /out/notices/
cp ffbuild/config.log config.h config_components.h /out/notices/build/
x86_64-w64-mingw32-objdump -p /out/bin/ffmpeg.exe | sed -n '/DLL Name:/p' > /out/notices/build/ffmpeg-imports.txt
x86_64-w64-mingw32-objdump -p /out/bin/ffprobe.exe | sed -n '/DLL Name:/p' > /out/notices/build/ffprobe-imports.txt
cp /work/x264/COPYING /out/notices/X264-COPYING
cp /work/nv-codec-headers/README /out/notices/NV-CODEC-README
python3 - <<'PY'
from pathlib import Path
with open('/out/notices/NV-CODEC-LICENSES.txt', 'w') as out:
    for header in sorted(Path('/work/nv-codec-headers/include/ffnvcodec').glob('*.h')):
        out.write(header.name + '\n' + header.read_text().split('*/',1)[0] + '*/\n\n')
PY
cp /work/libvpl/LICENSE /out/notices/LIBVPL-LICENSE
cp /work/libvpl/third-party-programs.txt /out/notices/LIBVPL-THIRD-PARTY-PROGRAMS.txt
cp /usr/share/doc/mingw-w64-common/copyright /out/notices/MINGW-COPYRIGHT
cp /usr/share/doc/gcc-12-base/copyright /out/notices/GCC-COPYRIGHT
cp /sources/mingw-w64*.tar.* /out/notices/sources/
