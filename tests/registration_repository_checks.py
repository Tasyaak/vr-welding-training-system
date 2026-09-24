"""Issue #49 ownership, scene hygiene and dependency-free deterministic suite."""
from pathlib import Path
import json
import os
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def check_registration(errors: list[str]) -> None:
    registration = ROOT / 'unity/Assets/Trainer/Registration'
    allowed = ('WeldingTrainer.Registration', 'WeldingTrainer.Content.Spatial')
    vendors = {'Oculus.VR', 'Oculus.VR.Editor', 'meta.xr.mrutilitykit'}
    for path in registration.rglob('*'):
        if path.suffix == '.meta':
            continue
        if not Path(str(path) + '.meta').is_file():
            errors.append(f'Registration: Unity metadata missing: {path.relative_to(ROOT)}')
        if path.suffix == '.asmdef':
            definition = json.loads(path.read_text())
            for ref in definition.get('references', []):
                if not ref.startswith(allowed) and ref not in vendors:
                    errors.append(f'Registration must remain Group A independent: {path.name}: {ref}')
        if path.suffix == '.cs' and 'Meta' in path.parts:
            text = path.read_text()
            if re.search(r'\.(Save|Load|Share|Erase)(Anchor\w*)?Async\s*\(', text):
                errors.append(f'Session anchors must never persist: {path.name}')
    preview = registration / 'Preview/RegistrationPreview.unity'
    if not preview.is_file():
        errors.append('Standalone registration preview scene is missing.')
    build_settings = (ROOT / 'unity/ProjectSettings/EditorBuildSettings.asset').read_text()
    if 'RegistrationPreview.unity' in build_settings:
        errors.append('Standalone registration preview must not replace production Bootstrap composition.')


def run_registration_tests(errors: list[str]) -> None:
    dotnet = shutil.which('dotnet')
    if dotnet is None:
        errors.append('Registration tests require .NET SDK 8 or later.')
        return
    try:
        installed = subprocess.check_output([dotnet, '--list-sdks'], text=True)
        majors = [int(v) for v in re.findall(r'^(\d+)\.\d+\.\d+\s', installed, re.MULTILINE) if int(v) >= 8]
        if not majors:
            raise ValueError('No stable .NET SDK >= 8')
        framework = f'net{max(majors)}.0'
        project = ROOT / 'tests/Registration.Tests'
        environment = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')
        build = subprocess.run([dotnet, 'build', str(project), '--nologo', f'-p:RegistrationTargetFramework={framework}'],
                               cwd=ROOT, env=environment, text=True, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, timeout=120, check=False)
        if build.returncode:
            errors.append('Registration C# build failed:\n' + build.stdout)
            return
        result = subprocess.run([dotnet, str(project / 'bin/Debug' / framework / 'Registration.Tests.dll'), str(ROOT)],
                                cwd=ROOT, env=environment, text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, timeout=120, check=False)
        if result.returncode:
            errors.append('Registration suite failed:\n' + result.stdout)
        else:
            print(f'Registration C# suite ({framework}): ' + result.stdout.strip().splitlines()[-1])
    except (ValueError, OSError, subprocess.SubprocessError) as error:
        errors.append('Registration suite could not execute: ' + str(error))
