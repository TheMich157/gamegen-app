@echo off
powershell.exe -NoExit -ExecutionPolicy Bypass -Command "cd '%~dp0'; git add -A; git commit -m 'v3.1.1: fixes'; git pull --rebase origin main; git tag v3.1.1; git push origin main --tags; Write-Host 'Done.' -ForegroundColor Green"
