using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Harmony.Format.Execution.History;
using Harmony.Format.Execution.Session;
using Harmony.Format.Execution.Storage;
using Harmony.Format.Execution.Tooling;
using Harmony.Format.Execution.Concurrency;
using Harmony.Format.Execution.Api;
using Harmony.Format.Execution;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution;

// -------------------------------------------------------------------------------------------------
// MCP Friendly APIs

/// <summary>
/// Provides methods for executing messages and commands within a Harmony session.
/// </summary>
/// <remarks>This class is designed to facilitate the execution of commands and 
/// messages in a Harmony session, ensuring that the session state is maintained and updated
/// correctly. It handles asynchronous execution and provides responses that include session
/// details and execution results.</remarks>
public sealed partial class HarmonyExecutionService
{

	public async Task<HarmonyExecuteMessageResponse> ExecuteNextMcpAsync(
		string sessionId,
		IDictionary<string, object?>? input = null,
		string? executionId = null,
		CancellationToken ct = default)
	{
		var record = await ExecuteNextAsync(
			sessionId, input, executionId, ct).ConfigureAwait(false);
		var session = await RequireSessionAsync(sessionId, ct).ConfigureAwait(false);

		return ToMcpResponse(session, record);
	}

	public async Task<HarmonyExecuteMessageResponse> ExecuteMessageMcpAsync(
		string sessionId,
		int index,
		IDictionary<string, object?>? input = null,
		string? executionId = null,
		CancellationToken ct = default)
	{
		var record = await ExecuteMessageAsync(
			sessionId, index, input, executionId, ct).ConfigureAwait(false);
		var session = await RequireSessionAsync(sessionId, ct).ConfigureAwait(false);

      return ToMcpResponse(session, record);
   }

	// ----------------------------------------------------------------------------------------------
	// Note: GetSessionStatusMcpAsync and GetHistoryMcpAsync could be merged into a single method
	// with a flag to include history, but keeping them separate for clarity and potential future
	// divergence in response structure.

	/// <summary>
	/// Asynchronously retrieves the current status details of a specified session.
	/// </summary>
	/// <remarks>Throws an exception if the session with the specified identifier does not exist. 
	/// Use this method to monitor or report the status of an ongoing session.</remarks>
	/// <param name="sessionId">The unique identifier of the session to retrieve. Cannot be null or 
	/// empty.</param>
	/// <param name="ct">A cancellation token that can be used to cancel the operation. The default 
	/// value is <see cref="CancellationToken.None"/>.</param>
	/// <returns>A task that represents the asynchronous operation. The task result contains a <see
	/// cref="HarmonySessionStatusResponse"/> object with the status information of the session.
	/// </returns>
	public async Task<HarmonySessionStatusResponse> GetSessionStatusMcpAsync(
		string sessionId,
		CancellationToken ct = default)
	{
		var session = await RequireSessionAsync(sessionId, ct).ConfigureAwait(false);

		return new HarmonySessionStatusResponse
		{
			SessionId = session.SessionId,
			ScriptId = session.ScriptId,
			CurrentIndex = session.CurrentIndex,
			Status = session.Status.ToString(),
			CreatedAt = session.CreatedAt,
			UpdatedAt = session.UpdatedAt,
			HistoryCount = session.History?.Count ?? 0,
			ArtifactCount = session.Artifacts?.Count ?? 0,
			Metadata = session.Metadata
		};
	}

	/// <summary>
	/// Asynchronously retrieves the history details for the specified session.
	/// </summary>
	/// <remarks>An exception is thrown if the specified session ID is invalid or does not exist.
	/// </remarks>
	/// <param name="sessionId">The unique identifier of the session whose history is to be 
	/// retrieved. This parameter cannot be null or empty.</param>
	/// <param name="ct">A cancellation token that can be used to cancel the operation. 
	/// The default value is <see cref="CancellationToken.None"/>.</param>
	/// <returns>A task that represents the asynchronous operation. The task result contains a 
	/// <see cref="HarmonyHistoryResponse"/> object with the session's history details.</returns>
	public async Task<HarmonyHistoryResponse> GetHistoryMcpAsync(
		string sessionId,
		CancellationToken ct = default)
	{
		var session = await RequireSessionAsync(sessionId, ct).ConfigureAwait(false);

		return new HarmonyHistoryResponse
		{
			SessionId = session.SessionId,
			ScriptId = session.ScriptId,
			CurrentIndex = session.CurrentIndex,
			Status = session.Status.ToString(),
			History = session.History
		};
	}

	/// <summary>
	/// Retrieves a specific history item from the session's history by its zero-based index.
	/// </summary>
	/// <remarks>If the specified index is not found or the session does not contain a history, the 
	/// returned record may be null.</remarks>
	/// <param name="sessionId">The unique identifier of the session from which to retrieve the 
	/// history item. Cannot be null.</param>
	/// <param name="index">The zero-based index of the history item to retrieve. Must be within the 
	/// valid range of the session's history.</param>
	/// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.
	/// </param>
	/// <returns>A task that represents the asynchronous operation. The task result contains a 
	/// HarmonyHistoryItemResponse with the details of the requested history item, or null if the 
	/// index is out of range or the session has no history.</returns>
	public async Task<HarmonyHistoryItemResponse> GetHistoryItemMcpAsync(
		string sessionId,
		int index,
		CancellationToken ct = default)
	{
		var session = await RequireSessionAsync(sessionId, ct).ConfigureAwait(false);

		HarmonyMessageExecutionRecord? record = null;
		if (index >= 0 && session.History is not null && index < session.History.Count)
		{
			// Prefer exact index match if history isn't dense/ordered
			record = session.History.FirstOrDefault(r => r.Index == index)
					?? session.History[index];
		}

		return new HarmonyHistoryItemResponse
		{
			SessionId = session.SessionId,
			ScriptId = session.ScriptId,
			Index = index,
			Record = record
		};
	}

	/// <summary>
	/// Converts the specified session and execution record into a standardized response object for
	/// Harmony message execution.
	/// </summary>
	/// <param name="session">The session context containing identifiers, state, and variables for 
	/// the current Harmony execution.</param>
	/// <param name="record">The execution record detailing the message execution, including the 
	/// executed index and any generated outputs.</param>
	/// <returns>A HarmonyExecuteMessageResponse object that encapsulates the session ID, script ID,
	/// executed index, next index, session status, execution record, outputs, and session variables.
	/// </returns>
   private static HarmonyExecuteMessageResponse ToMcpResponse(
      HarmonySession session,
      HarmonyMessageExecutionRecord record)
   {
      return new HarmonyExecuteMessageResponse
      {
         SessionId = session.SessionId,
         ScriptId = session.ScriptId,
         ExecutedIndex = record.Index,
         NextIndex = session.CurrentIndex,
         SessionStatus = session.Status.ToString(),
         Record = record,
         Outputs = record.Outputs ?? new List<HarmonyArtifact>(),
         Vars = session.Vars ?? new Dictionary<string, object?>()
      };
   }

}

// -------------------------------------------------------------------------------------------------

/// <summary>
/// Provides methods for managing Harmony sessions, including listing and deleting sessions.
/// </summary>
/// <remarks>This service interacts with an IHarmonySessionIndexStore to retrieve and manage 
/// session data. It is designed to handle asynchronous operations for session management,
/// ensuring non-blocking behavior during execution.</remarks>
public sealed partial class HarmonyExecutionService
{

	// Add this parameter to your constructor and store it
	// public HarmonyExecutionService(..., IHarmonySessionIndexStore sessionIndex, ...)
	// { _sessionIndex = sessionIndex; }

	public async Task<HarmonySessionListResponse> ListSessionsMcpAsync(
		string? scriptId = null,
      HarmonyPageRequest? page = null,
      CancellationToken ct = default)
   {
      var result = await _sessionIndex
         .ListSessionIdsAsync(scriptId, page, ct)
         .ConfigureAwait(false);

      return new HarmonySessionListResponse
      {
         ScriptId = scriptId,
         SessionIds = result.Items,
         ContinuationToken = result.ContinuationToken
      };
   }

	public async Task<bool> DeleteSessionMcpAsync(
		string sessionId,
		CancellationToken ct = default)
	{
		// delete is already stable through IHarmonySessionStore
		return await _sessionStore.DeleteAsync(sessionId, ct).ConfigureAwait(false);
	}
}
