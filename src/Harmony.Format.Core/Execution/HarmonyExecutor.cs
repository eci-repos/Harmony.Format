using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format;

/// <summary>
/// Executes Harmony workflows by orchestrating chat-based operations, tool invocations, and
/// conditional logic using a provided kernel and chat completion service.
/// </summary>
/// <remarks>HarmonyExecutor coordinates the execution of multi-step workflows defined in Harmony 
/// envelopes, managing context variables, chat history, and plugin function calls. It is designed 
/// for scenarios where conversational AI and workflow automation are integrated, such as chatbots 
/// or virtual assistants. This class is intended to be used as a top-level executor and is not 
/// thread-safe; concurrent usage should be managed externally.</remarks>
public sealed class HarmonyExecutor
{

   private readonly ILanguageModelChatService _chatService;
   private readonly IToolExecutionService _toolRouter;

   private readonly JsonSerializerOptions _jsonOpts;

   public HarmonyExecutor(ILanguageModelChatService chatService, IToolExecutionService toolRouter,
      JsonSerializerOptions? jsonOptions = null)
   {
      _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
      _toolRouter = toolRouter ?? throw new ArgumentNullException(nameof(toolRouter));
      _jsonOpts = jsonOptions ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
   }

   /// <summary>
   /// Checks if the given channel is valid for tool calls (i.e., "commentary").
   /// </summary>
   /// <param name="channel">expected channel text</param>
   /// <returns>true is returned if channel value is commentary as expected, elese false</returns>
   /// <exception cref="ArgumentNullException"></exception>
   public static bool IsToolChannel(string channel)
   {
      if (channel is null) throw new ArgumentNullException(nameof(channel));
      return channel.Equals(HarmonyConstants.ChannelCommentary, StringComparison.OrdinalIgnoreCase);
   }

   public static void EnsureToolChannel(string channel)
   {
      if (!IsToolChannel(channel))
         throw new InvalidOperationException(
             "HRF violation: tool calls must use channel='commentary' per Harmony conventions.");
   }

   /// <summary>
   /// Executes the Harmony script defined in the specified envelope using the provided input 
   /// variables and returns the result asynchronously.
   /// </summary>
   /// <remarks>If the script does not produce an explicit final result, a summary is generated 
   /// automatically from the execution context. The method executes all steps in the script 
   /// sequentially and may halt early if a step requests it.</remarks>
   /// <param name="envelope">The envelope containing the Harmony script and associated metadata
   /// to be executed. Cannot be null.</param>
   /// <param name="input">A dictionary of input variables to be supplied to the script. Keys are 
   /// variable names; values are their
   /// corresponding values. May be empty if no inputs are required.</param>
   /// <param name="chatHistoryOverride">An optional chat history to use instead of building one
   /// from the envelope's messages.</param> 
   /// <param name="toolRouterOverride">An optional override for the tool execution service. 
   /// If provided, this instance will be used for all tool invocations during execution instead 
   /// of the default service configured in the executor.</param>
   /// <param name="ct">A cancellation token that can be used to cancel the execution operation.
   /// </param>
   /// <returns>A task that represents the asynchronous operation. The task result contains a 
   /// HarmonyExecutionResult with the
   /// final output text and any updated variables from the script execution.</returns>
   public async Task<HarmonyExecutionResult> ExecuteAsync(
      HarmonyEnvelope envelope,
      IDictionary<string, object?> input,
      ChatConversation? chatHistoryOverride,
      IToolExecutionService? toolRouterOverride = null,
      CancellationToken ct = default)
   {
      // Full HRF validation (shema + semantics) before execution
      var hrfError = envelope.ValidateForHrf();
      if (hrfError != null)
      {
         // Surface HRF issues as a structured error result
         return HarmonyExecutionResult.ErrorResult(
            hrfError.Code,
            message: $"Invalid Harmony envelope: {hrfError.Message}",
            details: hrfError.Details);
      }

      // Extract 'harmony-script'
      var script = envelope.GetScript();
      if (script == null)
      {
         return HarmonyExecutionResult.ErrorResult(
            code: "MISSING_HARMONY_SCRIPT",
            message: "Error: Harmony envelope does not contain a valid 'harmony-script' section.");
      }

      // Initialize vars (from script.vars defaults)
      var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
      if (script != null && script.Vars is not null)
      {
         foreach (var kvp in script.Vars)
         {
            vars[kvp.Key] = ExecutorContext.FromJsonElement(kvp.Value);
         }
      }

      // Use caller-provided chat history if supplied; otherwise build it
      ChatConversation chatHistory = chatHistoryOverride ?? BuildInitialChatHistory(envelope);

      // Prepare execution context
      var execCtx = new ExecutorContext(
         _chatService, toolRouterOverride ?? _toolRouter, chatHistory, vars, input, _jsonOpts);

      // Execute steps sequentially
      try
      {
         if (script?.Steps is null || script.Steps.Count == 0)
         {
            return HarmonyExecutionResult.ErrorResult(
               code: "NO_HARMONY_STEPS",
               message: "Error: Harmony script contains no steps to execute.");
         }
         foreach (var step in script.Steps)
         {
            var halted = await ExecuteStepAsync(execCtx, step, ct);
            if (halted) break;

            // Only check NEW messages since last check
            //while (processedMessageCount < envelope.Messages.Count)
            //{
            //   var msg = envelope.Messages[processedMessageCount++];
            //   if (string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            //   {
            //      switch (msg.Termination)
            //      {
            //         case HarmonyTermination.end:
            //            return Finalize(execCtx, "Execution ended by termination marker.");
            //         case HarmonyTermination.@return:
            //            return Finalize(execCtx, execCtx.FinalText);
            //         case HarmonyTermination.call:
            //            await HandleToolCallAsync(execCtx, msg, ct);
            //            break;
            //      }
            //   }
            //}
         }
      }
      catch (Exception ex) 
       when (ex is InvalidOperationException
             || ex is JsonException
             || ex is FormatException)
      {
         // Treat all such failures as HRF execution violations
         return HarmonyExecutionResult.ErrorResult(
            code: "HRF_EXECUTION_ERROR",
            message: "Error: Harmony execution failed due to HRF violation.",
            details: new
            {
               exception = ex.GetType().Name,
               message = ex.Message
            });
      }

      // Fallback: If no explicit final text, ask the LLM to summarize
      if (string.IsNullOrWhiteSpace(execCtx.FinalText))
      {
         // As a fallback, ask LLM to compile a final answer from the context
         chatHistory.AddSystemMessage(
            "Summarize the results from the executed plan above for the user.");
         execCtx.FinalText = 
            await _chatService.GetAssistantReplyAsync(execCtx.ChatHistory, ct) ?? string.Empty;
      }

      return Finalize(execCtx, execCtx.FinalText);
   }

   /// <summary>
   /// Helper to builds the old-style initial prompt history (preserves behavior)
   /// </summary>
   /// <param name="envelope"></param>
   /// <returns></returns>
   // ----------------------------------------------------------------------------------------------
   private static ChatConversation BuildInitialChatHistory(HarmonyEnvelope envelope)
   {
      var chatHistory = new ChatConversation();

      foreach (var (channel, content) in envelope.GetPlainSystemPrompts())
      {
         if (!string.IsNullOrWhiteSpace(content))
            chatHistory.AddSystemMessage(content);
      }

      var user = envelope.GetUserMessage();
      if (user is { } u && !string.IsNullOrWhiteSpace(u.Content))
      {
         chatHistory.AddUserMessage(u.Content);
      }

      return chatHistory;
   }

   /// <summary>
   /// Finalizes the execution result by packaging the final text and variables.
   /// </summary>
   /// <param name="ctx">execution context</param>
   /// <param name="finalText">final text</param>
   /// <returns>HarmonyExecutionResult</returns>
   private HarmonyExecutionResult Finalize(ExecutorContext ctx, string finalText) =>
       new HarmonyExecutionResult { FinalText = finalText, Vars = new(ctx.Vars) };

   /// <summary>
   /// Handles tool call terminations by invoking the specified plugin function.
   /// </summary>
   /// <param name="ctx">execution context</param>
   /// <param name="msg">harmony message</param>
   /// <returns>Task</returns>
   /// <exception cref="InvalidOperationException"></exception>
   private async Task HandleToolCallAsync(ExecutorContext ctx, HarmonyMessage msg, CancellationToken ct)
   {
      // Extract from HRF assistant message
      var (recipient, rawArgs) = ExtractToolCallFromMessage(msg, ctx.JsonOptions);

      // Evaluate expressions inside arguments, same as ToolCallStep
      var resolvedArgs = new Dictionary<string, object?>();
      foreach (var kvp in rawArgs)
      {
         if (kvp.Value is string s && s.StartsWith("$"))
            resolvedArgs[kvp.Key] = EvaluateExpression(ctx, s);
         else
            resolvedArgs[kvp.Key] = kvp.Value;
      }

      // Make the actual tool call
      var result = await ctx.ToolRouter.InvokeToolAsync(recipient, resolvedArgs, ct);

      ctx.Vars["last_tool_result"] = result;
   }

   #region -- 4.00 - Evaluate Tool Call Arguments

   private bool ValidateExpressionSyntax(string expr)
   {
      // More comprehensive validation
      return Regex.IsMatch(expr, @"^\$(?:vars\.|input\.|len\(|map\()");
   }

   private IReadOnlyDictionary<string, object?> EvaluateToolArgs(ExecutorContext ctx, ToolCallStep step)
   {
      var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

      if (step.Args is null || step.Args.Count == 0)
         return new ReadOnlyDictionary<string, object?>(result);

      foreach (var kvp in step.Args)
      {
         var name = kvp.Key;
         var element = kvp.Value;

         object? value;

         // If it's a string starting with '$', treat as an expression.
         if (element.ValueKind == JsonValueKind.String)
         {
            var s = element.GetString() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(s) && s[0] == '$')
            {
               value = EvaluateExpression(ctx, s);
            }
            else
            {
               // Plain string literal argument
               value = s;
            }
         }
         else
         {
            // Non-string JSON: convert to a .NET object using your existing helper
            value = ExecutorContext.FromJsonElement(element);
         }

         result[name] = value;
      }

      return new ReadOnlyDictionary<string, object?>(result);
   }

   private object? EvaluateExpression(ExecutorContext ctx, string expr)
   {
      // Very simple expression model:
      //   $input.userId
      //   $input.profile.name
      //   $vars.temperature
      //   $vars.recipeQuery
      //
      // Anything we don't understand we just return as-is (string).

      if (expr.StartsWith(ExecutorContext.ExprPrefixInput, StringComparison.OrdinalIgnoreCase))
      {
         var path = expr.Substring(ExecutorContext.ExprPrefixInput.Length);
         return ResolvePath(ctx.Input, path);
      }

      if (expr.StartsWith(ExecutorContext.ExprPrefixVars, StringComparison.OrdinalIgnoreCase))
      {
         var path = expr.Substring(ExecutorContext.ExprPrefixVars.Length);
         return ResolvePath(ctx.Vars, path);
      }

      // Fallback: unknown expression syntax -> just return the raw string
      return expr;
   }

   private static object? ResolvePath(IDictionary<string, object?> root, string path)
   {
      if (string.IsNullOrWhiteSpace(path))
         return root;

      var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
      object? current = root;

      foreach (var seg in segments)
      {
         if (current is IDictionary<string, object?> dict)
         {
            if (!dict.TryGetValue(seg, out current))
            {
               // Missing key -> null
               return null;
            }
         }
         else
         {
            // Can't traverse further (e.g., primitive, unsupported type)
            return null;
         }
      }

      return current;
   }

   #endregion
   #region -- 4.00 - Extract Tool Call From Message

   private (string Recipient, IReadOnlyDictionary<string, object?> Args)
      ExtractToolCallFromMessage(HarmonyMessage msg, JsonSerializerOptions jsonOpts)
   {
      if (msg is null)
         throw new ArgumentNullException(nameof(msg));

      // 1. Tool recipient is mandatory for call termination
      if (string.IsNullOrWhiteSpace(msg.Recipient))
      {
         throw new InvalidOperationException(
             "Tool call message must specify a 'recipient' field (e.g., plugin.function).");
      }

      string recipient = msg.Recipient;

      // 2. Extract arguments from msg.Content depending on contentType
      var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

      if (msg.ContentType?.Equals(
         HarmonyConstants.ContentTypeJson, StringComparison.OrdinalIgnoreCase) == true)
      {
         if (msg.Content.ValueKind == JsonValueKind.Object)
         {
            foreach (var prop in msg.Content.EnumerateObject())
            {
               object? value = ConvertJsonValue(prop.Value, jsonOpts);

               // Support HRF-style expressions: "$vars.x", "$input.y"
               if (value is string s && s.StartsWith("$", StringComparison.Ordinal))
               {
                  // Leave expression evaluation to EvaluateExpression/EvaluateToolArgs
                  // or you can evaluate it here if desired.
                  args[prop.Name] = s;
               }
               else
               {
                  args[prop.Name] = value;
               }
            }
         }
         else
         {
            throw new InvalidOperationException(
                "Tool-call assistant message with contentType=json must contain a JSON object.");
         }
      }
      else if (msg.ContentType is null)
      {
         // Optional: support plain text content interpreted as a single argument
         // Example: recipient="shell.run", content="ls -la"
         string? text = msg.Content.ValueKind == JsonValueKind.String
             ? msg.Content.GetString()
             : JsonSerializer.Serialize(msg.Content, jsonOpts);

         args["input"] = text;
      }
      else
      {
         throw new InvalidOperationException(
             $"Unsupported contentType '{msg.ContentType}' for tool-call assistant message.");
      }

      return (recipient, args);
   }

   private object? ConvertJsonValue(JsonElement element, JsonSerializerOptions jsonOpts)
   {
      return element.ValueKind switch
      {
         JsonValueKind.String => element.GetString(),
         JsonValueKind.Number => element.TryGetInt64(out long l) ? l :
                                 element.TryGetDouble(out double d) ? d : (object?)element.ToString(),
         JsonValueKind.True => true,
         JsonValueKind.False => false,
         JsonValueKind.Null => null,
         JsonValueKind.Array => JsonSerializer.Deserialize<object?[]>(element, jsonOpts),
         JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(element, jsonOpts),
         _ => JsonSerializer.Serialize(element, jsonOpts),
      };
   }

   #endregion

   private async Task<bool> ExecuteToolCallStepAsync(
      ExecutorContext ctx, ToolCallStep step, CancellationToken ct)
   {
      // Evaluate arguments (expressions -> concrete values)
      var argsDict = EvaluateToolArgs(ctx, step);

      // Invoke tool via the *context* router so overrides (recording/tracing) work
      var result = await ctx.ToolRouter.InvokeToolAsync(step.Recipient, argsDict, ct);

      // Store result in vars under step.SaveAs
      ctx.Vars[step.SaveAs] = result;

      // Optionally add commentary to chat history (since channel is commentary)
      // if you want the LLM to see the tool result later.
      // e.g., ctx.ChatHistory.AddAssistantMessage(
      //    $"[tool:{step.Recipient}] {Json.Serialize(result)}");

      return false; // tool-call doesn’t halt by itself
   }

   /// <summary>
   /// Executes a single workflow step asynchronously within the provided execution context.
   /// </summary>
   /// <remarks>This method processes various step types, such as input extraction, tool invocation,
   /// conditional branching, assistant messaging, and halting. The behavior and side effects 
   /// depend on the specific step provided. The method may update context variables, chat history,
   /// or final output text as part of execution.</remarks>
   /// <param name="ctx">The execution context that maintains state, variables, and services 
   /// required for step execution.</param>
   /// <param name="step">The workflow step to execute. The type of step determines the specific 
   /// action performed.</param>
   /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.
   /// </param>
   /// <returns>A task that represents the asynchronous operation. The task result is 
   /// <see langword="true"/> if execution should be halted; otherwise, 
   /// <see langword="false"/>.</returns>
   /// <exception cref="InvalidOperationException">Thrown if a referenced plugin function cannot be 
   /// found during a tool call step.</exception>
   /// <exception cref="NotSupportedException">Thrown if the specified step type is not supported.
   /// </exception>
   private async Task<bool> ExecuteStepAsync(
      ExecutorContext ctx, HarmonyStep step, CancellationToken ct)
   {
      switch (step)
      {
         case ExtractInputStep s:
            foreach (var (varName, expr) in s.Output)
            {
               if (!ValidateExpressionSyntax(expr))
                  throw new InvalidOperationException($"Invalid expression syntax: {expr}");
               var value = ctx.EvalExpression(expr);
               ctx.Vars[varName] = value;
            }
            return false;

         case ToolCallStep s:
            {
               return await ExecuteToolCallStepAsync(ctx, s, ct);
            }

         case IfStep s:
            {
               if (!ValidateExpressionSyntax(s.Condition))
                  throw new InvalidOperationException($"Invalid condition syntax: {s.Condition}");

               var condition = ctx.EvalBoolean(s.Condition);

               var branch = condition ? s.Then : s.Else;
               foreach (var inner in branch)
               {
                  var halted = await ExecuteStepAsync(ctx, inner, ct);
                  if (halted) return true;
               }
               return false;
            }

         case AssistantMessageStep s:
            {
               var channelText = s.Channel ?? string.Empty;
               var channelLower = channelText.ToLowerInvariant();

               // Allowed HRF channels for assistant-message steps: analysis | final
               if (channelLower != HarmonyConstants.ChannelAnalysis && 
                  channelLower != HarmonyConstants.ChannelFinal)
               {
                  throw new InvalidOperationException(
                     $"Invalid channel '{s.Channel}' for assistant-message step. " +
                     "Expected 'analysis' or 'final'.");
               }

               // Render content or template
               var text = !string.IsNullOrWhiteSpace(s.ContentTemplate)
                   ? ctx.RenderTemplate(s.ContentTemplate!)
                   : (s.Content ?? string.Empty);

               if (channelLower == HarmonyConstants.ChannelAnalysis)
               {
                  // Analysis is internal-only; do not surface to end user
                  if (!string.IsNullOrWhiteSpace(text))
                     ctx.ChatHistory.AddAssistantMessage(text);
                  return false;
               }

               // channelLower == "final"
               if (!string.IsNullOrWhiteSpace(text) && text != ".")
               {
                  // If template produced material text, use it directly as final answer
                  ctx.FinalText = text;
                  return false;
               }

               // Otherwise, ask LLM to produce final answer given the history
               ctx.FinalText = 
                  await _chatService.GetAssistantReplyAsync(ctx.ChatHistory, ct);
               return false;
            }

         case HaltStep:
            return true;

         default:
            throw new NotSupportedException($"Unsupported step type: {step.Type}");
      }
   }

}

