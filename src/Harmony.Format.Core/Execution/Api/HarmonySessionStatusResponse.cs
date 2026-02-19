using System;
using System.Collections.Generic;

// ----------------------------------------------------------------------------
namespace Harmony.Format.Execution.Api;

public sealed class HarmonySessionStatusResponse
{
   public required string SessionId { get; init; }
   public required string ScriptId { get; init; }

   public int CurrentIndex { get; init; }
   public string Status { get; init; } = string.Empty;

   public DateTimeOffset CreatedAt { get; init; }
   public DateTimeOffset UpdatedAt { get; init; }

   public int HistoryCount { get; init; }
   public int ArtifactCount { get; init; }

   /// <summary>
   /// Optional: lightweight metadata (good for dashboards).
   /// </summary>
   public IReadOnlyDictionary<string, string> Metadata { get; init; } =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
