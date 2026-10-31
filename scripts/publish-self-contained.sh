#!/usr/bin/env bash
set -euo pipefail
rid="${1:-linux-x64}"
dotnet publish AfterburnerStudio/AfterburnerStudio.csproj -c Release -r "$rid" --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "publish/$rid"
