#!/usr/bin/env bash
set -euxo pipefail

mkdir -p .nuget/
curl -k -f -L https://dist.nuget.org/win-x86-commandline/v5.4.0/nuget.exe --output .nuget/nuget.exe
mkdir -p packages/
mono .nuget/nuget.exe install Microsoft.Build -Version 16.11.0 -OutputDirectory packages

