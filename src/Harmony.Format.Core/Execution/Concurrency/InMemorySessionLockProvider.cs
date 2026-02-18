using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Concurrency;

/// <summary>
/// Provides a per-session async lock (single-writer) for in-memory scenarios.
/// Later, a durable/distributed implementation can replace this.
/// </summary>
public interface IHarmonySessionLockProvider
{
   Task<IDisposable> AcquireAsync(string sessionId, CancellationToken ct = default);
}

public sealed class InMemorySessionLockProvider : IHarmonySessionLockProvider
{
   private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
      new(StringComparer.OrdinalIgnoreCase);

   public async Task<IDisposable> AcquireAsync(string sessionId, CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(sessionId))
         throw new ArgumentException("sessionId must be provided.", nameof(sessionId));

      var sem = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
      await sem.WaitAsync(ct).ConfigureAwait(false);
      return new Releaser(sem);
   }

   private sealed class Releaser : IDisposable
   {
      private SemaphoreSlim? _sem;
      public Releaser(SemaphoreSlim sem) => _sem = sem;

      public void Dispose()
      {
         var sem = Interlocked.Exchange(ref _sem, null);
         sem?.Release();
      }
   }

}
