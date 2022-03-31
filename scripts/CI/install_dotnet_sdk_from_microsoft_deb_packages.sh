#!/usr/bin/env bash
set -e

# taken from https://docs.microsoft.com/en-gb/dotnet/core/install/linux-ubuntu#2004-
apt install -y wget
wget -q https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb
dpkg -i packages-microsoft-prod.deb
apt install -y apt-transport-https
apt update

DEBIAN_FRONTEND=noninteractive apt-get install -y dotnet-sdk-5.0

dotnet --version
