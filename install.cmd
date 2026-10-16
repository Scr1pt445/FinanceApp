@echo off
rem Builds FinanceApp and installs it for the current user; "install.cmd -Uninstall" removes it (data stays).
if /i not "%~1"=="-Uninstall" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish.ps1" || exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install.ps1" %*
