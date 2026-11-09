# Five native survival actors, zero AI viewports

## Verified on dot cloud desktop, 2026-11-09
This scene uses five real SCAPI Player entities, not animals, metadata records or hidden GameWidgets. No model is connected. All commands are fixed bounded local movement pulses.

| Check | Observed result |
| --- | --- |
| Native identities | PlayerIndex2,3,4,5,6; five distinct entities |
| AI/human viewports | 0/0; GameWidgets.Count remains0 |
| Horizontal movement after individual pulses | 0.867,0.867,0.034,0.900,0.864 cells |
| Stop speeds | zero or floating-point residue below0.1 |
| Survival | all five Food decreased, all five Health remained1 |
| Terrain ownership | five negative actor keys only; initial positive spawn leases retired after ready/publication |
| Save | five PlayerData control flags and matching entity PlayerIndex set in actual Project.xml |
| Restart | same identities/control types/inventories; position tolerance0.15; finite Health/Food/Stamina/Sleep difference<=0.001 |
| After restart | ten additional game seconds; food continued to decrease, movement distances and stop speeds0 |
| Update errors |0 in both processes |
| Exit / elapsed wall time | new0 /16.974s; reload0 /16.671s |

The smallest movement (actor3,0.034cells) meets only the narrow nonzero motion check. It does not establish successful navigation around obstacles or all actors executing useful survival tasks.

Independent review strengthened the initial passing run: use XZ distance instead of3D fall distance, compare restored survival values, match saved entity IDs, record stop speed, retire obsolete spawn centers, freeze control kind after registration. Evidence here is the rerun after those fixes. See [safe evidence summary](evidence-summary.json). Full inventories were compared in the running test; the committed summary replaces their arrays with SHA256 digests and slot counts.

### Regression
- Human-only original viewport scene (scapi-interest-smoke) rebuilt against changed game: exit0; five remote chunks Valid; original positive key retained.
- Core contracts70/70; compiled native hook checks7/7.
- Build0errors,15existing upstream dependency vulnerability warnings.
- OpenAL IllegalCommand messages remain an environment issue; these are not hidden. They did not fail this scene.
- No user computer was used. Runtime worlds, binary outputs, dependency caches and credentials are not committed.

## Implementation boundary
- PlayerData.IsExternalActor is saved and restored before entity load; missing flag defaults to original human behavior. It must be set before Add and cannot change while registered/alive.
- Human viewport limit remains4. Separate experimental external-actor cap50 is a configuration ceiling, **not a50-actor validation**.
- External actors do not create GameWidget or bind cameras, skip Intro/spawn dialogs/hardware input, but retain native bodies, inventory, health, hunger, stamina, sleep and other player components.
- GUI compatibility retains detached value widgets for native survival feedback. These are not attached views. This is **zero AI viewports**, not zero GUI objects or a graphics-free bootstrap.
- SubmitExternalMovement is game-thread-only, finite/normalized, and consumed for a single native update. No fresh pulse means neutral next input. The production skill scheduler is still pending.
- SubsystemUpdate now exposes failure count and latest error instead of making failures completely invisible. This test rejects any such error. It does not implement automatic production recovery.
- The experiment publishes the existing activity adapter after all native actors become ready, then retires their positive spawn loading keys. A production host must repeat ownership transfer after respawn and reconcile partially loaded worlds.
- Core ActorRegistry GUID/checkpoint and model interfaces are not yet fully wired into this native experiment. Persistent native PlayerIndex identity is validated here.
- Follow-up [same-world human/AI coexistence gate](COEXISTENCE.md) verifies one original human viewport alongside five AI bodies, including restart and input-channel separation. A real person interacting or late joining has not been exercised.
- No long-duration ecological survival, multi-agent collaboration, LLM decisions, resource scaling or purely graphics-free process is claimed.

## Reproduce
.NET10 and normal authorized graphical execution context required. Build at repo root:
```sh
dotnet build experiments/scapi-actor-gate/ActorGate.csproj -c Release -m:1 -nodeReuse:false -o work/cloud-actors-run
cd work/cloud-actors-run
dotnet ActorGate.dll --new-disposable-world
dotnet ActorGate.dll --reload
```
First run requires fresh output under ignored work/ with no doc/ or Mods/. Reuse that exact output only for --reload; marker/name and no-mod checks prevent accidental arbitrary-world loading. To repeat the pair choose a new work output, never erase prior evidence to pass guards.120second per-frame timeout is not a hard kill for a stalled thread.

Next full milestone: native actor lifecycle host, stable registry binding and checkpoint reconciliation, then one actor's observation→skill→world/inventory outcome with slow-model continuity. Keep the complete survival components and game-thread evidence.
