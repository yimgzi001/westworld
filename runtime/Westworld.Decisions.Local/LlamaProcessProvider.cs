using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Westworld.Runtime.Core;
namespace Westworld.Decisions.Local;

public sealed record LocalInferenceMetrics(double WallSeconds,long PeakSampledRssBytes,double SampledCpuSeconds,int? PromptTokens,int? BackendGenerationEvaluations,double? PromptMilliseconds,double? GenerationMilliseconds,string Response);
// Transitional, finite process-per-request integration. Not a persistent shared model pool.
// No shell, network, generated script execution or native game objects.
public sealed class LlamaProcessProvider:IDecisionProvider {
 int inFlight;readonly int contextTokens,maxOutputTokens;readonly string executable,model;readonly Func<int,bool> supportedHarvest;readonly TimeSpan timeout;readonly bool allowSurvey;
 public LocalInferenceMetrics? LastMetrics{get;private set;}
 public string? LastFailure{get;private set;}
 public string? LastResponse{get;private set;}
 public LlamaProcessProvider(string executable,string model,Func<int,bool> supportedHarvest,TimeSpan? timeout=null,bool allowSurvey=false,int contextTokens=2048,int maxOutputTokens=96){
  this.allowSurvey=allowSurvey;
  if(contextTokens<2048||contextTokens>8192||maxOutputTokens<64||maxOutputTokens>512)throw new ArgumentOutOfRangeException(nameof(contextTokens));
  this.contextTokens=contextTokens;this.maxOutputTokens=maxOutputTokens;
  this.executable=Path.GetFullPath(executable);this.model=Path.GetFullPath(model);this.supportedHarvest=supportedHarvest;
  this.timeout=timeout??TimeSpan.FromSeconds(60);if(this.timeout<=TimeSpan.Zero||this.timeout>TimeSpan.FromSeconds(120))throw new ArgumentOutOfRangeException(nameof(timeout));if(!File.Exists(this.executable)||!File.Exists(this.model))throw new FileNotFoundException("Verified local model/runtime required");
 }
 public Task<HighLevelDecision> DecideAsync(ActorObservation observation,CancellationToken cancellation){
  var prompt=LocalDecisionPrompt.Create(observation,supportedHarvest,allowSurvey:allowSurvey);
  return CompleteStructuredAsync(prompt.Prompt,prompt.ResponseSchema,prompt.ParseResponse,cancellation);
 }
 // A host-owned immutable capability prompt and parser. Still one bounded local
 // process, no generated code execution. Callers must serialize requests per instance.
 public async Task<T> CompleteStructuredAsync<T>(string promptText,string responseSchema,Func<string,T> parse,CancellationToken cancellation){
  if(Interlocked.CompareExchange(ref inFlight,1,0)!=0)throw new InvalidOperationException("One local inference per provider instance");
  LastFailure=null;LastResponse=null;LastMetrics=null;
  try{return await CompleteCoreAsync(promptText,responseSchema,parse,cancellation).ConfigureAwait(false);}
  catch(Exception e){LastFailure=e.GetType().Name+": "+(e.Message.Length<=256?e.Message:e.Message[..256]);throw;}
  finally{Volatile.Write(ref inFlight,0);}
 }
 async Task<T> CompleteCoreAsync<T>(string promptText,string responseSchema,Func<string,T> parse,CancellationToken cancellation){
  ArgumentNullException.ThrowIfNull(parse);
  if(string.IsNullOrWhiteSpace(promptText)||promptText.Length>32768)throw new ArgumentException("Bounded prompt required",nameof(promptText));
  if(string.IsNullOrWhiteSpace(responseSchema)||responseSchema.Length>32768)throw new ArgumentException("Bounded schema required",nameof(responseSchema));
  LastFailure=null;LastResponse=null;LastMetrics=null;
  var start=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
  string schema=responseSchema;
  foreach(var arg in new[]{"-m",model,"-c",contextTokens.ToString(CultureInfo.InvariantCulture),"-t","4","-tb","4","-ngl","0","-n",maxOutputTokens.ToString(CultureInfo.InvariantCulture),"--reasoning","off","--single-turn","--simple-io","--no-display-prompt","--temp","0","--seed","42","--json-schema",schema,"-p",promptText})start.ArgumentList.Add(arg);
  using var stop=CancellationTokenSource.CreateLinkedTokenSource(cancellation);stop.CancelAfter(timeout);
  using var process=new Process{StartInfo=start};var watch=Stopwatch.StartNew();if(!process.Start())throw new InvalidOperationException("Local inference did not start");
  var output=BoundedText.ReadAsync(process.StandardOutput,32768,stop.Token);var errors=BoundedText.ReadAsync(process.StandardError,262144,stop.Token);long peak=0;double cpu=0;
  try{
   var exit=process.WaitForExitAsync(stop.Token);
   while(!exit.IsCompleted){if(output.IsFaulted)await output.ConfigureAwait(false);if(errors.IsFaulted)await errors.ConfigureAwait(false);try{process.Refresh();peak=Math.Max(peak,process.WorkingSet64);cpu=Math.Max(cpu,process.TotalProcessorTime.TotalSeconds);}catch(InvalidOperationException){}await Task.WhenAny(exit,Task.Delay(100,stop.Token)).ConfigureAwait(false);stop.Token.ThrowIfCancellationRequested();}
   await exit.ConfigureAwait(false);
   string stdout=await output.ConfigureAwait(false),stderr=await errors.ConfigureAwait(false);
   if(process.ExitCode!=0)throw new InvalidOperationException("Local inference process failed");
   if(stdout.Length>32768||stderr.Length>262144)throw new InvalidOperationException("Unexpected local inference output size");
   // This verified runtime appends exactly this fixed marker, not an instruction or JSON wrapper.
   string response=stdout.Trim();const string end="[end of text]";if(response.EndsWith(end,StringComparison.Ordinal))response=response[..^end.Length].TrimEnd();
   LastResponse=response.Length<=4096?response:response[..4096];
   var input=Regex.Match(stderr,@"prompt eval time\s*=\s*([\d.]+) ms /\s*(\d+) tokens");
   var generated=Regex.Match(stderr,@"(?m)^.*(?<!prompt )eval time\s*=\s*([\d.]+) ms /\s*(\d+) runs");
   double? Ms(Match m)=>m.Success?double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture):null;int? Count(Match m)=>m.Success?int.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture):null;
   LastMetrics=new(watch.Elapsed.TotalSeconds,peak,cpu,Count(input),Count(generated),Ms(input),Ms(generated),response);return parse(response);
  }catch(Exception e){LastFailure=e.GetType().Name+": "+(e.Message.Length<=256?e.Message:e.Message[..256]);stop.Cancel();if(!process.HasExited){process.Kill(entireProcessTree:true);try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}catch{}}try{await Task.WhenAll(output,errors).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}catch{}throw;}
 }
}
