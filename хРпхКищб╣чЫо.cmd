@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-project.ps1"
if errorlevel 1 pause

