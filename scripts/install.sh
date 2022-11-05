#!/usr/bin/env bash
set -e

if [ ! -f ./build.config ]; then
    echo "Please run ./configure.sh first" >&2
    exit 1
fi
source build.config

$BuildTool $Solution /p:Configuration=Release

FSX_INSTALL_DIR="$Prefix/lib/fsx"
BIN_INSTALL_DIR="$Prefix/bin"

mkdir -p $FSX_INSTALL_DIR
mkdir -p $BIN_INSTALL_DIR

if [[ x"$Solution" == "xfsx.sln" ]]; then
    cp -rfvp ./fsxc/bin/Release/net6.0/* $FSX_INSTALL_DIR
else
    cp -v ./fsxc/bin/Release/* $FSX_INSTALL_DIR
fi
cp -v ./scripts/launcher.sh "$BIN_INSTALL_DIR/fsx"
chmod ugo+x "$BIN_INSTALL_DIR/fsx"
