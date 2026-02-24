using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

using Harmony.Tooling.Llm;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.SemanticKernel;


/// <summary>
/// SK-backed implementation of the host-agnostic ILanguageModelChatService.
/// Projects Harmony.Tooling.Llm chat models to Semantic Kernel's ChatHistory.
/// </summary>
public sealed class ChatCompletionService : ILanguageModelChatService
{
   private readonly Kernel _kernel;
   private readonly IChatCompletionService _chat;

   public ChatCompletionService(Kernel kernel, IChatCompletionService chat)
   {
      _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
      _chat = chat ?? throw new ArgumentNullException(nameof(chat));
   }

   /// <summary>
   /// Gets an assistant reply using the provided transcript.
   /// </summary>
   public async Task<string> GetAssistantReplyAsync(
      ChatTranscript transcript, CancellationToken ct = default)
          => await GetAssistantReplyAsync(transcript, modelFilter: null, ct);

   /// <summary>
   /// Gets an assistant reply using the provided transcript, with an optional model-side message filter.
   /// Messages filtered out will not be projected into the SK ChatHistory.
   /// </summary>
   public async Task<string> GetAssistantReplyAsync(ChatTranscript transcript, 
      Func<ChatMessage, bool> modelFilter, CancellationToken ct = default)
   {
      if (transcript is null) throw new ArgumentNullException(nameof(transcript));

      // Apply optional filter (if provided) BEFORE projecting to SK types.
      var source = (modelFilter is null)
          ? transcript.Messages
          : transcript.Messages.Where((Func<Harmony.Tooling.Llm.ChatMessage, bool>)modelFilter);

      var skHistory = new ChatHistory();

      foreach (var msg in source)
      {
         // Map the neutral roles to SK roles.
         // You can expand this with tool/content-type handling later if desired.
         switch (msg.Role)
         {
            case "system":
               if (!string.IsNullOrWhiteSpace(msg.Content))
                  skHistory.AddSystemMessage(msg.Content);
               break;

            case "user":
               if (!string.IsNullOrWhiteSpace(msg.Content))
                  skHistory.AddUserMessage(msg.Content);
               break;

            case "assistant":
               if (!string.IsNullOrWhiteSpace(msg.Content))
                  skHistory.AddAssistantMessage(msg.Content);
               break;

            case "tool":
               // Optional: If you later want to surface tool outputs in SK history,
               // you can add AuthorRole.Tool messages here.
               // For now, ignore to keep parity with the earlier thin adapter.
               break;

            default:
               // Unknown roles are ignored to keep the adapter thin and safe.
               break;
         }
      }

      // Ask SK for the response
      var result = await _chat
          .GetChatMessageContentsAsync(skHistory, kernel: _kernel, cancellationToken: ct)
          .ConfigureAwait(false);

      // Concatenate multiple candidates if the SK provider returns more than one.
      return string.Join("\n", result.Select(r => r.Content));
   }

}



