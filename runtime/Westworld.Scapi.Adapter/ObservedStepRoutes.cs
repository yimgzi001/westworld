using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public enum StepKind { Up, Down }
public sealed record ObservedStepRoute(Guid Id,Guid SessionId,Guid ActorId,Guid ObservationId,
    double ObservedAt,Position Origin,Position Destination,StepKind Kind,
    IReadOnlyList<LocalObservedCell> Evidence,double MaximumAge,bool AllowsLooseGround=false);

/// <summary>Only cardinal, one-full-cube transitions with known source and landing support.
/// This is geometric eligibility, NOT a proof the native controller can complete a jump.
/// Missing/occluded launch or landing cells refuse the route. No terrain access.</summary>
public static class ObservedStepRoutes {
    public static IReadOnlyList<ObservedStepRoute> Project(LocalObservationWindow window,bool allowObservedSandAndGravel=false) {
        ArgumentNullException.ThrowIfNull(window);
        var p=window.Position;
        if(window.SessionId==Guid.Empty||window.ActorId==Guid.Empty||window.ObservationId==Guid.Empty
          ||!double.IsFinite(window.Time)||window.Time<0||!float.IsFinite(window.Health)||window.Health<=0
          ||!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z)
          ||Math.Abs(p.X)>10_000_000||Math.Abs(p.Z)>10_000_000||p.Y<2||p.Y>250
          ||Math.Abs(p.Y-Math.Round(p.Y))>.05||window.Cells.Count>4096)return [];
        var cells=new Dictionary<(int,int,int),LocalObservedCell>();
        foreach(var c in window.Cells)if(c is null||!cells.TryAdd((c.X,c.Y,c.Z),c))return [];
        var result=new List<ObservedStepRoute>();int feet=(int)Math.Round(p.Y);
        foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})foreach(int dy in new[]{1,-1}) {
            // Full landing-cell centre avoids stopping while straddling the ledge.
            var target=new Position(Math.Floor(p.X)+.5+dx,feet+dy,Math.Floor(p.Z)+.5+dz);
            if(Math.Abs(dx==0?p.X-target.X:p.Z-target.Z)>.15)continue;
            var proof=new Dictionary<(int,int,int),LocalObservedCell>();bool ok=true;
            bool Need(int x,int y,int z,bool solid) {
                if(!cells.TryGetValue((x,y,z),out var c)||c.Value<0||c.SourceObservationId==Guid.Empty
                  ||!double.IsFinite(c.ObservedAt)||c.ObservedAt<0||!double.IsFinite(c.Age)||c.Age<0||c.Age>3||Math.Abs(window.Time-c.ObservedAt-c.Age)>1e-6
                  ||(solid?!Ground(c.Value,allowObservedSandAndGravel):((c.Value&1023)!=0)))return false;
                proof[(x,y,z)]=c;return true;
            }
            int sx0=(int)Math.Floor(p.X-.25+1e-5),sx1=(int)Math.Floor(p.X+.25-1e-5);
            int sz0=(int)Math.Floor(p.Z-.25+1e-5),sz1=(int)Math.Floor(p.Z+.25-1e-5);
            for(int x=sx0;x<=sx1&&ok;x++)for(int z=sz0;z<=sz1&&ok;z++) {
                ok=Need(x,feet-1,z,true);
                for(int y=feet;y<=feet+1&&ok;y++)ok=Need(x,y,z,false);
            }
            int tx=(int)Math.Floor(target.X),tz=(int)Math.Floor(target.Z);
            ok &= Need(tx,feet+dy-1,tz,true);
            int minX=(int)Math.Floor(Math.Min(p.X,target.X)-.25+1e-5),maxX=(int)Math.Floor(Math.Max(p.X,target.X)+.25-1e-5);
            int minZ=(int)Math.Floor(Math.Min(p.Z,target.Z)-.25+1e-5),maxZ=(int)Math.Floor(Math.Max(p.Z,target.Z)+.25-1e-5);
            // Conservative swept envelope: up-jump reserves 3.9m above launch;
            // descent reserves source-height body clearance, maximum one metre drop.
            int top=feet+(dy>0?3:1);
            for(int x=minX;x<=maxX&&ok;x++)for(int z=minZ;z<=maxZ&&ok;z++) {
                bool landing=x==tx&&z==tz;
                int bottom=landing?feet+dy:feet;
                for(int y=bottom;y<=top&&ok;y++)ok=Need(x,y,z,false);
            }
            if(ok)result.Add(new(Guid.NewGuid(),window.SessionId,window.ActorId,window.ObservationId,window.Time,p,target,
                dy>0?StepKind.Up:StepKind.Down,Array.AsReadOnly(proof.Values.ToArray()),proof.Values.Max(c=>c.Age),allowObservedSandAndGravel));
        }
        return result.AsReadOnly();
    }
    internal static bool Ground(int value,bool allowLoose)=>(value&1023) is 1 or 2 or 3 or 4 or 5 or 8
        ||allowLoose&&(value&1023) is 6 or 7;
}
