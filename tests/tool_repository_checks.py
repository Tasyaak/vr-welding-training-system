"""Group A tool boundary checks and the shared engine-independent C# cases."""
import json
import re
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def check_tool(errors):
    tool = ROOT / 'unity/Assets/Trainer/Platform/Meta/Tool'
    allowed = ('WeldingTrainer.Tool', 'WeldingTrainer.Content.Spatial')
    for path in tool.rglob('*.asmdef'):
        for ref in json.loads(path.read_text())['references']:
            if not ref.startswith(allowed) and ref not in ('Unity.InputSystem', 'Unity.InputSystem.TestFramework', 'Unity.XR.OpenXR', 'Oculus.VR'):
                errors.append(f'Tool Group A dependency violation: {path}: {ref}')
    actions = json.loads((ROOT / 'unity/Assets/InputSystem_Actions.inputactions').read_text())
    for action_map in actions['maps']:
        if any('<XRController>' in b['path'] for b in action_map['bindings']):
            errors.append('Generic Player/UI XR controller bindings compete with physical tool input')
    for path in tool.rglob('*.cs'):
        if 'OVRInput.' in path.read_text() or 'InputDevices.GetDeviceAtXRNode' in path.read_text():
            errors.append(f'Parallel legacy polling in tool adapter: {path}')
    try:
        dotnet = shutil.which('dotnet')
        if not dotnet:
            raise ValueError('Tool tests require .NET SDK >= 8')
        sdk = subprocess.check_output([dotnet, '--list-sdks'], text=True)
        major = max(int(x) for x in re.findall(r'^(\d+)\.', sdk, re.M) if int(x) >= 8)
        framework = f'net{major}.0'
        project = ROOT / 'tests/Tool.Tests'
        result = subprocess.run([dotnet, 'run', '--project', str(project), f'-p:ToolTargetFramework={framework}'], cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        if result.returncode:
            errors.append('Tool C# suite failed:\n' + result.stdout)
        else:
            print('Tool C# suite: ' + result.stdout.strip().splitlines()[-1])
    except (ValueError, OSError, subprocess.SubprocessError) as e:
        errors.append('Tool tests could not run: ' + str(e))
