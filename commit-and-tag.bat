@echo off
powershell.exe -NoExit -ExecutionPolicy Bypass -Command "cd '%~dp0'; git add -A; git commit -m 'Add in-app update installer'; git tag v1.2.1; git push origin main --tags; Write-Host 'Done.' -ForegroundColor Green"
