using Engine;
using Game;
using System.Text.Json;

// Isolated deterministic fixture. No browser, network or model invocation.
// Inputs use current observed references. Exact known-cell reads are outcome
// oracles only, never additional world knowledge exposed to the controller.
internal sealed partial class ToolAgentScenario {
 Point3 matrix42InteractCell;
 int matrix42InteractFace,matrix42PaintSlot=-1,matrix42PaintValue;
 string Matrix42InteractTarget()=>Matrix40Block(matrix42InteractCell.X,matrix42InteractCell.Y,matrix42InteractCell.Z);
 void Matrix42PlaceInteractFixture(int value,string name,bool wallSupport=false){
  Vector3 forward=p.ComponentBody.Matrix.Forward;forward.Y=0;var front=p.ComponentBody.Position+2*forward;
  matrix42InteractCell=new(Terrain.ToCell(front.X),Terrain.ToCell(p.ComponentBody.Position.Y)+1,Terrain.ToCell(front.Z));
  matrix42InteractFace=CellFace.Vector3ToFace(-forward,3);
  if(wallSupport){var normal=CellFace.FaceToPoint3(matrix42InteractFace);Matrix40Set(matrix42InteractCell.X-normal.X,matrix42InteractCell.Y-normal.Y,matrix42InteractCell.Z-normal.Z,DirtBlock.Index);}
  Matrix40Set(matrix42InteractCell.X,matrix42InteractCell.Y,matrix42InteractCell.Z,value);
  ToolAgentGate.Log(new{kind="matrix42_interact_fixture",fixture=name,artificial=true,worldEdited=true,cell=new[]{matrix42InteractCell.X,matrix42InteractCell.Y,matrix42InteractCell.Z},value,wallSupport,modelApi=false});
 }
 void Matrix42EnqueueInteract(){
  Matrix40Enqueue("act_SetPosture",()=>new{posture="standing"});
  Matrix40Enqueue("act_FaceTowards",()=>new{bearing=0,pitch=0,timeout=4},()=>{
   Matrix42PlaceInteractFixture(PlanksBlock.Index,"unpainted_plank");
   int limit=Math.Min(p.ComponentMiner.Inventory.VisibleSlotsCount,p.ComponentMiner.Inventory.SlotsCount);
   matrix42PaintSlot=Matrix40Read("read_inventory",new{}).GetProperty("slots").EnumerateArray().Where(s=>s.GetProperty("slot").GetInt32()<limit&&s.GetProperty("count").GetInt32()==0).Select(s=>s.GetProperty("slot").GetInt32()).Last();
   matrix42PaintValue=Terrain.MakeBlockValue(PaintBucketBlock.Index,0,PaintBucketBlock.SetColor(0,3));p.ComponentMiner.Inventory.AddSlotItems(matrix42PaintSlot,matrix42PaintValue,1);
   ToolAgentGate.Log(new{kind="matrix42_interact_fixture",fixture="paint_bucket_color_3",artificial=true,inventoryInjected=true,slot=matrix42PaintSlot,value=matrix42PaintValue,count=1,modelApi=false});
  });
  Matrix40Enqueue("act_UseItem",()=>new{slot=matrix42PaintSlot,value=matrix42PaintValue,target=Matrix42InteractTarget()},()=>{
   int value=p.Project.FindSubsystem<SubsystemTerrain>(true).Terrain.GetCellValue(matrix42InteractCell.X,matrix42InteractCell.Y,matrix42InteractCell.Z);
   Matrix40Check(Terrain.ExtractContents(value)==PlanksBlock.Index&&PaintedCubeBlock.GetColor(Terrain.ExtractData(value))==3,"interact42_use_item_native_painted_plank_color_3");
  });
  Matrix40Enqueue("act_Wait",()=>new{duration=.1},()=>{
   Vector3 forward=p.ComponentBody.Matrix.Forward;forward.Y=0;int face=CellFace.Vector3ToFace(-forward,3);
   int value=Terrain.MakeBlockValue(SwitchBlock.Index,0,SwitchBlock.SetVoltageLevel(face<<1,15));
   Matrix42PlaceInteractFixture(SwitchBlock.SetLeverState(value,false),"supported_switch_off",true);
  });
  Matrix40Enqueue("read_block_settings",()=>new{target=Matrix42InteractTarget()},()=>Matrix40Check(!Matrix40Read("read_block_settings",new{target=Matrix42InteractTarget()}).GetProperty("settings").GetProperty("isOn").GetBoolean(),"interact42_switch_initial_off"));
  Matrix40Enqueue("act_Interact",()=>new{target=Matrix42InteractTarget()},()=>{
   int value=p.Project.FindSubsystem<SubsystemTerrain>(true).Terrain.GetCellValue(matrix42InteractCell.X,matrix42InteractCell.Y,matrix42InteractCell.Z);
   Matrix40Check(Terrain.ExtractContents(value)==SwitchBlock.Index&&SwitchBlock.GetLeverState(value),"interact42_native_switch_toggled_on");
  });
  Matrix40Enqueue("read_block_settings",()=>new{target=Matrix42InteractTarget()},()=>Matrix40Check(Matrix40Read("read_block_settings",new{target=Matrix42InteractTarget()}).GetProperty("settings").GetProperty("isOn").GetBoolean(),"interact42_public_switch_read_on"));
  Matrix40Enqueue("act_Wait",()=>new{duration=.1},()=>{
   Vector3 forward=p.ComponentBody.Matrix.Forward;forward.Y=0;int face=CellFace.Vector3ToFace(-forward,3);
   Matrix42PlaceInteractFixture(Terrain.MakeBlockValue(WoodenAttachedSignBlock.Index,0,AttachedSignBlock.SetFace(0,face)),"supported_empty_sign",true);
  });
  Matrix40Enqueue("read_sign",()=>new{target=Matrix42InteractTarget()},()=>{
   var sign=Matrix40Read("read_sign",new{target=Matrix42InteractTarget()});Matrix40Check(sign.ValueKind==JsonValueKind.Object&&sign.TryGetProperty("lines",out _),"interact42_native_sign_readable_before");
  });
  Matrix40Enqueue("act_ConfigureBlock",()=>new{target=Matrix42InteractTarget(),settings=new{lines=new[]{"营地公告","先采集再建造"},colors=new[]{0,1,6,7},url=""}});
  Matrix40Enqueue("read_sign",()=>new{target=Matrix42InteractTarget()},()=>{
   var sign=Matrix40Read("read_sign",new{target=Matrix42InteractTarget()});var lines=sign.GetProperty("lines");
   Matrix40Check(lines.GetArrayLength()==4&&lines[0].GetString()=="营地公告"&&lines[1].GetString()=="先采集再建造"&&lines[2].GetString()==""&&lines[3].GetString()==""&&sign.GetProperty("url").GetString()=="","interact42_native_sign_text_persisted_no_url");
   // Known-cell oracle verifies the exact palette values; it supplies no extra
   // target or world knowledge to the actor. Public read independently proves
   // the same current sign is readable with four persisted colors.
   var colors=sign.GetProperty("colors");var stored=p.Project.FindSubsystem<SubsystemSignBlockBehavior>(true).GetSignData(matrix42InteractCell);
   var expected=new[]{new Color(0,0,0),new Color(140,0,0),new Color(160,112,0),new Color(180,180,180)};
   Matrix40Check(colors.GetArrayLength()==4&&stored!=null&&stored.Colors.SequenceEqual(expected),"interact42_native_sign_exact_palette_persisted");
  });
 }
}
