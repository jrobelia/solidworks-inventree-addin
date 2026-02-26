# Architecture

Module map of the add-in. Update this when files are added or deleted.

## Source tree

```
SwInventreeAddin/
  AddIn/
    SwAddin.cs              — COM entry point, SolidWorks lifecycle, event wiring
  Config/
    IConfigProvider.cs      — interface: GetServerConfig / SaveServerConfig
    ServerConfig.cs         — data class: Url + ApiKey
    EncryptedConfigProvider  — DPAPI-encrypted file in %AppData%
  InvenTree/
    IInventreeClient.cs     — interface: GetPartByIpnAsync / PatchPartRevisionAsync
    InventreeHttpClient.cs  — real HTTP client (System.Net.Http)
    InventreePart.cs        — data class: Pk, Name, Notes, Revision, Ipn
  SolidWorks/
    IDocumentPropertyService — interface: get/set custom properties
    SwDocumentPropertyService — real SolidWorks implementation
  UI/
    TaskPaneControl.cs      — main panel (fetch, compare, apply, push)
    SettingsForm.cs         — modal dialog for server URL + API key
```

## Data flow

1. SolidWorks loads SwAddin ? reads encrypted settings ? creates HTTP client
2. User opens a part ? SwAddin fires LoadPartNumber ? panel shows current properties
3. User clicks Fetch ? TaskPaneControl calls IInventreeClient ? displays comparison
4. User clicks Apply ? TaskPaneControl calls IDocumentPropertyService ? writes to part
5. User clicks Push Rev ? TaskPaneControl calls IInventreeClient.PatchPartRevisionAsync

## Module boundaries

| Module    | Depends on          | Must not know about     |
|-----------|---------------------|-------------------------|
| UI        | IInventreeClient, IDocumentPropertyService, IConfigProvider | HTTP details, SolidWorks API |
| InvenTree | System.Net.Http     | SolidWorks, UI          |
| Config    | System.Security (DPAPI) | Everything else       |
| SolidWorks| SolidWorks.Interop  | InvenTree, Config       |
| AddIn     | All interfaces      | Implementation details  |
