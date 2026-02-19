using System;
using System.Collections.Generic;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Api;

public sealed class HarmonySessionListResponse
{
   public string? ScriptId { get; init; }
   public IReadOnlyList<string> SessionIds { get; init; } = Array.Empty<string>();

   /// <summary>Null means no more pages.</summary>
   public string? ContinuationToken { get; init; }
}
