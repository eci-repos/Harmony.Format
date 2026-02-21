
// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Tooling;

public sealed class HarmonyToolDescriptor
{
   public required string Recipient { get; init; }  // "maps.search"
   public string? Description { get; init; }        // human-friendly
   public string? Version { get; init; }            // optional
   public object? InputSchema { get; init; }        // keep loose; MCP can pass JSON schema later
   public object? OutputSchema { get; init; }       // keep loose
   public Dictionary<string, string>? Tags { get; init; } // optional metadata
}
