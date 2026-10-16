@echo off
rem Builds artifacts\FinanceApp\FinanceApp.exe (one file, no .NET needed to run it).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish.ps1" %*
