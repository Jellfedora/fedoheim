# FedoheimClient

Small features specific to one community server. Install it on **both** the dedicated
server and every client.

## Quest events

Adds a `fedoevent <name>` console command that asks the server to start a vanilla random
event (raid) at the player's position, e.g. `fedoevent army_theelder`. It is meant to be
run by quest/dialogue NPC mods that can execute console commands.

The command is not a cheat, so anyone can type it — that's why the **server** decides,
using its own config (`fedo.fedoheimclient.cfg`):

| Setting | Meaning |
|---|---|
| `AllowedEvents` | Comma-separated event names players may start. Empty = disabled (default). |
| `AllowedAreas` | `x,z,radius` entries separated by `;`, where the player must stand. Empty = anywhere. |
| `CooldownSeconds` | Minimum delay between two events started this way (default 300). |

A request is also ignored while another event is already running. The list of event names
available in your game version is written to the server log at startup.

## More World Locations port help pages

The help pages of the More World Locations port interface are built into that mod in
English only. To replace them, put one file per page in
`BepInEx/config/FedoheimClient/mwl_tutorials/`, named after the English tab title in lower
case: `introduction.md`, `port.md`, `manifest.md`, `shipment.md`, `delivery.md`,
`teleport.md`. The first line becomes the tab title, the rest is the page text (Unity rich
text tags such as `<color=orange>` work). Missing files leave the original page untouched.
Does nothing if More World Locations is not installed.
