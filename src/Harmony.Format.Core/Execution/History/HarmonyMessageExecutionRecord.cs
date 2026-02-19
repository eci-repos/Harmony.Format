using System;
using System.Collections.Generic;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.History;

/// <summary>
/// Immutable record of executing a single envelope message (or an "executable unit")
/// within a session.
/// </summary>
public sealed class HarmonyMessageExecutionRecord
{
   public int Index { get; init; }

   /// <summary>Optional future-proofing if messages later gain ids.</summary>
   public string? MessageId { get; init; }

   public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
   public DateTimeOffset? CompletedAt { get; set; }

   public HarmonyExecutionStatus Status { get; set; } = HarmonyExecutionStatus.Running;

   /// <summary>
   /// Optional idempotency key to dedupe retries for the same message execution.
   /// </summary>
   public string? ExecutionId { get; init; }

   /// <summary>
   /// Inputs snapshot (usually Vars/Input summaries) for traceability.
   /// Keep it light; store full snapshots only if needed.
   /// </summary>
   public Dictionary<string, object?> Inputs { get; set; } =
      new(StringComparer.OrdinalIgnoreCase);

   /// <summary>Artifacts produced while processing this message.</summary>
   public List<HarmonyArtifact> Outputs { get; set; } = new();

   /// <summary>Optional human-readable notes/log lines.</summary>
   public List<string> Logs { get; set; } = new();

   /// <summary>Structured error (reuse your existing HarmonyError type).</summary>
   public HarmonyError? Error { get; set; }
}

