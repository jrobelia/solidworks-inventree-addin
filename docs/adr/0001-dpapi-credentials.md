# DPAPI over AES key file for credential storage

Settings encrypted with Windows DPAPI (user scope). No key management needed — Windows ties decryption to the logged-in user account. Trade-off: credentials don't survive a Windows account migration, but that's rare and re-entering a URL + API key is trivial.
