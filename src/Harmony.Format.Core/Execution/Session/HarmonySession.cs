using System;
using System.Collections.Generic;
using System.Text.Json;
using Harmony.Format.Execution.History;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Session;

/// <summary>
/// Runtime session tied to an immutable Harmony envelope/script.
/// Holds mutable execution state: pointer, vars, artifacts, and history.
/// </summary>
public sealed class HarmonySession
{
   public string SessionId { get; init; } = Guid.NewGuid().ToString("n");
   public string ScriptId { get; init; } = string.Empty; // caller-defined (registered id)

   /// <summary>Current message pointer in the envelope message list.</summary>
   public int CurrentIndex { get; set; } = 0;

   public HarmonySessionStatus Status { get; set; } = HarmonySessionStatus.Created;

   public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
   public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

   /// <summary>
   /// Durable execution memory used by expressions ($vars.*) across messages.
   /// </summary>
   public Dictionary<string, object?> Vars { get; set; } =
      new(StringComparer.OrdinalIgnoreCase);

   /// <summary>
   /// Named artifacts produced during execution (tool outputs, snapshots, generated text, etc.).
   /// </summary>
   public Dictionary<string, HarmonyArtifact> Artifacts { get; set; } =
      new(StringComparer.OrdinalIgnoreCase);

   /// <summary>
   /// Append-only execution history by processed message index.
   /// </summary>
   public List<HarmonyMessageExecutionRecord> History { get; set; } = new();

   /// <summary>
   /// Optional: labels/tags or custom info for dashboards.
   /// </summary>
   public Dictionary<string, string> Metadata { get; set; } =
      new(StringComparer.OrdinalIgnoreCase);

   /// <summary>
   /// Gets or sets the collection of chat messages exchanged during the session.
   /// </summary>
   /// <remarks>Messages in the collection are ordered chronologically, allowing consumers to 
   /// retrieve and display the conversation history in sequence.</remarks>
   public List<HarmonyChatMessage> Transcript { get; set; } = [];

   // Fast idempotency lookup: executionId -> history index (or record index)
   public Dictionary<string, int> ExecutionIdIndex { get; set; } =
      new(StringComparer.OrdinalIgnoreCase);
}


