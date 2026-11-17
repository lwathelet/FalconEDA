# Design decisions

Where FalconEDA deliberately differs from Eagle. Everything not listed here follows Eagle's
commands and workflow.

## Copy, cut and paste work like other software

Eagle's CUT only copies a group into the paste buffer, which surprises most people.
FalconEDA uses the usual meaning:

| Command | Single object (no group) | Group selected |
|---|---|---|
| `COPY` | As in Eagle: duplicates the clicked object and attaches it to the mouse to place it. | Copies the group to the clipboard. The drawing does not change. |
| `CUT` | – | Copies the group to the clipboard **and deletes it** from the drawing. |
| `PASTE` | As in Eagle: places the clipboard contents at the mouse, click to drop. | Same. |

Ctrl+C, Ctrl+X and Ctrl+V run `COPY`, `CUT` and `PASTE` on the current group.

## No side panel with tabs

Eagle 9 has a side panel with the tabs *Sheets*, *Inspector* and *Selection Filter*.
FalconEDA has no tabbed side panel. Sheets are chosen with the sheet box in the toolbar
(`EDIT .s2` on the command line).

## No SPICE simulation and no Design Link

The SPICE simulator buttons and Autodesk Design Link are not part of FalconEDA.
SPICE data in Eagle files (`<spice>`, pin mappings) is kept untouched when saving.

## Libraries: delete, rename, cut and copy items

In a library, every item (symbol, package, device set) can be deleted, renamed, cut, copied
and pasted, also from one library into another, the same way as objects in a drawing.

## Only local libraries

FalconEDA reads Eagle libraries (`.lbr`) from local folders only. There are no cloud or managed
libraries (Eagle 9's online libraries and its `cache\lbr` folder are not used). The default
library folder is `C:\Wattlet\R&D\eagle\lbr`; it can be changed under Options.

## A component has a list of values, each with manufacturer and reference

In Eagle a part has one VALUE. In FalconEDA a component has a **list of values**, and every value
carries its manufacturer (`MNF`) and reference (`REF`) with it.

- **The list lives in the library**, shared by all designs. It is stored the way the existing
  libraries already do it, so files stay readable by Eagle: each value is an Eagle *technology* of
  the device with the attributes `VAL`, `MNF` and `REF` (for example technology `100K_` with
  `VAL=100K`, `MNF=...`, `REF=000008`).
- **Choosing a value sets VAL, MNF and REF on the part together**, picked from a table of the
  component's values. Symbols show the value with `>VAL`.
- **A value that is not in the list yet** can be typed in (VAL, MNF, REF); FalconEDA adds it to the
  component's list in the library, so it can be chosen next time.

## BOM and DNP are part properties, set with CHANGE

Two properties of a part (not of the library), set with Eagle's `CHANGE` command: on the parts named
after it, else on the selected group, else on the parts clicked.

- **`BOM`** is on by default. `CHANGE BOM OFF` leaves a part out of the bill of materials,
  `CHANGE BOM ON` puts it back.
- **`DNP`** (do not populate) is off by default. `CHANGE DNP ON` keeps the part's value, shows `DNP`
  after it on the schematic (for example `100K DNP`) and lists the part as DNP in the bill of materials.

In the file they are part attributes with the value `yes`: `NO_BOM` for a part out of the BOM, `DNP`
for a part not populated. Eagle keeps them like any other attribute. This replaces the old way of
choosing a `DNP_` technology, which lost the part's real value.

MNF and REF are not properties: they belong to the value chosen from the library list.

## NAME on a net asks only when it matters

- A net in one piece, only on this sheet: renamed straight away.
- A net in several pieces on this sheet: NAME asks whether to rename only the piece clicked or the
  whole net on this sheet.
- A net on several sheets: NAME asks whether to rename only the piece (when there are several), the
  net on this sheet, or the net on all sheets.

On the command line: `NAME (x y) NEW` renames the piece at that point, `NAME OLD NEW SHEET` the net on
this sheet, `NAME OLD NEW` the net on all sheets.
