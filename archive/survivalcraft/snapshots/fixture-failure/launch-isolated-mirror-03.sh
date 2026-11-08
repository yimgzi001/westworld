#!/bin/bash
set -eu
export SC_RECOVERY_FIXTURE=1
ROOT=/workspace/scratch/5b9c67580fe7/survivalcraft-recovery-20261108
export HOME="$ROOT/isolated-mirror-03/home" XDG_CONFIG_HOME="$ROOT/isolated-mirror-03/config" XDG_DATA_HOME="$ROOT/isolated-mirror-03/data" SC_RECOVERY_VISION_DIR="$ROOT/isolated-mirror-03/ipc" ALSOFT_DRIVERS=null LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=softpipe
export LD_LIBRARY_PATH="$ROOT/isolated-mirror-03/native-compat"
cd "$ROOT/isolated-mirror-03/runtime"
exec "$ROOT/dependencies/dotnet/dotnet" Survivalcraft.dll > "$ROOT/isolated-mirror-03/launch.log" 2>&1
