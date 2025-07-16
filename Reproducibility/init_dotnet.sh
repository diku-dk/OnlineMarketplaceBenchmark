#!/usr/bin/env bash

# .NET 7
sudo apt-get update && \
  sudo apt-get install -y dotnet-sdk-7.0

# control number of cpus in experiments
sudo apt install cpulimit
