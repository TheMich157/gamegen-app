@echo off
powershell.exe -NoExit -ExecutionPolicy Bypass -Command "cd '%~dp0'; git add -A; git commit -m 'v1.4.5: fixes'; git pull --rebase origin main; git tag v1.4.5; git push origin main --tags; Write-Host 'Done.' -ForegroundColor Green"
