using System.Text.Json;
namespace Westworld.Scapi.Adapter;

/// <summary>Executable offers and explicitly non-executable facts are sent separately. JSON is selection, never code.
/// Parsing binds to the immutable plan; execution must revalidate live native state.</summary>
public sealed class FoodSituationPrompt {
 readonly FoodSituationPlan plan;
 readonly HashSet<Guid> offeredIds;
 public int OmittedExecutableCount {get;}
 public int OmittedDiagnosticCount {get;}
 public string Prompt {get;}
 public string ResponseSchema {get;}
 public FoodSituationPrompt(FoodSituationPlan plan){
  ArgumentNullException.ThrowIfNull(plan);this.plan=plan;
  // Round-robin across kinds keeps Search/other alternatives when inventory has many slots.
  var groups=plan.ExecutableCandidates.GroupBy(c=>c.Kind).Select(g=>g.ToArray()).ToArray();
  var selected=Enumerable.Range(0,groups.Select(g=>g.Length).DefaultIfEmpty(0).Max())
   .SelectMany(i=>groups.Where(g=>i<g.Length).Select(g=>g[i])).Take(16).ToList();
  var diagnostics=plan.Candidates.Where(c=>!c.Executable).Take(8).ToList();
  int diagnosticCount=plan.Candidates.Count-plan.ExecutableCandidates.Count;
  const string instruction="Choose your next food-related action from executable_offers using observed facts and current needs. Food is fullness (0 hungry, 1 full). Choose eating, acquisition, search or wait yourself. Search only explores locally, never knows hidden food. Diagnostic facts are known resources/preparation gaps with unavailable actions, not absent world resources, and cannot be selected. Observed target is not proof of reachability. Return exactly one JSON object with candidate_id equal to a listed ID or wait. No invented objects, coordinates, code or extra fields. Omitted offers/facts exist but are not shown; omission never proves absence.\n";
  string Short(string text,int limit)=>text.Length<=limit?text:text[..limit];
  string Render()=>instruction+JsonSerializer.Serialize(new{
   health=plan.Health,food=plan.Food,
   executable_offers=selected.Select(c=>new{candidate_id=c.Id.ToString("D"),strategy=c.Kind.ToString(),risk=Short(c.Risk,72),slot=c.Slot,item=c.ExpectedValue}),
   diagnostic_facts=diagnostics.Select(c=>new{strategy=c.Kind.ToString(),executable=false,reason=Short(c.Reason,64),risk=Short(c.Risk,72),recipe=c.RecipeId is null?null:Short(c.RecipeId,48),missing_materials=c.MissingMaterials.Take(4),omitted_materials=Math.Max(0,c.MissingMaterials.Count-4)}),
   omitted_executable_count=plan.ExecutableCandidates.Count-selected.Count,omitted_diagnostic_count=diagnosticCount-diagnostics.Count
  });
  string rendered=Render();
  // Conservative character budget, not a claim about tokenizer-specific token counts.
  while(rendered.Length>5600&&diagnostics.Count>0){diagnostics.RemoveAt(diagnostics.Count-1);rendered=Render();}
  while(rendered.Length>5600&&selected.Count>1){selected.RemoveAt(selected.Count-1);rendered=Render();}
  if(rendered.Length>5600)throw new ArgumentException("Food prompt budget exceeded");
  Prompt=rendered;offeredIds=selected.Select(c=>c.Id).ToHashSet();
  OmittedExecutableCount=plan.ExecutableCandidates.Count-selected.Count;OmittedDiagnosticCount=diagnosticCount-diagnostics.Count;
  ResponseSchema=JsonSerializer.Serialize(new{type="object",properties=new{candidate_id=new{type="string",@enum=selected.Select(c=>c.Id.ToString("D")).Append("wait").ToArray()}},required=new[]{"candidate_id"},additionalProperties=false});
 }
 public FoodStrategyCandidate? ParseResponse(string response){
  if(string.IsNullOrWhiteSpace(response)||response.Length>4096)throw new FormatException("Invalid response size");
  using var json=JsonDocument.Parse(response,new JsonDocumentOptions{MaxDepth=3});var root=json.RootElement;
  if(root.ValueKind!=JsonValueKind.Object)throw new FormatException("Object required");
  var fields=root.EnumerateObject().ToArray();
  if(fields.Length!=1||fields[0].Name!="candidate_id"||fields[0].Value.ValueKind!=JsonValueKind.String)throw new FormatException("Exactly candidate_id required");
  // This is a planning selection only. Slow inference never refreshes old evidence.
  // Host must acquire a new observation and match/revalidate before native execution.
  string? id=fields[0].Value.GetString();if(id=="wait")return null;
  if(!Guid.TryParseExact(id,"D",out var candidateId))throw new FormatException("Invalid candidate ID");
  if(!offeredIds.Contains(candidateId))throw new ArgumentException("Candidate was not offered within prompt budget");
  return plan.Resolve(candidateId,plan.SessionId,plan.ActorId,plan.ObservationId,plan.InventoryVersion,plan.ObservedAt);
 }
}
