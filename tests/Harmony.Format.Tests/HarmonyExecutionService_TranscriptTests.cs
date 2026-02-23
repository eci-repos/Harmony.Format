
using Harmony.Format;
using Harmony.Format.Execution;
using Harmony.Format.Execution.Concurrency;
using Harmony.Format.Execution.Storage;
using Harmony.Format.Execution.Tooling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

using Harmony.Tooling.Discovery;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Tests;

public sealed class HarmonyExecutionService_TranscriptTests
{
   public HarmonyExecutionService_TranscriptTests()
   {
      // Ensure schema validation is initialized for tests that rely on it.
      // If your executor doesn't enforce schema validation at runtime, you can remove this.
      HarmonySchemaValidator.Initialize("Schemas");
   }

   [Fact]
   public async Task ExecuteNext_AppendsTranscript_ContextToolFinal()
   {
      // -----------------------------
      // Arrange
      // -----------------------------
      var scriptStore = new InMemoryHarmonyScriptStore();
      var sessionStore = new InMemoryHarmonySessionStore();
      var lockProvider = new InMemorySessionLockProvider();

      var toolAvailability = new AllowAllToolAvailability();

      var fakeTool = new FakeToolRouter(new Dictionary<string, object?>
      {
         ["demo.search"] = new { items = new[] { "A", "B" } }
      });

      var fakeChat = new FakeChatService("Final answer from LLM.");

      var executor = new HarmonyExecutor(fakeChat, fakeTool);

      var executionService = new HarmonyExecutionService(
         scriptStore: scriptStore,
         sessionStore: sessionStore,
         executor: executor,
         toolRouter: fakeTool,
         locks: lockProvider,
         sessionIndex: sessionStore,
         toolAvailability: toolAvailability);

      var envelope = BuildEnvelope_ContextToolFinal();
      await scriptStore.RegisterAsync("demo-script", envelope);

      var session = await executionService.StartSessionAsync("demo-script");

      // -----------------------------
      // Act: step through messages
      // -----------------------------
      // 0: system context
      var r0 = await executionService.ExecuteNextAsync(session.SessionId);

      // 1: user context
      var r1 = await executionService.ExecuteNextAsync(session.SessionId);

      // 2: harmony-script execution message
      var r2 = await executionService.ExecuteNextAsync(session.SessionId);

      // Reload session for transcript
      var updated = await sessionStore.GetAsync(session.SessionId);
      Assert.NotNull(updated);

      // -----------------------------
      // Assert: transcript tells the story
      // -----------------------------
      var t = updated!.Transcript;

      // At least: system + user + tool summary + final assistant
      Assert.True(t.Count >= 4);

      // system context applied
      Assert.Contains(t, m =>
         m.Role == "system" &&
         m.Content.Contains("You are Harmony demo", StringComparison.OrdinalIgnoreCase) &&
         m.SourceIndex == 0);

      // user context applied
      Assert.Contains(t, m =>
         m.Role == "user" &&
         m.Content.Contains("Find two items", StringComparison.OrdinalIgnoreCase) &&
         m.SourceIndex == 1);

      // tool summary line (written by HarmonyTranscriptWriter.ToolSummary)
      Assert.Contains(t, m =>
         m.Role == "assistant" &&
         m.Content.StartsWith("[tool:demo.search]", StringComparison.OrdinalIgnoreCase) &&
         m.SourceIndex == 2);

      // final assistant appended
      Assert.Contains(t, m =>
         m.Role == "assistant" &&
         m.Content.Contains("Final answer from LLM.", StringComparison.OrdinalIgnoreCase) &&
         m.SourceIndex == 2);
   }

   private static HarmonyEnvelope BuildEnvelope_ContextToolFinal()
   {
      var scriptObj = new
      {
         steps = new object[]
         {
         new
         {
            type = "extract-input",
            output = new Dictionary<string, string>
            {
               ["query"] = "$input.text"
            }
         },
         new
         {
            type = "tool-call",
            recipient = "demo.search",
            channel = "commentary",
            args = new
            {
               query = "$vars.query",
               limit = 2
            },
            save_as = "results"
         },
         new
         {
            type = "assistant-message",
            channel = "final",
            content = "."
         }
         }
      };

      return new HarmonyEnvelope
      {
         HRFVersion = "1.0.0",
         Messages = new List<HarmonyMessage>
      {
         new HarmonyMessage
         {
            Role = "system",
            Channel = HarmonyChannel.analysis,
            ContentType = "text",
            Content = JsonSerializer.SerializeToElement("You are Harmony demo. Follow HRF.")
         },
         new HarmonyMessage
         {
            Role = "user",
            Channel = HarmonyChannel.analysis,
            ContentType = "text",
            Content = JsonSerializer.SerializeToElement("Find two items and summarize them.")
         },
         new HarmonyMessage
         {
            Role = "assistant",
            Channel = HarmonyChannel.commentary,
            Recipient = "demo.plan",                 // ✅ REQUIRED by schema for assistant/commentary
            ContentType = "harmony-script",
            Termination = HarmonyTermination.end,    // ✅ REQUIRED by schema for assistant/commentary
            Content = JsonSerializer.SerializeToElement(scriptObj)
         }
      }
      };
   }

   private sealed class FakeChatService : ILanguageModelChatService
   {
      private readonly string _answer;
      public FakeChatService(string answer) => _answer = answer;

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history, CancellationToken ct = default)
         => Task.FromResult(_answer);

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history, Func<ChatMessage, bool> modelFilter, 
         CancellationToken ct = default)
         => Task.FromResult(_answer);
   }

   private sealed class FakeToolRouter : IToolExecutionService
   {
      private readonly Dictionary<string, object?> _responses;
      public FakeToolRouter(Dictionary<string, object?> responses) => _responses = responses;

      public Task<object?> InvokeToolAsync(
         string recipient, IReadOnlyDictionary<string, object?> args, 
         CancellationToken ct = default)
      {
         if (_responses.TryGetValue(recipient, out var result))
            return Task.FromResult(result);

         throw new InvalidOperationException($"Tool not found: {recipient}");
      }
   }
}

