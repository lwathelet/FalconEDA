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
