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

public sealed class HarmonyExecutionService_ToolTraceArtifactTests
{
   public HarmonyExecutionService_ToolTraceArtifactTests()
   {
      // Ensure schema validation is initialized for tests that rely on it.
      // If your executor doesn't enforce schema validation at runtime, you can remove this.
      HarmonySchemaValidator.Initialize("Schemas");
   }

   [Fact]
   public async Task ExecuteNextAsync_ToolCall_CapturesToolTraceArtifact_AndTranscriptSummary()
   {
      // -----------------------------
      // Arrange
      // -----------------------------
      var scriptStore = new InMemoryHarmonyScriptStore();
      var sessionStore = new InMemoryHarmonySessionStore();
      var lockProvider = new InMemorySessionLockProvider();

      // allow tools for this test
      var toolAvailability = new AllowAllToolAvailability();

      var fakeTool = new FakeToolExecutionService((recipient, args) =>
      {
         // return any payload; execution should save it to vars under save_as
         return new Dictionary<string, object?> { ["ok"] = true, ["recipient"] = recipient };
      });

      var fakeChat = new FakeLanguageModelChatService("Final answer from LLM.");
      var executor = new HarmonyExecutor(fakeChat, fakeTool);

      var executionService = new HarmonyExecutionService(
         scriptStore: scriptStore,
         sessionStore: sessionStore,
         executor: executor,
         toolRouter: fakeTool,
         locks: lockProvider,
         sessionIndex: sessionStore,
         toolAvailability: toolAvailability);

      var envelope = BuildToolCallEnvelope(); // recipient: "demo.search");
      await scriptStore.RegisterAsync("demo-script", envelope);

      var session = await executionService.StartSessionAsync("demo-script");

      // Step 0 = system text (context-only)
      await executionService.ExecuteNextAsync(session.SessionId);

      // Step 1 = user text (context-only)
      await executionService.ExecuteNextAsync(session.SessionId);

      // Step 2 = assistant harmony-script (executable) => should produce tool trace + transcript summary
      var record = await executionService.ExecuteNextAsync(session.SessionId);
      Assert.Equal(2, record.Index);

      // reload session after execution
      var updated = await sessionStore.GetAsync(session.SessionId);
      Assert.NotNull(updated);

      // -----------------------------
      // Assert
      // -----------------------------

      // 1) tool-trace artifact exists
      Assert.Contains(record.Outputs, a =>
         string.Equals(a.ContentType, "tool-trace", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(a.Producer, "demo.search", StringComparison.OrdinalIgnoreCase));

      // 2) transcript includes the compact tool summary line
      Assert.Contains(updated!.Transcript, m =>
         m.Role == "assistant" &&
         m.Content.StartsWith("[tool:demo.search]", StringComparison.OrdinalIgnoreCase));

      // 3) save_as wrote vars
      Assert.True(updated.Vars.ContainsKey("places"));
      // (optional) verify final artifact exists too
      Assert.Contains(record.Outputs, a =>
         string.Equals(a.Name, "final", StringComparison.OrdinalIgnoreCase));
   }

   private static HarmonyEnvelope BuildToolCallEnvelope()
   {
      // harmony-script payload (must be a JSON object)
      var scriptJson = @"
{
  ""steps"": [
    {
      ""type"": ""extract-input"",
      ""output"": { ""userQuery"": ""$input.text"" }
    },
    {
      ""type"": ""tool-call"",
      ""recipient"": ""demo.search"",
      ""channel"": ""commentary"",
      ""args"": { ""query"": ""$vars.userQuery"", ""limit"": 2 },
      ""save_as"": ""places""
    },
    {
      ""type"": ""assistant-message"",
      ""channel"": ""final"",
      ""content"": "".""
    }
  ]
}";
      using var scriptDoc = JsonDocument.Parse(scriptJson);

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
            Content = JsonSerializer.SerializeToElement(
               "You are a helpful assistant. Follow HRF.")
         },
         new HarmonyMessage
         {
            Role = "user",
            Channel = HarmonyChannel.analysis,
            ContentType = "text",
            Content = JsonSerializer.SerializeToElement(
               "Find two nearby coffee shops and summarize them.")
         },

         // IMPORTANT: assistant + commentary => MUST include recipient + termination (schema rule)
         new HarmonyMessage
         {
            Role = "assistant",
            Channel = HarmonyChannel.commentary,
            Recipient = "orchestrator.plan",      // any plugin.function is valid by schema
            Termination = HarmonyTermination.end, // call|return|end required for assistant/commentary
            ContentType = "harmony-script",
            Content = scriptDoc.RootElement.Clone()
         }
      }
      };
   }

   private sealed class FakeToolExecutionService : IToolExecutionService
   {
      private readonly Func<string, IReadOnlyDictionary<string, object?>, object?> _impl;

      public FakeToolExecutionService(Func<string, IReadOnlyDictionary<string, object?>, object?> impl)
      {
         _impl = impl;
      }

      public Task<object?> InvokeToolAsync(
         string recipient,
         IReadOnlyDictionary<string, object?> args,
         CancellationToken ct = default)
         => Task.FromResult(_impl(recipient, args));
   }

   private sealed class FakeLanguageModelChatService : ILanguageModelChatService
   {
      private readonly string _reply;
      public FakeLanguageModelChatService(string reply) => _reply = reply;

      public Task<string> GetAssistantReplyAsync(ChatConversation history, CancellationToken ct = default)
         => Task.FromResult(_reply);

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history,
         Func<ChatMessage, bool> modelFilter,
         CancellationToken ct = default)
         => Task.FromResult(_reply);
   }
}
