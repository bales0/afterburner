#!/usr/bin/env bash
set -euo pipefail
base="https://raw.githubusercontent.com/bales0/afterburner/master/xsvf"
mkdir -p xsvf
for name in erase_ATF1502AS.xsvf erase_ATF1504AS.xsvf id_ATF150X.xsvf; do
    curl --fail --location --proto '=https' --tlsv1.2 "$base/$name" -o "xsvf/$name"
    echo "Saved xsvf/$name"
done
