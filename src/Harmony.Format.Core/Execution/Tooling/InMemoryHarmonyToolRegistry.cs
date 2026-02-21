
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Harmony.Format.Execution.Tooling;

public sealed class InMemoryHarmonyToolRegistry : IHarmonyToolRegistry
{
   private readonly ConcurrentDictionary<string, HarmonyToolDescriptor> _tools =
      new(StringComparer.OrdinalIgnoreCase);

   public Task<IReadOnlyList<HarmonyToolDescriptor>> ListAsync(CancellationToken ct = default)
      => Task.FromResult((IReadOnlyList<HarmonyToolDescriptor>)_tools.Values
         .OrderBy(t => t.Recipient, StringComparer.OrdinalIgnoreCase)
         .ToList());

   public Task<HarmonyToolDescriptor?> GetAsync(string recipient, CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(recipient))
         throw new ArgumentException("recipient must be provided.", nameof(recipient));

      _tools.TryGetValue(recipient, out var tool);
      return Task.FromResult(tool);
   }

   public void Upsert(HarmonyToolDescriptor tool)
   {
      if (tool is null) throw new ArgumentNullException(nameof(tool));
      _tools[tool.Recipient] = tool;
   }

   public bool Remove(string recipient) => _tools.TryRemove(recipient, out _);
}

