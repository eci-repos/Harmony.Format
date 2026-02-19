using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Harmony.Format.Execution.History;

public enum HarmonyExecutionStatus
{
   Running = 0,
   Succeeded = 1,
   Blocked = 2,
   Skipped = 3,
   Failed = 4
}
