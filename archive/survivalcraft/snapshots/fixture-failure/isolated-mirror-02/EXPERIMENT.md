# Recovery native vision fixture 02

Status: prepared, not run.

Independent copy of newly saved Hollowbeach world; original world and checkpoint stay unchanged. No historical experiment continuation claimed. Single player; hidden view; LAN disabled; no models or cloud requests.

Purpose: verify reconstructed vision on real native terrain using explicitly artificial, transient fixtures. Front/behind and left/right classification plus non-Air obstruction. These tests are NOT natural resource discovery, not exact geometric reflection, not rendered-pixel visibility, and not AI survival.

Fixture implementation will persist one-time claim and old values before writing, restrict writes to head-height ray cells, never edit player occupancy or supporting ground, and restore values synchronously. Report every applied/restored edit and fail closed on unsafe geometry. No retry of consumed claim.

After native run, inspect result, restore report, world reload, and final stopped state; close GUI and preserve evidence whether successful or failed.
