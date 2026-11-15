# Run elevated on Windows, once per boot. Connects the Windows iSCSI initiator to a running Wobble.
# The session then survives emulator restarts: the initiator reconnects by itself when the same target
# name comes back on the same port, so tests need no elevation.
param(
  [string]$Portal = '127.0.0.1',
  [int]$Port = 3260,
  [string]$Target = 'iqn.2026-11.test.wobble:drive0',
  [string]$Log = (Join-Path $env:TEMP 'wobble-attach.log')
)
$ErrorActionPreference = 'Continue'
& {
  Start-Service MSiSCSI
  "service: $((Get-Service MSiSCSI).Status)"
  # Drop sessions to other Wobble targets on this portal (for example the proof target), which would retry forever.
  Get-IscsiSession | Where-Object { $_.TargetNodeAddress -like 'iqn.2026-11.test.wobble:*' -and $_.TargetNodeAddress -ne $Target } |
    ForEach-Object { "dropping $($_.TargetNodeAddress)"; Disconnect-IscsiTarget -NodeAddress $_.TargetNodeAddress -Confirm:$false }
  if (-not (Get-IscsiTargetPortal | Where-Object { $_.TargetPortalAddress -eq $Portal -and $_.TargetPortalPortNumber -eq $Port })) {
    New-IscsiTargetPortal -TargetPortalAddress $Portal -TargetPortalPortNumber $Port | Out-Null
  }
  Update-IscsiTarget
  Get-IscsiTarget | Format-List NodeAddress,IsConnected
  if (-not (Get-IscsiTarget -NodeAddress $Target).IsConnected) {
    Connect-IscsiTarget -NodeAddress $Target -IsPersistent $false | Format-List TargetNodeAddress,IsConnected
  }
  Get-IscsiSession | Format-List TargetNodeAddress,IsConnected
} *>&1 | Out-File -Encoding utf8 $Log
