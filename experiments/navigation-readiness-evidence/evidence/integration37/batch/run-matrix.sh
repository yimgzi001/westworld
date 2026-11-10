#!/bin/bash
set -u
exec python3 -B <CLOUD_REPO>/work/integration37-final-native-batch/frozen/runner.py --manifest <CLOUD_REPO>/work/integration37-final-native-batch/matrix.json
