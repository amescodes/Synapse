using System;

namespace Synapse.Revit
{
    public class SynapseRevitException : Exception
    {
        public string Message { get; } = "";
        public new Exception InnerException { get; }

        public SynapseRevitException(string message)
        {
            Message = message;
        }

        public SynapseRevitException(string message, Exception innerException)
        {
            Message = message;
            InnerException = innerException;
        }
    }
}
