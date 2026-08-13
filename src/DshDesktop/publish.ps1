param(
    [switch]$SelfContained,
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = if ($SelfContained) { "$root/dist/self-contained" } else { "$root/dist/framework-dependent" }

dotnet publish "$root/src/DshDesktop/DshDesktop.csproj" -c Release -r $Runtime `
    -p:Version=$Version `
    --self-contained:$($SelfContained.ToString().ToLowerInvariant()) `
    -p:PublishSingleFile=$SelfContained `
    -o $out

Write-Host "发布完成: $out"
