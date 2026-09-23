"""Dependency-free provenance/ownership checks; C# owns semantic validation."""
from __future__ import annotations
import hashlib
import json
import os
import re
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GENERATED = ROOT / 'unity/Assets/Trainer/Content/Spatial/Generated'


def check_spatial_content(errors: list[str]) -> None:
    try:
        catalog = json.loads((GENERATED / 'supplied.spatial').read_text())
        for source in catalog['sources'] + catalog['bakeInputs']:
            path = ROOT / source['path']
            if path.resolve().is_relative_to(ROOT.resolve()) is False:
                raise ValueError('source path escapes repository')
            if hashlib.sha256(path.read_bytes()).hexdigest() != source['sha256']:
                raise ValueError(f"source/bake identity mismatch: {source['path']}")
        for item in catalog['imports']:
            for kind in ('geometry', 'visual'):
                path = GENERATED / item[kind+'File']
                if path.parent != GENERATED:
                    raise ValueError('artifact path escapes generated directory')
                if hashlib.sha256(path.read_bytes()).hexdigest() != item[kind+'Sha256']:
                    raise ValueError(f'generated artifact identity mismatch: {path.name}')
        if list((ROOT / 'unity/Assets').rglob('*.step')):
            raise ValueError('engineering STEP must not enter Unity Assets')
        spatial = ROOT / 'unity/Assets/Trainer/Content/Spatial'
        for path in spatial.rglob('*.asmdef'):
            definition = json.loads(path.read_text())
            for reference in definition.get('references', []):
                if not reference.startswith('WeldingTrainer.Content.Spatial'):
                    raise ValueError(f'Group A assembly references another subsystem: {path.name}: {reference}')
        for path in spatial.rglob('*'):
            if path.suffix == '.meta':
                continue
            if not Path(str(path)+'.meta').is_file():
                raise ValueError(f'Unity-generated metadata missing: {path.relative_to(ROOT)}')
    except (KeyError, ValueError, OSError) as error:
        errors.append('Spatial content: '+str(error))


def run_spatial_tests(errors: list[str]) -> None:
    """Run pure tests through the existing CI entry point using an installed SDK."""
    dotnet = shutil.which('dotnet')
    if dotnet is None:
        errors.append('Spatial tests require a .NET SDK (8 or later) on PATH.')
        return
    try:
        installed = subprocess.check_output([dotnet, '--list-sdks'], text=True)
        majors = [int(v) for v in re.findall(r'^(\d+)\.\d+\.\d+\s', installed, re.MULTILINE) if int(v) >= 8]
        if not majors:
            errors.append('Spatial tests require a stable .NET SDK 8 or later.')
            return
        framework = f'net{max(majors)}.0'
        environment = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')
        project = ROOT / 'tests/Spatial.Tests'
        build = subprocess.run(
            [dotnet, 'build', str(project), '--nologo', f'-p:SpatialTargetFramework={framework}'],
            cwd=ROOT, env=environment, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, timeout=120, check=False,
        )
        if build.returncode:
            errors.append('Spatial C# build failed:\n'+build.stdout)
            return
        result = subprocess.run(
            [dotnet, str(project / 'bin/Debug' / framework / 'Spatial.Tests.dll'), str(ROOT)],
            cwd=ROOT, env=environment, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, timeout=120, check=False,
        )
        if result.returncode:
            errors.append('Spatial C# suite failed:\n'+result.stdout)
        else:
            print(f'Spatial C# suite ({framework}): '+result.stdout.strip().splitlines()[-1])
    except (OSError, subprocess.SubprocessError) as error:
        errors.append('Spatial C# suite could not execute: '+str(error))


if __name__ == '__main__':
    errors: list[str] = []
    check_spatial_content(errors)
    for error in errors:
        print(error)
    if errors:
        raise SystemExit(1)
    print('Spatial provenance and ownership checks passed.')
