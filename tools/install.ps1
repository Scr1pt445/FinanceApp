# Installs FinanceApp for the current user: %LocalAppData%\Programs\FinanceApp\FinanceApp.exe and a Start menu
# shortcut. No admin rights. Data (%LocalAppData%\FinanceApp) is never touched, also not by -Uninstall.
param(
    [switch]$Uninstall,
    [string]$Source = (Join-Path $PSScriptRoot '..\artifacts\FinanceApp\FinanceApp.exe')
)
$ErrorActionPreference = 'Stop'
$target = Join-Path $env:LOCALAPPDATA 'Programs\FinanceApp'
$exe = Join-Path $target 'FinanceApp.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'FinanceApp.lnk'

# A running copy holds the file and the single-instance lock: ask it to quit cleanly (--exit) and wait.
$running = Get-Process FinanceApp -ErrorAction SilentlyContinue | Select-Object -First 1
if ($running) {
    'Закрываю запущенный FinanceApp…'
    Start-Process $running.Path -ArgumentList '--exit' -Wait
    if (-not $running.WaitForExit(15000)) {
        Write-Host 'FinanceApp не закрылся. Закройте его (значок в трее → Выход) и запустите установку снова.' -ForegroundColor Yellow
        exit 1
    }
}

if ($Uninstall) {
    Remove-Item $shortcut -ErrorAction SilentlyContinue
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $value = (Get-ItemProperty $run -ErrorAction SilentlyContinue).FinanceApp
    if ($value -and $value -like "*$exe*") { Remove-ItemProperty $run -Name FinanceApp }
    'FinanceApp удалён. Данные остались в {0}' -f (Join-Path $env:LOCALAPPDATA 'FinanceApp')
    exit 0
}

if (-not (Test-Path $Source)) { throw "Нет $Source — сначала соберите приложение: publish.cmd" }

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item $Source $exe -Force

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.Arguments = '--main'
$link.WorkingDirectory = $target
$link.Description = 'Учёт личных финансов'
$link.Save()

'Установлено: {0}' -f $exe
'Ярлык в меню «Пуск»: FinanceApp'
Start-Process $exe -ArgumentList '--main'
