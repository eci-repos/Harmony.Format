using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Tooling;

public interface IHarmonyToolAvailability
{
   /// <summary>
   /// Returns true if the tool recipient (e.g., "plugin.function") is available for execution.
   /// </summary>
   Task<bool> IsAvailableAsync(string recipient, CancellationToken ct = default);

   /// <summary>
   /// Optional: returns known available tools. May be empty if provider can’t enumerate.
   /// </summary>
   Task<IReadOnlyCollection<string>> ListAvailableAsync(CancellationToken ct = default);
}

/// <summary>
/// Default permissive implementation: does not block execution.
/// </summary>
public sealed class AllowAllToolAvailability : IHarmonyToolAvailability
{
   public Task<bool> IsAvailableAsync(string recipient, CancellationToken ct = default)
      => Task.FromResult(true);

   public Task<IReadOnlyCollection<string>> ListAvailableAsync(CancellationToken ct = default)
      => Task.FromResult((IReadOnlyCollection<string>)System.Array.Empty<string>());
}
