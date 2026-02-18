using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Storage;

/// <summary>
/// Default in-memory implementation of IHarmonyScriptStore.
/// Intended for development, testing, and single-process scenarios.
/// </summary>
public sealed class InMemoryHarmonyScriptStore : IHarmonyScriptStore
{
   private readonly ConcurrentDictionary<string, HarmonyEnvelope> _scripts =
      new(StringComparer.OrdinalIgnoreCase);

   public Task RegisterAsync(
      string scriptId,
      HarmonyEnvelope envelope,
      CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(scriptId))
         throw new ArgumentException("scriptId must be provided.", nameof(scriptId));

      if (envelope is null)
         throw new ArgumentNullException(nameof(envelope));

      // Overwrite if already exists (replace semantics)
      _scripts[scriptId] = envelope;

      return Task.CompletedTask;
   }

   public Task<HarmonyEnvelope?> GetAsync(
      string scriptId,
      CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(scriptId))
         throw new ArgumentException("scriptId must be provided.", nameof(scriptId));

      _scripts.TryGetValue(scriptId, out var envelope);
      return Task.FromResult(envelope);
   }

   public Task<IReadOnlyList<string>> ListAsync(
      CancellationToken ct = default)
   {
      IReadOnlyList<string> ids = _scripts.Keys
         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
         .ToList();

      return Task.FromResult(ids);
   }

   public Task<bool> DeleteAsync(
      string scriptId,
      CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(scriptId))
         throw new ArgumentException("scriptId must be provided.", nameof(scriptId));

      var removed = _scripts.TryRemove(scriptId, out _);
      return Task.FromResult(removed);
   }

   public Task<bool> ExistsAsync(
      string scriptId,
      CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(scriptId))
         throw new ArgumentException("scriptId must be provided.", nameof(scriptId));

      return Task.FromResult(_scripts.ContainsKey(scriptId));
   }
}
