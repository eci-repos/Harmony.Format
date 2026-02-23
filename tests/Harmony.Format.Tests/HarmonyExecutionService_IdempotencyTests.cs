using Harmony.Format;
using Harmony.Format.Execution;
using Harmony.Format.Execution.Concurrency;
using Harmony.Format.Execution.Storage;
using Harmony.Format.Execution.Tooling;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

using Harmony.Tooling.Discovery;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Tests;

public sealed class HarmonyExecutionService_IdempotencyTests
{
   /// <summary>
   /// Tests that executing the same message (same sessionId + index) with the same executionId 
   /// returns the prior execution record without creating a new history entry or re-invoking tools.
   /// </summary>
   /// <returns></returns>
   [Fact]
   public async Task 
      ExecuteMessageAsync_WithSameExecutionId_ReturnsPriorRecord_AndDoesNotAppendHistory()
   {
      // -----------------------------
      // Arrange
      // -----------------------------
      var scriptStore = new InMemoryHarmonyScriptStore();
      var sessionStore = new InMemoryHarmonySessionStore();
      var lockProvider = new InMemorySessionLockProvider();

      var toolAvailability = new AllowAllToolAvailability();

      var fakeTool = new FakeToolExecutionService();
      var fakeChat = new FakeChatService("unused");

      var executor = new HarmonyExecutor(fakeChat, fakeTool);

      var executionService = new HarmonyExecutionService(
         scriptStore: scriptStore,
         sessionStore: sessionStore,
         executor: executor,
         toolRouter: fakeTool,
         locks: lockProvider,
         sessionIndex: sessionStore,
         toolAvailability: toolAvailability);

      var envelope = BuildSimpleEnvelope_FinalIsDeterministic();

      await scriptStore.RegisterAsync("demo-script", envelope);

      var session = await executionService.StartSessionAsync("demo-script");

      // The harmony-script message is at index 2 in this envelope.
      const int scriptIndex = 2;
      const string executionId = "exec-123";

      // -----------------------------
      // Act (first run)
      // -----------------------------
      var record1 = await executionService.ExecuteMessageAsync(
         sessionId: session.SessionId,
         index: scriptIndex,
         input: null,
         executionId: executionId);

      var sessionAfter1 = await sessionStore.GetAsync(session.SessionId);
      Assert.NotNull(sessionAfter1);
      var historyCountAfter1 = sessionAfter1!.History.Count;

      // -----------------------------
      // Act (retry same executionId + same index)
      // -----------------------------
      var record2 = await executionService.ExecuteMessageAsync(
         sessionId: session.SessionId,
         index: scriptIndex,
         input: null,
         executionId: executionId);

      var sessionAfter2 = await sessionStore.GetAsync(session.SessionId);
      Assert.NotNull(sessionAfter2);

      // -----------------------------
      // Assert
      // -----------------------------
      // Same execution should not create a new history record
      Assert.Equal(historyCountAfter1, sessionAfter2!.History.Count);

      // The record returned on retry should match the original one (same index + executionId)
      Assert.Equal(record1.Index, record2.Index);
      Assert.Equal(record1.ExecutionId, record2.ExecutionId);

      // Stronger: verify we got the SAME record instance from session history
      // (typical with your scan)
      // (If you later clone/deserialize records, swap to value assertions only.)
      Assert.Same(record1, record2);

      // Status should be stable
      Assert.Equal(record1.Status, record2.Status);
      Assert.NotNull(record2.CompletedAt);

      // Optional: ensure we didn’t re-invoke tools (this script doesn’t call tools anyway)
      Assert.Equal(0, fakeTool.CallCount);
   }

   private static HarmonyEnvelope BuildSimpleEnvelope_FinalIsDeterministic()
   {
      // Minimal valid HRF v1.0.0 envelope matching your schema:
      // - system + user text
      // - assistant commentary harmony-script with steps
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
               Content = JsonSerializer.SerializeToElement("You are a helpful assistant.")
            },
            new HarmonyMessage
            {
               Role = "user",
               Channel = HarmonyChannel.analysis,
               ContentType = "text",
               Content = JsonSerializer.SerializeToElement("Say hello.")
            },
            new HarmonyMessage
            {
               Role = "assistant",
               Channel = HarmonyChannel.commentary,
               ContentType = "harmony-script",
               Termination = HarmonyTermination.end,
               // recipient is required for assistant/commentary by schema, but can be any
               // plugin.function pattern
               Recipient = "demo.plan",
               Content = JsonSerializer.SerializeToElement(new
               {
                  steps = new object[]
                  {
                     new
                     {
                        type = "assistant-message",
                        channel = "final",
                        content = "Hello from the script."
                     }
                  }
               })
            }
         }
      };
   }

   private sealed class FakeToolExecutionService : IToolExecutionService
   {
      public int CallCount { get; private set; }

      public Task<object?> InvokeToolAsync(
         string recipient,
         IReadOnlyDictionary<string, object?> args,
         CancellationToken ct = default)
      {
         CallCount++;
         return Task.FromResult<object?>(new { ok = true });
      }
   }

   private sealed class FakeChatService : ILanguageModelChatService
   {
      private readonly string _reply;

      public FakeChatService(string reply) => _reply = reply;

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history, CancellationToken ct = default)
         => Task.FromResult(_reply);

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history,
         Func<ChatMessage, bool> modelFilter,
         CancellationToken ct = default)
         => Task.FromResult(_reply);
   }

}
