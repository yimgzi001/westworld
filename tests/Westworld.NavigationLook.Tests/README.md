# Missing-cell observation planner

Pure snapshot tests, no world/GUI/model calls. Build with the cloud .NET SDK and run NavigationLook.Tests.dll. The planner accepts the route evaluator's missing local cells and prioritizes current support, current body (including rear), forward support, then head clearance. It does not create routes or infer unseen air.

Each Next consumes one observation attempt, requires a new observation ID, and skips already tried coordinates. Default budget 8 (maximum configurable 16), no time-based or new-ID reset. Actual body relocation >0.5 m from scan anchor resets the budget. Evidence older than 3 seconds is not treated as known. Blocked/occluded targets may remain unknown forever: exhaustion is an honest result, not permission to move. The host should obtain another real observation after turning and use Direction to adapt to native angle conventions. Position/eye and missing-cell provenance must come from trusted host sensors, never model assertions.

No fixed panoramic loop and no guarantee that one aim reveals a cell: voxel-centre aiming can remain occluded. This is only finite targeted observation planning; native visibility and route proof remain authoritative.
