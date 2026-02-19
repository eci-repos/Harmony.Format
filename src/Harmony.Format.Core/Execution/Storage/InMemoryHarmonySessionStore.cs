using Harmony.Format.Execution.Session;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Storage;

/// <summary>
/// Provides an in-memory implementation of the IHarmonySessionStore and IHarmonySessionIndexStore 
/// interfaces for managing Harmony sessions.
/// </summary>
/// <remarks>This class allows for the creation, retrieval, updating, deletion, and existence 
/// checking of Harmony sessions stored in memory. It is designed for scenarios where persistence is 
/// not required, making it suitable for testing or temporary session management.</remarks>
public sealed class InMemoryHarmonySessionStore :
   IHarmonySessionStore,
   IHarmonySessionIndexStore
{
   private readonly ConcurrentDictionary<string, HarmonySession> _sessions =
      new(StringComparer.OrdinalIgnoreCase);

   public Task<HarmonySession> CreateAsync(string scriptId, CancellationToken ct = default)
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

   public Task<HarmonySession?> GetAsync(string sessionId, CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(sessionId))
         throw new ArgumentException("sessionId must be provided.", nameof(sessionId));

      _sessions.TryGetValue(sessionId, out var session);
      return Task.FromResult(session);
   }

   public Task SaveAsync(HarmonySession session, CancellationToken ct = default)
   {
      if (session is null)
         throw new ArgumentNullException(nameof(session));

      session.UpdatedAt = DateTimeOffset.UtcNow;
      _sessions[session.SessionId] = session;
      return Task.CompletedTask;
   }

   public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(sessionId))
         throw new ArgumentException("sessionId must be provided.", nameof(sessionId));

      var removed = _sessions.TryRemove(sessionId, out _);
      return Task.FromResult(removed);
   }

   public Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(sessionId))
         throw new ArgumentException("sessionId must be provided.", nameof(sessionId));

      return Task.FromResult(_sessions.ContainsKey(sessionId));
   }

   /// <summary>
   /// Asynchronously retrieves a paginated list of session identifiers, optionally filtered by 
   /// script.
   /// </summary>
   /// <remarks>If the requested limit exceeds 500, it is capped at 500. Results are ordered by 
   /// the most
   /// recently updated sessions first, then by session ID in a case-insensitive manner. The 
   /// continuation token can be used to retrieve the next page of results.</remarks>
   /// <param name="scriptId">The optional identifier of the script to filter sessions by. If 
   /// specified, only session IDs associated with this script are returned. If null, session IDs
   /// for all scripts are included.</param>
   /// <param name="page">An optional pagination request specifying the maximum number of items 
   /// to return and a continuation token for
   /// fetching subsequent results. If null, default pagination settings are used.</param>
   /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.
   /// </param>
   /// <returns>A task that represents the asynchronous operation. The task result contains a 
   /// HarmonyPageResult with a read-only list of session IDs and a continuation token for 
   /// retrieving additional results, if available.</returns>
   public Task<HarmonyPageResult<IReadOnlyList<string>>> ListSessionIdsAsync(
      string? scriptId = null,
      HarmonyPageRequest? page = null,
      CancellationToken ct = default)
   {
      page ??= new HarmonyPageRequest();

      var limit = page.Limit <= 0 ? 50 : page.Limit;
      if (limit > 500) limit = 500; // guardrail for MCP/UI callers

      var offset = ParseOffsetToken(page.ContinuationToken);

      IEnumerable<HarmonySession> sessions = _sessions.Values;

      if (!string.IsNullOrWhiteSpace(scriptId))
      {
         sessions = sessions.Where(s =>
            string.Equals(s.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase));
      }

      // Deterministic ordering: newest first, then SessionId tie-breaker
      var ordered = sessions
         .OrderByDescending(s => s.UpdatedAt)
         .ThenBy(s => s.SessionId, StringComparer.OrdinalIgnoreCase)
         .ToList();

      var slice = ordered
         .Skip(offset)
         .Take(limit)
         .Select(s => s.SessionId)
         .ToList();

      string? nextToken = null;
      var nextOffset = offset + slice.Count;
      if (nextOffset < ordered.Count)
         nextToken = $"offset:{nextOffset}";

      return Task.FromResult(new HarmonyPageResult<IReadOnlyList<string>> 
         { Items = slice, ContinuationToken = nextToken });
   }

   private static int ParseOffsetToken(string? token)
   {
      if (string.IsNullOrWhiteSpace(token))
         return 0;

      // token format: "offset:123"
      if (token.StartsWith("offset:", StringComparison.OrdinalIgnoreCase) &&
          int.TryParse(token.Substring("offset:".Length), out var n) &&
          n >= 0)
      {
         return n;
      }

      // Unknown token => treat as 0 (or throw; MVP keep lenient)
      return 0;
   }

}
