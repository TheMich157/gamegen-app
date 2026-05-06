@echo off
powershell.exe -NoExit -ExecutionPolicy Bypass -Command "cd '%~dp0'; git add -A; git commit -m 'v1.4.0: bump version'; git tag v1.4.0; git push origin main --tags; Write-Host 'Done.' -ForegroundColor Green"
