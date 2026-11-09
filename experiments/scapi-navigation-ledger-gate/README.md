# Native gateway navigation gate

Same full external Player/one AtomicActions/observation-only senses as bounded navigation gate. Controlled flat fixture, no model or inventory injection. Every start/status/stop goes through NativeToolSession ledger; only frame progression calls BoundedLocalNavigation.Update. Not arbitrary generated code.

Assertions: replay same request sequence returns same receipt/navigation ID without starting twice; ordinary MoveTo rejected while navigation busy; mismatched cancel ID does not stop navigation. Flat must reach 3m objective via >=3 local segments; cancel must drain to Cancelled; wall case must fail and settle (reason recorded, not presumed Blocked). Status is host_navigation_status, never stale ActorTools action_status.

Stage: `bash experiments/scapi-navigation-ledger-gate/stage.sh LABEL EXISTING_ASSETS_DIRECTORY`. This serial-builds and creates three fresh work directories with 63 DLL hashes checked before launch. Launch run.sh from normal dot desktop terminal. No saved world/assets are committed. Each test normally <20s, hard gate limit90s.

Current integration explicitly remains serial: ToolAgent's prefetch eligibility hook is reserved but AsyncDecisionHandoff is not yet attached to real model calls. Do not claim request/movement overlap from these ledger tests. Existing navigation-handoff gate separately verifies delayed deterministic decision lifecycle.

## Native results

01-flat failed5.337s/updateFailure1: lease idle callback attempted nested gateway.Call while ledger already admitting host_navigate_local; wrapper Start failed, then test wrongly assumed a busy rejection shape. Both mistakes fixed: internal native idle proof uses read-only ActorTools.Invoke (no write bypass) and gate explicitly checks start receipt. Failure evidence retained.

02-flat PASS10.974s,3segments,2.78322m path,remaining.21678m;02-cancel PASS6.721s,1segment,.23341m path,host status cancelled;02-obstacle negativePASS8.981s,.25m,host status failed/reason segment_evidence_expired, not native Blocked. All02: health1, no AI view, zero update errors, clean settled cleanup. Same-sequence replay retained same request ID; ordinary MoveTo rejected while busy; wrong request cancel rejected; native cancel_action explicitly rejected with use_host_cancel_navigation. No API/model was called.

Evidence includes full01 failure and02 rawJSONL plus installed DLL hashes. deployed02-source-hashes.json records sources before the final one-line extension of OwnerQuarantined to also include lease.ClockFaulted; that extension requires final integration build and is not claimed as an02 native clock-regression test. Per-segment monotonic timestamps now appear on wrapper segment/terminal events; lower-level detailed observe/dwell transitions remain separate.
