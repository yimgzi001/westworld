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
 public LocalInferenceAudit? LastAudit{get;private set;}
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
  LastFailure=null;LastResponse=null;LastMetrics=null;LastAudit=null;
  try{return await CompleteCoreAsync(promptText,responseSchema,parse,cancellation).ConfigureAwait(false);}
  catch(Exception e){LastFailure=e.GetType().Name+": "+(e.Message.Length<=256?e.Message:e.Message[..256]);throw;}
  finally{Volatile.Write(ref inFlight,0);}
 }
 async Task<T> CompleteCoreAsync<T>(string promptText,string responseSchema,Func<string,T> parse,CancellationToken cancellation){
  var watch=Stopwatch.StartNew();bool started=false,sampled=false;long peak=0;double cpu=0;int? exitCode=null;
  string outcome="preflight_failure";Task<string>? output=null,errors=null;Process? process=null;CancellationTokenSource? stop=null;
  void Sample(){if(process is null||!started)return;try{process.Refresh();long rss=process.WorkingSet64;double seconds=process.TotalProcessorTime.TotalSeconds;peak=Math.Max(peak,rss);cpu=Math.Max(cpu,seconds);sampled=true;}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
  try{
   ArgumentNullException.ThrowIfNull(parse);
   if(string.IsNullOrWhiteSpace(promptText)||promptText.Length>32768)throw new ArgumentException("Bounded prompt required",nameof(promptText));
   if(string.IsNullOrWhiteSpace(responseSchema)||responseSchema.Length>32768)throw new ArgumentException("Bounded schema required",nameof(responseSchema));
   cancellation.ThrowIfCancellationRequested();
   var start=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
   foreach(var arg in new[]{"-m",model,"-c",contextTokens.ToString(CultureInfo.InvariantCulture),"-t","4","-tb","4","-ngl","0","-n",maxOutputTokens.ToString(CultureInfo.InvariantCulture),"--reasoning","off","--single-turn","--simple-io","--no-display-prompt","--temp","0","--seed","42","--json-schema",responseSchema,"-p",promptText})start.ArgumentList.Add(arg);
   stop=CancellationTokenSource.CreateLinkedTokenSource(cancellation);stop.CancelAfter(timeout);
   process=new Process{StartInfo=start};outcome="start_failure";
   if(!process.Start())throw new InvalidOperationException("Local inference did not start");started=true;outcome="process_failure";
   output=BoundedText.ReadAsync(process.StandardOutput,32768,stop.Token);errors=BoundedText.ReadAsync(process.StandardError,262144,stop.Token);
   var exit=process.WaitForExitAsync(stop.Token);
   while(!exit.IsCompleted){if(output.IsFaulted)await output.ConfigureAwait(false);if(errors.IsFaulted)await errors.ConfigureAwait(false);Sample();await Task.WhenAny(exit,Task.Delay(100,stop.Token)).ConfigureAwait(false);stop.Token.ThrowIfCancellationRequested();}
   await exit.ConfigureAwait(false);Sample();exitCode=process.ExitCode;
   string stdout=await output.ConfigureAwait(false),stderr=await errors.ConfigureAwait(false);
   if(exitCode!=0){outcome="nonzero_exit";throw new InvalidOperationException("Local inference process failed");}
   string response=stdout.Trim();const string end="[end of text]";if(response.EndsWith(end,StringComparison.Ordinal))response=response[..^end.Length].TrimEnd();
   LastResponse=response.Length<=4096?response:response[..4096];
   var timing=LocalInferenceTiming.Parse(stderr);
   LastMetrics=new(watch.Elapsed.TotalSeconds,peak,cpu,timing.PromptTokens,timing.GenerationEvaluations,timing.PromptMilliseconds,timing.GenerationMilliseconds,response);
   outcome="parser_failure";T result=parse(response);outcome="succeeded";return result;
  }catch{
   if(cancellation.IsCancellationRequested)outcome=started?"canceled":"canceled_before_start";
   else if(stop?.IsCancellationRequested==true)outcome="timeout";
   Sample();
   stop?.Cancel();
   if(started&&process is not null){try{if(!process.HasExited)process.Kill(entireProcessTree:true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);exitCode=process.ExitCode;}catch{} }
   if(output is not null&&errors is not null)try{await Task.WhenAll(output,errors).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}catch{}
   throw;
  }finally{
   bool complete=errors?.IsCompletedSuccessfully==true;
   var timing=LocalInferenceTiming.Parse(complete?errors!.Result:null);
   LastAudit=new(started,outcome,watch.Elapsed.TotalSeconds,sampled?peak:null,sampled?cpu:null,exitCode,complete,timing.PromptTokens,timing.GenerationEvaluations,timing.PromptMilliseconds,timing.GenerationMilliseconds);
   process?.Dispose();stop?.Dispose();
  }
 }
}

// Numeric diagnostics only; no stderr, prompt, environment or credential retention.
public sealed record LocalInferenceAudit(bool Started,string Outcome,double WallSeconds,long? PeakSampledRssBytes,double? SampledCpuSeconds,int? ExitCode,bool CompleteStderr,int? PromptTokens,int? GenerationEvaluations,double? PromptMilliseconds,double? GenerationMilliseconds);
public sealed record LocalInferenceTiming(int? PromptTokens,int? GenerationEvaluations,double? PromptMilliseconds,double? GenerationMilliseconds){
 public static LocalInferenceTiming Parse(string? stderr){
  if(stderr is null||stderr.Length>262144)return new(null,null,null,null);
  var input=Regex.Match(stderr,@"prompt eval time\s*=\s*([\d.]+) ms /\s*(\d+) tokens");
  var generated=Regex.Match(stderr,@"(?m)^.*(?<!prompt )eval time\s*=\s*([\d.]+) ms /\s*(\d+) runs");
  double? Ms(Match m)=>m.Success&&double.TryParse(m.Groups[1].Value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n)&&n>=0?n:null;
  int? Count(Match m)=>m.Success&&int.TryParse(m.Groups[2].Value,NumberStyles.None,CultureInfo.InvariantCulture,out int n)?n:null;
  return new(Count(input),Count(generated),Ms(input),Ms(generated));
 }
}
