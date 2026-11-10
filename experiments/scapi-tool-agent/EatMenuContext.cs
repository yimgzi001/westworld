using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Disclosure only: never changes observed inventory, tool selection or world state.
public static class EatMenuContext {
 public static void VerifyTransportBudget(string prompt,string schema){
  if(string.IsNullOrEmpty(prompt)||string.IsNullOrEmpty(schema)||prompt.Length>32768||schema.Length>32768)throw new InvalidOperationException("Complete tool prompt/schema exceeds existing official transport character budget; no silent tool truncation");
 }
 public static JsonElement Attach(JsonElement context,JsonElement coverage,int maxChars=14000,int maxUtf8Bytes=16000){
  if(maxChars is <256 or >14000||maxUtf8Bytes is <256 or >16000)throw new ArgumentOutOfRangeException("Existing context budget may not be expanded");
  if(context.ValueKind!=JsonValueKind.Object||coverage.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Context and explicit coverage required");
  var root=JsonNode.Parse(context.GetRawText())!.AsObject();
  if(root["projectionStatus"]?.GetValue<string>()!="projected"||root["facts"] is not JsonObject facts)throw new ArgumentException("Final projected facts required");
  facts["eatMenuCoverage"]=JsonNode.Parse(coverage.GetRawText());
  var result=JsonSerializer.SerializeToElement(root);string raw=result.GetRawText();
  if(raw.Length>maxChars||Encoding.UTF8.GetByteCount(raw)>maxUtf8Bytes)throw new InvalidOperationException("Eat menu disclosure exceeds existing context budget; no partial facts or expanded budget returned");
  return result;
 }
}
