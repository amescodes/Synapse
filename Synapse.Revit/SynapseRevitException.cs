using System;

namespace Synapse.Revit
{
    [Serializable]
    public class SynapseRevitException : Exception
    {
        public SynapseRevitException(string message): base (message) { }

        public SynapseRevitException(string message, Exception innerException) : base(message, innerException) { }      
    }
}
