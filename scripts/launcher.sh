#!/bin/sh
set -e

which fsharpc >/dev/null || \
  (echo "Please install fsharp package first via apt" && exit 2)

if [ $# -lt 1 ]; then
    echo "At least one argument expected"
    exit 1
fi

DIR_OF_THIS_SCRIPT=$(cd `dirname $0` && pwd)
FSXC_PATH="$DIR_OF_THIS_SCRIPT/../lib/fsx/fsxc.exe"

FIRST_ARGS=""
FSX_SCRIPT=""
REST_ARGS=""
while [ $# -gt 0 ];
do
    ARG=$1
    # FIXME: edge case: arg is simply "fsx", then below would think it had the
    #        .fsx extension
    EXTENSION="${ARG##*.}"

    if [ -z "$FSX_SCRIPT" ]; then
        FIRST_ARGS="$FIRST_ARGS $ARG"
    fi
    shift
    if [ $EXTENSION = fsx ]; then
        FSX_SCRIPT=$ARG
        REST_ARGS=$@
    fi
done

mono $FSXC_PATH $FIRST_ARGS

if [ -z "$FSX_SCRIPT" ]; then
    echo "Compilation of anything that is not an .fsx should have been rejected by fsx"
    echo "and shouldn't have reached this point. Please report this bug."
    exit 3
fi

TARGET_DIR=$(dirname -- "$FSX_SCRIPT")
TARGET_FILE=$(basename -- "$FSX_SCRIPT")

exec mono "$TARGET_DIR/bin/$TARGET_FILE.exe" $REST_ARGS
