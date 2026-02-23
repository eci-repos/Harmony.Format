
using Harmony.Format;
using Harmony.Format.Execution;
using Harmony.Format.Execution.Concurrency;
using Harmony.Format.Execution.History;
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

public sealed class HarmonyExecutionService_ContextOnly_Tests
{
   public HarmonyExecutionService_ContextOnly_Tests()
   {
      // Ensure schema validation is initialized for tests that rely on it.
      // If your executor doesn't enforce schema validation at runtime, you can remove this.
      HarmonySchemaValidator.Initialize("Schemas");
   }

   [Fact]
   public async Task ExecuteNextAsync_ContextOnlyMessage_AppendsTranscript_AndAdvancesIndex()
   {
      // -----------------------------
      // Arrange
      // -----------------------------
      var scriptStore = new InMemoryHarmonyScriptStore();
      var sessionStore = new InMemoryHarmonySessionStore();
      var lockProvider = new InMemorySessionLockProvider();

      var fakeTool = new FakeToolExecutionService();
      var fakeChat = new FakeLanguageModelChatService();
      var executor = new HarmonyExecutor(fakeChat, fakeTool);

      var toolAvailability = DenyAllToolAvailability.Instance; // not used in this test

      var executionService = new HarmonyExecutionService(
         scriptStore: scriptStore,
         sessionStore: sessionStore,
         executor: executor,
         toolRouter: fakeTool,
         locks: lockProvider,
         sessionIndex: sessionStore,
         toolAvailability: toolAvailability);

      var envelope = BuildContextOnlyEnvelope();

      await scriptStore.RegisterAsync("ctx-only", envelope);
      var session = await executionService.StartSessionAsync("ctx-only");

      // -----------------------------
      // Act (executes message[0])
      // -----------------------------
      var record = await executionService.ExecuteNextAsync(session.SessionId);

      // Reload session to verify persisted changes
      var saved = await sessionStore.GetAsync(session.SessionId);
      Assert.NotNull(saved);

      // -----------------------------
      // Assert
      // -----------------------------
      Assert.Equal(0, record.Index);
      Assert.Equal(HarmonyExecutionStatus.Succeeded, record.Status);

      // pointer advanced
      Assert.Equal(1, saved!.CurrentIndex);

      // transcript appended
      Assert.Single(saved.Transcript);
      Assert.Equal("system", saved.Transcript[0].Role);
      Assert.Contains("You are Harmony MVP", saved.Transcript[0].Content);

      // execution record has a "message" artifact
      Assert.Contains(record.Outputs, a =>
         string.Equals(a.ContentType, "text", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(a.Name, "message", StringComparison.OrdinalIgnoreCase));

      // history persisted
      Assert.Single(saved.History);
      Assert.Equal(0, saved.History[0].Index);
   }

   private static HarmonyEnvelope BuildContextOnlyEnvelope()
   {
      // Schema-valid: role + channel + content required; contentType optional but we set it.
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
               Content = JsonSerializer.SerializeToElement("You are Harmony MVP. Follow HRF.")
            },
            new HarmonyMessage
            {
               Role = "user",
               Channel = HarmonyChannel.analysis,
               ContentType = "text",
               Content = JsonSerializer.SerializeToElement("Hello")
            },

            // Include a script message later so the envelope looks realistic,
            // but this test only executes index 0.
            new HarmonyMessage
            {
               Role = "assistant",
               Channel = HarmonyChannel.commentary,
               ContentType = "harmony-script",
               Termination = HarmonyTermination.end,
               Recipient = "demo.search", // required by schema for assistant+commentary
               Content = JsonSerializer.SerializeToElement(new
               {
                  steps = new object[]
                  {
                     new {
                        type = "assistant-message",
                        channel = "final",
                        content = "Not reached in this test."
                     }
                  }
               })
            }
         }
      };
   }

   private sealed class FakeToolExecutionService : IToolExecutionService
   {
      public Task<object?> InvokeToolAsync(
         string recipient,
         IReadOnlyDictionary<string, object?> args,
         System.Threading.CancellationToken ct = default)
         => Task.FromResult<object?>(new { ok = true });
   }

   private sealed class FakeLanguageModelChatService : ILanguageModelChatService
   {
      public Task<string> GetAssistantReplyAsync(ChatConversation history,
         System.Threading.CancellationToken ct = default)
         => Task.FromResult("LLM reply (unused)");

      public Task<string> GetAssistantReplyAsync(ChatConversation history,
         Func<ChatMessage, bool> modelFilter,
         System.Threading.CancellationToken ct = default)
         => Task.FromResult("LLM reply (unused)");
   }
}
