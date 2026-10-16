# Builds FinanceApp.exe: one self-contained file for Windows x64, no .NET installation needed to run it.
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\FinanceApp')
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\FinanceApp.Desktop'
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $Output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:PublishDocumentationFile=false `
    -p:PublishReferencesDocumentationFiles=false
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась.' }

$exe = Join-Path (Resolve-Path $Output) 'FinanceApp.exe'
'Готово: {0} ({1:N0} МБ)' -f $exe, ((Get-Item $exe).Length / 1MB)
