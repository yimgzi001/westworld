# Recovery checkpoint, 2026-11-08

Recovered Library libfile_66699bf3dc088191b6992581fa79b353 version 10 into a new v10 directory, 216 files. Original ZIP retained with verified Library xattrs. SHA256: 4b437e6dfa42815d2019794a1ec8a1f621a8a98948d4fd81c16fcd4d1165cef6.

Current cloud GUI inventory has no Survivalcraft window. Current process inventory has no dotnet, wine, llama, Qwen or Survivalcraft process. This does not establish why the prior environment disappeared.

Offline recovery verification: Bridge 2/2, continuous 19/19, navigation 22/22, autonomy 12/12 passed (55 total). Bridge initially errored because the read-only sandbox forbade temporary directories; approved escalated retry passed. These are the old v10 tests, not the later 123-test mirror regression suite.

Missing from this archive: game DLL/runtime/assets, model GGUF, world saves, later VisionBridge/yaw/mirror changes and evidence. dotnet is not installed in current shell PATH. Latest Library version confirmed as 10, dated 2026-11-07 14:09 UTC. Newer findings remain in the research Page and shared project note but are not source recovery.

No gameplay actions, claim resets, cloud model calls, GitHub branches or pushes occurred. Do not restart old experimental bridges on real worlds: some add players automatically. Resume native testing only in an isolated recovered runtime after reconstructing latest safety checks and recording a new experiment, never claiming continuity with the previous run.

## Dependency recovery completed

SCNET X26.07.01.01 downloaded from original release, SHA256 matches historical 8078d67a6696cb86ef50ba84013301257da1d4f1a28498b08b1d698d5918e230. Microsoft SDK 10.0.403 downloaded and SHA512 checked against official metadata; --info verified runtime 10.0.14. Official llama.cpp b12185 archive checked; version b12185-5d7d4aec1. Unsloth Qwen3.5-4B-GGUF revision e87f176479d0855a907a41277aca2f8ee7a09523 Q4_K_M downloaded and SHA256 checked: 00fe7986ff5f6b463e62455821146049db6f9313603938a70800d1fb69ef11a4. This is a newly pinned model artifact, not proof of identity with the lost previous GGUF.

Independent build-check/Bridge compiled with 0 errors and 0 warnings; C# protocol 17/17 passed. validate_offline.py runs all 55 Python +17 C# tests and saves timestamped JSON. This remains old v10 validation, not later vision/mirror tests.

Single local model installation smoke: prompt 'Reply with exactly: READY', returned READY, exit 0, wall 4.15s including load, child peak RSS 4,370,840 KiB. Not representative game decision latency or benchmark. model-install-smoke.json retains details. No game was launched. Need recover or reconstruct later safety/vision/mirror code and create a new isolated world; no old .scworld/Project.xml/Chunks.dat was found in permitted workspace/common user-data paths. Exact Library title searches only found v10. Do not overwrite historical failed experiment records.

## Reconstructed native checkpoint, 2026-11-08

New recovery-vision-core and recovery-vision-game are reconstructions, not recovered late-stage originals. Core regression: 191/191; both core and game bridge build cleanly. The bridge provides read-only pumpkin vision plus tightly bounded real native yaw; no move/dig/place/eat/admin mutations. Commands use session, monotonic ID, age, same-tick safety checks, stop acknowledgment and budgets. Conservative loaded-Air DDA and radius 8, horizontal 90 / vertical 70 FOV were added.

New isolated default survival world Hollowbeach / player Basil successfully loaded in clean SCNET runtime with game views hidden and LAN connections disabled. Private library symlinks fixed missing unversioned X11 native dependencies; initial failed launch retained. Native smoke is SCRIPTED, not AI: next float above 0.1f (0.10000000894069672) rejected without motion; +0.1 and -0.1 each for 0.08 s accepted and turned approximately 5.511 and 5.503 degrees. Position stayed (1042.5,78,-160.5), health/air stayed 1, stop acknowledged, final active=false. Evidence: isolated-world-01/ipc/recovery-native-smoke.json and guard-audit.jsonl.

Native vision produced fresh empty resource arrays; no naturally visible pumpkin was present. Therefore positive native visibility, occlusion, model-guided discovery, and end-to-end mirror experiment remain unverified. No cloud model call or GitHub push.

Game window closed through window-manager close and absence verified in GUI inventory. Saved Project.json, Project.json.bak and Region 4,-1.dat exist under isolated-world-01/runtime/Worlds/World. Final IPC stop_ack, active=false, safe=true, 2 pulses, accumulated turn 11.01449 degrees. Not yet reopened to prove saved-world reload. Historical text above describes earlier checkpoints, not current dependency availability.

## Artificial fixture native run 03
Saved-world player binding fixed and verified via UpdateInput with one main player. Fixture DID NOT PASS. ChangeCell invalidated chunk lighting (Valid -> InvalidLight), causing production visibility to fail closed. The same-tick fixture cannot establish the intended visibility positive/negative cases. Only front and front_wall reached; neither passed. Next stage pre_stage_guard stopped execution. Original Air raw values 15360 restored via ChangeCell became 0: contents returned to Air but light bits differ, so strict restoreOk=false. Do not erase this result or treat it as successful restoration. This disposable world remains evidence only. No model/yaw/movement actions. Game window closed and absence verified. Original source-world three file hashes unchanged. Next design must stage edits over ticks, wait for Valid, and separately validate block content/data and regenerated lighting.
