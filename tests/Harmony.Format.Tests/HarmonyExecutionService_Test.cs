using Harmony.Format;
using Harmony.Format.Execution;
using Harmony.Format.Execution.Api;
using Harmony.Format.Execution.Concurrency;
using Harmony.Format.Execution.Session;
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

public sealed class HarmonyExecutionService_Tests
{
   public HarmonyExecutionService_Tests()
   {
      // Ensure schema validation is initialized for tests that rely on it.
      // If your executor doesn't enforce schema validation at runtime, you can remove this.
      HarmonySchemaValidator.Initialize("Schemas");
   }

   [Fact]
   public async Task ExecuteNextMcpAsync_HappyPath_CompletesSession_RecordsVarsAndFinalArtifact()
   {
      // -----------------------------
      // Arrange
      // -----------------------------

      // NOTE: If your executor enforces schema validation at runtime (ValidateForHrf),
      // your test harness must ensure HarmonySchemaValidator.Initialize(...) has been called.
      // If your tests already do this globally, remove this.
      // HarmonySchemaValidator.Initialize("Schemas");

      var scriptStore = new InMemoryHarmonyScriptStore();
      var sessionStore = new InMemoryHarmonySessionStore();
      var lockProvider = new InMemorySessionLockProvider();

      // Tool availability: allow tools in this happy-path test
      IToolAvailability toolAvailability = new AllowAllToolAvailability();

      var fakeTool = new FakeToolExecutionService();
      var chat = new FakeLanguageModelChatService("Final answer from LLM.");

      var executor = new HarmonyExecutor(chat, fakeTool);

      var executionService = new HarmonyExecutionService(
         scriptStore: scriptStore,
         sessionStore: sessionStore,
         executor: executor,
         toolRouter: fakeTool,
         locks: lockProvider,
         sessionIndex: sessionStore,
         toolAvailability: toolAvailability);

      var envelope = BuildDemoEnvelope_SystemScript();

      await scriptStore.RegisterAsync("demo-script", envelope);

      var session = await executionService.StartSessionAsync("demo-script");

      // -----------------------------
      // Act
      // -----------------------------
      HarmonyExecuteMessageResponse? last = null;

      // Run message-by-message until terminal (guard against infinite loops)
      for (var i = 0; i < 25; i++)
      {
         last = await executionService.ExecuteNextMcpAsync(session.SessionId);

         if (string.Equals(last.SessionStatus, HarmonySessionStatus.Completed.ToString(),
               StringComparison.OrdinalIgnoreCase) ||
             string.Equals(last.SessionStatus, HarmonySessionStatus.Failed.ToString(),
               StringComparison.OrdinalIgnoreCase) ||
             string.Equals(last.SessionStatus, HarmonySessionStatus.Cancelled.ToString(),
               StringComparison.OrdinalIgnoreCase))
         {
            break;
         }
      }

      Assert.NotNull(last);

      // Reload session to assert final stored state
      var finalSession = await sessionStore.GetAsync(session.SessionId);
      Assert.NotNull(finalSession);

      // -----------------------------
      // Assert
      // -----------------------------
      Assert.Equal("demo-script", last!.ScriptId);
      Assert.Equal(HarmonySessionStatus.Completed.ToString(), last.SessionStatus);

      // Vars should include tool result (save_as from script)
      Assert.True(finalSession!.Vars.ContainsKey("toolResult"));

      // Ensure we produced a "final" text artifact during harmony-script execution
      // (ExecutionService uses MakeTextArtifact(name:"final", ...))
      var anyFinal = finalSession.Artifacts.TryGetValue("final", out var finalArtifact);
      Assert.True(anyFinal);
      Assert.Equal("text", finalArtifact!.ContentType);

      // JsonElement string content
      var finalText = finalArtifact.Content.ValueKind == JsonValueKind.String
         ? finalArtifact.Content.GetString()
         : finalArtifact.Content.ToString();

      Assert.Equal("Final answer from LLM.", finalText);

      // History should have at least processed system+user+script messages
      Assert.True(finalSession.History.Count >= 3);

      // Transcript should have the final assistant output appended
      Assert.Contains(finalSession.Transcript, m =>
         m.Role == "assistant" && (m.Content?.Contains("Final answer from LLM.") ?? false));
   }

   private static HarmonyEnvelope BuildDemoEnvelope_SystemScript()
   {
      // IMPORTANT: Put harmony-script under role=system (or developer) to avoid the
      // assistant/commentary termination requirement and early-termination semantics.

      var scriptJson = JsonSerializer.SerializeToElement(new
      {
         steps = new object[]
         {
            new
            {
               type = "tool-call",
               recipient = "demo.echo",
               channel = "commentary",
               args = new { text = "hello from tool" },
               save_as = "toolResult"
            },
            // Let the LLM generate final output
            new
            {
               type = "assistant-message",
               channel = "final",
               content = "."
            }
         }
      });

      return new HarmonyEnvelope
      {
         HRFVersion = "1.0.0",
         Messages = new List<HarmonyMessage>
         {
            new()
            {
               Role = "system",
               Channel = HarmonyChannel.analysis,
               ContentType = "text",
               Content = JsonSerializer.SerializeToElement("You are a helpful assistant.")
            },
            new()
            {
               Role = "user",
               Channel = HarmonyChannel.analysis,
               ContentType = "text",
               Content = JsonSerializer.SerializeToElement("Say hi and call a tool.")
            },
            new()
            {
               Role = "system",
               Channel = HarmonyChannel.commentary,
               ContentType = "harmony-script",
               Content = scriptJson
            }
         }
      };
   }

   private sealed class FakeLanguageModelChatService : ILanguageModelChatService
   {
      private readonly string _reply;

      public FakeLanguageModelChatService(string reply) => _reply = reply;

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history, CancellationToken ct = default)
         => Task.FromResult(_reply);

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history,
         Func<ChatMessage, bool> modelFilter,
         CancellationToken ct = default)
         => Task.FromResult(_reply);
   }

   private sealed class FakeToolExecutionService : IToolExecutionService
   {
      public Task<object?> InvokeToolAsync(
         string recipient,
         IReadOnlyDictionary<string, object?> args,
         CancellationToken ct = default)
      {
         // Return something deterministic and serializable-ish
         var result = new Dictionary<string, object?>
         {
            ["recipient"] = recipient,
            ["args"] = args.ToDictionary(k => k.Key, v => 
               v.Value, StringComparer.OrdinalIgnoreCase),
            ["ok"] = true
         };

         return Task.FromResult<object?>(result);
      }
   }
}
