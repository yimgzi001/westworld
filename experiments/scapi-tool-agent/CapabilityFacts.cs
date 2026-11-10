using System.Text.Json;
using Capability39;
using Capability39.SocialTasks;
using Westworld.Decisions.Local;

// Public native result adapter only. Caller must assign scope when collecting on the game
// thread, not relabel cached results as a new capture. This class never queries/drains inboxes.
public sealed record CapabilityRead(SocialCaptureId Scope,string Tool,JsonElement Arguments,JsonElement Data,bool Success=true);
public sealed record CapabilityFactsBundle(BodyCapture Body,ProductionFacts Production,SocialTaskFacts? Social,
 IReadOnlyDictionary<string,bool?> ActorSupport,IReadOnlyList<string> Missing);
public static class CapabilityFacts {
 public static CapabilityFactsBundle Build(SocialCaptureId scope,IReadOnlyList<CapabilityRead> reads,
  IReadOnlyList<string>? currentNativeToolNames=null,bool? ownerIdle=null,IReadOnlyList<ToolOffer>? currentActions=null){
  scope.Validate();
  if(reads.Count>64||reads.Any(r=>r.Scope!=scope))throw new ArgumentException("same_capture_public_reads_required");
  if(currentNativeToolNames?.Count>128||currentNativeToolNames?.Distinct().Count()!=currentNativeToolNames?.Count)throw new ArgumentException("invalid_native_capability_snapshot");
  var good=reads.Where(r=>r.Success).ToArray();
  // Singletons cannot silently select a newer result within a supposedly immutable capture.
  JsonElement? Single(string tool){var a=good.Where(r=>r.Tool==tool).ToArray();if(a.Length>1)throw new ArgumentException("duplicate_capture_read:"+tool);return a.Length==1?a[0].Data:null;}
  var missing=new List<string>();
  var obs=Single("observe");var inv=Single("read_inventory");var equipment=Single("read_equipment");var inbox=Single("read_social");
  var support=new Dictionary<string,bool?>(StringComparer.Ordinal);
  if(currentNativeToolNames!=null)foreach(var name in currentNativeToolNames)support[name]=true;
  bool? Supports(string name)=>currentNativeToolNames is null?null:currentNativeToolNames.Contains(name);
  // Record queried-but-unsupported tools explicitly; never infer capability from an empty query result.
  foreach(var name in BodyNames.Concat(ProductionFamily.Names).Concat(SocialNames))support[name]=Supports(name);
  bool? Group(params string[] names){var s=names.Select(Supports).ToArray();return s.All(x=>x==true)?true:s.All(x=>x==false)?false:null;}
  var targets=new Dictionary<string,ProductionTargetKind>(StringComparer.Ordinal);var locations=new HashSet<string>();var entities=new HashSet<string>();
  void Target(JsonElement item,ProductionTargetKind kind){string reference=Text(item,"reference");if(targets.TryGetValue(reference,out var old)&&old!=kind)throw new ArgumentException("conflicting_target_kind");targets[reference]=kind;locations.Add(reference);if(kind==ProductionTargetKind.Entity)entities.Add(reference);}
  if(obs is {} o){
   foreach(var b in Items(o,"visibleBlocks"))Target(b,ProductionTargetKind.Block);
   foreach(var e in Items(o,"entities"))Target(e,ProductionTargetKind.Entity);
   foreach(var p in Items(o,"pickables"))locations.Add(Text(p,"reference"));
   if(o.TryGetProperty("focus",out var f)&&f.ValueKind==JsonValueKind.Object&&f.TryGetProperty("reference",out _)){
    string k=f.GetProperty("kind").ToString();Target(f,k switch{"block" or "Block" or "0"=>ProductionTargetKind.Block,"movingBlocks" or "MovingBlocks" or "1"=>ProductionTargetKind.MovingBlock,"entity" or "Entity" or "2"=>ProductionTargetKind.Entity,_=>throw new ArgumentException("unknown_focus_kind")});
   }
  }else missing.Add("observe_not_read");
  if(targets.Count>128||locations.Count>128)throw new ArgumentException("current_reference_budget_exceeded");
  OwnSlot[]? slots=null;
  if(inv is {} iv&&iv.ValueKind==JsonValueKind.Object){slots=Array(iv.GetProperty("slots"),128).Select(s=>new OwnSlot(Int(s,"slot"),Int(s,"value"),Int(s,"count"))).ToArray();
   if(slots.Select(s=>s.Slot).Distinct().Count()!=slots.Length||slots.Any(s=>s.Slot<0||s.Slot>4095||s.Value<0||s.Count<0||s.Count>0&&s.Value==0))throw new ArgumentException("invalid_inventory");
  }else missing.Add("inventory_unknown");
  WornSlot[]? worn=null;
  if(equipment is {} eq&&eq.ValueKind==JsonValueKind.Array){worn=Array(eq,128).Select(e=>new WornSlot(Text(e,"slot"),Int(e,"layer"),Int(e,"value"))).ToArray();
   if(worn.Any(w=>w.Layer<0||w.Value<=0)||worn.Select(w=>(w.Slot,w.Layer)).Distinct().Count()!=worn.Length)throw new ArgumentException("invalid_equipment");
  }else missing.Add("equipment_unknown");
  bool? mounted=null;if(obs is {} mo&&mo.TryGetProperty("self",out var self)&&self.ValueKind==JsonValueKind.Object&&self.TryGetProperty("isMounted",out var m)&&m.ValueKind is JsonValueKind.True or JsonValueKind.False)mounted=m.GetBoolean();
  var remembered=new HashSet<string>();
  foreach(var read in good.Where(r=>r.Tool=="read_map")){
   // Map refs are navigation-only, never live action targets.
   var entries=read.Data.ValueKind==JsonValueKind.Array?Array(read.Data,128):Items(read.Data,"entries");
   foreach(var item in entries)remembered.Add(Text(item,"reference"));
  }
  if(remembered.Count>128)throw new ArgumentException("memory_reference_budget_exceeded");
  var body=new BodyCapture(scope.Session,scope.Actor,scope.Capture,Group("act_MoveTo","act_FaceTowards","act_SetPosture","act_Jump","act_ClimbTo"),Group("act_Mount","act_Dismount","act_Steer"),Group("act_Equip","act_Unequip"),mounted,targets.Where(t=>t.Value==ProductionTargetKind.Block).Select(t=>t.Key).ToArray(),entities.ToArray(),locations.ToArray(),remembered.ToArray(),slots,worn,support);
  var settings=new List<ProductionSettings>();var crafts=new List<ProductionCraftPreview>();
  foreach(var read in good){
   if(read.Tool is "read_block_settings" or "read_item_settings"&&read.Data.ValueKind==JsonValueKind.Object){
    string type=Text(read.Data,"type");if(read.Tool=="read_block_settings"){
     string target=Text(read.Arguments,"target");if(!targets.TryGetValue(target,out var kind)||kind!=ProductionTargetKind.Block)throw new ArgumentException("settings_target_not_current_block");settings.Add(new(target,null,null,type));
    }else {int slot=Int(read.Arguments,"slot"),value=Int(read.Arguments,"value");if(slots is null||!slots.Any(s=>s.Slot==slot&&s.Value==value&&s.Count>0))throw new ArgumentException("settings_item_not_current_owned_pair");settings.Add(new(null,slot,value,type));}
   }
   if(read.Tool=="read_storage"&&read.Data.ValueKind==JsonValueKind.Object&&read.Data.TryGetProperty("crafting",out var c)&&c.ValueKind==JsonValueKind.Object&&c.TryGetProperty("recipe",out var recipe)&&recipe.ValueKind==JsonValueKind.Object){
    // Native act_Craft count is output items; preserve recipe output multiplicity.
    string workspace=Text(read.Arguments,"storage");int value=Int(recipe,"resultValue"),batches=Int(c,"materialBatches");if(value<=0||batches<0)throw new ArgumentException("invalid_crafting_preview");int resultCount=Int(recipe,"resultCount");if(resultCount<=0)throw new ArgumentException("invalid_recipe_count");crafts.Add(new(workspace,value,checked(batches*resultCount),resultCount,resultCount));
   }
  }
  var production=new ProductionFacts(new(scope.Session,scope.Actor,scope.Capture),targets.Select(t=>new ProductionTarget(t.Key,t.Value)).ToArray(),slots?.Select(s=>new ProductionSlot(s.Slot,s.Value,s.Count)).ToArray()??[],settings,crafts);
  SocialTaskFacts? social=null;
  if(obs is {} so&&so.TryGetProperty("gameTime",out var gt)&&gt.TryGetDouble(out double now)&&double.IsFinite(now)&&now>=0){
   var received=new List<ReceivedSocialOffer>();
   if(inbox is {} si&&si.ValueKind==JsonValueKind.Array)foreach(var item in Array(si,64)){
    string kind=item.GetProperty("kind").ToString();if(kind is not("ItemOffered" or "itemOffered" or "1"))continue;
    if(!item.TryGetProperty("offerReference",out var offer)||offer.ValueKind!=JsonValueKind.String)continue;
    string reference=Text(item,"offerReference");double at=item.GetProperty("gameTime").GetDouble();
    received.Add(new(scope,scope.Actor,reference,at,false,item.TryGetProperty("source",out var src)&&src.ValueKind==JsonValueKind.String?src.GetString():null));
   }
   bool? visibleKnown=so.TryGetProperty("entities",out var visibleArray)&&visibleArray.ValueKind==JsonValueKind.Array?true:null;
   social=new(scope,now,ownerIdle,visibleKnown,slots is null?null:true,inbox?.ValueKind==JsonValueKind.Array?true:null,
    entities.Select(r=>new VisibleSocialEntity(scope,r,false)),slots?.Select(s=>new OwnSocialItem(scope,s.Slot,s.Value,s.Count))??[],received,(currentActions??[]).Select(a=>new CurrentActionSchema(scope,a)),support);
  }else missing.Add("social_native_game_time_unknown");
  if(inbox is null)missing.Add("social_inbox_not_read_or_retained");if(currentNativeToolNames is null)missing.Add("actor_capabilities_unknown");
  return new(body,production,social,support,missing);
 }
 static readonly string[] BodyNames=["act_MoveTo","act_FaceTowards","act_SetPosture","act_Jump","act_ClimbTo","act_Mount","act_Dismount","act_Steer","read_equipment","act_Equip","act_Unequip"];
 static readonly string[] SocialNames=["read_social","act_Speak","act_OfferItem","act_RespondToOffer","submit_plan"];
 static string Text(JsonElement o,string k){var p=o.GetProperty(k);if(p.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(p.GetString())||p.GetString()!.Length>160)throw new ArgumentException("invalid_reference_or_type:"+k);return p.GetString()!;}
 static int Int(JsonElement o,string k)=>o.GetProperty(k).GetInt32();
 static JsonElement[] Items(JsonElement o,string k,int max=128)=>o.ValueKind==JsonValueKind.Object&&o.TryGetProperty(k,out var a)?Array(a,max):[];
 static JsonElement[] Array(JsonElement a,int max){if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()>max)throw new ArgumentException("invalid_or_unbounded_array");return a.EnumerateArray().ToArray();}
}
