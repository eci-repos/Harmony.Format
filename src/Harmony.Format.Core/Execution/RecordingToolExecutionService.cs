using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution;

/// <summary>
/// Decorator for IToolExecutionService that records tool invocations/results.
/// Keeps Harmony.Format provider-agnostic.
/// </summary>
internal sealed class RecordingToolExecutionService : IToolExecutionService
{
   private readonly IToolExecutionService _inner;
   private readonly Action<ToolCallTrace> _onTrace;

   public RecordingToolExecutionService(IToolExecutionService inner, Action<ToolCallTrace> onTrace)
   {
      _inner = inner ?? throw new ArgumentNullException(nameof(inner));
      _onTrace = onTrace ?? throw new ArgumentNullException(nameof(onTrace));
   }

   public async Task<object?> InvokeToolAsync(
      string recipient,
      IReadOnlyDictionary<string, object?> args,
      CancellationToken ct = default)
   {
      var trace = new ToolCallTrace
      {
         Recipient = recipient,
         Args = new Dictionary<string, object?>(args, StringComparer.OrdinalIgnoreCase),
         StartedAt = DateTimeOffset.UtcNow
      };

      try
      {
         var result = await _inner.InvokeToolAsync(recipient, args, ct).ConfigureAwait(false);
         trace.CompletedAt = DateTimeOffset.UtcNow;
         trace.Result = result;
         trace.Succeeded = true;

         _onTrace(trace);
         return result;
      }
      catch (Exception ex)
      {
         trace.CompletedAt = DateTimeOffset.UtcNow;
         trace.Succeeded = false;
         trace.Error = new { exception = ex.GetType().Name, message = ex.Message };

         _onTrace(trace);
         throw;
      }
   }
}

/// <summary>
/// Structured trace payload for tool calls (stored as an artifact in history).
/// </summary>
internal sealed class ToolCallTrace
{
   public string Recipient { get; set; } = string.Empty;
   public Dictionary<string, object?> Args { get; set; } = new(StringComparer.OrdinalIgnoreCase);

   public DateTimeOffset StartedAt { get; set; }
   public DateTimeOffset? CompletedAt { get; set; }

   public bool Succeeded { get; set; }
   public object? Result { get; set; }
   public object? Error { get; set; }
}
