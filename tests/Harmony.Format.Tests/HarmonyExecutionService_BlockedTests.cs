using Harmony.Format;
using Harmony.Format.Execution;
using Harmony.Format.Execution.Concurrency;
using Harmony.Format.Execution.Storage;
using Harmony.Format.Execution.Tooling;
using Harmony.Format.Execution.Session;
using Harmony.Format.Execution.History;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

using Harmony.Tooling.Discovery;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Tests;

public sealed class HarmonyExecutionService_BlockedTests
{
   public HarmonyExecutionService_BlockedTests()
   {
      // Ensure schema validation is initialized for tests that rely on it.
      // If your executor doesn't enforce schema validation at runtime, you can remove this.
      HarmonySchemaValidator.Initialize("Schemas");
   }

   [Fact]
   public async Task ExecuteNextAsync_WhenToolsMissing_BlocksAndDoesNotInvokeToolOrLlm()
   {
      // -----------------------------
      // Arrange
      // -----------------------------
      var scriptStore = new InMemoryHarmonyScriptStore();
      var sessionStore = new InMemoryHarmonySessionStore();
      var lockProvider = new InMemorySessionLockProvider();

      // Deny all tools => preflight must block
      var toolAvailability = DenyAllToolAvailability.Instance;

      // Tool + LLM should NOT be called in blocked scenario
      var fakeTool = new ThrowingToolExecutionService();
      var fakeChat = new ThrowingChatService();

      var executor = new HarmonyExecutor(fakeChat, fakeTool);

      var executionService = new HarmonyExecutionService(
         scriptStore: scriptStore,
         sessionStore: sessionStore,
         executor: executor,
         toolRouter: fakeTool,
         locks: lockProvider,
         sessionIndex: sessionStore,
         toolAvailability: toolAvailability);

      // Script includes a tool-call step recipient => preflight detects it
      var envelopeJson = """
      {
        "hrfVersion": "1.0.0",
        "messages": [
          {
            "role": "system",
            "channel": "analysis",
            "contentType": "text",
            "content": "You are a test system prompt."
          },
          {
            "role": "user",
            "channel": "analysis",
            "contentType": "text",
            "content": "Search something."
          },
          {
            "role": "assistant",
            "channel": "commentary",
            "recipient": "planner.execute",
            "contentType": "harmony-script",
            "termination": "end",
            "content": {
              "steps": [
                {
                  "type": "tool-call",
                  "recipient": "demo.search",
                  "channel": "commentary",
                  "args": { "q": "coffee" },
                  "save_as": "results"
                },
                {
                  "type": "assistant-message",
                  "channel": "final",
                  "content": "This should never run because tools are missing."
                }
              ]
            }
          }
        ]
      }
      """;

      var envelope = HarmonyEnvelope.Deserialize(envelopeJson);

      // Register script
      await scriptStore.RegisterAsync("blocked-demo", envelope);

      // Start session
      var session = await sessionStore.CreateAsync("blocked-demo");
      session.CurrentIndex = 0;
      await sessionStore.SaveAsync(session);

      // Act (message-by-message)
      var r0 = await executionService.ExecuteNextAsync(session.SessionId); // idx 0
      var r1 = await executionService.ExecuteNextAsync(session.SessionId); // idx 1
      var r2 = await executionService.ExecuteNextAsync(session.SessionId); // idx 2 (script)

      // Assert
      Assert.Equal(0, r0.Index);
      Assert.Equal(HarmonyExecutionStatus.Succeeded, r0.Status);

      Assert.Equal(1, r1.Index);
      Assert.Equal(HarmonyExecutionStatus.Succeeded, r1.Status);

      Assert.Equal(2, r2.Index);
      Assert.Equal(HarmonyExecutionStatus.Blocked, r2.Status);

      // session should now be blocked and pointing at script index for retry
      var s2 = await sessionStore.GetAsync(session.SessionId);
      Assert.NotNull(s2);
      Assert.Equal(HarmonySessionStatus.Blocked, s2!.Status);
      Assert.Equal(2, s2.CurrentIndex);

      // Optional: transcript should include the preflight note
      Assert.Contains(s2.Transcript, t =>
         t.Content.StartsWith("[preflight] blocked", StringComparison.OrdinalIgnoreCase));
   }

   private sealed class ThrowingToolExecutionService : IToolExecutionService
   {
      public int CallCount { get; private set; }

      public Task<object?> InvokeToolAsync(
         string recipient,
         IReadOnlyDictionary<string, object?> args,
         CancellationToken ct = default)
      {
         CallCount++;
         throw new InvalidOperationException("Tool should not be invoked when preflight blocks.");
      }
   }

   private sealed class ThrowingChatService : ILanguageModelChatService
   {
      public int CallCount { get; private set; }

      public Task<string> GetAssistantReplyAsync(ChatConversation history, CancellationToken ct = default)
      {
         CallCount++;
         throw new InvalidOperationException("LLM should not be invoked when preflight blocks.");
      }

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history,
         Func<ChatMessage, bool> modelFilter,
         CancellationToken ct = default)
      {
         CallCount++;
         throw new InvalidOperationException("LLM should not be invoked when preflight blocks.");
      }
   }
}
