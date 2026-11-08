#!/bin/bash
set -eu
ROOT=/workspace/scratch/5b9c67580fe7/survivalcraft-recovery-20261108
export HOME="$ROOT/isolated-world-01/home" XDG_CONFIG_HOME="$ROOT/isolated-world-01/config" XDG_DATA_HOME="$ROOT/isolated-world-01/data" SC_RECOVERY_VISION_DIR="$ROOT/isolated-world-01/ipc" ALSOFT_DRIVERS=null LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=softpipe
export LD_LIBRARY_PATH="$ROOT/isolated-world-01/native-compat"
cd "$ROOT/isolated-world-01/runtime"
exec "$ROOT/dependencies/dotnet/dotnet" Survivalcraft.dll > "$ROOT/isolated-world-01/launch.log" 2>&1
