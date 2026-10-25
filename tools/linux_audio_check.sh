#!/usr/bin/env sh
# OM-61's check on Linux inside Docker: the game's WMA wave banks are transcoded with ffmpeg and
# AudioSpike plays every cue of Characters.xsb and Sound Bank.xsb (SDL's dummy audio device).
# Usage: tools/linux_audio_check.sh <the game's Content directory>   (needs Docker and lib/fnalibs)
set -eu

root=$(cd "$(dirname "$0")/.." && pwd)
content=$1
case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) mount_root=$(cygpath -w "$root"); mount_content=$(cygpath -w "$content") ;;
    *) mount_root=$root; mount_content=$content ;;
esac

MSYS_NO_PATHCONV=1 docker run --rm -v "$mount_root:/src:ro" -v "$mount_content:/content:ro" mcr.microsoft.com/dotnet/sdk:10.0 sh -euc '
    apt-get update -qq >/dev/null
    apt-get install -y -qq ffmpeg >/dev/null
    mkdir /work
    cd /src && tar --exclude=bin --exclude=obj --exclude=.git -cf - . | tar -C /work -xf -
    cd /work
    dotnet build spikes/AudioSpike -v q -nologo
    cp lib/fnalibs/lib64/* spikes/AudioSpike/bin/Debug/net10.0/
    LD_LIBRARY_PATH=/work/spikes/AudioSpike/bin/Debug/net10.0 SDL_VIDEO_DRIVER=dummy SDL_AUDIO_DRIVER=dummy AUDIOSPIKE_TRANSCODE=1 AUDIOSPIKE_BANKS="Characters.xsb,Sound Bank.xsb" \
        dotnet spikes/AudioSpike/bin/Debug/net10.0/AudioSpike.dll /content/Audio 0.02
'
