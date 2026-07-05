# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/gbfr.damage.control/*" -Force -Recurse
dotnet publish "./GBFRDamageControl.csproj" -c Release -o "$env:RELOADEDIIMODS/gbfr.damage.control" /p:OutputPath="./bin/Release" /p:ReloadedILLink="true"

# Restore Working Directory
Pop-Location