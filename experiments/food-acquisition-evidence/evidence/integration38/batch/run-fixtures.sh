#!/bin/bash
set -u
exec python3 -B <CLOUD_REPO>/work/integration38-batch/frozen/runner.py --manifest <CLOUD_REPO>/work/integration38-batch/matrix.json
