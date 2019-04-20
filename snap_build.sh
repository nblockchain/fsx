#!/bin/sh
set -e

./configure.fsx --prefix ./staging
make
make install

#this below is to prevent the possible error "Failed to reuse files from previous run: The 'pull' step of 'fsx' is out of date: The source has changed on disk."
snapcraft clean fsx -s pull

snapcraft
