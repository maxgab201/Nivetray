param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$portableRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/portable'))
$output = [IO.Path]::GetFullPath((Join-Path $portableRoot $Runtime))
if (-not $output.StartsWith($portableRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package directory.' }
# Only remove this validated, generated package folder, never preferences or other artifacts.
if (Test-Path -LiteralPath $output) {
    if ((Get-Item -LiteralPath $output).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to delete a linked package folder.' }
    $personalData = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Name -like 'settings.json*' -or $_.Name -like 'diagnostics.log*' }
    if ($personalData) { throw 'Package output contains a local profile. It was preserved; move that profile before rebuilding.' }
    Remove-Item -LiteralPath $output -Recurse -Force
}
dotnet publish (Join-Path $repo 'src/Nivetray.App') -c Release -r $Runtime --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)." }
foreach ($document in 'LICENSE', 'README.md', 'CONTRIBUTING.md', 'SECURITY.md', 'CHANGELOG.md') { Copy-Item (Join-Path $repo $document) $output }
$null = New-Item -ItemType Directory -Force -Path (Join-Path $output 'docs')
Copy-Item (Join-Path $repo 'docs/*') (Join-Path $output 'docs') -Recurse -Force
$zip = Join-Path $repo "artifacts/Nivetray-$Runtime.zip"
Compress-Archive -Path "$output/*" -DestinationPath $zip -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  Nivetray-$Runtime.zip" | Set-Content "$zip.sha256" -Encoding ascii
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($required in 'Nivetray.exe', 'Nivetray.runtimeconfig.json', 'LICENSE', 'README.md', 'docs/safety.md') {
        if (-not ($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $required })) { throw "Missing package file: $required" }
    }
} finally { $archive.Dispose() }
if ((Get-Content "$zip.sha256").Split(' ')[0] -ne (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Checksum mismatch.' }
Write-Output "PASS: $Runtime portable package, required files and SHA-256 verified: $zip"
