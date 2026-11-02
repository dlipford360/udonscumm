"""Headless probe: run the REAL VM + REAL actor layer against real game data and
real baked tables, then print exactly what the layer tells the material."""
import json
import os
import tempfile
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parent.parent
unity = Path(os.environ.get("UNITY_CONTENTS", "/Applications/Unity/Unity.app/Contents"))
dotnet = unity / "NetCoreRuntime/dotnet"
runtimes = unity / "NetCoreRuntime/shared/Microsoft.NETCore.App"
runtime = sorted(runtimes.iterdir())[-1]
with tempfile.TemporaryDirectory(prefix="scumm-actorprobe-") as temporary:
    output = Path(temporary) / "probe.dll"
    response = Path(temporary) / "probe.rsp"
    response.write_text("\n".join([
        "-nologo", "-target:exe", f'-out:"{output}"',
        *[f'-r:"{ref}"' for ref in runtime.glob("*.dll")],
        f'"{root / "Tools/ActorLayerProbe.cs"}"',
        f'"{root / "Assets/Scumm/Runtime/ScummV5Vm.cs"}"',
        f'"{root / "Assets/Scumm/Runtime/ScummActorLayer.cs"}"',
    ]))
    subprocess.run([str(dotnet), str(unity / "DotNetSdkRoslyn/csc.dll"), "@" + str(response)], check=True)
    output.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
        "tfm": "net6.0", "framework": {"name": "Microsoft.NETCore.App", "version": runtime.name}
    }}))
    subprocess.run([str(dotnet), str(output), str(root / "Assets/Scumm/Generated")], check=True)
