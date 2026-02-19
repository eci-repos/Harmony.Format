using Harmony.Format.Execution.History;
using System;
using System.Collections.Generic;

// ----------------------------------------------------------------------------
namespace Harmony.Format.Execution.Api;

public sealed class HarmonyHistoryResponse
{
   public required string SessionId { get; init; }
   public required string ScriptId { get; init; }

   public int CurrentIndex { get; init; }
   public string Status { get; init; } = string.Empty;

   public IReadOnlyList<HarmonyMessageExecutionRecord> History { get; init; } =
      Array.Empty<HarmonyMessageExecutionRecord>();
}
