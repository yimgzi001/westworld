using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Westworld.Runtime.Core;
namespace Westworld.Decisions.Local;

public sealed record LocalInferenceMetrics(double WallSeconds,long PeakSampledRssBytes,double SampledCpuSeconds,int? PromptTokens,int? BackendGenerationEvaluations,double? PromptMilliseconds,double? GenerationMilliseconds,string Response);
// Transitional, finite process-per-request integration. Not a persistent shared model pool.
// No shell, network, generated script execution or native game objects.
public sealed class LlamaProcessProvider:IDecisionProvider {
 readonly string executable,model;readonly Func<int,bool> supportedHarvest;readonly TimeSpan timeout;
 public LocalInferenceMetrics? LastMetrics{get;private set;}
 public LlamaProcessProvider(string executable,string model,Func<int,bool> supportedHarvest,TimeSpan? timeout=null){
  this.executable=Path.GetFullPath(executable);this.model=Path.GetFullPath(model);this.supportedHarvest=supportedHarvest;
  this.timeout=timeout??TimeSpan.FromSeconds(60);if(this.timeout<=TimeSpan.Zero||this.timeout>TimeSpan.FromSeconds(120))throw new ArgumentOutOfRangeException(nameof(timeout));if(!File.Exists(this.executable)||!File.Exists(this.model))throw new FileNotFoundException("Verified local model/runtime required");
 }
 public async Task<HighLevelDecision> DecideAsync(ActorObservation observation,CancellationToken cancellation){
  var prompt=LocalDecisionPrompt.Create(observation,supportedHarvest);
  var start=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
  string schema="{\"type\":\"object\",\"properties\":{\"skill\":{\"type\":\"string\",\"enum\":[\"ForageOne\",\"EatOne\",\"Wait\"]},\"candidate_index\":{\"type\":\"integer\"}},\"required\":[\"skill\",\"candidate_index\"],\"additionalProperties\":false}";
  foreach(var arg in new[]{"-m",model,"-c","2048","-t","4","-tb","4","-ngl","0","-n","96","--reasoning","off","--single-turn","--simple-io","--no-display-prompt","--temp","0","--seed","42","--json-schema",schema,"-p",prompt.Prompt})start.ArgumentList.Add(arg);
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
   var decision=prompt.ParseResponse(response);
   var input=Regex.Match(stderr,@"prompt eval time\s*=\s*([\d.]+) ms /\s*(\d+) tokens");
   var generated=Regex.Match(stderr,@"(?m)^.*(?<!prompt )eval time\s*=\s*([\d.]+) ms /\s*(\d+) runs");
   double? Ms(Match m)=>m.Success?double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture):null;int? Count(Match m)=>m.Success?int.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture):null;
   LastMetrics=new(watch.Elapsed.TotalSeconds,peak,cpu,Count(input),Count(generated),Ms(input),Ms(generated),response);return decision;
  }catch{stop.Cancel();if(!process.HasExited){process.Kill(entireProcessTree:true);try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}catch{}}try{await Task.WhenAll(output,errors).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}catch{}throw;}
 }
}
