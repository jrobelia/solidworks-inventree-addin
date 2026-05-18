# Registration is one-time per DLL path

`RegAsm /codebase` writes the DLL path to the Windows registry once. SolidWorks reads that path on every startup. Re-registration is only needed if the DLL path changes (e.g. moving the project folder). Rebuilding in place does not require re-registration.
