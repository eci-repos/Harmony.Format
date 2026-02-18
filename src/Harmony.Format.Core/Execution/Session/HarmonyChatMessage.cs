using System;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Session;

/// <summary>
/// Durable chat transcript entry captured during session execution.
/// (Separate from ChatConversation, which is an in-memory prompt container.)
/// </summary>
public sealed class HarmonyChatMessage
{
   public required string Role { get; init; }   // "system" | "developer" | "user" | "assistant"
   public required string Content { get; init; }

   public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

   /// <summary>Optional: source envelope index for traceability.</summary>
   public int? SourceIndex { get; init; }
}
