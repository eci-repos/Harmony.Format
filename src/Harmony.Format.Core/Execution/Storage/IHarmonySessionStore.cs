using Harmony.Format.Execution.Session;
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Storage;

/// <summary>
/// Stores mutable execution sessions and their execution history.
/// </summary>
public interface IHarmonySessionStore
{
   /// <summary>
   /// Create a new session instance for a given script id.
   /// The store is responsible for persisting the new session.
   /// </summary>
   Task<HarmonySession> CreateAsync(
      string scriptId,
      CancellationToken ct = default);

   /// <summary>
   /// Load an existing session; returns null if not found.
   /// </summary>
   Task<HarmonySession?> GetAsync(
      string sessionId,
      CancellationToken ct = default);

   /// <summary>
   /// Persist updated session state (pointer, status, vars, artifacts, timestamps).
   /// </summary>
   Task SaveAsync(
      HarmonySession session,
      CancellationToken ct = default);

   /// <summary>
   /// Delete a session; returns true if removed; false if not found.
   /// </summary>
   Task<bool> DeleteAsync(
      string sessionId,
      CancellationToken ct = default);

   /// <summary>
   /// Returns true if a session id exists.
   /// </summary>
   Task<bool> ExistsAsync(
      string sessionId,
      CancellationToken ct = default);

}
