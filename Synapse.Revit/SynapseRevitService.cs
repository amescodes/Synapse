using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Grpc.Core;

using Newtonsoft.Json;

namespace Synapse.Revit
{
    public class SynapseRevitService : RevitRunner.RevitRunnerBase
    {
        private const int Port = 8278;

        public static bool ServerReady { get; private set; }
        private static Server GrpcServer { get; set; }

        // method id and corresponding method
        private static Dictionary<string, MethodInfo> synapseMethodDictionary = new Dictionary<string, MethodInfo>();
        // method id and id synapse containing method
        private static Dictionary<string, SynapseProcess> synapseDictionary = new Dictionary<string, SynapseProcess>();

        private SynapseRevitService() { }

        public static bool Initialize()
        {
            try
            {
                if (ServerReady)
                {
                    return true;
                }

                GrpcServer = StartGrpcServer("127.0.0.1", Port);
            }
            catch
            {

            }

            return ServerReady;
        }

        public static SynapseProcess RegisterSynapse(IRevitSynapse synapse)
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

        public static void DeregisterSynapse(IRevitSynapse synapse)
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


        public override Task<SynapseOutput> DoRevit(SynapseRequest request, ServerCallContext context)
        {
            if (!synapseMethodDictionary.TryGetValue(request.MethodId, out MethodInfo method))
            {
                throw new SynapseRevitException("Method not found in SynapseMethodDictionary!");
            }

            if (!synapseDictionary.TryGetValue(request.MethodId, out SynapseProcess synapse))
            {
                throw new SynapseRevitException("IRevitSynapse not found in SynapseDictionary!");
            }

            if (method.GetCustomAttribute<SynapseRevitMethodAttribute>() is not { } revitCommandAttribute)
            {
                throw new SynapseRevitException("Command registered without RevitCommandAttribute!");
            }

            object[] commandInputsAsArray = JsonConvert.DeserializeObject<object[]>(request.MethodInputJson);
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != commandInputsAsArray?.Length)
            {
                throw new ArgumentException(
                    $"Number of input arguments ({commandInputsAsArray?.Length}) from the attribute on method {method.Name} " +
                    $"does not match the number needed by the method ({method.GetGenericArguments().Length}).");
            }

            object output = method.Invoke(synapse.Synapse, commandInputsAsArray);
            string jsonOutput = JsonConvert.SerializeObject(output);

            return Task.FromResult(new SynapseOutput()
            {
                MethodOutputJson = jsonOutput
            });
        }

        internal static Server StartGrpcServer(string host, int port)
        {
            SynapseRevitService service = new SynapseRevitService();
            // start grpc server
            try
            {
                Server grpcServer = new Server
                {
                    Services = { RevitRunner.BindService(service) },
                    Ports = { new ServerPort(host, port, ServerCredentials.Insecure) }
                };


                grpcServer.Start();
                ServerReady = true;

                return grpcServer;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
                return null;
            }
        }

        internal static void StopGrpcServer()
        {
            GrpcServer.ShutdownAsync();
            ServerReady = false;
        }

        private static void AddSynapseMethodsToMethodDictionary(SynapseProcess synapseProcess)
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