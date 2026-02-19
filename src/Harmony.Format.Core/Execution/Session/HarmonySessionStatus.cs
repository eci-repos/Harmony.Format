using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Harmony.Format.Execution.Session;

public enum HarmonySessionStatus
{
   Created = 0,
   Running = 1,
   Blocked = 2,
   Completed = 3,
   Failed = 4,
   Cancelled = 5
}
