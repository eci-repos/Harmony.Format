using Harmony.Format.Execution.History;

namespace Harmony.Format.Execution.Api;

public sealed class HarmonyHistoryItemResponse
{
   public required string SessionId { get; init; }
   public required string ScriptId { get; init; }

   public int Index { get; init; }

   public HarmonyMessageExecutionRecord? Record { get; init; }
}
