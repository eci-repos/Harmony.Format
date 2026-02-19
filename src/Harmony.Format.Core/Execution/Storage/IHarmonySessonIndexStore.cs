using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Storage;

/// <summary>
/// Lists session ids (optionally filtered by script id).
/// Separated from IHarmonySessionStore to keep storage and indexing concerns distinct.
/// </summary>
public interface IHarmonySessionIndexStore
{
   Task<HarmonyPageResult<IReadOnlyList<string>>> ListSessionIdsAsync(
      string? scriptId = null,
      HarmonyPageRequest? page = null,
      CancellationToken ct = default);
}
