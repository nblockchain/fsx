#!/bin/sh
set -e

RUNNER=mono
ASSEMBLY_EXTENSION=exe
if ! which dotnet >/dev/null 2>&1; then
    if ! which fsharpc >/dev/null 2>&1; then
        echo "Please install dotnet (or legacy 'fsharp' apt package)"
        exit 1
    fi
else
    RUNNER=dotnet
    ASSEMBLY_EXTENSION=dll
fi

if [ $# -lt 1 ]; then
    echo "At least one argument expected"
    exit 1
fi

DIR_OF_THIS_SCRIPT=$(cd `dirname $0` && pwd)
FSXC_PATH="$DIR_OF_THIS_SCRIPT/../lib/fsx/fsxc.dll"
if ! [ -e $FSXC_PATH ]; then
    FSXC_PATH="$DIR_OF_THIS_SCRIPT/../lib/fsx/fsxc.exe"
fi

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

$RUNNER $FSXC_PATH $FIRST_ARGS

# if user didn't pass a .fsx script
if [ -z "$FSX_SCRIPT" ]; then
    # either a) fsxc already errored w/ exitCode<>0 <- but if that was the case, `set -e` would cause prev call to abort this script, so it'd not reach here
    # or b) user gave valid flag that exited with exitCode=0 even without .fsx (e.g. `--help`) <- this case here, so let's exit:
    exit 0
fi

TARGET_DIR=$(dirname -- "$FSX_SCRIPT")
TARGET_FILE=$(basename -- "$FSX_SCRIPT")
TARGET_FILE_PATH="$TARGET_DIR/bin/$TARGET_FILE.$ASSEMBLY_EXTENSION"

exec $RUNNER $TARGET_FILE_PATH $REST_ARGS
