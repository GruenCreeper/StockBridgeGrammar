[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'grasshopper-plugin/StockBridgeGrammar'
$output = Join-Path $root 'release'
[xml]$projectXml = Get-Content -LiteralPath (Join-Path $project 'StockBridgeGrammar.csproj') -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$') { throw 'Invalid project version.' }
$binary = Join-Path $project 'bin/Release/StockBridgeGrammar.gha'
if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
    throw 'Build the Release configuration before packaging.'
}
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($binary).Version
if ($assemblyVersion.ToString(3) -ne ($version -split '[-+]')[0]) {
    throw 'The built plugin version does not match the project version.'
}
$newerSources = @(Get-ChildItem -LiteralPath $project -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
        $_.Extension -in @('.cs', '.csproj') -and $_.LastWriteTimeUtc -gt (Get-Item -LiteralPath $binary).LastWriteTimeUtc })
if ($newerSources.Count -gt 0) { throw 'Source files changed after the Release build. Rebuild before packaging.' }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $output -Force | Out-Null

function Add-PublicTree {
    param([Collections.Generic.List[object]]$Entries, [string]$Directory, [string]$Prefix, [string[]]$Extensions)
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File) {
        $relative = $file.FullName.Substring($Directory.Length + 1).Replace('\', '/')
        if ($relative -match '(^|/)(bin|obj|TestResults|__pycache__)(/|$)|(^|/)\.') { continue }
        if ($file.Extension -notin $Extensions) { continue }
        $Entries.Add([pscustomobject]@{ Path = $file.FullName; Name = "$Prefix/$relative" })
    }
}

function Write-Archive {
    param([string]$Name, [Collections.Generic.List[object]]$Entries)
    $destination = Join-Path $output $Name
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination }
    $archive = [IO.Compression.ZipFile]::Open($destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $Entries | Sort-Object Name) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $entry.Path, $entry.Name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    Write-Output $destination
}

$source = [Collections.Generic.List[object]]::new()
foreach ($name in @('README.md', '.gitignore', '.gitattributes', 'LICENSE', 'NOTICE', 'CITATION.cff')) {
    $path = Join-Path $root $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $source.Add([pscustomobject]@{ Path = $path; Name = "StockBridgeGrammar/$name" })
    }
}
Add-PublicTree $source $project 'StockBridgeGrammar/grasshopper-plugin/StockBridgeGrammar' @('.cs', '.csproj', '.md', '.json', '.py')
Add-PublicTree $source (Join-Path $root 'grasshopper-plugin/tests') 'StockBridgeGrammar/grasshopper-plugin/tests' @('.cs', '.csproj', '.md', '.xml')
Add-PublicTree $source $PSScriptRoot 'StockBridgeGrammar/scripts' @('.ps1')

$install = [Collections.Generic.List[object]]::new()
$install.Add([pscustomobject]@{ Path = $binary; Name = 'StockBridgeGrammar/StockBridgeGrammar.gha' })
foreach ($name in @('README.md', 'README_UserGuide_EN.md', '使用说明_中文.md')) {
    $install.Add([pscustomobject]@{ Path = (Join-Path $project $name); Name = "StockBridgeGrammar/$name" })
}
Add-PublicTree $install (Join-Path $project 'examples') 'StockBridgeGrammar/examples' @('.md', '.json', '.py')
foreach ($name in @('LICENSE', 'NOTICE', 'CITATION.cff')) {
    $path = Join-Path $root $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $install.Add([pscustomobject]@{ Path = $path; Name = "StockBridgeGrammar/$name" })
    }
}

$archives = @(
    Write-Archive "StockBridgeGrammar-$version-source.zip" $source
    Write-Archive "StockBridgeGrammar-$version-plugin.zip" $install
)
$checksums = foreach ($path in $archives) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($path)
}
$checksumPath = Join-Path $output "StockBridgeGrammar-$version-SHA256SUMS.txt"
[IO.File]::WriteAllLines($checksumPath, $checksums, [Text.UTF8Encoding]::new($false))
$archives
$checksumPath
