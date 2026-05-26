using System;

namespace EvacLogix.Sandbox.Data.Migrations
{
    public sealed class SandboxMigrationException : Exception
    {
        public SandboxMigrationException(string message) : base(message)
        {
        }
    }
}
