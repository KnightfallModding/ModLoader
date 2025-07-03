using System;

namespace MelonLoader
{
    /// <summary>
    /// An info class for broken Melons.
    /// </summary>
    public sealed class RottenMelon
    {
        public readonly MelonAssembly assembly;
        public readonly Type type;
        public readonly string errorMessage;
        public readonly Exception exception;

        public RottenMelon(MelonAssembly assembly, string errorMessage, Exception exception = null)
        {
            this.assembly = assembly;
            this.errorMessage = errorMessage;
            this.exception = exception;
        }

        public RottenMelon(Type type, string errorMessage, Exception exception = null)
        {
            assembly = MelonAssembly.LoadMelonAssembly(null, type.Assembly);
            this.type = type;
            this.errorMessage = errorMessage;
            this.exception = exception;
        }
    }
}
