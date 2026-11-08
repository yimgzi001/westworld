using Engine;
using Game;
namespace SurvivalcraftAgentLab;
// Call only on the game update thread. No world editing, inventory injection or teleporting.
public sealed class AgentBridge(ComponentPlayer player)
{
    public object Observe(int radius = 2)
    {
        if (radius is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(radius));
        var body = player.Entity.FindComponent<ComponentBody>(true) ?? throw new InvalidOperationException("Body missing");
        var p = body.Position;
        var inventory = player.ComponentMiner.Inventory;
        var vital = player.Entity.FindComponent<ComponentVitalStats>();
        var slots = Enumerable.Range(0, inventory.SlotsCount).Select(i => new { slot=i, value=inventory.GetSlotValue(i), count=inventory.GetSlotCount(i),nutrition=BlocksManager.Blocks[Terrain.ExtractContents(inventory.GetSlotValue(i))].GetNutritionalValue(inventory.GetSlotValue(i)),sicknessProbability=BlocksManager.Blocks[Terrain.ExtractContents(inventory.GetSlotValue(i))].GetSicknessProbability(inventory.GetSlotValue(i)) }).ToArray();
        var terrain = player.Project.FindSubsystem<SubsystemTerrain>(true).Terrain;
        var cells = new List<object>();
        for (int x=-radius;x<=radius;x++) for(int y=-radius;y<=radius;y++) for(int z=-radius;z<=radius;z++) {
            int cx=(int)MathF.Floor(p.X)+x,cy=(int)MathF.Floor(p.Y)+y,cz=(int)MathF.Floor(p.Z)+z;
            cells.Add(new {x=cx,y=cy,z=cz,value=terrain.GetCellValue(cx,cy,cz)});
        }
        return new { player=player.PlayerGuid, position=new {x=p.X,y=p.Y,z=p.Z}, health=player.ComponentHealth.Health, air=player.ComponentHealth.Air, vitals=vital==null?null:new {food=vital.Food,stamina=vital.Stamina,sleep=vital.Sleep,temperature=vital.Temperature,wetness=vital.Wetness}, slots, cells };
    }
    // Issue every tick during the desired movement, then explicitly Stop. Values are local axes.
    public void Move(float strafe, float forward, bool jump=false)
    {
        if(!float.IsFinite(strafe)||!float.IsFinite(forward)) throw new ArgumentException("Non-finite motion");
        var locomotion=player.Entity.FindComponent<ComponentLocomotion>(true) ?? throw new InvalidOperationException("Locomotion missing");
        locomotion.WalkOrder=new Vector2(Math.Clamp(strafe,-1,1),Math.Clamp(forward,-1,1));
        locomotion.JumpOrder=jump?1:0;
    }
    public void Stop()=>Move(0,0);
    public bool Act(Vector3 direction, bool place)
    {
        if(!float.IsFinite(direction.LengthSquared()) || direction.LengthSquared()<0.0001f) throw new ArgumentException("Invalid ray");
        var eye=(player.Entity.FindComponent<ComponentCreatureModel>(true) ?? throw new InvalidOperationException("Model missing")).EyePosition;
        var ray=new Ray3(eye,Vector3.Normalize(direction));
        var hit=player.ComponentMiner.Raycast<TerrainRaycastResult>(ray,RaycastMode.Digging,true,false,false);
        if(!hit.HasValue) return false;
        return place?player.ComponentMiner.Place(hit.Value):player.ComponentMiner.Dig(hit.Value);
    }
}
