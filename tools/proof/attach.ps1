# Run elevated on Windows. Starts the iSCSI initiator service and connects to the proof target.
param(
  [string]$Portal = '127.0.0.1',
  [int]$Port = 3260,
  [string]$Target = 'iqn.2026-11.test.wobble:proof',
  [string]$Log = (Join-Path $env:TEMP 'wobble-attach.log')
)
$ErrorActionPreference = 'Continue'
& {
  Start-Service MSiSCSI
  "service: $((Get-Service MSiSCSI).Status)"
  New-IscsiTargetPortal -TargetPortalAddress $Portal -TargetPortalPortNumber $Port | Format-List TargetPortalAddress,TargetPortalPortNumber
  Get-IscsiTarget | Format-List NodeAddress,IsConnected
  Connect-IscsiTarget -NodeAddress $Target -IsPersistent $false | Format-List TargetNodeAddress,IsConnected,SessionIdentifier
} *>&1 | Out-File -Encoding utf8 $Log
