# Clash to BCF (Navisworks Manage plugin)

Exports one or many Clash Detective results to a `.bcf` file. Each clash becomes one BCF topic, and its view matches the one Clash Detective shows.

## What gets exported per clash

| BCF part | Source |
|---|---|
| Camera | The result's saved viewpoint if it has one. Otherwise the viewpoint Navisworks itself computes for the clash (COM `GetSuitableViewPoint`). If neither is available, the view active at export start is zoomed to the clash's `ViewBounds`. |
| Snapshot | Rendered by Navisworks from that camera at the viewport's aspect ratio. Item 1 is red, Item 2 is green, and the other elements are dimmed, hidden or left as they are. |
| Components | Both items go in the selection and get red/green coloring. In *Hide* mode, `DefaultVisibility=false` and the two items are the only exceptions. |
| Component ids | `IfcGuid` comes from an `IfcGUID`/`GlobalId` property. If there isn't one, it falls back to the item's `InstanceGuid`. `AuthoringToolId` is the Revit Element ID. |
| Topic | The title is `<clash> - <test>`. The description lists the distance, clash point and both item paths. The topic is Closed if the clash is Resolved or Approved, and Open otherwise. The test name and the status are added as labels. Clash comments are included. |

Coordinates are converted to meters, as BCF requires.

**BCF 2.1 for Autodesk Forma** (the default) writes BCF 2.1 the way Forma itself exports issues, and always isolates the two clashing elements:
- `DefaultVisibility="false"` with the clashing pair as the exceptions.
- `Selected="true"` on the selected components.
- Components that carry only the `IfcGuid`.
- Viewpoint files named `<guid>_viewpoint.bcfv`.
- A comment linked to the viewpoint on every topic.

The optional **Section box around clash** adds six BCF clipping planes boxing the clash area (`ViewBounds`). Viewers that apply clipping planes then show only that area.

**BCF 2.1** limits FieldOfView to 45–60°. When the Navisworks FOV falls outside that range, the plugin clamps it and moves the camera along its view direction. The clash then stays the same size on screen. **BCF 3.0** keeps the exact FOV and also writes the aspect ratio.

## Install from a release

1. Download `NavisworksBcfClash-<version>-Navisworks2024.zip` from Releases. Before extracting, right-click it, choose **Properties**, tick **Unblock** and click OK. Otherwise Windows may stop Navisworks from loading the plugin.
2. Extract the `NavisworksBcfClash.bundle` folder into `%APPDATA%\Autodesk\ApplicationPlugins\`. No admin rights are needed.
3. Restart Navisworks Manage 2024. The command is under **PCMR** ribbon tab → **BCF** panel → **Clash to BCF**. If the tab is missing, right-click the ribbon and turn it on under **Show Tabs**.

The zip also contains a `README.txt` with these steps, plus usage, uninstall and troubleshooting notes.

## Build

The build needs a local Navisworks Manage install, because it compiles against Autodesk's API DLLs. Those DLLs can't be redistributed, so the project can't be built on GitHub-hosted CI.

```bash
dotnet build NavisworksBcfClash/NavisworksBcfClash.csproj -c Release -p:NavisworksVersion=2024
```

- The build references the Navisworks API, Clash and COM API DLLs from `C:\Program Files\Autodesk\Navisworks Manage <version>`. To use another location, pass `-p:NavisworksInstallPath=...`.
- The build creates `bin\Release\bundle\NavisworksBcfClash.bundle` and copies it to `%APPDATA%\Autodesk\ApplicationPlugins\`. To skip the copy, pass `-p:DeployToNavisworks=false`.
- Restart Navisworks to load the new build.

## Usage

1. Run your clash tests in Clash Detective.
2. Open **Clash to BCF**, check the tests, groups or results you want, and pick the options. Then click **Export...**.

The plugin restores the current viewpoint, selection and visibility when it finishes. It also clears temporary color/transparency overrides.
