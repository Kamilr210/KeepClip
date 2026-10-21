FROM debian:bookworm-slim@sha256:3783cc01769c7b2b1b83a5c5ad96c815348e28ed7da68e2e3687004faa906251
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates build-essential gcc-mingw-w64-x86-64-posix g++-mingw-w64-x86-64-posix \
    cmake ninja-build pkg-config nasm clang python3 xz-utils curl dpkg-dev \
    && rm -rf /var/lib/apt/lists/*
COPY build.sh /recipe/build.sh
COPY sources /sources
RUN bash /recipe/build.sh
