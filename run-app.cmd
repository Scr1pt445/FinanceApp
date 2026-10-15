@echo off
rem Builds and starts FinanceApp with the main window open.
dotnet run --project "%~dp0src\FinanceApp.Desktop" -c Release -- --main
