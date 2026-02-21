
// ----------------------------------------------------------------------------
namespace Harmony.Format.Execution.Tooling;

public sealed class RegistryBackedToolAvailability :
   AllowAllToolAvailability, IHarmonyToolAvailability
{
   private readonly IHarmonyToolRegistry _registry;

   public RegistryBackedToolAvailability(IHarmonyToolRegistry registry)
      => _registry = registry ?? 
         throw new ArgumentNullException(nameof(registry));

   public new async Task<bool> IsAvailableAsync(
      string recipient, CancellationToken ct = default)
      => await _registry.GetAsync(
         recipient, ct).ConfigureAwait(false) is not null;
}
