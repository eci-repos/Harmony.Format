using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Harmony.Tooling.Contracts;
using Harmony.Tooling.Models;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.SemanticKernel;


/// <summary>
/// Statics for interoperation between Harmony conversation format and Semantic Kernel.
/// </summary>
public static class KernelInterop
{
   /// <summary>
   /// Converts a <see cref="HarmonyConversation"/> instance into a
   /// <see cref="ChatHistory"/> object.
   /// </summary>
   /// <remarks>
   /// This method processes the messages in the provided <see cref="HarmonyConversation"/>
   /// and maps them to the appropriate roles in the <see cref="ChatHistory"/>:
   /// <list type="bullet">
   ///    <item>
   ///       <description>
   ///       Messages with the <c>system</c> or <c>developer</c> role are added as system 
   ///       messages.
   ///       </description>
   ///    </item>
   ///    <item>
   ///       <description>
   ///       Messages with the <c>user</c> role are added as user messages.
   ///       </description>
   ///    </item>
   ///    <item>
   ///       <description>
   ///       Messages with the <c>assistant</c> role and <see cref="HarmonyChannel.Final"/>
   ///       channel are added as assistant messages.
   ///       </description>
   ///    </item>
   ///    <item>
   ///       <description>
   ///       Assistant commentary without a recipient (preambles) can optionally be surfaced
   ///       as assistant messages.
   ///       </description>
   ///    </item>
   ///    <item>
   ///       <description>
   ///       Messages with any other role are interpreted as tool results and mapped to
   ///       <see cref="AuthorRole.Tool"/> messages.
   ///       </description>
   ///    </item>
   /// </list>
   /// </remarks>
   /// <param name="convo">The Harmony conversation to convert.</param>
   /// <returns>A new <see cref="ChatHistory"/> representing the conversation.</returns>
   /// <exception cref="ArgumentNullException">Thrown if <paramref name="convo"/> is null.
   /// </exception>
   public static ChatHistory ToChatHistory(this HarmonyConversation convo)
   {
      if (convo is null) throw new ArgumentNullException(nameof(convo));

      var history = new ChatHistory();

      foreach (var m in convo.Messages ?? Enumerable.Empty<HarmonyMessage>())
      {
         var role = m.Role ?? string.Empty;
         var content = m.Content.GetRawText();// ?? string.Empty;

         switch (role)
         {
            case "system":
            case "developer":
               if (!string.IsNullOrWhiteSpace(content))
                  history.AddSystemMessage(content);
               break;

            case "user":
               if (!string.IsNullOrWhiteSpace(content))
                  history.AddUserMessage(content);
               break;

            case "assistant":
               // Only add assistant messages that the user should see (channel == Final)
               if (m.Channel == HarmonyChannel.final)
               {
                  if (!string.IsNullOrWhiteSpace(content))
                     history.AddAssistantMessage(content);
               }
               // analysis/commentary are internal; Harmony spec advises not to show analysis;
               // commentary may include preambles - add if you want them visible:
               else if (m.Channel == HarmonyChannel.commentary &&
                        string.IsNullOrWhiteSpace(m.Recipient))
               {
                  // Optional: expose preambles to the user
                  if (!string.IsNullOrWhiteSpace(content))
                     history.AddAssistantMessage(content);
               }

               // Tool calls (assistant + commentary + recipient) are not projected into
               // ChatHistory; they are consumed by ExecuteToolCallsAsync instead.
               break;

            default:
               // Tool result: Harmony uses tool name as role (e.g., functions.getweather)
               if (!string.IsNullOrWhiteSpace(content))
               {
                  var toolName = role;
                  history.Add(new ChatMessageContent(AuthorRole.Tool, content)
                  {
                     AuthorName = toolName
                  });
               }
               break;
         }
      }

      return history;
   }

   /// <summary>
   /// Executes tool calls asynchronously based on the messages in the conversation.
   /// </summary>
   /// <remarks>
   /// This method looks for assistant messages on the commentary channel that specify
   /// a tool recipient (e.g. <c>functions.getweather</c>). For each such message it:
   /// <list type="number">
   ///    <item><description>Parses the recipient into plugin and function names.
   ///       </description></item>
   ///    <item><description>Builds <see cref="KernelArguments"/> from the JSON body, if any.
   ///       </description></item>
   ///    <item><description>Invokes the Semantic Kernel function.</description></item>
   ///    <item><description>Appends a tool-result <see cref="HarmonyMessage"/> back to the 
   ///       conversation.</description></item>
   /// </list>
   /// If the tool cannot be found or throws, a JSON error payload is appended instead so that
   /// the model can gracefully recover.
   /// </remarks>
   /// <param name="convo">The Harmony conversation containing tool call messages.</param>
   /// <param name="kernel">The Semantic Kernel instance providing tool functions.</param>
   /// <param name="ct">Cancellation token that can be used to cancel the operation.</param>
   /// <exception cref="ArgumentNullException">
   /// Thrown if <paramref name="convo"/> or <paramref name="kernel"/> is null.
   /// </exception>
   public static async Task ExecuteToolCallsAsync(
      this HarmonyConversation convo, Kernel kernel, CancellationToken ct = default)
   {
      if (convo is null) throw new ArgumentNullException(nameof(convo));
      if (kernel is null) throw new ArgumentNullException(nameof(kernel));

      // Walk a copy: we will append tool results to Messages during iteration
      var snapshot = (convo.Messages ?? Enumerable.Empty<HarmonyMessage>()).ToList();

      foreach (var m in snapshot)
      {
         ct.ThrowIfCancellationRequested();

         if (!string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ||
             m.Channel != HarmonyChannel.commentary ||
             string.IsNullOrWhiteSpace(m.Recipient))
         {
            continue;
         }

         var recipient = m.Recipient!;
         HarmonyMessage toolResultMessage;

         if (convo.Messages is null)
         {
            convo.Messages = new List<HarmonyMessage>();
         }

         try
         {
            // Recipient format: "<namespace>.<function>"
            var (nsName, funcName) = SplitRecipient(recipient);

            // Build KernelArguments from the JSON content
            var args = BuildKernelArgumentsFromMessage(m);

            // Try to find the function in the Kernel's plugins
            if (!kernel.Plugins.TryGetFunction(nsName, funcName, out var kf))
            {
               // Fall back: no-op result so the model can recover
               toolResultMessage = new HarmonyMessage
               {
                  Role = recipient, // tool role
                  Channel = HarmonyChannel.commentary,
                  ContentType = "json",
                  Content = JsonSerializer.SerializeToElement(new
                  {
                     error = "tool_not_found",
                     tool = recipient
                  }),
                  Termination = HarmonyTermination.end
               };

               convo.Messages.Add(toolResultMessage);
               continue;
            }

            // Invoke the function
            var result = await kernel.InvokeAsync(kf, args, ct).ConfigureAwait(false);

            // Append tool output as a tool-role message on commentary
            var resultElement = JsonSerializer.SerializeToElement(result);
            toolResultMessage = new HarmonyMessage
            {
               Role = recipient, // tool-role
               Channel = HarmonyChannel.commentary,
               Content = resultElement,
               Termination = HarmonyTermination.end
            };
         }
         catch (JsonException jsonEx) when (!ct.IsCancellationRequested)
         {
            toolResultMessage = new HarmonyMessage
            {
               Role = recipient,
               Channel = HarmonyChannel.commentary,
               ContentType = "json",
               Content = JsonSerializer.SerializeToElement(new
               {
                  error = "invalid_tool_arguments",
                  tool = recipient,
                  message = jsonEx.Message
               }),
               Termination = HarmonyTermination.end
            };
         }
         catch (Exception ex) when (!ct.IsCancellationRequested)
         {
            toolResultMessage = new HarmonyMessage
            {
               Role = recipient,
               Channel = HarmonyChannel.commentary,
               ContentType = "json",
               Content = JsonSerializer.SerializeToElement(new
               {
                  error = "tool_execution_failed",
                  tool = recipient,
                  message = ex.Message
               }),
               Termination = HarmonyTermination.end
            };
         }

         convo.Messages.Add(toolResultMessage);
      }
   }

   /// <summary>
   /// Executes tool calls asynchronously based on the messages in the conversation, using an 
   /// <see cref="IToolRegistry"/> to resolve tools instead of a Semantic Kernel instance. 
   /// </summary>
   /// <param name="convo"></param>
   /// <param name="registry"></param>
   /// <param name="execCtx"></param>
   /// <param name="ct"></param>
   /// <returns></returns>
   /// <exception cref="ArgumentNullException"></exception>
   public static async Task ExecuteToolCallsAsync(
       this HarmonyConversation convo,
       IToolRegistry registry,
       ToolExecutionContext? execCtx = null,
       CancellationToken ct = default)
   {
      if (convo is null) throw new ArgumentNullException(nameof(convo));
      var snapshot = (convo.Messages ?? Enumerable.Empty<HarmonyMessage>()).ToList();

      foreach (var m in snapshot)
      {
         ct.ThrowIfCancellationRequested();

         if (!string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)
             || m.Channel != HarmonyChannel.commentary
             || string.IsNullOrWhiteSpace(m.Recipient))
            continue;

         var recipient = m.Recipient!;
         var tool = registry.Resolve(recipient);

         if (convo.Messages is null) convo.Messages = new List<HarmonyMessage>();

         if (tool is null)
         {
            convo.Messages.Add(new HarmonyMessage
            {
               Role = recipient,
               Channel = HarmonyChannel.commentary,
               ContentType = "json",
               Content = JsonSerializer.SerializeToElement(
                  new { error = "tool_not_found", tool = recipient }),
               Termination = HarmonyTermination.end
            });
            continue;
         }

         JsonDocument input;
         if (string.Equals(m.ContentType, "json", StringComparison.OrdinalIgnoreCase) &&
             m.Content.ValueKind != JsonValueKind.Undefined)
            input = JsonDocument.Parse(m.Content.GetRawText());
         else
            input = JsonDocument.Parse(
               @"{ ""value"": " + JsonSerializer.Serialize(m.Content.ToString()) + " }");

         var result = await tool.ExecuteAsync(input, execCtx, ct).ConfigureAwait(false);

         convo.Messages.Add(result.Ok
             ? new HarmonyMessage 
             { 
                Role = recipient, 
                Channel = HarmonyChannel.commentary, 
                Content = result.Data, 
                Termination = HarmonyTermination.end 
             }
             : new HarmonyMessage
             {
                Role = recipient,
                Channel = HarmonyChannel.commentary,
                ContentType = "json",
                Content = JsonSerializer.SerializeToElement(new 
                { 
                   error = result.Error?.Code ?? "tool_execution_failed", 
                   tool = recipient, 
                   message = result.Error?.Message 
                }),
                Termination = HarmonyTermination.end
             });
      }
   }

   /// <summary>
   /// Builds <see cref="KernelArguments"/> from the JSON content of a Harmony tool-call message.
   /// </summary>
   /// <param name="m">The Harmony message containing tool arguments.</param>
   /// <returns>A populated <see cref="KernelArguments"/> instance.</returns>
   private static KernelArguments BuildKernelArgumentsFromMessage(HarmonyMessage m)
   {
      var args = new KernelArguments();

      if (!string.Equals(m.ContentType, "json", StringComparison.OrdinalIgnoreCase) ||
          m.Content.ValueKind == JsonValueKind.Undefined)
      {
         return args;
      }

      using var doc = JsonDocument.Parse(m.Content.GetRawText());
      if (doc.RootElement.ValueKind != JsonValueKind.Object)
      {
         // Treat non-object roots as a single "value" parameter
         args["value"] = doc.RootElement.ToString();
         return args;
      }

      foreach (var prop in doc.RootElement.EnumerateObject())
      {
         args[prop.Name] = prop.Value.ValueKind switch
         {
            JsonValueKind.String => prop.Value.GetString()!,
            JsonValueKind.Number => prop.Value.ToString(),
            JsonValueKind.True or JsonValueKind.False => prop.Value.GetBoolean().ToString(),
            JsonValueKind.Null => null!,
            _ => prop.Value.ToString() // pass raw JSON for nested structures
         };
      }

      return args;
   }

   /// <summary>
   /// Splits a Harmony tool recipient into its namespace and function components.
   /// </summary>
   /// <param name="recipient">The tool recipient string, e.g. "functions.getweather".</param>
   /// <returns>
   /// A tuple where the first element is the namespace and the second is the function name.
   /// </returns>
   /// <exception cref="FormatException">
   /// Thrown if the <paramref name="recipient"/> is null, empty, does not contain a period,
   /// or ends with a period.
   /// </exception>
   private static (string ns, string fn) SplitRecipient(string recipient)
   {
      if (string.IsNullOrWhiteSpace(recipient))
         throw new FormatException("Recipient must be a non-empty string.");

      // "functions.getweather" => ("functions", "getweather")
      int dot = recipient.LastIndexOf('.');
      if (dot <= 0 || dot == recipient.Length - 1)
         throw new FormatException($"Invalid recipient '{recipient}'");
      return (recipient.Substring(0, dot), recipient.Substring(dot + 1));
   }
}


