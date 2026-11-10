# Direct action run identity

ActorTools.ReadDirect adds `runId` to the existing action/status/failure/affectedCount view. The identifier follows the actual AtomicActionRun object, not action name, receipt or last polling call. It remains stable across running/terminal/cancellation states, and changes for a replacement run (including immediate failure/success). There is no identifier when no direct run exists; the existing idle response remains unchanged.

The mapping is a ConditionalWeakTable, so completed runs are not retained solely by the identity registry. It is in-memory only, not a persistent resume token or actor/session authorization. Hosts must additionally bind session, actor, receipt and native owner. A stale terminal from a previous run is not authority to control a newer action.

Tests instantiate an isolated ActorTools shell with reflection and inject real AtomicActionRun objects to exercise ReadDirect identity and serialization without a game world. They do not demonstrate native MoveTo success or execute StartDirect preconditions/cancellation physics. Production StartDirect and cancel behavior are unchanged; invalid input/busy before a run is created cannot replace the existing m_direct reference.
