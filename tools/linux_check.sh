#!/usr/bin/env sh
# Build, test and smoke-run OpenMagicka on Linux inside Docker (no GPU needed):
# the window runs under Xvfb with Mesa software rendering.
# Usage: tools/linux_check.sh   (from any directory; needs Docker and lib/fnalibs)
set -eu

root=$(cd "$(dirname "$0")/.." && pwd)
case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) mount_root=$(cygpath -w "$root") ;;
    *) mount_root=$root ;;
esac

MSYS_NO_PATHCONV=1 docker run --rm -v "$mount_root:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 sh -euc '
    apt-get update -qq >/dev/null
    apt-get install -y -qq xvfb xauth libgl1 libgl1-mesa-dri libegl1 >/dev/null
    mkdir /work
    cd /src && tar --exclude=bin --exclude=obj --exclude=.git -cf - . | tar -C /work -xf -
    cd /work
    dotnet build OpenMagicka.slnx -v q -nologo
    dotnet test tests/OpenMagicka.Tests --no-build -v q -nologo
    cd src/OpenMagicka.Launcher/bin/Debug/net10.0
    ls libFNA3D.so.0 libSDL3.so.0 libFAudio.so.0 >/dev/null
    FNA3D_FORCE_DRIVER=OpenGL SDL_VIDEO_DRIVER=x11 timeout 8 xvfb-run -a ./OpenMagicka > run.log 2>&1 || code=$?
    cat run.log
    # timeout exits 124 when the game was still running after 8 s: that is the success case
    if [ "${code:-0}" = 124 ]; then echo "SMOKE: window ran for 8 s"; else echo "SMOKE: FAILED (exit ${code:-0})"; exit 1; fi
'
