using System;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Storage;

/// <summary>
/// Cursor-based paging request.
/// ContinuationToken is an opaque string (store-specific).
/// </summary>
public sealed class HarmonyPageRequest
{
   public int Limit { get; init; } = 50;              // sensible default
   public string? ContinuationToken { get; init; }    // opaque cursor
}

/// <summary>
/// Cursor-based paging response.
/// </summary>
public sealed class HarmonyPageResult<T>
{
   public required T Items { get; init; }
   public string? ContinuationToken { get; init; }    // null when no more pages
}
