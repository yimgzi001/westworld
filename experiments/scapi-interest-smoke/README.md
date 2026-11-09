# Native interest-center integration gate (2026-11-09)

This is an architecture adapter acceptance scene, not a five-AI survival demo.
The dot cloud desktop ran the real SCAPI world through its supported desktop terminal.
Only one original human player is spawned. Five remote test interest centers are **not entities**, and are never inserted into ActorRegistry pretending to be native-bound actors.

## Observed result
- Fresh disposable Survival world, original human visibility range32.
- Five centers512–896 cells away were initially unallocated.
- Adapter submitted six negative location keys: five remote fixtures plus the human body's center.
- All five center chunks reached native TerrainChunkState.Valid in0.5141601seconds after submission.
- Native camera/player key2 remained present, six negative keys active, simulation provider returned six centers.
- Adapter disposed; native updater drained all negative active/pending keys, original key2 remained, provider reset to null.
- Native save invoked; successful experiment exit0, total5.7421178seconds.
- OpenAL IllegalCommand warnings occurred, without preventing these checks.

This is real chunk processing, not merely accepted requests. It does not establish native animal ecology, multi-actor survival, zero human viewport operation, performance capacity or saved-world reload. Save was invoked, but save/reload content was not independently verified in this scene. The five-actor architecture gate remains open.

## Reproduce in an authorized graphical cloud context
Build from repository root with.NET10:
```sh
dotnet build experiments/scapi-interest-smoke/InterestSmoke.csproj -c Release -m:1 -nodeReuse:false -o work/cloud-interest-run
cd work/cloud-interest-run
dotnet InterestSmoke.dll --new-disposable-world
```
The output directory must be fresh, inside an ignored work directory, with no doc or Mods directory. Use a new output directory for another run; do not delete existing worlds to satisfy the guard. Keep dependencies and Content.zip from build output together.

A240second per-frame timeout closes the experiment; this is not a hard external timeout for a stalled thread. Normal desktop initialization remains required. Do not change security settings or bypass shell restrictions.

## Evidence (safe structured excerpt)
No model calls or credentials. Runtime worlds and binary output are excluded.
```jsonl
{"kind":"start","scripted":true,"scope":"native interest centers only; one real player; no AI actors"}
{"kind":"world_created","DirectoryName":"app:/doc/Worlds/World"}
{"kind":"submitted","nativePlayers":1,"remoteCenters":5,"aiActors":0,"centers":[{"X":-158.5,"Y":65.01000213623047,"Z":154.5},{"X":353.5,"Y":65.01000213623047,"Z":154.5},{"X":449.5,"Y":65.01000213623047,"Z":154.5},{"X":545.5,"Y":65.01000213623047,"Z":154.5},{"X":641.5,"Y":65.01000213623047,"Z":154.5},{"X":737.5,"Y":65.01000213623047,"Z":154.5}],"negativePending":6}
{"kind":"remote_ready","seconds":0.5141600999999998,"states":["Valid","Valid","Valid","Valid","Valid"],"negativeActive":6,"nativeKeys":[2],"simulationCenters":6,"nativePlayers":1,"health":1}
{"kind":"released","released":true,"nativeKeys":[2]}
{"kind":"result","passed":true,"reason":"remote chunks valid and owned keys released","seconds":5.7421178,"scope":"terrain adapter only; no five-actor or reload claim"}
```

## Next coherent gate
Introduce persistent actor control kind; exclude AI from GameWidget construction; preserve full native survival components and move visual feedback behind a nonvisual presentation boundary. Initial source review found direct GUI writes in VitalStats(food/temperature), Health and Level; merely deleting Gui would cause swallowed update exceptions and false survival claims. Test five real bodies, independent actions, zero AI viewports, survival values, exception visibility, save and reload together.

Independent review fixed negative-coordinate cell rounding (Terrain.ToCell) and added equality assertion for original native viewpoint keys after release. The recorded evidence above is the rerun after those fixes. Build:0errors,15upstream dependency warnings.
