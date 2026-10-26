# CCTV Report Desk design

## Product and use

Portable Windows utility for technicians who review Hikvision recording status and maintain camera/NVR CSV data. The main job is choosing a report, seeing whether it is still running, and keeping its output easy to find.

## Visual system

- Canvas `#F1F4F2`; table and form surface `#FFFFFF`; navigation rail `#E7EDEA`.
- Main text `#1D2D33`; secondary text `#5A6C70`; primary action `#136B5C`; running state `#8F5F0E`; masthead `#19323A`.
- Segoe UI for navigation, labels, and actions; Consolas only for process output.
- Use the standard Windows controls and keyboard focus behavior. Keep data tables wide enough to scan; mask NVR passwords until the user chooses **Lihat password**.
- Show an indeterminate progress bar only during a running task. The remote report process does not expose a reliable completion percentage.

## Report destinations

The application folder remains the working location for the loader, CSV configuration, and report history. A user-selected folder receives copies of new reports after a successful run so the Daily report history continues to work in the portable folder.

## Credentials

The NVR table masks passwords by default and offers an explicit reveal toggle. A license-key dialog replaces `license.key` locally, makes a timestamped backup, and leaves remote validation until the next report run.

