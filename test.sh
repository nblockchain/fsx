#!/usr/bin/env bash
set -euxo pipefail

./test/test.fsx
./test/testProcessConcurrency.fsx
