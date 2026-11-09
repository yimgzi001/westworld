using System.Text;
namespace Westworld.Decisions.Local;
public static class BoundedText {
 // Local child-process text only: bounded before append, cancellation covers EOF waits.
 public static async Task<string> ReadAsync(TextReader reader,int limit,CancellationToken cancellation){
  ArgumentNullException.ThrowIfNull(reader);if(limit<1)throw new ArgumentOutOfRangeException(nameof(limit));
  var text=new StringBuilder();var buffer=new char[Math.Min(1024,limit)];
  while(true){cancellation.ThrowIfCancellationRequested();int n=await reader.ReadAsync(buffer.AsMemory(),cancellation).ConfigureAwait(false);if(n==0)return text.ToString();if(n>limit-text.Length)throw new InvalidOperationException("Child process output exceeded limit");text.Append(buffer,0,n);}
 }
}
