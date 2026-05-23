# Installer version stamped from git tags

Package.ps1 runs `git describe --tags --always` to name the zip and stamp a version.txt inside it. No manual version bumping required.
