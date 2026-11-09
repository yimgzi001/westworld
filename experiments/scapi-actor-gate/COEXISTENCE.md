# One human viewport and five native AI actors (2026-11-09)

The existing actor gate now accepts optional `--human`. It creates five external native survival actors plus one original human-controlled Player in the **same** isolated world. Only the human owns a GameWidget. The AI still receive bounded scripted pulses, not model decisions.

## Actually observed on dot cloud desktop
- Six native identities2–7: five external actors, one human Player; exactly one viewport.
- The actual rendered human camera showed neighboring actors in the same terrain. The human Player was programmatically created; **no person supplied keyboard/controller input**.
- External movement API rejects the human Player.
-1159 input-routing checks during first run, with correctly observed individual AI pulses40/36/61/63/63 times; no wrong-actor or simultaneous AI inputs.
- AI horizontal displacements0.875/0.907/0.919/0.910/0.870cells; human displacement0 and no Move input.
- Six bodies' Food decreased, all Health1. Final speeds<=0.012.
- Six actor/body activity centers use negative keys-1..-6. Original human camera key7 remains. External actors' obsolete positive spawn leases are gone.
- Each save checks current native body positions and Health/Food/Stamina/Sleep against the actual Project.xml, in addition to six PlayerData/entity ID correspondence and five external flags. This guards against accepting an old file when SaveProject swallows an error.
- Independent process reload preserved identities/control kinds/inventories and position/survival values within declared tolerances, with one viewport.
-1228 routing checks after reload, no new AI pulses. Physical drift was0 for five bodies and0.038cells for actor3; all speeds<=0.022. Human remained stationary. Normal physics is not claimed to stop mathematically instantaneously.
- Both runs: update failure count0, exit0; wall times17.371s and17.131s.
- Build0errors,15existing upstream dependency warnings. OpenAL warnings remain.

Safe structured evidence: [coexistence-evidence.json](coexistence-evidence.json). Original inventory arrays were compared by the test; committed summary uses inventory digests. Worlds and binaries are not committed.

## Reproduce
Use a **new** ignored output directory:
```sh
dotnet build experiments/scapi-actor-gate/ActorGate.csproj -c Release -m:1 -nodeReuse:false -o work/cloud-actors-human
cd work/cloud-actors-human
dotnet ActorGate.dll --new-disposable-world --human
dotnet ActorGate.dll --reload --human
```
The human scenario has a separate marker and world name; missing/incorrect mode on reload is rejected. The original zero-human mode remains available without `--human`.

## Scope
This closes the short-scene coexistence gate. It does **not** establish actual human interaction, late join/leave, long-running stability, terrain streaming over long distances, distributed multiplayer, AI planning or useful survival cooperation. The next host/API step must keep identity and game-thread command routing separate from the human input channel and continue preserving real-world/checkpoint evidence.
