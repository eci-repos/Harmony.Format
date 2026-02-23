using Harmony.Format;
using Harmony.Format.Execution;
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

public sealed class HarmonyExecutionService_ToolTracingTests
{
   public HarmonyExecutionService_ToolTracingTests()
   {
      // Ensure schema validation is initialized for tests that rely on it.
      // If your executor doesn't enforce schema validation at runtime, you can remove this.
      HarmonySchemaValidator.Initialize("Schemas");
   }

   [Fact]
   public async Task ExecuteNext_ToolCallTrace_IsRecorded_AsArtifact_AndTranscriptSummary()
   {
      // -----------------------------
      // Arrange
      // -----------------------------
      var scriptStore = new InMemoryHarmonyScriptStore();
      var sessionStore = new InMemoryHarmonySessionStore();
      var lockProvider = new InMemorySessionLockProvider();

      var fakeTool = new FakeToolExecutionService();
      var fakeChat = new FakeChatService("Final answer from LLM.");

      // IMPORTANT: allow tools for this test (otherwise preflight blocks)
      var toolAvailability = new AllowAllToolAvailability();

      var executor = new HarmonyExecutor(fakeChat, fakeTool);

      var executionService = new HarmonyExecutionService(
         scriptStore: scriptStore,
         sessionStore: sessionStore,
         executor: executor,
         toolRouter: fakeTool,
         locks: lockProvider,
         sessionIndex: sessionStore,
         toolAvailability: toolAvailability);

      var envelope = BuildToolTraceDemoEnvelope();

      await scriptStore.RegisterAsync("demo-tooltrace", envelope);

      var session = await executionService.StartSessionAsync("demo-tooltrace");

      // -----------------------------
      // Act
      // -----------------------------
      // 0) system context
      var r0 = await executionService.ExecuteNextAsync(session.SessionId);
      // 1) user context
      var r1 = await executionService.ExecuteNextAsync(session.SessionId);
      // 2) assistant harmony-script -> executes and completes (MVP behavior)
      var r2 = await executionService.ExecuteNextAsync(session.SessionId);

      var reloaded = await sessionStore.GetAsync(session.SessionId);
      Assert.NotNull(reloaded);

      // -----------------------------
      // Assert
      // -----------------------------
      Assert.Equal(HarmonySessionStatus.Completed, reloaded!.Status);

      // Tool must have been invoked once
      Assert.True(fakeTool.Calls.Count >= 1);
      Assert.Equal("demo.lookup", fakeTool.Calls[0].Recipient);

      // Execution record should include a tool-trace artifact
      var toolTraceArtifact = r2.Outputs.FirstOrDefault(a =>
         a.ContentType.Equals("tool-trace", StringComparison.OrdinalIgnoreCase) &&
         (a.Name ?? "").Equals("tool:demo.lookup", StringComparison.OrdinalIgnoreCase));

      Assert.NotNull(toolTraceArtifact);

      // Transcript should include a compact tool summary line
      Assert.Contains(reloaded.Transcript, m =>
         m.Role == "assistant" &&
         m.Content.Contains("[tool:demo.lookup]", StringComparison.OrdinalIgnoreCase) &&
         m.Content.Contains("ok", StringComparison.OrdinalIgnoreCase));

      // ToolCallStep save_as should be present in session vars after execution
      Assert.True(reloaded.Vars.ContainsKey("toolResult"));
      Assert.NotNull(reloaded.Vars["toolResult"]);

      // Final should exist (LLM output)
      Assert.Contains(reloaded.Transcript, m => m.Role == "assistant" && 
         m.Content.Contains("Final answer", StringComparison.OrdinalIgnoreCase));
   }

   private static HarmonyEnvelope BuildToolTraceDemoEnvelope()
   {
      // NOTE: Schema requires assistant+commentary message to have recipient + termination.
      // This is our orchestration container message (contentType=harmony-script).
      var scriptJson = JsonSerializer.SerializeToElement(new
      {
         steps = new object[]
         {
            new
            {
               type = "tool-call",
               recipient = "demo.lookup",
               channel = "commentary",
               args = new { query = "hello" },
               save_as = "toolResult"
            },
            new
            {
               type = "assistant-message",
               channel = "final",
               content = "." // let the chat service produce final (MVP-friendly)
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
               Content = JsonSerializer.SerializeToElement("You are a demo assistant.")
            },
            new()
            {
               Role = "user",
               Channel = HarmonyChannel.analysis,
               ContentType = "text",
               Content = JsonSerializer.SerializeToElement("Run the demo tool.")
            },
            new()
            {
               Role = "assistant",
               Channel = HarmonyChannel.commentary,
               Recipient = "demo.plan",
               ContentType = "harmony-script",
               Termination = HarmonyTermination.end,
               Content = scriptJson
            }
         }
      };
   }

   private sealed class FakeChatService : ILanguageModelChatService
   {
      private readonly string _reply;

      public FakeChatService(string reply) => _reply = reply;

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history, CancellationToken ct = default)
         => Task.FromResult(_reply);

      public Task<string> GetAssistantReplyAsync(
         ChatConversation history, Func<ChatMessage, bool> modelFilter, 
         CancellationToken ct = default)
         => Task.FromResult(_reply);
   }

   private sealed class FakeToolExecutionService : IToolExecutionService
   {
      public List<(
         string Recipient, IReadOnlyDictionary<string, object?> Args)> Calls { get; } = new();

      public Task<object?> InvokeToolAsync(
         string recipient, IReadOnlyDictionary<string, object?> args, 
         CancellationToken ct = default)
      {
         Calls.Add((recipient, args));
         // Return any serializable object
         return Task.FromResult<object?>(new Dictionary<string, object?>
         {
            ["ok"] = true,
            ["recipient"] = recipient
         });
      }
   }
}
