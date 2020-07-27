#!/usr/bin/env bash
set -euxo pipefail

./test/test.fsx
./test/testTsv.fsx
./test/testVersion.fsx
./test/testProcessConcurrency.fsx
