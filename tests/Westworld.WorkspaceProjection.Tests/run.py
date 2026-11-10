#!/usr/bin/env python3
"""Compile the CURRENT production OwnWorkspace with fake receipts, never the game."""
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default=str(ROOT / 'work/dotnet/dotnet'))
    args = parser.parse_args()
    sdk = shutil.which(args.dotnet)
    if not sdk:
        raise SystemExit('dotnet SDK not found; pass --dotnet /path/to/dotnet')
    source = (ROOT / 'experiments/scapi-tool-agent/Program.cs').read_text()
    start, end = ' object OwnWorkspace(', ' object CompactInventory('
    if source.count(start) != 1 or source.count(end) != 1:
        raise SystemExit('Production method boundaries changed; review extraction instead of running stale logic')
    method = source[source.index(start):source.index(end)]
    if not method.rstrip().endswith('}'):
        raise SystemExit('Unexpected OwnWorkspace method boundary')
    template = (HERE / 'Harness.cs.template').read_text()
    token = '/* CURRENT_PRODUCTION_OWN_WORKSPACE */'
    if template.count(token) != 1:
        raise SystemExit('Invalid harness template')
    print('Current production OwnWorkspace SHA256:', hashlib.sha256(method.encode()).hexdigest(), flush=True)
    with tempfile.TemporaryDirectory(prefix='westworld-workspace-projection-') as folder:
        path = Path(folder)
        (path / 'Program.cs').write_text(template.replace(token, method))
        (path / 'Regression.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>')
        # No game dependencies, third-party packages, network calls, or native execution.
        (path / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        subprocess.run([sdk, 'run', '--project', str(path / 'Regression.csproj'), '-p:UseSharedCompilation=false'], check=True, cwd=path)

if __name__ == '__main__':
    main()
