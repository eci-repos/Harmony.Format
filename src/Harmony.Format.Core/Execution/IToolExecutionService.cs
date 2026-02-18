using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format;


public interface IToolExecutionService
{
   Task<object?> InvokeToolAsync(
      string recipient, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default);
}
