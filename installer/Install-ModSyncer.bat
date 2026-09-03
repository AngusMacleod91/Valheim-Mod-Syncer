@echo off
REM Double-click this file to install Valheim Mod Syncer.
REM It runs the PowerShell script next to it. "-ExecutionPolicy Bypass" is needed because
REM Windows blocks downloaded scripts by default; it applies to this one run only.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-ModSyncer.ps1" %*
