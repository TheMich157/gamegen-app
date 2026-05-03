@echo off
powershell.exe -NoExit -ExecutionPolicy Bypass -Command "cd '%~dp0'; git add -A; git commit -m 'v1.1.0: progress feedback, launch in Steam, direct App ID, SteamTools auto-invoke, startup update check, system tray, keyboard shortcuts'; git pull --rebase origin main; git tag v1.1.0; git push origin main --tags; Write-Host 'Done.' -ForegroundColor Green"
