using System.Text.Json;
using Engine;
using Game;
namespace RecoveryVision;
public sealed partial class BridgeMod
{
    readonly bool fixtureEnabled=Environment.GetEnvironmentVariable("SC_RECOVERY_FIXTURE")=="1";
    bool fixtureDone; double fixtureNext;
    record Cell(int X,int Y,int Z);
    record Layout(string Name,Cell Target,Cell Wall,double LocalX,double LocalY,double LocalZ);
    void RunFixture()
    {
        if(!fixtureEnabled||fixtureDone||directory==null||player==null||terminated||gameTime<fixtureNext)return;
        fixtureNext=gameTime+1;
        if(File.Exists(Path.Combine(directory,"fixture.claim"))){fixtureDone=true;outcome="fixture_existing_claim_no_replay";return;}
        if(!Safe(out var flags)||active!=null)return;
        var p=player;var model=p.Entity.FindComponent<ComponentCreatureModel>(true) ?? throw new InvalidOperationException("missing_eye_model");var eye=model.EyePosition;
        var basis=Matrix.CreateFromQuaternion(model.EyeRotation);var sub=p.Project.FindSubsystem<SubsystemTerrain>(true);var terrain=sub.Terrain;
        if(p.Project.FindSubsystem<SubsystemPlayers>(true).PlayersData.Count!=1)return;
        if(Math.Abs(basis.Forward.Y)>.25f){Atomic("fixture-wait.json",new{reason="requires_near_horizontal_view"});return;}
        int floorY=(int)MathF.Floor(p.ComponentBody.Position.Y),headY=(int)MathF.Floor(eye.Y);
        if(headY<floorY+1){Atomic("fixture-wait.json",new{reason="head_height_guard"});return;}
        bool Loaded(Cell c){var chunk=terrain.GetChunkAtCell(c.X,c.Z);return chunk!=null&&chunk.State>=TerrainChunkState.Valid&&terrain.IsCellValid(c.X,c.Y,c.Z);}
        bool Air(Cell c)=>Loaded(c)&&terrain.GetCellContents(c.X,c.Y,c.Z)==0;
        bool SafeEdit(Cell c)=>c.Y>=floorY+1&&
          (Math.Abs(c.X+.5-p.ComponentBody.Position.X)>=1.5||Math.Abs(c.Z+.5-p.ComponentBody.Position.Z)>=1.5);
        bool Geometric(Cell c)=>VisionCore.ContainsWorld(V(eye),new(c.X+.5,c.Y+.5,c.Z+.5),V(basis.Right),V(basis.Up),V(basis.Forward));
        bool Clear(Cell c)=>VoxelVisibility.HasClearLine(V(eye),c.X,c.Y,c.Z,(x,y,z)=>Air(new Cell(x,y,z)));
        var candidates=new List<(Cell cell,double x,double y,double z,List<Cell> ray)>();
        for(int x=(int)Math.Floor(eye.X)-6;x<=(int)Math.Floor(eye.X)+6;x++)
        for(int z=(int)Math.Floor(eye.Z)-6;z<=(int)Math.Floor(eye.Z)+6;z++){
            var cell=new Cell(x,headY,z);if(!SafeEdit(cell)||!Air(cell))continue;
            var d=new VisionCore.Vec(x+.5-eye.X,headY+.5-eye.Y,z+.5-eye.Z);
            double lx=d.Dot(V(basis.Right)),ly=d.Dot(V(basis.Up)),lz=d.Dot(V(basis.Forward));
            if(d.Dot(d)>36||d.Dot(d)<9)continue;
            var ray=new List<Cell>();
            bool clear=VoxelVisibility.HasClearLine(V(eye),x,headY,z,(cx,cy,cz)=>{var c=new Cell(cx,cy,cz);ray.Add(c);return Air(c);});
            if(clear)candidates.Add((cell,lx,ly,lz,ray));
        }
        var layouts=new List<Layout>();
        foreach(var spec in new[]{("front",0d,4d),("left",-2d,4d),("right",2d,4d),("behind",0d,-4d)}){
            var selected=candidates.Where(c=>spec.Item1=="behind"?c.z < -3&&Math.Abs(c.x)<1:Geometric(c.cell)&&c.z>3&&
                (spec.Item1=="left"?c.x<-.9:spec.Item1=="right"?c.x>.9:Math.Abs(c.x)<.9))
                .OrderBy(c=>(c.x-spec.Item2)*(c.x-spec.Item2)+(c.z-spec.Item3)*(c.z-spec.Item3)).ToList();
            Layout? result=null;
            foreach(var c in selected){
                var wall=c.ray.Where(w=>w!=c.cell&&SafeEdit(w)&&Air(w)).OrderBy(w=>Math.Abs((w.X+.5-eye.X)*(w.X+.5-eye.X)+(w.Z+.5-eye.Z)*(w.Z+.5-eye.Z)-5)).FirstOrDefault();
                if(wall!=null){result=new Layout(spec.Item1,c.cell,wall,c.x,c.y,c.z);break;}
            }
            if(result==null){Atomic("fixture-wait.json",new{reason="no_safe_naturally_clear_layout",missing=spec.Item1,candidateCount=candidates.Count});return;}
            layouts.Add(result);
        }
        var planned=layouts.SelectMany(l=>new[]{l.Target,l.Wall}).Distinct().ToArray();
        if(planned.Length>8||planned.Any(c=>!SafeEdit(c)||!Air(c)))return;
        int pumpkin=Terrain.MakeBlockValue(131,0,BasePumpkinBlock.SetSize(BasePumpkinBlock.SetIsDead(0,true),7));
        if(!BlocksManager.Blocks[Terrain.ExtractContents(pumpkin)].GetType().Name.Contains("Pumpkin"))throw new InvalidOperationException("pumpkin_id_mismatch");
        // Known base Stone contents=3 is checked by runtime name before any terrain write.
        int wallValue=Terrain.MakeBlockValue(3);
        if(!BlocksManager.Blocks[3].GetType().Name.Contains("Granite")&&!BlocksManager.Blocks[3].GetType().Name.Contains("Stone"))throw new InvalidOperationException("wall_id_mismatch");
        var originals=planned.ToDictionary(c=>c,c=>terrain.GetCellValue(c.X,c.Y,c.Z));
        Atomic("fixture-plan.json",new{scope="Artificial same-tick native terrain API fixture, not autonomous discovery, not renderer or persistence proof",session,eye=new{eye.X,eye.Y,eye.Z},flags,layouts,
            edits=planned.Select(c=>new{cell=c,original=originals[c]}),pumpkin,wallValue});
        using(var claim=new FileStream(Path.Combine(directory,"fixture.claim"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){
            var bytes=System.Text.Encoding.UTF8.GetBytes(session);claim.Write(bytes);claim.Flush(true);
        }
        fixtureDone=true;active=null;
        var results=new List<object>();bool all=true;
        void Log(object data)=>File.AppendAllText(Path.Combine(directory,"fixture-edits.jsonl"),JsonSerializer.Serialize(data)+"\n");
        string ChunkState(Cell c)=>terrain.GetChunkAtCell(c.X,c.Z)?.State.ToString() ?? "missing";
        void Capture(string stage){Observe();File.Copy(Path.Combine(directory,"vision.json"),Path.Combine(directory,"fixture-"+stage+".vision.json"),false);}
        void Set(Cell c,int value){
            if(!originals.ContainsKey(c)||!SafeEdit(c))throw new InvalidOperationException("unplanned_edit");
            int before=terrain.GetCellValue(c.X,c.Y,c.Z);
            Log(new{stage="intent",cell=c,before,value,chunkState=ChunkState(c)});
            sub.ChangeCell(c.X,c.Y,c.Z,value);
            Log(new{stage="applied",cell=c,after=terrain.GetCellValue(c.X,c.Y,c.Z),chunkState=ChunkState(c)});
        }
        try{
            foreach(var l in layouts){
                if(!Safe(out _)||!Air(l.Target)||!Air(l.Wall))throw new InvalidOperationException("pre_stage_guard");
                Set(l.Target,pumpkin);
                bool exists=terrain.GetCellContents(l.Target.X,l.Target.Y,l.Target.Z)==131;
                bool geometric=Geometric(l.Target),clear=Clear(l.Target),visible=exists&&geometric&&clear;
                Capture(l.Name);
                bool expected=l.Name!="behind";bool pass=exists&&clear&&visible==expected;all&=pass;
                results.Add(new{stage=l.Name,exists,geometric,clear,visible,expected,pass,targetChunk=ChunkState(l.Target),wallChunk=ChunkState(l.Wall),local=new{x=l.LocalX,y=l.LocalY,z=l.LocalZ}});
                if(expected){
                    Set(l.Wall,wallValue);
                    bool wallExists=terrain.GetCellContents(l.Wall.X,l.Wall.Y,l.Wall.Z)==3;
                    bool blocked=!Clear(l.Target);bool validChunks=Loaded(l.Target)&&VoxelVisibility.HasClearLine(V(eye),l.Target.X,l.Target.Y,l.Target.Z,(x,y,z)=>Loaded(new Cell(x,y,z)));bool wallPass=wallExists&&terrain.GetCellContents(l.Target.X,l.Target.Y,l.Target.Z)==131&&blocked&&validChunks;
                    Capture(l.Name+"_wall");all&=wallPass;
                    results.Add(new{stage=l.Name+"_wall",wallExists,blocked,validChunks,targetChunk=ChunkState(l.Target),wallChunk=ChunkState(l.Wall),pass=wallPass});
                    Set(l.Wall,originals[l.Wall]);
                }
                Set(l.Target,originals[l.Target]);
            }
            Atomic("fixture-result.json",new{session,scope="new artificial fixture; classification pairs are not exact geometric reflections",allPassed=all,results});
            outcome=all?"fixture_passed":"fixture_failed";
        }catch(Exception ex){outcome="fixture_error";Atomic("fixture-result.json",new{session,allPassed=false,error=ex.ToString(),results});}
        finally{
            var restored=new List<object>();bool restoreOk=true;
            foreach(var c in planned)try{if(terrain.GetCellValue(c.X,c.Y,c.Z)!=originals[c])Set(c,originals[c]);bool ok=terrain.GetCellValue(c.X,c.Y,c.Z)==originals[c];restoreOk&=ok;restored.Add(new{cell=c,ok});}catch(Exception ex){restoreOk=false;restored.Add(new{cell=c,error=ex.Message});}
            Atomic("fixture-restoration.json",new{session,restoreOk,restored});
            terminated=true;active=null;
        }
    }
}
