CLASH TO BCF - Navisworks Manage plugin
=======================================

Exports Clash Detective results to a BCF file (.bcf). Each clash keeps
the same view you see in Clash Detective.

https://github.com/alb5194/NavisoworksBcfBatchExportPlugin


REQUIREMENTS
------------
- Navisworks Manage {NAVIS_VERSION}. This build only works with that version.
- Windows 64-bit.


INSTALL
-------
1. Unblock the zip BEFORE extracting it:
   right-click the zip > Properties > tick "Unblock" > OK.
   (If you skip this, Windows may stop Navisworks from loading the plugin,
   and no error is shown.)

2. Press Win + R, paste this and press Enter:

      %APPDATA%\Autodesk\ApplicationPlugins

   If the ApplicationPlugins folder does not exist, create it.

3. Copy the "NavisworksBcfClash.bundle" folder from the zip into that folder.
   You should end up with:

      ...\ApplicationPlugins\NavisworksBcfClash.bundle\PackageContents.xml
      ...\ApplicationPlugins\NavisworksBcfClash.bundle\Contents\{NAVIS_VERSION}\NavisworksBcfClash.dll

   Make sure the bundle folder is not nested twice
   (NavisworksBcfClash.bundle\NavisworksBcfClash.bundle\...).

4. Start (or restart) Navisworks.
   The command is on the ribbon: PCMR tab > BCF panel > Clash to BCF.
   If you don't see the "PCMR" tab, right-click the ribbon >
   Show Tabs > PCMR.


HOW TO USE
----------
1. Run your clash tests in Clash Detective.
2. Click PCMR > Clash to BCF.
3. Tick the clash tests, groups or single clashes you want to export.
   Use the filter box and the status list to narrow the list down.
4. Choose your options:
   - BCF version:
       "for Autodesk Forma": BCF 2.1 written the way Forma exports it.
         The two clashing elements are always isolated (everything else
         hidden). Use this when importing into Forma / Autodesk Docs.
       "2.1": works with most other tools (BIMcollab, Revit, Solibri...).
       "3.0": keeps the exact field of view.
   - Other elements: Dim or Hide, like Clash Detective's "Dim Other" and
     "Hide Other". Or leave the rest of the model as it is.
   - Author, snapshot, and whether to include clash comments.
   - Section box around clash: adds a section box (BCF clipping planes)
     around the clash area, for viewers that support sectioning.
5. Click Export... and choose where to save the .bcf file.

Each clash becomes one BCF issue with:
- a snapshot of the clash view (Item 1 red, Item 2 green)
- the same camera as Clash Detective: the clash's saved viewpoint, or the
  view Navisworks computes for it
- both elements selected and colored, identified by IFC GUID and Revit
  Element ID
- the title, status, assignee, distance, clash point and comments

When the export finishes, your view, selection and hidden items are
restored.


UNINSTALL
---------
Close Navisworks and delete:
   %APPDATA%\Autodesk\ApplicationPlugins\NavisworksBcfClash.bundle


TROUBLESHOOTING
---------------
- Button missing: check the Navisworks version, the folder layout in step 3,
  and that the zip was unblocked. Then restart Navisworks.
- Elements are not found in Revit or other BCF tools: the plugin uses the
  IfcGUID/GlobalId property when the model has one. Exporting the NWC/IFC
  with IFC GUIDs gives the best matching.
