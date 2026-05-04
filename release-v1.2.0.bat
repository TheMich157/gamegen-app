@echo off
powershell.exe -NoExit -ExecutionPolicy Bypass -Command "cd '%~dp0'; git add -A; git commit -m 'v1.3: version bump'; git pull --rebase origin main; git tag v1.3; git push origin main --tags; Write-Host 'Done.' -ForegroundColor Green"
