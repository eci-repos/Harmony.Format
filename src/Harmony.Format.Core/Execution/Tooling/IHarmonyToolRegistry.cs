
// ----------------------------------------------------------------------------
namespace Harmony.Format.Execution.Tooling;

public interface IHarmonyToolRegistry
{
   /// <summary>Returns all available tool descriptors (recipient + metadata).</summary>
   Task<IReadOnlyList<HarmonyToolDescriptor>> ListAsync(
      CancellationToken ct = default);

   /// <summary>Returns descriptor for a single recipient, or null if missing.</summary>
   Task<HarmonyToolDescriptor?> GetAsync(
      string recipient, CancellationToken ct = default);
}
