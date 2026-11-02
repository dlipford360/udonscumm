"""Compile and exercise the real tick pacer using host-only stubs."""
import json
import os
from pathlib import Path
import subprocess
import tempfile

root = Path(__file__).resolve().parent.parent
unity = Path(os.environ.get("UNITY_CONTENTS", "/Applications/Unity/Unity.app/Contents"))
dotnet = unity / "NetCoreRuntime/dotnet"
runtime = sorted((unity / "NetCoreRuntime/shared/Microsoft.NETCore.App").iterdir())[-1]
with tempfile.TemporaryDirectory(prefix="scumm-pacer-") as temporary:
    output = Path(temporary) / "checks.dll"
    response = Path(temporary) / "checks.rsp"
    response.write_text("\n".join([
        "-nologo", "-target:exe", f'-out:"{output}"',
        *[f'-r:"{ref}"' for ref in runtime.glob("*.dll")],
        f'"{root / "Tools/TickPacerChecks.cs"}"',
        f'"{root / "Assets/Scumm/Runtime/ScummTickPacer.cs"}"',
    ]))
    subprocess.run([str(dotnet), str(unity / "DotNetSdkRoslyn/csc.dll"), "@" + str(response)], check=True)
    output.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
        "tfm": "net6.0", "framework": {"name": "Microsoft.NETCore.App", "version": runtime.name}
    }}))
    subprocess.run([str(dotnet), str(output)], check=True)
