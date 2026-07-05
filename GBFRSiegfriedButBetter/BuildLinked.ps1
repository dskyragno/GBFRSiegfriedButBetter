# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/gbfr.siegfried.but.better/*" -Force -Recurse
dotnet publish "./GBFRSiegfriedButBetter.csproj" -c Release -o "$env:RELOADEDIIMODS/gbfr.siegfried.but.better" /p:OutputPath="./bin/Release" /p:ReloadedILLink="true"

# Restore Working Directory
Pop-Location
