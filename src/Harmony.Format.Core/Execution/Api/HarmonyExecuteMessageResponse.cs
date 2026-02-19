using Harmony.Format.Execution.History;
using System;
using System.Collections.Generic;

// ---------------------------------------------------------------------------- 
namespace Harmony.Format.Execution.Api;

/// <summary>
/// MCP-friendly response envelope for executing one message.
/// Keeps payload stable for clients (MCP server) while Harmony.Format evolves.
/// </summary>
public sealed class HarmonyExecuteMessageResponse
{
   public required string SessionId { get; init; }
   public required string ScriptId { get; init; }

   public int ExecutedIndex { get; init; }
   public int NextIndex { get; init; }

   public string SessionStatus { get; init; } = string.Empty;

   /// <summary>Execution record for the executed message.</summary>
   public HarmonyMessageExecutionRecord Record { get; init; } = new();

   /// <summary>Convenience: outputs for the executed message.</summary>
   public IReadOnlyList<HarmonyArtifact> Outputs { get; init; } = 
      Array.Empty<HarmonyArtifact>();

   /// <summary>Convenience: current session vars (optional; can be 
   /// removed/trimmed later).</summary>
   public IReadOnlyDictionary<string, object?> Vars { get; init; } =
      new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}
