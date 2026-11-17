#!/usr/bin/env bash
# Rebuild the API shim, then regenerate src/Facades from its public types.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build src/OpenVP.SimplSharp -c Release -o artifacts/shim "$@"
dotnet run --project tools/FacadeGen -- artifacts/shim/OpenVP.SimplSharp.dll src/Facades tools/facade-names.txt
