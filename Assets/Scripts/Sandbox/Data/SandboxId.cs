using System;

namespace EvacLogix.Sandbox.Data
{
    public static class SandboxId
    {
        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
