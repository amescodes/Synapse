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
        private const int port = 8278;
        private static SynapseRegistry synapseRegistry = new SynapseRegistry();
        private static Server server;

        /// <summary>
        /// True if gRPC server is ready to receive requests.
        /// </summary>
        public static bool Ready => server != null;

        private SynapseRevitService() { }

        /// <inheritdoc cref="Initialize(string, int)"/>
        public static async Task<bool> Initialize()
        {
            return await Initialize("127.0.0.1", port);
        }

        /// <summary>
        /// Starts the gRPC server that will receive calls from 
        /// the client to perform actions within Revit.
        /// </summary>
        /// <returns>True if server is started successfully.</returns>
        public static async Task<bool> Initialize(string address, int port)
        {
            try
            {
                if (Ready)
                {
                    return true;
                }

                server = await StartGrpcServer(address, port);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }

            return Ready;
        }

        /// <summary>
        /// Registers an IRevitSynapse to make its contained methods 
        /// callable from a process outside of the Revit application domain.
        /// </summary>
        /// <param name="synapse"></param>
        /// <returns></returns>
        public static SynapseProcess RegisterSynapse(IRevitSynapse synapse)
        {
            return synapseRegistry.RegisterSynapse(synapse);
        }

        /// <summary>
        /// Removes the Synapse from the registry. Its methods will
        /// no longer be able to be called from an outside client.
        /// </summary>
        /// <param name="synapse"></param>
        public static void DeregisterSynapse(IRevitSynapse synapse)
        {
            synapseRegistry.DeregisterSynapse(synapse);
        }

        /// <summary>
        /// Tries to perform the method from the SynapseRequest by
        /// looking up the method in its internal registry.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override Task<SynapseOutput> TryDoRevit(SynapseRequest request, ServerCallContext context)
        {
            try
            {
                if (synapseRegistry.TryGetMethod(request.MethodId, out MethodInfo method))
                {
                    throw new SynapseRevitException("Method not found in SynapseMethodDictionary!");
                }

                if (synapseRegistry.TryGetSynapse(request.MethodId, out SynapseProcess synapseProcess))
                {
                    throw new SynapseRevitException("IRevitSynapse not found in SynapseDictionary!");
                }

                if (method.GetCustomAttribute<SynapseRevitMethodAttribute>() is not SynapseRevitMethodAttribute)
                {
                    throw new SynapseRevitException("Command registered without RevitCommandAttribute!");
                }

                object[] commandInputsAsArray = JsonConvert.DeserializeObject<object[]>(request.MethodInputJson);
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != commandInputsAsArray?.Length)
                {
                    throw new SynapseRevitException(
                        $"Number of input arguments ({commandInputsAsArray?.Length}) from the attribute on method {method.Name} " +
                        $"does not match the number needed by the method ({method.GetGenericArguments().Length}).");
                }

                object output = method.Invoke(synapseProcess.Synapse, commandInputsAsArray);
                string jsonOutput = JsonConvert.SerializeObject(output);

                SynapseOutput result = new SynapseOutput()
                {
                    MethodOutputJson = jsonOutput
                };

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                string errorJson = JsonConvert.SerializeObject(ex, Formatting.Indented);

                Trace.WriteLine(ex);

                SynapseOutput result = new SynapseOutput()
                {
                    MethodOutputJson = errorJson
                };

                return Task.FromResult(result);
            }
        }

        /// <inheritdoc cref="TryDoRevit(SynapseRequest, ServerCallContext)"/>
        public async Task<SynapseOutput> TryDoRevitAsync(SynapseRequest request, ServerCallContext context)
        {
            return await TryDoRevit(request, context);
        }

        /// <summary>
        /// Starts a gRPC server at the input address and port.
        /// Binds the RevitRunner as a service.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        internal static async Task<Server> StartGrpcServer(string address, int port)
        {
            return await Task.Run(() =>
            {
                SynapseRevitService service = new SynapseRevitService();

                try
                {
                    ServerServiceDefinition revitRunnerServiceDefinition = RevitRunner.BindService(service);

                    Server grpcServer = new Server
                    {
                        Services = { revitRunnerServiceDefinition },
                        Ports = { new ServerPort(address, port, ServerCredentials.Insecure) }
                    };

                    grpcServer.Start();

                    return grpcServer;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(JsonConvert.SerializeObject(ex, Formatting.Indented));

                    return null;
                }
            });
        }

        /// <summary>
        /// Shuts down the internal gRPC server.
        /// </summary>
        internal static async void StopGrpcServer()
        {
            await server.ShutdownAsync();
            server = null;
        }
    }
}