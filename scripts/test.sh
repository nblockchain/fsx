#!/usr/bin/env bash
set -euxo pipefail

./test/test.fsx

# this generates a test.dll file
cd ./test/ && fsharpc test.fs --target:library --out:test1.dll && cd ..
./test/testRefLib.fsx

mkdir ./test/lib
cd ./test/ && fsharpc test.fs --target:library --out:lib/test2.dll && cd ..
./test/testRefLibOutsideCurrentFolder.fsx

./test/testRefNugetLib1.sh
./test/testRefNugetLib2.fsx

./test/testFsiCommandLineArgs.fsx one 2 three

./test/testTsv.fsx
./test/testProcessConcurrency.fsx
