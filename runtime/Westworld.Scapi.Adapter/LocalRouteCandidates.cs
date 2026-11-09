using Westworld.Runtime.Core;

namespace Westworld.Scapi.Adapter;

/// <summary>Both policies concern currently observed geometry, not future guarantees.</summary>
public enum RouteGroundPolicy {
    StrictStable,
    // Also accepts observed native sand/gravel cubes. Their unseen support and
    // future falling state are unknown; the executing host must monitor hazards.
    ObservedFullCube
}

/// <summary>Evidence belongs to one observation, not a durable permission to move.</summary>
public sealed record LocalRouteProjection(Guid SessionId, Guid ActorId, Guid ObservationId,
    IReadOnlyList<ExplorationCandidate> Candidates,
    IReadOnlyDictionary<Guid, IReadOnlyList<CellObservation>> Evidence, RouteGroundPolicy GroundPolicy);

/// <summary>
/// Conservative snapshot-only projection for the ordinary 0.5 x 1.77 player body.
/// No terrain, entity, chunk, or inventory reads. KnownSafe means only that the
/// supplied trusted observation supports a flat static corridor. The host must
/// reobserve before execution and use native bounded movement/collision handling.
/// A modified/larger body, dynamic hazard, or later world change is NOT covered.
/// </summary>
public static class LocalRouteCandidates {
    const double HalfWidth = 0.25, ClearanceHeight = 1.9;
    // Shared host convention: ignore only <=1e-5 numerical contact at cell planes.
    public const double ContactEpsilon = 1e-5;
    static readonly (int X, int Z)[] Directions = [(0,1),(1,1),(1,0),(1,-1),(0,-1),(-1,-1),(-1,0),(-1,1)];

    // One metre first; caller may explicitly request a two metre straight corridor.
    public static LocalRouteProjection Project(ActorObservation observation, double distance = 1,
        RouteGroundPolicy groundPolicy = RouteGroundPolicy.StrictStable) {
        ArgumentNullException.ThrowIfNull(observation);
        if (!Enum.IsDefined(groundPolicy)) throw new ArgumentOutOfRangeException(nameof(groundPolicy));
        if (!double.IsFinite(distance) || distance < 1 || distance > 2)
            throw new ArgumentOutOfRangeException(nameof(distance));
        var candidates = new List<ExplorationCandidate>();
        var evidence = new Dictionary<Guid, IReadOnlyList<CellObservation>>();
        LocalRouteProjection Result() => new(observation.SessionId, observation.ActorId, observation.ObservationId,
            candidates.AsReadOnly(), new System.Collections.ObjectModel.ReadOnlyDictionary<Guid,IReadOnlyList<CellObservation>>(evidence), groundPolicy);
        var p = observation.Position;
        if (observation.SessionId == Guid.Empty || observation.ActorId == Guid.Empty || observation.ObservationId == Guid.Empty
            || !double.IsFinite(observation.Time) || observation.Time < 0
            || !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)
            || Math.Abs(p.X) > 10_000_000 || Math.Abs(p.Z) > 10_000_000 || p.Y < 1 || p.Y > 253
            || !float.IsFinite(observation.Health) || observation.Health <= 0 || observation.Cells is null
            || observation.Cells.Length > 4096) return Result();
        int feet = (int)Math.Round(p.Y);
        // No stairs, falling, jumping, partial block surfaces, or uncertain floor height.
        if (Math.Abs(p.Y - feet) > 0.05) return Result();
        var cells = new Dictionary<(int X,int Y,int Z), CellObservation>();
        foreach (var cell in observation.Cells) {
            // Even identical duplicates make provenance ambiguous: reject the snapshot.
            if (cell is null || !cells.TryAdd((cell.X,cell.Y,cell.Z),cell)) return Result();
        }
        var destinationCells = new HashSet<(int X,int Z)>();
        foreach (var direction in Directions) {
            double length = Math.Sqrt(direction.X*direction.X + direction.Z*direction.Z);
            var target = new Position(p.X + direction.X/length*distance, p.Y, p.Z + direction.Z/length*distance);
            int minX=(int)Math.Floor(Math.Min(p.X,target.X)-HalfWidth+ContactEpsilon), maxX=(int)Math.Floor(Math.Max(p.X,target.X)+HalfWidth-ContactEpsilon);
            int minZ=(int)Math.Floor(Math.Min(p.Z,target.Z)-HalfWidth+ContactEpsilon), maxZ=(int)Math.Floor(Math.Max(p.Z,target.Z)+HalfWidth-ContactEpsilon);
            // Rectangular superset of swept body, intentionally conservative on diagonals.
            var proof = new List<CellObservation>(); bool safe=true;
            for(int x=minX;x<=maxX && safe;x++) for(int z=minZ;z<=maxZ && safe;z++) {
                if(!Known(x,feet-1,z,out var ground) || !SupportedGround(ground.Value!.Value, groundPolicy)){safe=false;break;}
                proof.Add(ground);
                int top=(int)Math.Floor(Math.Max(p.Y,feet)+ClearanceHeight-ContactEpsilon);
                for(int y=feet;y<=top;y++) {
                    if(!Known(x,y,z,out var air) || Contents(air.Value!.Value)!=0){safe=false;break;}
                    proof.Add(air);
                }
            }
            if(!safe || !destinationCells.Add(((int)Math.Floor(target.X),(int)Math.Floor(target.Z)))) continue;
            var id=Guid.NewGuid();
            candidates.Add(new ExplorationCandidate(id,target,true));
            evidence.Add(id,proof.AsReadOnly());
        }
        return Result();
        bool Known(int x,int y,int z,out CellObservation cell) =>
            cells.TryGetValue((x,y,z),out cell!) && cell.Visibility=="known" && cell.Value.HasValue && cell.Value.Value>=0;
    }
    static int Contents(int value) => value & 0x3ff;
    // Native fixed cube classes only, never fluids, fire, plants, traps, furniture,
    // slabs, or modded geometry. Accepting sand/gravel does not inspect or infer
    // any layer below the directly observed floor cube.
    static bool SupportedGround(int value, RouteGroundPolicy policy) =>
        Contents(value) is 1 or 2 or 3 or 4 or 5 or 8
        || policy == RouteGroundPolicy.ObservedFullCube && Contents(value) is 6 or 7;
}
