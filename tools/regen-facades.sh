#!/usr/bin/env bash
# Rebuild the API shims, then regenerate the facade projects from their public types.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build src/OpenVP.SimplSharp -c Release -o artifacts/shim "$@"
dotnet run --project tools/FacadeGen -- artifacts/shim/OpenVP.SimplSharp.dll src/Facades tools/facade-names.txt
dotnet build src/OpenVP.Splus -c Release -o artifacts/splus "$@"
dotnet run --project tools/FacadeGen -- artifacts/splus/OpenVP.Splus.dll src/SplusFacades tools/splus-facade-names.txt ../../OpenVP.Splus/OpenVP.Splus.csproj
