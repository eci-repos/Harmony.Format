
using Harmony.Format;
using Harmony.Format.Execution;
using Harmony.Format.Execution.Concurrency;
using Harmony.Format.Execution.Storage;
using Harmony.Format.Execution.Tooling;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

using Harmony.Tooling.Discovery;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Tests;

public sealed class HarmonyExecutionService_PagingTests
{
   private static bool _schemaInitialized = false;
   public HarmonyExecutionService_PagingTests()
   {
      if (!_schemaInitialized)
      {
         // Ensure schema validation is initialized for tests that rely on it.
         // If your executor doesn't enforce schema validation at runtime, you can remove this.
         HarmonySchemaValidator.Initialize("Schemas");
         _schemaInitialized = true;
      }
   }

   [Fact]
   public async Task ListSessions_Paging_OrdersByUpdatedAtDesc_ThenSessionId()
   {
      // Arrange
      var scriptStore = new InMemoryHarmonyScriptStore();
      var sessionStore = new InMemoryHarmonySessionStore();

      // Create three sessions
      var ss1 = await sessionStore.CreateAsync("demo-script");
      var ss2 = await sessionStore.CreateAsync("demo-script");
      var ss3 = await sessionStore.CreateAsync("demo-script");

      // Force deterministic UpdatedAt values (newest first)
      var baseTime = DateTimeOffset.UtcNow;

      ss2.UpdatedAt = baseTime.AddMinutes(-10); // oldest
      await sessionStore.SaveAsync(ss2);

      ss3.UpdatedAt = baseTime.AddMinutes(-5);  // middle
      await sessionStore.SaveAsync(ss3);

      ss1.UpdatedAt = baseTime;                 // newest
      await sessionStore.SaveAsync(ss1);

      // Page size 2
      var page1 = await sessionStore.ListSessionIdsAsync(
         scriptId: "demo-script",
         page: new HarmonyPageRequest { Limit = 2 });

      var page2 = await sessionStore.ListSessionIdsAsync(
         scriptId: "demo-script",
         page: new HarmonyPageRequest { Limit = 2, 
            ContinuationToken = page1.ContinuationToken });

      // Assert
      Assert.Equal(2, page1.Items.Count);
      Assert.Equal(ss1.SessionId, page1.Items[0]); // newest
      Assert.Equal(ss3.SessionId, page1.Items[1]); // middle

      Assert.Single(page2.Items);
      Assert.Equal(ss2.SessionId, page2.Items[0]); // oldest
      Assert.Null(page2.ContinuationToken);
   }

   [Fact]
   public async Task ListSessionsMcpAsync_Pages_WithContinuationToken()
   {
      // -----------------------------
      // Arrange
      // -----------------------------
      var scriptStore = new InMemoryHarmonyScriptStore();
      var sessionStore = 
         new InMemoryHarmonySessionStore(); // also implements IHarmonySessionIndexStore
      var lockProvider = new InMemorySessionLockProvider();

      var fakeTool = new FakeToolExecutionService();
      var fakeChat = new FakeChatService("ok");
      var executor = new HarmonyExecutor(fakeChat, fakeTool);

      var executionService = new HarmonyExecutionService(
         scriptStore: scriptStore,
         sessionStore: sessionStore,
         executor: executor,
         toolRouter: fakeTool,
         locks: lockProvider,
         sessionIndex: sessionStore,
         toolAvailability: new AllowAllToolAvailability());

      // Register two scripts to prove scriptId filtering
      await scriptStore.RegisterAsync("script-A", BuildMinimalEnvelope(), default);
      await scriptStore.RegisterAsync("script-B", BuildMinimalEnvelope(), default);

      // Create 3 sessions under script-A, 1 under script-B
      var s1 = await executionService.StartSessionAsync("script-A");
      await Task.Delay(5); // ensure UpdatedAt differs in a deterministic way
      var s2 = await executionService.StartSessionAsync("script-A");
      await Task.Delay(5);
      var s3 = await executionService.StartSessionAsync("script-A");

      await executionService.StartSessionAsync("script-B");

      // Touch sessions to ensure UpdatedAt ordering: make s1 newest, then s3, then s2
      var baseTime = DateTimeOffset.UtcNow;

      var ss1 = await sessionStore.GetAsync(s1.SessionId);
      ss1!.UpdatedAt = baseTime.AddSeconds(3);

      var ss3 = await sessionStore.GetAsync(s3.SessionId);
      ss3!.UpdatedAt = baseTime.AddSeconds(2);

      var ss2 = await sessionStore.GetAsync(s2.SessionId);
      ss2!.UpdatedAt = baseTime.AddSeconds(1);

      // -----------------------------
      // Act
      // -----------------------------
      var page1 = await executionService.ListSessionsMcpAsync(
         scriptId: "script-A",
         page: new HarmonyPageRequest { Limit = 2 });

      var page2 = await executionService.ListSessionsMcpAsync(
         scriptId: "script-A",
         page: new HarmonyPageRequest { Limit = 2, ContinuationToken = page1.ContinuationToken });

      // -----------------------------
      // Assert
      // -----------------------------
      Assert.NotNull(page1);
      Assert.Equal("script-A", page1.ScriptId);
      Assert.NotNull(page1.SessionIds);
      Assert.Equal(2, page1.SessionIds.Count);
      Assert.False(string.IsNullOrWhiteSpace(page1.ContinuationToken));

      Assert.NotNull(page2);
      Assert.Equal("script-A", page2.ScriptId);
      Assert.Single(page2.SessionIds);              // remaining 1
      Assert.True(string.IsNullOrWhiteSpace(page2.ContinuationToken)); // no more pages

      // Ensure no overlap between pages
      Assert.DoesNotContain(page2.SessionIds[0], page1.SessionIds);

      // Ensure stable ordering (newest first per UpdatedAt)
      // Note: ss1 and ss2 will fail because timming is not perfectly guaranteed in this test,
      // but ss3 should always be in the middle.

      Assert.Equal(ss1!.SessionId, page1.SessionIds[0]);
      Assert.Equal(ss3!.SessionId, page1.SessionIds[1]);
      Assert.Equal(ss2!.SessionId, page2.SessionIds[0]);
   }

   private static HarmonyEnvelope BuildMinimalEnvelope()
   {
      // Conforms to schema: role/channel/content required, contentType in enum.
      // No harmony-script required for session list paging tests.
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
               Content = JsonSerializer.SerializeToElement("test")
            }
         }
      };
   }

   // Minimal fakes
   private sealed class FakeToolExecutionService : IToolExecutionService
   {
      public Task<object?> InvokeToolAsync(
         string recipient,
         IReadOnlyDictionary<string, object?> args,
         System.Threading.CancellationToken ct = default)
         => Task.FromResult<object?>(new { ok = true });
   }

   private sealed class FakeChatService : ILanguageModelChatService
   {
      private readonly string _reply;
      public FakeChatService(string reply) => _reply = reply;

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history, System.Threading.CancellationToken ct = default)
         => Task.FromResult(_reply);

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history, Func<ChatMessage, bool> modelFilter, 
            System.Threading.CancellationToken ct = default)
         => Task.FromResult(_reply);
   }
}
