#!/bin/sh
set -e

which fsharpc >/dev/null || \
  (echo "Please install fsharp package first via apt" && exit 2)

if [ $# -lt 1 ]; then
    echo "At least one argument expected"
    exit 1
fi

DIR_OF_THIS_SCRIPT=$(dirname "$(realpath "$0")")
FSXC_PATH="$DIR_OF_THIS_SCRIPT/../lib/fsx/fsxc.exe"
mono "$FSXC_PATH" "$1"
TARGET_DIR=$(dirname -- "$1")
TARGET_FILE=$(basename -- "$1")
shift
exec mono "$TARGET_DIR/bin/$TARGET_FILE.exe" "$@"
