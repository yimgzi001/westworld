using Westworld.Decisions.Local;
int count=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
var blank=LocalInferenceTiming.Parse(null);Check(blank.PromptTokens is null&&blank.GenerationEvaluations is null,"missing not zero");
var known=LocalInferenceTiming.Parse("llama_perf_context_print: prompt eval time = 123.50 ms / 200 tokens\nllama_perf_context_print: eval time = 35.75 ms / 12 runs");
Check(known.PromptTokens==200&&known.PromptMilliseconds==123.5,"prompt");Check(known.GenerationEvaluations==12&&known.GenerationMilliseconds==35.75,"generation");
var partial=LocalInferenceTiming.Parse("prompt eval time = 1 ms / 2 tokens");Check(partial.PromptTokens==2&&partial.GenerationEvaluations is null,"partial");
var huge=LocalInferenceTiming.Parse("prompt eval time = 1..2 ms / 999999999999999999999999 tokens");Check(huge.PromptTokens is null&&huge.PromptMilliseconds is null,"malformed safe");
Check(LocalInferenceTiming.Parse(new string('x',262145)).PromptTokens is null,"oversize");
Check(LocalInferenceTiming.Parse("prompt eval time = 1 ms / 3 runs").GenerationEvaluations is null,"prompt not generation");
string fixture=typeof(LlamaProcessProvider).Assembly.Location;var p=new LlamaProcessProvider(fixture,fixture,_=>false);
try{await p.CompleteStructuredAsync("","{}",s=>s,default);throw new Exception("must reject");}catch(ArgumentException){}
Check(p.LastAudit is {Started:false,Outcome:"preflight_failure",CompleteStderr:false},"preflight audit");Check(p.LastAudit!.PromptTokens is null&&p.LastAudit.PeakSampledRssBytes is null&&p.LastMetrics is null,"preflight unknown");
using var stop=new CancellationTokenSource();stop.Cancel();try{await p.CompleteStructuredAsync("hi","{}",s=>s,stop.Token);throw new Exception("must cancel");}catch(OperationCanceledException){}
Check(p.LastAudit is {Started:false,Outcome:"canceled_before_start"},"prestart cancellation");Check(p.LastAudit!.WallSeconds>=0&&p.LastAudit.SampledCpuSeconds is null,"prestart time no cpu");
Console.WriteLine($"PASS {count}/{count} audit pure tests; no process or inference started");
