#!/bin/sh
set -eu
cd "$(dirname "$0")"
if [ -d bin ]; then
  mkdir -p build-backups
  cp -a bin "build-backups/bin-$(date +%Y%m%d-%H%M%S)-$$"
fi
mkdir -p bin/assets
mcs -sdk:4 -langversion:4 -platform:x86 -optimize+ -target:winexe -win32icon:assets/oldym.ico -r:System.Windows.Forms -r:System.Drawing -r:System.Web.Extensions -r:System.Security -r:lib/BouncyCastle.Crypto.dll -out:bin/OldYM.Desktop.exe src/*.cs
cp lib/BouncyCastle.Crypto.dll bin/
cp OldYM.Desktop.exe.config bin/
cp assets/roots.pem assets/roots-source.txt assets/oldym.ico bin/assets/
printf '\nBuilt: bin/OldYM.Desktop.exe\n'
