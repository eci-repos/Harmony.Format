using Harmony.Format.Execution.History;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Storage;

/// <summary>
/// Optional convenience interface if you want history append to be explicit
/// and possibly optimized for durable stores later.
/// You can merge this into IHarmonySessionStore if preferred.
/// </summary>
public interface IHarmonyHistoryStore
{
   /// <summary>
   /// Append a message execution record to the session history.
   /// Implementations should treat history as append-only.
   /// </summary>
   Task AppendAsync(
      string sessionId,
      HarmonyMessageExecutionRecord record,
      CancellationToken ct = default);

   /// <summary>
   /// Get full history for a session (ordered).
   /// </summary>
   Task<IReadOnlyList<HarmonyMessageExecutionRecord>> GetAsync(
      string sessionId,
      CancellationToken ct = default);

   /// <summary>
   /// Get one history record by message index; returns null if not found.
   /// </summary>
   Task<HarmonyMessageExecutionRecord?> GetItemAsync(
      string sessionId,
      int index,
      CancellationToken ct = default);
}
