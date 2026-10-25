#Requires -Version 7.2
# Builds the beta for testers (OM-53): the launcher published self-contained (no .NET to install) for each runtime
# given, with its native libraries and the testers' guide (docs/TESTING.md), as artifacts/OpenMagicka-<version>-
# <runtime>.zip for Windows or .tar.gz elsewhere (a tar keeps the launcher's executable bit, which a zip made on
# Windows loses). The game's own files are never in it: the player's Magicka is found at run time. Needs
# tools/fetch_fnalibs.sh to have been run.
#
# The version is Directory.Build.props', or the one given (the release workflow gives its tag's).
#
#   pwsh tools/package_beta.ps1 [-Runtimes win-x64,linux-x64] [-Version 0.0.1]
param(
    [string[]]$Runtimes = @('win-x64', 'linux-x64'),
    [string]$Version = ''
)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src/OpenMagicka.Launcher/OpenMagicka.Launcher.csproj'
$artifacts = Join-Path $root 'artifacts'
$version = if ($Version) { $Version } else { (Select-Xml -Path (Join-Path $root 'Directory.Build.props') -XPath '//Version').Node.InnerText }

# A .tar.gz of a folder (under its own name), the launcher and the native libraries executable, the rest not.
function Write-TarGz([string]$folder, [string]$package) {
    $stream = [IO.File]::Create($package)
    $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionLevel]::Optimal)
    $tar = [Formats.Tar.TarWriter]::new($gzip, [Formats.Tar.TarEntryFormat]::Pax)
    try {
        $base = Split-Path $folder
        foreach ($file in Get-ChildItem $folder -Recurse -File) {
            $name = [IO.Path]::GetRelativePath($base, $file.FullName).Replace('\', '/')
            $entry = [Formats.Tar.PaxTarEntry]::new([Formats.Tar.TarEntryType]::RegularFile, $name)
            $executable = $file.Name -eq 'OpenMagicka' -or $file.Name -match '\.so(\.\d+)*$'
            $entry.Mode = if ($executable) { [IO.UnixFileMode]493 } else { [IO.UnixFileMode]420 } # 755, 644
            $entry.ModificationTime = $file.LastWriteTimeUtc
            $entry.DataStream = [IO.File]::OpenRead($file.FullName)
            try {
                $tar.WriteEntry($entry)
            }
            finally {
                $entry.DataStream.Dispose()
            }
        }
    }
    finally {
        $tar.Dispose()
        $gzip.Dispose()
        $stream.Dispose()
    }
}

foreach ($runtime in $Runtimes) {
    $out = Join-Path $artifacts "beta/$runtime/OpenMagicka"
    if (Test-Path $out) {
        Remove-Item -Recurse -Force $out
    }
    dotnet publish $project -c Release -r $runtime --self-contained true -p:DebugType=embedded -p:Version=$version -o $out
    if ($LASTEXITCODE -ne 0) {
        throw "the publish for $runtime failed"
    }
    Copy-Item (Join-Path $root 'docs/TESTING.md') (Join-Path $out 'TESTING.md')
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $out 'LICENSE.txt')
    $name = "OpenMagicka-$version-$runtime"
    if ($runtime.StartsWith('win')) {
        $package = Join-Path $artifacts "$name.zip"
        Remove-Item -Force $package -ErrorAction SilentlyContinue
        Compress-Archive -Path $out -DestinationPath $package
    }
    else {
        $package = Join-Path $artifacts "$name.tar.gz"
        Write-TarGz $out $package
    }
    Write-Host "packaged $package"
}
