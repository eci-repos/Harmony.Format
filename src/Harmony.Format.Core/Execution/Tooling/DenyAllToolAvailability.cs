
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Tooling;

/// <summary>
/// Default restrictive implementation: blocks all tool execution unless a host injects
/// a permissive/registry-backed implementation.
/// </summary>
public sealed class DenyAllToolAvailability : IHarmonyToolAvailability
{
   public static readonly DenyAllToolAvailability Instance = new();

   public Task<bool> IsAvailableAsync(string recipient, CancellationToken ct = default)
      => Task.FromResult(false);

   public Task<IReadOnlyCollection<string>> ListAvailableAsync(CancellationToken ct = default)
      => Task.FromResult((IReadOnlyCollection<string>)Array.Empty<string>());
}

