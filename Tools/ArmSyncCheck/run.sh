#!/bin/bash
# Compiles the ArmSync runtime scripts against Unity-math stand-ins and runs the checks.
# Needs mono (mcs). Usage: Tools/ArmSyncCheck/run.sh   (from the repository root)
set -e
here=$(cd "$(dirname "$0")" && pwd); src="$here/../../AnseongSteel/Assets/_Claude/ArmSync"
out=$(mktemp -d)
mcs -langversion:7.2 -nowarn:219 -out:"$out/test.exe" "$here/UnityStubs.cs" "$here/Data.cs" "$here/Test.cs" "$src"/Runtime/*.cs
mcs -langversion:7.2 -target:library -out:"$out/editor.dll" "$here/UnityStubs.cs" "$src"/Runtime/*.cs "$src"/Editor/*.cs
mono "$out/test.exe"
