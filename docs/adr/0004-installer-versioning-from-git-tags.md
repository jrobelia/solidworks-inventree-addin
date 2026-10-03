# Installer version stamped from git tags

Package.ps1 runs `git describe --tags --always` to name `SwInventreeAddin-<version>-Setup.exe` and pass the version to ISCC as `/DAppVersion`. No manual version bumping required.

(Originally the tag named a zip and stamped a version.txt inside it; superseded when the Inno exe became the sole installer, #302.)
