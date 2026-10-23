#Requires -Version 7.2
# Builds OpenMagicka's own FNA3D for Windows x64 (native/README.md): the FNA3D that FNA pins
# (lib/FNA/lib/FNA3D, with its MojoShader) plus the patches in native/patches, into
# native/win-x64/FNA3D.dll, with the C runtime linked in (no Visual C++ redistributable needed).
# Needs Visual Studio or its Build Tools with the C++ workload and its CMake component. SDL3's
# development files are downloaded from libsdl-org's GitHub release (and checked against a known
# hash) unless -SdlDevel points at an unpacked SDL3-devel-<version>-VC folder.
#
#   pwsh tools/build_fna3d.ps1 [-SdlDevel <folder>]
param(
    [string]$SdlDevel = ''
)
$ErrorActionPreference = 'Stop'

$SdlVersion = '3.4.18'
$SdlSha256 = '78d84602ae616cfe26b33a73b7d3b9b6b56e8a6650e53d92c9c6ca938f747acd'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $root 'lib/FNA/lib/FNA3D'
$patches = Get-ChildItem (Join-Path $root 'native/patches/MojoShader') -Filter *.patch | Sort-Object Name
$output = Join-Path $root 'native/win-x64'

function Invoke-Native([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}

if (-not (Test-Path (Join-Path $source 'MojoShader/mojoshader.c'))) {
    throw 'FNA3D or MojoShader sources are missing: run git submodule update --init --recursive'
}
$dirty = git -C $source status --porcelain --ignore-submodules=none
if ($LASTEXITCODE -ne 0) { throw "git status failed in $source" }
if ($dirty) { throw "$source has local changes; the build must match its recorded commits" }

# The MSVC environment, taken from vcvars64.bat; CMake and Ninja come with Visual Studio.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    Microsoft.VisualStudio.Component.VC.CMake.Project -property installationPath
if (-not $vs) { throw 'Visual Studio with the C++ x64 tools and CMake was not found' }
$env:PATH = "$(Split-Path $vswhere);$env:PATH"   # vcvars64.bat looks vswhere up itself
$environment = cmd /c "`"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul && set"
if ($LASTEXITCODE -ne 0) { throw 'vcvars64.bat failed' }
$environment | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') { Set-Item "env:$($Matches[1])" $Matches[2] }
}
$env:PATH = "$vs\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin;$vs\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja;$env:PATH"
Get-Command cl, cmake, ninja | Out-Null

$work = Join-Path ([IO.Path]::GetTempPath()) "openmagicka-fna3d-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force $work | Out-Null
try {
    if (-not $SdlDevel) {
        $zip = Join-Path $work "SDL3-devel-$SdlVersion-VC.zip"
        Invoke-WebRequest "https://github.com/libsdl-org/SDL/releases/download/release-$SdlVersion/SDL3-devel-$SdlVersion-VC.zip" -OutFile $zip
        if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $SdlSha256) { throw "SDL3-devel-$SdlVersion-VC.zip does not have the expected hash" }
        Expand-Archive $zip -DestinationPath $work
        $SdlDevel = Join-Path $work "SDL3-$SdlVersion"
    }
    $sdlHeader = Join-Path $SdlDevel 'include/SDL3/SDL_version.h'
    $sdlLibrary = Join-Path $SdlDevel 'lib/x64/SDL3.lib'
    if (-not ((Test-Path $sdlHeader) -and (Test-Path $sdlLibrary))) { throw "$SdlDevel is not an SDL3 development folder for Visual C++" }
    $sdlParts = 'MAJOR', 'MINOR', 'MICRO' | ForEach-Object {
        (Select-String -Path $sdlHeader -Pattern "#define SDL_$($_)_VERSION\s+(\d+)").Matches[0].Groups[1].Value
    }

    # A copy of the sources outside any git work tree, so the patches apply to it alone.
    $src = Join-Path $work 'src'
    robocopy $source $src /E /XD .git /XF .git /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copying $source failed" }
    foreach ($patch in $patches) {
        Invoke-Native "applying $($patch.Name)" { git -C (Join-Path $src 'MojoShader') apply --ignore-whitespace $patch.FullName }
    }
    foreach ($patch in $patches) {
        # Applied for real: every file a patch changes carries the fork's marker afterwards.
        foreach ($target in Select-String -Path $patch.FullName -Pattern '^\+\+\+ b/(.+)$') {
            $file = Join-Path $src "MojoShader/$($target.Matches[0].Groups[1].Value.Trim())"
            if (-not (Select-String -Path $file -Pattern 'OpenMagicka fork' -Quiet)) { throw "$($patch.Name) did not reach $file" }
        }
    }

    $build = Join-Path $work 'build'
    Invoke-Native 'cmake configure' {
        cmake -S $src -B $build -G Ninja -DCMAKE_BUILD_TYPE=Release `
            -DCMAKE_POLICY_DEFAULT_CMP0091=NEW -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded `
            "-DSDL3_INCLUDE_DIRS=$SdlDevel/include" "-DSDL3_LIBRARIES=$sdlLibrary"
    }
    Invoke-Native 'cmake build' { cmake --build $build }
    $dependents = dumpbin /nologo /dependents (Join-Path $build 'FNA3D.dll') | Out-String
    if ($dependents -match '(?i)vcruntime|msvcp|api-ms-win-crt') { throw "FNA3D.dll still needs the Visual C++ runtime:`n$dependents" }

    New-Item -ItemType Directory -Force $output | Out-Null
    Copy-Item (Join-Path $build 'FNA3D.dll') $output -Force

    $fna3d = git -C $source rev-parse HEAD
    $mojo = git -C (Join-Path $source 'MojoShader') rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'git rev-parse failed' }
    $env:VSLANG = '1033'   # cl's banner in English
    $compiler = cl 2>&1 | ForEach-Object ToString | Where-Object { $_ -match 'Version \d+\.\d+\.\d+' } | Select-Object -First 1
    if (-not $compiler) { throw 'could not read the version of cl' }
    $lines = @(
        'OpenMagicka build of FNA3D (altered source: see native/README.md).',
        "FNA3D $fna3d",
        "MojoShader $mojo",
        "SDL3 headers $($sdlParts -join '.')",
        "Compiler: $compiler, static C runtime",
        'Patches:'
    ) + ($patches | ForEach-Object {
        "  MojoShader/$($_.Name) sha256 $((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())"
    }) + "FNA3D.dll sha256 $((Get-FileHash (Join-Path $output 'FNA3D.dll') -Algorithm SHA256).Hash.ToLowerInvariant())"
    Set-Content -Path (Join-Path $output 'SOURCE.txt') -Value $lines -Encoding utf8NoBOM
    Write-Host "FNA3D: built $(Join-Path $output 'FNA3D.dll')"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
