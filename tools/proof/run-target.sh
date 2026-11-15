#!/usr/bin/env bash
# Runs a patched tgt (no root, no kernel modules) exposing an ISO as a SCSI type 5 device.
# Usage: run-target.sh <image.iso> [workdir]
# Stop it with Ctrl-C; that ends tgtd and everything it serves.
set -euo pipefail
iso=$(readlink -f "$1")
work=${2:-${XDG_RUNTIME_DIR:-/tmp}/wobble-proof}
here=$(dirname "$(readlink -f "$0")")
mkdir -p "$work"
if [ ! -x "$work/tgt/usr/tgtd" ]; then
  git clone -q --depth 1 --branch v1.0.85 https://github.com/fujita/tgt.git "$work/tgt"
  git -C "$work/tgt" apply "$here/tgt-read-toc-length.patch"
  make -C "$work/tgt/usr" -j"$(nproc)" tgtd tgtadm >/dev/null
fi
# The control socket path must fit in sun_path (108 bytes), so keep it short.
export TGT_IPC_SOCKET=${XDG_RUNTIME_DIR:-/tmp}/wobble-tgt.sock
"$work/tgt/usr/tgtd" -f --iscsi portal=0.0.0.0:3260 &
tgtd=$!
trap 'kill -9 "$tgtd" 2>/dev/null || true; rm -f "$TGT_IPC_SOCKET".0*' EXIT
sleep 1
adm="$work/tgt/usr/tgtadm"
"$adm" --lld iscsi --op new --mode target --tid 1 -T iqn.2026-11.test.wobble:proof
"$adm" --lld iscsi --op new --mode logicalunit --tid 1 --lun 1 --device-type cd --bstype rdwr -b "$iso"
"$adm" --lld iscsi --op bind --mode target --tid 1 -I ALL
echo "target iqn.2026-11.test.wobble:proof on port 3260, backing $iso"
wait "$tgtd"
