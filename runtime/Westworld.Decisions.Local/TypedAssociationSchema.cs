using System.Text.Json;
namespace Capability39;
/// <summary>Conservative structural subset proof, not proof of native semantic applicability.
/// An untyped native object MUST first acquire an audited native type template; never infer settings keys.</summary>
public static class TypedAssociationSchema {
 static readonly HashSet<string> Keys=new(StringComparer.Ordinal){"type","properties","required","additionalProperties","items","minItems","maxItems","minLength","maxLength","minimum","maximum","enum","const","oneOf","description"};
 public static JsonElement Build(JsonElement effectiveNativeSchema,IReadOnlyList<JsonElement> closedVariants){
  if(closedVariants is null||closedVariants.Count is <1 or >32)throw new ArgumentException("Bounded variants required");
  foreach(var v in closedVariants)if(!IsSubset(effectiveNativeSchema,v))throw new ArgumentException("Variant not a proved native schema subset");
  // Overlapping alternatives are allowed structurally but oneOf rejects ambiguous concrete calls.
  return JsonSerializer.SerializeToElement(new{oneOf=closedVariants});
 }
 public static bool IsSubset(JsonElement original,JsonElement candidate){try{return Unique(original)&&Unique(candidate)&&Sub(original,candidate,0);}catch(Exception e)when(e is InvalidOperationException or FormatException or KeyNotFoundException or OverflowException){return false;}}
 static bool Unique(JsonElement x){if(x.ValueKind==JsonValueKind.Object)return x.EnumerateObject().Select(p=>p.Name).Distinct().Count()==x.EnumerateObject().Count()&&x.EnumerateObject().All(p=>Unique(p.Value));if(x.ValueKind==JsonValueKind.Array)return x.EnumerateArray().All(Unique);return true;}
 static bool Shape(JsonElement x)=>x.ValueKind==JsonValueKind.Object&&x.EnumerateObject().Select(p=>p.Name).Distinct().Count()==x.EnumerateObject().Count()&&x.EnumerateObject().All(p=>Keys.Contains(p.Name));
 static bool Sub(JsonElement b,JsonElement c,int depth){
  if(depth>8||!Shape(b)||!Shape(c))return false;
  if(c.TryGetProperty("oneOf",out var cb))return c.EnumerateObject().All(p=>p.Name is "oneOf" or "description")&&cb.ValueKind==JsonValueKind.Array&&cb.GetArrayLength() is >=1 and <=32&&cb.EnumerateArray().All(x=>Sub(b,x,depth+1));
  if(b.TryGetProperty("oneOf",out var bb))return b.EnumerateObject().All(p=>p.Name is "oneOf" or "description")&&bb.ValueKind==JsonValueKind.Array&&bb.EnumerateArray().Any(x=>Sub(x,c,depth+1));
  if(!b.TryGetProperty("type",out var bt)||!c.TryGetProperty("type",out var ct)||bt.GetString()!=ct.GetString())return false;
  string type=ct.GetString()!;
  if(type is "object" or "array"&&(b.TryGetProperty("const",out _)||b.TryGetProperty("enum",out _)||c.TryGetProperty("const",out _)||c.TryGetProperty("enum",out _)))return false;
  if(type=="object"){
   if(!b.TryGetProperty("properties",out var bp)||bp.ValueKind!=JsonValueKind.Object||!c.TryGetProperty("properties",out var cp)||cp.ValueKind!=JsonValueKind.Object||!c.TryGetProperty("additionalProperties",out var extra)||extra.ValueKind!=JsonValueKind.False)return false;
   var req=c.TryGetProperty("required",out var cr)?cr.EnumerateArray().Select(v=>v.GetString()!).ToArray():Array.Empty<string>();
   if(req.Distinct().Count()!=req.Length||req.Any(x=>!cp.TryGetProperty(x,out _)))return false;
   if(b.TryGetProperty("required",out var br)&&br.EnumerateArray().Any(x=>!req.Contains(x.GetString())))return false;
   return cp.EnumerateObject().All(p=>bp.TryGetProperty(p.Name,out var old)&&Sub(old,p.Value,depth+1));
  }
  if(type=="array")return BoundSubset(b,c,"minItems",true)&&BoundSubset(b,c,"maxItems",false)&&c.TryGetProperty("maxItems",out _)&&b.TryGetProperty("items",out var bi)&&c.TryGetProperty("items",out var ci)&&Sub(bi,ci,depth+1);
  if(type is not("string" or "integer" or "number" or "boolean"))return false;
  if(c.TryGetProperty("const",out var constant))return AcceptsScalar(b,constant)&&AcceptsScalar(c,constant);
  if(c.TryGetProperty("enum",out var ce))return ce.ValueKind==JsonValueKind.Array&&ce.GetArrayLength() is >=1 and <=128&&ce.EnumerateArray().All(v=>AcceptsScalar(b,v)&&AcceptsScalar(c,v));
  if(b.TryGetProperty("const",out _)||b.TryGetProperty("enum",out _))return false;
  return type=="string"?BoundSubset(b,c,"minLength",true)&&BoundSubset(b,c,"maxLength",false)&&c.TryGetProperty("maxLength",out _):type=="boolean"||BoundSubset(b,c,"minimum",true)&&BoundSubset(b,c,"maximum",false)&&c.TryGetProperty("minimum",out _)&&c.TryGetProperty("maximum",out _);
 }
 static bool BoundSubset(JsonElement b,JsonElement c,string name,bool lower){
  if(!c.TryGetProperty(name,out var cv))return !b.TryGetProperty(name,out _);
  if(!cv.TryGetDouble(out var cn)||!double.IsFinite(cn))return false;
  if(!b.TryGetProperty(name,out var bv))return true;
  if(!bv.TryGetDouble(out var bn)||!double.IsFinite(bn))return false;return lower?cn>=bn:cn<=bn;
 }
 static bool AcceptsScalar(JsonElement s,JsonElement value){
  if(s.TryGetProperty("const",out var con)&&!JsonElement.DeepEquals(con,value))return false;
  if(s.TryGetProperty("enum",out var en)&&(en.ValueKind!=JsonValueKind.Array||!en.EnumerateArray().Any(v=>JsonElement.DeepEquals(v,value))))return false;
  bool Range(double v,string low,string high)=>(!s.TryGetProperty(low,out var l)||v>=l.GetDouble())&&(!s.TryGetProperty(high,out var h)||v<=h.GetDouble());
  return s.GetProperty("type").GetString() switch{
   "integer"=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out var i)&&Range(i,"minimum","maximum"),
   "number"=>value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var d)&&double.IsFinite(d)&&Range(d,"minimum","maximum"),
   "string"=>value.ValueKind==JsonValueKind.String&&Range(value.GetString()!.Length,"minLength","maxLength"),
   "boolean"=>value.ValueKind is JsonValueKind.True or JsonValueKind.False,_=>false};
 }
}
