using System;
using System.Text.Json;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.History;

/// <summary>
/// A structured output produced during execution (tool result, saved var, generated text, etc.).
/// Stored as JSON for portability and easy persistence later.
/// </summary>
public sealed class HarmonyArtifact
{
   public string? Name { get; set; }

   /// <summary>e.g., "text", "json", "tool-result", "vars-snapshot"</summary>
   public string ContentType { get; set; } = "json";

   /// <summary>
   /// JSON payload for the artifact. Prefer JsonElement for persistence friendliness.
   /// </summary>
   public JsonElement Content { get; set; }

   public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

   /// <summary>Optional: which tool produced it.</summary>
   public string? Producer { get; set; }
}
