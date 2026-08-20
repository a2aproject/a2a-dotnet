"""ITK agent launcher for .NET.

This script is detected by the ITK runner (current.py) as a Python agent,
but actually launches the pre-built .NET ITK agent binary. This is necessary
because 'dotnet run --project' does restore+build which exceeds the ITK's
35-second readiness timeout on first run.

The run_itk.sh script (or container setup) must 'dotnet publish' first.
This launcher just execs the published binary for instant startup.
"""

import argparse
import os
import subprocess
import sys
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--httpPort', type=int, required=True)
    parser.add_argument('--grpcPort', type=int, required=False, default=0)
    args = parser.parse_args()

    itk_dir = Path(__file__).parent
    publish_dir = itk_dir / 'publish'
    published_dll = publish_dir / 'Itk.dll'

    # If not pre-published, build now
    if not published_dll.exists():
        print('[main.py] No published output found, building...', flush=True)
        env = os.environ.copy()
        env['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
        env['DOTNET_NOLOGO'] = '1'
        build_result = subprocess.run(
            ['dotnet', 'publish', '-c', 'Release', '-o', str(publish_dir),
             str(itk_dir / 'Itk.csproj')],
            cwd=str(itk_dir),
            env=env,
            capture_output=True,
            text=True,
        )
        if build_result.returncode != 0:
            print(f'[main.py] Build failed:\n{build_result.stdout}\n{build_result.stderr}',
                  flush=True)
            sys.exit(1)
        print('[main.py] Build succeeded.', flush=True)

    # Run the published DLL directly (instant startup)
    cmd = [
        'dotnet', str(published_dll),
        '--httpPort', str(args.httpPort),
    ]
    if args.grpcPort:
        cmd.extend(['--grpcPort', str(args.grpcPort)])

    print(f'[main.py] Starting: {" ".join(cmd)}', flush=True)
    os.execvp('dotnet', cmd)


if __name__ == '__main__':
    main()
