#!/usr/bin/env bash
set -e

if [ ! -f ./build.config ]; then
    echo "Please run ./configure.sh first" >&2
    exit 1
fi
source build.config

if [[ ! $BuildTool == dotnet* ]]; then
    mkdir -p .nuget/
    curl -o .nuget/NuGet.exe https://dist.nuget.org/win-x86-commandline/v5.4.0/nuget.exe
    mono .nuget/NuGet.exe restore $Solution
fi

$BuildTool $Solution $1
