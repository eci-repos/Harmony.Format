using Harmony.Format.Execution.Session;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Storage;

/// <summary>
/// Default in-memory implementation of IHarmonySessionStore.
/// Intended for development, testing, and single-process scenarios.
/// Not durable across process restarts.
/// </summary>
public sealed class InMemoryHarmonySessionStore : IHarmonySessionStore
{
   private readonly ConcurrentDictionary<string, HarmonySession> _sessions =
      new(StringComparer.OrdinalIgnoreCase);

   public Task<HarmonySession> CreateAsync(
      string scriptId,
      CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(scriptId))
         throw new ArgumentException("scriptId must be provided.", nameof(scriptId));

      var session = new HarmonySession
      {
         ScriptId = scriptId,
         Status = HarmonySessionStatus.Created,
         CurrentIndex = 0,
         UpdatedAt = DateTimeOffset.UtcNow
      };

      if (!_sessions.TryAdd(session.SessionId, session))
         throw new InvalidOperationException("Failed to create session.");

      return Task.FromResult(session);
   }

   public Task<HarmonySession?> GetAsync(
      string sessionId,
      CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(sessionId))
         throw new ArgumentException("sessionId must be provided.", nameof(sessionId));

      _sessions.TryGetValue(sessionId, out var session);
      return Task.FromResult(session);
   }

   public Task SaveAsync(
      HarmonySession session,
      CancellationToken ct = default)
   {
      if (session is null)
         throw new ArgumentNullException(nameof(session));

      session.UpdatedAt = DateTimeOffset.UtcNow;

      _sessions[session.SessionId] = session;

      return Task.CompletedTask;
   }

   public Task<bool> DeleteAsync(
      string sessionId,
      CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(sessionId))
         throw new ArgumentException("sessionId must be provided.", nameof(sessionId));

      var removed = _sessions.TryRemove(sessionId, out _);
      return Task.FromResult(removed);
   }

   public Task<bool> ExistsAsync(
      string sessionId,
      CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(sessionId))
         throw new ArgumentException("sessionId must be provided.", nameof(sessionId));

      return Task.FromResult(_sessions.ContainsKey(sessionId));
   }
}
