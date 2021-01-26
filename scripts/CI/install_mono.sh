#!/usr/bin/env bash
set -euxo pipefail

DEBIAN_FRONTEND=noninteractive apt install -y fsharp
mono --version
