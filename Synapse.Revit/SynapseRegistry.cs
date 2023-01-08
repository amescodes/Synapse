using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Synapse.Revit
{
    public interface ISynapseRegistry
    {
        SynapseProcess RegisterSynapse(IRevitSynapse synapse);
        void DeregisterSynapse(IRevitSynapse synapse);
        bool TryGetMethod(string methodId, out MethodInfo method);
        bool TryGetSynapse(string methodId, out SynapseProcess synapseProcess);
    }

    public class SynapseRegistry : ISynapseRegistry
    {
        private readonly Dictionary<string, MethodInfo> synapseMethodDictionary = new Dictionary<string, MethodInfo>();
        private readonly Dictionary<string, SynapseProcess> synapseDictionary = new Dictionary<string, SynapseProcess>();

        public SynapseProcess RegisterSynapse(IRevitSynapse synapse)
        {
            // check if synapse is already registered
            if (synapseDictionary.Values.FirstOrDefault(s => s.Id.Equals(synapse.Id)) is SynapseProcess process)
            {
                return process;
            }

            SynapseProcess synapseProcess = new SynapseProcess(synapse);
            AddSynapseMethodsToMethodDictionary(synapseProcess);

            return synapseProcess;
        }

        public void DeregisterSynapse(IRevitSynapse synapse)
        {
            foreach (KeyValuePair<string, SynapseProcess> methodIdAndProcess in synapseDictionary.ToList())
            {
                string synapseIdFromDictionary = methodIdAndProcess.Value.Id;
                if (synapseIdFromDictionary != synapse.Id)
                {
                    continue;
                }

                string methodId = methodIdAndProcess.Key;
                synapseMethodDictionary.Remove(methodId);
                synapseDictionary.Remove(methodId);
            }
        }

        public bool TryGetMethod(string methodId, out MethodInfo method)
        {
            return synapseMethodDictionary.TryGetValue(methodId, out method);
        }

        public bool TryGetSynapse(string methodId, out SynapseProcess synapseProcess)
        {
            return synapseDictionary.TryGetValue(methodId, out synapseProcess);
        }

        private void AddSynapseMethodsToMethodDictionary(SynapseProcess synapseProcess)
        {
            Type synapseToAdd = synapseProcess.Synapse.GetType();
            MethodInfo[] methods = synapseToAdd.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            foreach (MethodInfo method in methods)
            {
                if (method.GetCustomAttribute<SynapseRevitMethodAttribute>() is not SynapseRevitMethodAttribute revitCommandAttribute)
                {
                    continue;
                }

                synapseDictionary.Add(revitCommandAttribute.MethodId, synapseProcess);
                synapseMethodDictionary.Add(revitCommandAttribute.MethodId, method);
            }
        }
    }
}
