using Harmony.Format.Execution.Api;
using Harmony.Format.Execution.Concurrency;
using Harmony.Format.Execution.History;
using Harmony.Format.Execution.Session;
using Harmony.Format.Execution.Storage;
using Harmony.Format.Execution.Tooling;
using Harmony.Format.Execution.Transcript;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution;

/// <summary>
/// Thin orchestration wrapper that ties together:
/// - Script store (immutable envelopes)
/// - Session store (mutable runtime state + history)
/// - HarmonyExecutor (actual execution semantics)
///
/// This service is intentionally "MCP-friendly": it exposes single-step execution
/// (process one envelope message at a time) and records history/artifacts.
/// </summary>
public sealed partial class HarmonyExecutionService
{
   private readonly IHarmonyScriptStore _scriptStore;
   private readonly IHarmonySessionStore _sessionStore;
   private readonly HarmonyExecutor _executor;
   private readonly IToolExecutionService _toolRouter;
   private readonly IHarmonySessionLockProvider _locks;
   private readonly IHarmonySessionIndexStore _sessionIndexStore;
   private readonly IHarmonyToolAvailability _toolAvailability; 
   private readonly HarmonyPreflightAnalyzer _preflight;

   public HarmonyExecutionService(
      IHarmonyScriptStore scriptStore,
      IHarmonySessionStore sessionStore,
      HarmonyExecutor executor,
      IToolExecutionService toolRouter,
      IHarmonySessionLockProvider locks,
      IHarmonySessionIndexStore sessionIndex,
      IHarmonyToolAvailability? toolAvailability = null)
   {
      _scriptStore = scriptStore ?? throw new ArgumentNullException(nameof(scriptStore));
      _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
      _executor = executor ?? throw new ArgumentNullException(nameof(executor));
      _toolRouter = toolRouter ?? throw new ArgumentNullException(nameof(toolRouter));
      _locks = locks ?? throw new ArgumentNullException(nameof(locks));
      _sessionIndexStore = sessionIndex ?? throw new ArgumentNullException(nameof(sessionIndex));

      _toolAvailability = toolAvailability ?? new AllowAllToolAvailability();
      _preflight = new HarmonyPreflightAnalyzer(_toolAvailability);
   }

   private async Task<HarmonySession> RequireSessionAsync(string sessionId, CancellationToken ct)
   {
      var session = await _sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);
      if (session is null)
         throw new InvalidOperationException($"Session '{sessionId}' was not found.");
      return session;
   }

   private async Task<HarmonyEnvelope> RequireScriptAsync(string scriptId, CancellationToken ct)
   {
      var envelope = await _scriptStore.GetAsync(scriptId, ct).ConfigureAwait(false);
      if (envelope is null)
         throw new InvalidOperationException($"Script '{scriptId}' was not found.");
      return envelope;
   }
}

// -------------------------------------------------------------------------------------------------

/// <summary>
/// Provides methods for executing messages within a harmony session, allowing for both sequential 
/// and specific message execution.
/// </summary>
/// <remarks>This service manages the execution of messages based on the current index of the 
/// session, handling various session states and ensuring proper execution flow. It is designed to
/// work with harmony scripts and can record execution outcomes.</remarks>
public sealed partial class HarmonyExecutionService
{
   /// <summary>
   /// Execute exactly one message at the session's current index.
   /// If the message is context-only, we record it and advance.
   /// If the message contains harmony-script, we execute (current MVP behavior: run script to 
   /// completion) and record outputs, then mark the session completed.
   /// </summary>
   public async Task<HarmonyMessageExecutionRecord> ExecuteNextAsync(
   string sessionId,
   int index = -1, // Optional override for current index...if -1, we use session.CurrentIndex
   IDictionary<string, object?>? input = null,
   string? executionId = null,
   CancellationToken ct = default)
   {
      using var _ = await _locks.AcquireAsync(sessionId, ct).ConfigureAwait(false);
      var session = await RequireSessionAsync(sessionId, ct).ConfigureAwait(false);

      // If already terminal, return a deterministic "skipped" record
      if (session.Status is HarmonySessionStatus.Completed
          or HarmonySessionStatus.Failed
          or HarmonySessionStatus.Cancelled)
      {
         var record = new HarmonyMessageExecutionRecord
         {
            Index = session.CurrentIndex,
            ExecutionId = executionId,
            Status = HarmonyExecutionStatus.Skipped,
            CompletedAt = DateTimeOffset.UtcNow,
            Logs = { $"Session is terminal ({session.Status}); no execution performed." }
         };

         session.History.Add(record);
         if (!string.IsNullOrWhiteSpace(executionId))
            session.ExecutionIdIndex[executionId] = session.History.Count - 1;

         session.UpdatedAt = DateTimeOffset.UtcNow;
         await _sessionStore.SaveAsync(session, ct).ConfigureAwait(false);

         return record;
      }

      // Load envelope once (and reuse)
      var envelope = await RequireScriptAsync(session.ScriptId, ct).ConfigureAwait(false);

      if (index < 0 || index >= envelope.Messages.Count)
         throw new ArgumentOutOfRangeException(nameof(index), "Message index is out of range.");

      // If out of range, mark completed and return
      if (session.CurrentIndex < 0 || session.CurrentIndex >= envelope.Messages.Count)
      {
         session.Status = HarmonySessionStatus.Completed;
         session.UpdatedAt = DateTimeOffset.UtcNow;
         await _sessionStore.SaveAsync(session, ct).ConfigureAwait(false);

         return new HarmonyMessageExecutionRecord
         {
            Index = session.CurrentIndex,
            ExecutionId = executionId,
            Status = HarmonyExecutionStatus.Skipped,
            CompletedAt = DateTimeOffset.UtcNow,
            Logs = { "No remaining messages to execute. Session marked Completed." }
         };
      }

      var idx = session.CurrentIndex;

      // Idempotency: if this executionId already ran for this message, return prior record
      var existing = TryGetIdempotentResult(session, idx, executionId);
      if (existing is not null)
         return existing;

      // Execute at current index
      return await ExecuteAtIndexAsync(
         session: session,
         envelope: envelope,
         index: idx,
         input: input,
         executionId: executionId,
         ct: ct).ConfigureAwait(false);
   }

   /// <summary>
   /// Executes one specific message index and returns its execution record.
   /// Does not require the index to match session.CurrentIndex (useful for inspection/debug).
   /// Pointer advancement is handled by ExecuteAtIndexAsync (it advances past the executed index).
   /// </summary>
   public async Task<HarmonyMessageExecutionRecord> ExecuteMessageAsync(
      string sessionId,
      int index,
      IDictionary<string, object?>? input = null,
      string? executionId = null,
      CancellationToken ct = default)
   {
      using var _ = await _locks.AcquireAsync(sessionId, ct).ConfigureAwait(false);
      var session = await RequireSessionAsync(sessionId, ct).ConfigureAwait(false);

      // Load envelope once (and reuse)
      var envelope = await RequireScriptAsync(session.ScriptId, ct).ConfigureAwait(false);

      if (index < 0 || index >= envelope.Messages.Count)
         throw new ArgumentOutOfRangeException(nameof(index), "Message index is out of range.");

      var existing = TryGetIdempotentResult(session, index, executionId);
      if (existing is not null)
         return existing;

      // Execute at requested index
      return await ExecuteAtIndexAsync(
         session: session,
         envelope: envelope,
         index: index,
         input: input,
         executionId: executionId,
         ct: ct).ConfigureAwait(false);
   }

   /// <summary>
   /// Attempts to retrieve a previously recorded execution result from the session history that 
   /// matches the specified execution identifier and message index.
   /// </summary>
   /// <remarks>If the execution identifier is null or white space, the method returns null 
   /// immediately. The method first attempts to locate the result using a fast index lookup for
   /// efficiency, and if not found, performs a fallback scan through the session history.</remarks>
   /// <param name="session">The current Harmony session containing the execution history and 
   /// index.</param>
   /// <param name="messageIndex">The index of the message for which to retrieve the execution 
   /// result.</param>
   /// <param name="executionId">The unique identifier for the execution to locate. This value 
   /// cannot be null or consist only of white-space characters.</param>
   /// <returns>A HarmonyMessageExecutionRecord representing the matching execution result if found;
   /// otherwise, null.</returns>
   private static HarmonyMessageExecutionRecord? TryGetIdempotentResult(
      HarmonySession session,
      int messageIndex,
      string? executionId)
   {
      if (string.IsNullOrWhiteSpace(executionId))
         return null;

      // Fast path if you keep the index
      if (session.ExecutionIdIndex.TryGetValue(executionId, out var historyPos))
      {
         if (historyPos >= 0 && historyPos < session.History.Count)
         {
            var existing = session.History[historyPos];
            if (existing.Index == messageIndex)
               return existing;
         }
      }

      // Fallback scan (safe)
      foreach (var r in session.History)
      {
         if (string.Equals(r.ExecutionId, executionId, StringComparison.OrdinalIgnoreCase) &&
             r.Index == messageIndex)
         {
            return r;
         }
      }

      return null;
   }

}

// -------------------------------------------------------------------------------------------------
/// <summary>
/// Partial: session lifecycle helpers.
/// </summary>
public sealed partial class HarmonyExecutionService
{
   /// <summary>
   /// Starts a new execution session for a registered script.
   /// Optionally seeds session Vars and Metadata.
   /// </summary>
   public async Task<HarmonySession> StartSessionAsync(
      string scriptId,
      IDictionary<string, object?>? initialVars = null,
      IDictionary<string, string>? metadata = null,
      CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(scriptId))
         throw new ArgumentException("scriptId must be provided.", nameof(scriptId));

      // Ensure script exists (fail fast)
      var envelope = await _scriptStore.GetAsync(scriptId, ct).ConfigureAwait(false);
      if (envelope is null)
         throw new InvalidOperationException($"Script '{scriptId}' was not found.");

      // Create new session
      var session = await _sessionStore.CreateAsync(scriptId, ct).ConfigureAwait(false);

      // Seed vars (optional)
      if (initialVars is not null)
      {
         foreach (var kvp in initialVars)
            session.Vars[kvp.Key] = kvp.Value;
      }

      // Seed metadata (optional)
      if (metadata is not null)
      {
         foreach (var kvp in metadata)
            session.Metadata[kvp.Key] = kvp.Value;
      }

      // Initialize pointer to first message (0) and status
      session.CurrentIndex = 0;
      session.Status = HarmonySessionStatus.Created;
      session.UpdatedAt = DateTimeOffset.UtcNow;

      await _sessionStore.SaveAsync(session, ct).ConfigureAwait(false);
      return session;
   }
}

/// <summary>
/// Provides functionality to manage the execution of harmony sessions, enabling the replay and
/// resumption of chat conversations.
/// </summary>
/// <remarks>This service is designed to facilitate the reconstruction of chat conversations from 
/// durable session transcripts, ensuring that the execution flow is maintained accurately.
/// </remarks>
public sealed partial class HarmonyExecutionService
{
   /// <summary>
   /// Rebuilds a ChatConversation from the durable session transcript.
   /// This is the canonical way to reconstruct model input for replay,
   /// resume, or message-by-message execution.
   /// </summary>
   private static ChatConversation BuildChatHistoryFromSessionTranscript(
      HarmonySession session)
   {
      if (session is null)
         throw new ArgumentNullException(nameof(session));

      var conversation = new ChatConversation();

      // Ensure chronological order
      var ordered = session.Transcript
         .OrderBy(m => m.Timestamp)
         .ToList();

      foreach (var entry in ordered)
      {
         if (string.IsNullOrWhiteSpace(entry.Content))
            continue;

         conversation.Add(new ChatMessage(
            role: entry.Role,
            content: entry.Content,
            sourceIndex: entry.SourceIndex
         ));
      }

      return conversation;
   }
}

/// <summary>
/// Provides services for executing messages within a Harmony session, managing state and history
/// throughout the execution process.
/// </summary>
/// <remarks>This class is responsible for handling both context-only and executable messages,
/// ensuring that the session's state is updated accordingly. It manages the persistence of 
/// session and execution records after each operation, allowing for a robust execution flow 
/// within Harmony sessions.</remarks>
public sealed partial class HarmonyExecutionService
{

   /// <summary>
   /// Executes the message at the specified index within a Harmony session, processing the message 
   /// and updating the session state accordingly.
   /// </summary>
   /// <remarks>This method supports both context-only and executable messages within a Harmony 
   /// session. It updates the session's state, history, and artifacts based on the execution 
   /// outcome. If the message is not executable, it is skipped and the session advances to the next
   /// message. The method ensures that session and execution records are persisted after each 
   /// operation.</remarks>
   /// <param name="session">The HarmonySession instance representing the current session context. 
   /// This object is used to track the session's state, history, and variables throughout
   /// execution.</param>
   /// <param name="envelope">An optional HarmonyEnvelope containing the set of messages to be 
   /// executed. If null, the envelope is retrieved based on the session's script identifier.
   /// </param>
   /// <param name="index">The zero-based index of the message to execute within the envelope's 
   /// message collection. Must be within the bounds of the envelope's messages.</param>
   /// <param name="input">An optional dictionary of input parameters to be merged with the 
   /// session's existing inputs. These values can influence the execution of the message.</param>
   /// <param name="executionId">An optional unique identifier for this execution instance, used for 
   /// tracking and correlation purposes.</param>
   /// <param name="ct">A CancellationToken that can be used to cancel the execution operation.</param>
   /// <returns>A task that represents the asynchronous operation. The task result contains a 
   /// HarmonyMessageExecutionRecord describing the outcome of the message execution, including status,
   /// outputs, and any errors encountered.</returns>
   /// <exception cref="InvalidOperationException">Thrown if the envelope cannot be found for the 
   /// session's script identifier.</exception>
   private async Task<HarmonyMessageExecutionRecord> ExecuteAtIndexAsync(
      HarmonySession session,
      HarmonyEnvelope? envelope,
      int index,
      IDictionary<string, object?>? input,
      string? executionId,
      CancellationToken ct)
   {
      if (session is null) throw new ArgumentNullException(nameof(session));

      // Allow caller to pass envelope or let service load it.
      envelope = envelope ?? await _scriptStore.GetAsync(session.ScriptId, ct).ConfigureAwait(false);
      if (envelope is null)
         throw new InvalidOperationException("Script not found.");

      if (index < 0 || index >= envelope.Messages.Count)
         throw new ArgumentOutOfRangeException(nameof(index), "Message index is out of range.");

      var msg = envelope.Messages[index];

      var record = new HarmonyMessageExecutionRecord
      {
         Index = index,
         ExecutionId = executionId,
         Status = HarmonyExecutionStatus.Running,
         Inputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
         {
            ["sessionId"] = session.SessionId,
            ["scriptId"] = session.ScriptId,
            ["currentIndex"] = session.CurrentIndex,
            ["executeIndex"] = index
         }
      };

      // Track explicit inputs (not required, but useful)
      if (input is not null)
      {
         foreach (var kvp in input)
            record.Inputs[$"input.{kvp.Key}"] = kvp.Value;
      }

      try
      {
         session.Status = HarmonySessionStatus.Running;

         // 1) Context-only message => append to durable transcript + advance
         if (IsContextOnly(msg))
         {
            var text = GetMessageText(msg);

            if (!string.IsNullOrWhiteSpace(text))
            {
               AppendToTranscript(session, role: msg.Role, content: text, sourceIndex: index);

               record.Outputs.Add(MakeTextArtifact(
                  name: "message",
                  text: text,
                  producer: "context"));

               record.Logs.Add(
                  $"Applied context message to transcript: {msg.Role} [{msg.Channel}]");
            }
            else
            {
               record.Logs.Add("Context message had empty content; nothing appended.");
            }

            record.Status = HarmonyExecutionStatus.Succeeded;
            record.CompletedAt = DateTimeOffset.UtcNow;

            // Advance pointer past this message
            session.CurrentIndex = Math.Max(session.CurrentIndex, index + 1);
            session.UpdatedAt = DateTimeOffset.UtcNow;

            session.History.Add(record);
            await _sessionStore.SaveAsync(session, ct).ConfigureAwait(false);
            return record;
         }

         // 2) Executable message => for MVP, we execute envelope script with
         //    chat history rebuilt from transcript, then append assistant final.
         if (IsHarmonyScript(msg))
         {
            var preflight = await _preflight.AnalyzeAsync(envelope, ct).ConfigureAwait(false); ;
            if (!preflight.IsReady)
            {
               // 1) Compact transcript marker (optional but recommended)
               AppendToTranscript(
                  session,
                  role: "system",
                  content: HarmonyTranscriptWriter.PreflightBlockedSummary(preflight.MissingRecipients.Count),
                  sourceIndex: index);

               // 2) Structured artifact for MCP/UI
               record.Outputs.Add(new HarmonyArtifact
               {
                  Name = "preflight",
                  ContentType = "preflight",
                  Content = JsonSerializer.SerializeToElement(preflight),
                  Producer = "preflight"
               });

               // 3) Mark record + session
               record.Status = HarmonyExecutionStatus.Blocked;
               record.CompletedAt = DateTimeOffset.UtcNow;

               session.Status = HarmonySessionStatus.Blocked;
               session.CurrentIndex = Math.Max(session.CurrentIndex, index); // do NOT advance on blocked
               session.UpdatedAt = DateTimeOffset.UtcNow;

               // 4) Persist history + idempotency index
               session.History.Add(record);
               if (!string.IsNullOrWhiteSpace(record.ExecutionId))
                  session.ExecutionIdIndex[record.ExecutionId] = session.History.Count - 1;

               await _sessionStore.SaveAsync(session, ct).ConfigureAwait(false);
               return record;
            }

            record.Logs.Add("Executing harmony-script with session transcript as chat history.");

            // Build the prompt-ready chat history from the durable transcript
            var chatHistory = BuildChatHistoryFromSessionTranscript(session);

            // Merge session vars into the input context (core runtime memory)
            // and allow per-call input to override/add.
            var execInput = new Dictionary<string, object?>(
               session.Vars, StringComparer.OrdinalIgnoreCase);
            if (input is not null)
            {
               foreach (var kvp in input)
                  execInput[kvp.Key] = kvp.Value;
            }

            // Wrap tool router to capture tool traces into history
            var recordingRouter = new RecordingToolExecutionService(
               inner: _toolRouter, 
               onTrace: trace =>
               {
                  // Store each trace as an artifact in the message execution record
                  var artifact = new HarmonyArtifact
                  {
                     Name = $"tool:{trace.Recipient}",
                     ContentType = "tool-trace",
                     Content = JsonSerializer.SerializeToElement(trace),
                     Producer = trace.Recipient
                  };
                  record.Outputs.Add(artifact);

                  // Optional: keep the latest tool trace in session artifacts too
                  session.Artifacts["last_tool_trace"] = artifact;

                  // compact transcript entry (keeps transcript readable)
                  var duration = (trace.CompletedAt.HasValue)
                     ? trace.CompletedAt.Value - trace.StartedAt
                     : (TimeSpan?)null;

                  AppendToTranscript(
                     session,
                     role: "assistant",
                     content: HarmonyTranscriptWriter.ToolSummary(
                        trace.Recipient, trace.Succeeded, duration),
                     sourceIndex: index);

                  record.Logs.Add(
                     trace.Succeeded
                        ? $"Tool succeeded: {trace.Recipient}"
                        : $"Tool failed: {trace.Recipient}");
               });

            // Execute with overridden router
            var result = await _executor.ExecuteAsync(
               envelope,
               execInput,
               chatHistoryOverride: chatHistory,
               toolRouterOverride: recordingRouter,
               ct).ConfigureAwait(false);

            // Persist vars back into session state
            session.Vars = result.Vars ?? 
               new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            // Record final output as artifact + append to transcript
            if (!string.IsNullOrWhiteSpace(result.FinalText))
            {
               var finalArtifact = MakeTextArtifact(
                  name: "final",
                  text: result.FinalText,
                  producer: "llm/assistant");

               record.Outputs.Add(finalArtifact);
               session.Artifacts["final"] = finalArtifact;

               AppendToTranscript(
                  session, role: "assistant", content: result.FinalText, sourceIndex: index);
            }

            if (result.IsError)
            {
               record.Status = HarmonyExecutionStatus.Failed;
               record.Error = result.Error;
               session.Status = HarmonySessionStatus.Failed;
               record.Logs.Add("Execution failed; session marked Failed.");
            }
            else
            {
               record.Status = HarmonyExecutionStatus.Succeeded;

               // MVP behavior: assume this completes the script execution
               session.Status = HarmonySessionStatus.Completed;
               record.Logs.Add("Execution succeeded; session marked Completed (MVP behavior).");
            }

            record.CompletedAt = DateTimeOffset.UtcNow;

            // Advance pointer past this message
            session.CurrentIndex = Math.Max(session.CurrentIndex, index + 1);
            session.UpdatedAt = DateTimeOffset.UtcNow;

            session.History.Add(record);

            // Register executionId for dedupe (only if provided)
            if (!string.IsNullOrWhiteSpace(record.ExecutionId))
            {
               session.ExecutionIdIndex[record.ExecutionId] = session.History.Count - 1;
            }

            await _sessionStore.SaveAsync(session, ct).ConfigureAwait(false);
            return record;
         }

         // 3) Future: assistant tool-call termination, etc. MVP: record as skipped and advance
         record.Status = HarmonyExecutionStatus.Skipped;
         record.Logs.Add("Message not executable in MVP; skipped.");

         record.CompletedAt = DateTimeOffset.UtcNow;

         session.CurrentIndex = Math.Max(session.CurrentIndex, index + 1);
         session.UpdatedAt = DateTimeOffset.UtcNow;

         session.History.Add(record);
         await _sessionStore.SaveAsync(session, ct).ConfigureAwait(false);
         return record;
      }
      catch (Exception ex)
      {
         record.Status = HarmonyExecutionStatus.Failed;
         record.Error = new HarmonyError
         {
            Code = "EXECUTION_SERVICE_ERROR",
            Message = "ExecutionService failed to process message.",
            Details = new { exception = ex.GetType().Name, message = ex.Message }
         };
         record.CompletedAt = DateTimeOffset.UtcNow;

         session.Status = HarmonySessionStatus.Failed;
         session.UpdatedAt = DateTimeOffset.UtcNow;

         session.History.Add(record);
         await _sessionStore.SaveAsync(session, ct).ConfigureAwait(false);

         return record;
      }
   }

   // Helpers
   // ----------------------------------------------------------------------------------------------
   private static bool IsHarmonyScript(HarmonyMessage msg)
   {
      return msg.ContentType?.Equals(
         "harmony-script", StringComparison.OrdinalIgnoreCase) == true &&
         msg.Content.ValueKind == JsonValueKind.Object;
   }

   private static bool IsContextOnly(HarmonyMessage msg)
   {
      if (IsHarmonyScript(msg)) return false;
      if (msg.Termination is not null) return false;

      // No contentType => plain text per your semantic rules
      if (string.IsNullOrWhiteSpace(msg.ContentType) && 
         msg.Content.ValueKind == JsonValueKind.String)
         return true;

      // contentType=text treated as plain text
      if (msg.ContentType?.Equals("text", StringComparison.OrdinalIgnoreCase) == true &&
          msg.Content.ValueKind == JsonValueKind.String)
         return true;

      return false;
   }

   private static string GetMessageText(HarmonyMessage msg)
   {
      return msg.Content.ValueKind == JsonValueKind.String
         ? (msg.Content.GetString() ?? string.Empty)
         : msg.Content.ToString();
   }

   private static void AppendToTranscript(
      HarmonySession session,
      string role,
      string content,
      int sourceIndex)
   {
      if (string.IsNullOrWhiteSpace(content))
         return;

      session.Transcript.Add(new HarmonyChatMessage
      {
         Role = HarmonyTranscriptWriter.NormalizeRole(role),
         Content = content.Trim(),
         SourceIndex = sourceIndex,
         Timestamp = DateTimeOffset.UtcNow
      });
   }

   private static HarmonyArtifact MakeTextArtifact(string? name, string text, string? producer)
   {
      var json = JsonSerializer.SerializeToElement(text);

      return new HarmonyArtifact
      {
         Name = name,
         ContentType = "text",
         Content = json,
         Producer = producer
      };
   }
}
