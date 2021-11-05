#!/usr/bin/env bash
set -e

if [ ! -f ./build.config ]; then
    echo "Please run ./configure.sh first" >&2
    exit 1
fi
source build.config

$BuildTool fsx.sln
