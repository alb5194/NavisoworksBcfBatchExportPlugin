<#
.SYNOPSIS
  Downloads compile-time references for the Navisworks API, so the plugin can be built
  on a machine without Navisworks (for example a GitHub-hosted runner).

.DESCRIPTION
  The Autodesk DLLs come from the community NuGet packages Chuongmep.Navis.Api.*.
  They are used only to compile against (the project references them with Private=false),
  and are never copied into the plugin bundle: at runtime Navisworks loads its own copies.

  No 2024 package exists for Autodesk.Navisworks.Interop.ComApi, so the 2023 one is used and its
  assembly version is rewritten to the version that Autodesk.Navisworks.ComApi <Year> references
  (21.0.0.0 for 2024). The COM interfaces the plugin uses are the same in both versions, and the
  compiled plugin then references the same Interop.ComApi version that Navisworks <Year> ships.
#>
param(
    [Parameter(Mandatory)] [string] $OutDir,
    [string] $NavisworksVersion = '2024',
    [string] $InteropPackageVersion = '2023.0.5'
)

$ErrorActionPreference = 'Stop'
$feed = 'https://api.nuget.org/v3-flatcontainer'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$work = Join-Path ([IO.Path]::GetTempPath()) ("navis-refs-" + [guid]::NewGuid())
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Get-PackageDll([string] $id, [string] $version, [string] $dll) {
    $lower = $id.ToLowerInvariant()
    $nupkg = Join-Path $work "$lower.$version.zip"
    Invoke-WebRequest -Uri "$feed/$lower/$version/$lower.$version.nupkg" -OutFile $nupkg
    $dir = Join-Path $work "$lower.$version"
    Expand-Archive -Path $nupkg -DestinationPath $dir -Force
    $file = Get-ChildItem -Path $dir -Recurse -Filter $dll | Select-Object -First 1
    if (-not $file) { throw "$dll not found in $id $version" }
    Copy-Item $file.FullName (Join-Path $OutDir $dll) -Force
    Write-Host "  $dll  <-  $id $version"
}

function Get-AssemblyRefVersion([string] $path, [string] $name) {
    $stream = [IO.File]::OpenRead($path)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($h in $md.AssemblyReferences) {
            $r = $md.GetAssemblyReference($h)
            if ($md.GetString($r.Name) -eq $name) { return $r.Version }
        }
        throw "$path does not reference $name"
    } finally { $stream.Dispose() }
}

function Set-AssemblyVersion([string] $path, [version] $version) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $pe = [System.Reflection.PortableExecutable.PEReader]::new([IO.MemoryStream]::new($bytes))
    $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $table = [System.Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetTableMetadataOffset(
        $md, [System.Reflection.Metadata.Ecma335.TableIndex]::Assembly)
    # Assembly row: HashAlgId (4 bytes), then Major, Minor, Build, Revision (2 bytes each)
    $offset = $pe.PEHeaders.MetadataStartOffset + $table + 4
    $parts = @($version.Major, $version.Minor, [Math]::Max(0, $version.Build), [Math]::Max(0, $version.Revision))
    for ($i = 0; $i -lt 4; $i++) {
        [BitConverter]::GetBytes([uint16]$parts[$i]).CopyTo($bytes, $offset + 2 * $i)
    }
    $pe.Dispose()
    [IO.File]::WriteAllBytes($path, $bytes)
}

Write-Host "Navisworks $NavisworksVersion compile-time references -> $OutDir"
Get-PackageDll 'Chuongmep.Navis.Api.Autodesk.Navisworks.Api' "$NavisworksVersion.0.0" 'Autodesk.Navisworks.Api.dll'
Get-PackageDll 'Chuongmep.Navis.Api.Autodesk.Navisworks.Clash' "$NavisworksVersion.0.0" 'Autodesk.Navisworks.Clash.dll'
Get-PackageDll 'Chuongmep.Navis.Api.Autodesk.Navisworks.ComApi' "$NavisworksVersion.0.0" 'Autodesk.Navisworks.ComApi.dll'
Get-PackageDll 'Chuongmep.Navis.Api.Autodesk.Navisworks.Interop.ComApi' $InteropPackageVersion 'Autodesk.Navisworks.Interop.ComApi.dll'

$interop = Join-Path $OutDir 'Autodesk.Navisworks.Interop.ComApi.dll'
$wanted = Get-AssemblyRefVersion (Join-Path $OutDir 'Autodesk.Navisworks.ComApi.dll') 'Autodesk.Navisworks.Interop.ComApi'
Set-AssemblyVersion $interop $wanted
Write-Host "  Interop.ComApi assembly version set to $wanted (compile-time reference only)"

Remove-Item -Recurse -Force $work
