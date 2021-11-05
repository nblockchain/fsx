#!/usr/bin/env bash
set -euxo pipefail

./test/test.fsx

# this generates a test.dll file
cd ./test/ && fsharpc test.fs --target:library && cd ..

./test/testRefLib.fsx
./test/testTsv.fsx
./test/testProcessConcurrency.fsx
