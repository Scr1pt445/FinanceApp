# Builds a debug APK of the Android app (ADR 0013) into artifacts\android\FinanceApp-debug.apk.
# Needs the .NET MAUI Android workload (dotnet workload install maui-android), an Android SDK and JDK 17+;
# see README, section «Android». The APK is signed with the local debug key: install it on your own phone.
param(
    # Android SDK folder; by default the one .NET finds (ANDROID_HOME, the usual install places).
    [string]$AndroidSdk = $env:ANDROID_HOME,
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\android')
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\FinanceApp.Mobile\FinanceApp.Mobile.csproj'

$arguments = @(
    'build', $project,
    '--configuration', 'Debug',
    # Debug builds normally leave the assemblies out of the APK and push them from the IDE (fast deployment);
    # an APK copied to a phone by hand must carry them.
    '-p:EmbedAssembliesIntoApk=true'
)
if ($AndroidSdk) { $arguments += "-p:AndroidSdkDirectory=$AndroidSdk" }
if ($env:JAVA_HOME) { $arguments += "-p:JavaSdkDirectory=$($env:JAVA_HOME)" }

dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась.' }

$apk = Get-ChildItem (Join-Path $PSScriptRoot '..\src\FinanceApp.Mobile\bin\Debug\net10.0-android') -Filter '*-Signed.apk' | Select-Object -First 1
if (-not $apk) { throw 'APK не найден.' }
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$target = Join-Path (Resolve-Path $Output) 'FinanceApp-debug.apk'
Copy-Item $apk.FullName $target -Force
'Готово: {0} ({1:N0} МБ)' -f $target, ($apk.Length / 1MB)
