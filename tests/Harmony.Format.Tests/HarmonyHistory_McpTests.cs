
using Harmony.Format;
using Harmony.Format.Execution;
using Harmony.Format.Execution.Api;
using Harmony.Format.Execution.Concurrency;
using Harmony.Format.Execution.History;
using Harmony.Format.Execution.Storage;
using Harmony.Format.Execution.Tooling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

using Harmony.Tooling.Discovery;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Tests;

public sealed class HarmonyHistory_McpTests
{
   public HarmonyHistory_McpTests()
   {
      // Ensure schema validation is initialized for tests that rely on it.
      // If your executor doesn't enforce schema validation at runtime, you can remove this.
      HarmonySchemaValidator.Initialize("Schemas");
   }

   [Fact]
   public async Task GetHistoryItemMcpAsync_ReturnsRecordForRequestedIndex()
   {
      // -----------------------------
      // Arrange
      // -----------------------------
      var scriptStore = new InMemoryHarmonyScriptStore();
      var sessionStore = new InMemoryHarmonySessionStore();
      var lockProvider = new InMemorySessionLockProvider();

      var fakeTool = new FakeToolExecutionService();
      var fakeChat = new FakeChatService();

      var executor = new HarmonyExecutor(fakeChat, fakeTool);

      var toolAvailability = new AllowAllToolAvailability();

      var executionService = new HarmonyExecutionService(
         scriptStore: scriptStore,
         sessionStore: sessionStore,
         executor: executor,
         toolRouter: fakeTool,
         locks: lockProvider,
         sessionIndex: sessionStore,
         toolAvailability: toolAvailability);

      var envelope = BuildEnvelope_ContextToolFinal(); // 3 messages: system, user, assistant(harmony-script)
      await scriptStore.RegisterAsync("demo-script", envelope);

      var session = await executionService.StartSessionAsync("demo-script");

      // Execute 3 message steps (index 0,1,2) one at a time
      await executionService.ExecuteNextAsync(session.SessionId); // idx 0
      await executionService.ExecuteNextAsync(session.SessionId); // idx 1
      await executionService.ExecuteNextAsync(session.SessionId); // idx 2 (script)

      // -----------------------------
      // Act
      // -----------------------------
      var item = await executionService.GetHistoryItemMcpAsync(session.SessionId, index: 2);

      // -----------------------------
      // Assert
      // -----------------------------
      Assert.NotNull(item);
      Assert.Equal(session.SessionId, item.SessionId);
      Assert.Equal("demo-script", item.ScriptId);
      Assert.Equal(2, item.Index);

      Assert.NotNull(item.Record);
      Assert.Equal(2, item.Record!.Index);
      Assert.Equal(HarmonyExecutionStatus.Succeeded, item.Record.Status);

      // Since index 2 is the script message, it should usually produce at least "final" output artifact.
      Assert.True(item.Record.Outputs.Count >= 1);

      // Sanity check that "final" exists in outputs (your ExecuteAtIndexAsync creates "final" artifact)
      Assert.Contains(item.Record.Outputs, a =>
         string.Equals(a.Name, "final", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(a.ContentType, "text", StringComparison.OrdinalIgnoreCase));
   }

   // ---------------------------------------------------------------------------------------------
   // Test envelope (schema-valid)
   // ---------------------------------------------------------------------------------------------
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
            Recipient = "demo.plan",                 // REQUIRED by schema for assistant+commentary
            ContentType = "harmony-script",
            Termination = HarmonyTermination.end,    // REQUIRED by schema for assistant+commentary
            Content = JsonSerializer.SerializeToElement(scriptObj)
         }
      }
      };
   }

   // ---------------------------------------------------------------------------------------------
   // Fakes
   // ---------------------------------------------------------------------------------------------
   private sealed class FakeChatService : ILanguageModelChatService
   {
      public Task<string> GetAssistantReplyAsync(ChatConversation history, CancellationToken ct = default)
         => Task.FromResult("Final answer from LLM.");

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history,
         Func<ChatMessage, bool> modelFilter,
         CancellationToken ct = default)
         => Task.FromResult("Final answer from LLM.");
   }

   private sealed class FakeToolExecutionService : IToolExecutionService
   {
      public Task<object?> InvokeToolAsync(
         string recipient,
         IReadOnlyDictionary<string, object?> args,
         CancellationToken ct = default)
      {
         // return any stable object; this ends up in vars["results"] in the script
         return Task.FromResult<object?>(new[]
         {
            new Dictionary<string, object?> { ["name"] = "Item A" },
            new Dictionary<string, object?> { ["name"] = "Item B" }
         });
      }
   }
}
