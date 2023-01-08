using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;

using Newtonsoft.Json;

namespace Synapse
{
    public class SynapseClient : IDisposable
    {
        private const int defaultPort = 8278;

        private Channel channel;
        private RevitRunner.RevitRunnerClient revitRunner;

        private SynapseClient() { }

        ///<inheritdoc cref="StartSynapseClient(string, int)"/>
        public static SynapseClient StartSynapseClient()
        {
            SynapseClient synapseClient = new SynapseClient();

            synapseClient.channel = new Channel($"127.0.0.1:{defaultPort}", ChannelCredentials.Insecure);
            synapseClient.revitRunner = new RevitRunner.RevitRunnerClient(synapseClient.channel);

            return synapseClient;
        }

        /// <summary>
        /// Starts the gRPC RevitRunnerClient that will send 
        /// calls to the server in Revit to perform actions.
        /// </summary>
        /// <returns></returns>
        public static SynapseClient StartSynapseClient(string address, int port)
        {
            SynapseClient synapseClient = new SynapseClient();

            synapseClient.channel = new Channel($"{address}:{port}", ChannelCredentials.Insecure);
            synapseClient.revitRunner = new RevitRunner.RevitRunnerClient(synapseClient.channel);

            return synapseClient;
        }

        /// <summary>
        /// Sends a request to the Revit Server to run a method.
        /// </summary>
        /// <param name="methodId"></param>
        /// <param name="output"></param>
        /// <param name="inputs"></param>
        /// <returns></returns>
        public string TryDoRevit<TOut>(string methodId, out TOut output, params object[] inputs)
        {
            string inputAsJsonString = "";
            if (inputs.Any())
            {
                JsonConvert.SerializeObject(inputs);
            }

            SynapseOutput response = DoRevit(new SynapseRequest() { MethodId = methodId, MethodInputJson = inputAsJsonString });

            output = JsonConvert.DeserializeObject<TOut>(response.MethodOutputJson);

            return response.MethodOutputJson;
        }

        /// <summary>
        /// Sends a request to the Revit Server to run a method.
        /// </summary>
        /// <param name="methodId"></param>
        /// <param name="inputs"></param>
        /// <returns></returns>
        public async Task<(TOut, string)> TryDoRevitAsync<TOut>(string methodId, params object[] inputs)
        {
            string inputAsJsonString = JsonConvert.SerializeObject(inputs);

            SynapseOutput response = await DoRevitAsync(new SynapseRequest() { MethodId = methodId, MethodInputJson = inputAsJsonString });

            TOut deserializeObject = JsonConvert.DeserializeObject<TOut>(response.MethodOutputJson);

            return (deserializeObject, response.MethodOutputJson);
        }

        /// <summary>
        /// Shuts down the channel that the client is using to communicate with the Revit Server.
        /// </summary>
        /// <returns></returns>
        public async Task Shutdown()
        {
            await channel.ShutdownAsync();
        }

        /// <summary>
        /// Shuts down the channel the RevitRunner client
        /// is connected to. <br/>
        /// <inheritdoc/>
        /// </summary>
        public void Dispose()
        {
            Shutdown();
        }

        internal SynapseOutput DoRevit(SynapseRequest request)
        {
            try
            {
                return revitRunner.DoRevit(request);
            }
            catch (Exception ex)
            {
                string errorJson = JsonConvert.SerializeObject(ex, Formatting.Indented);

                Trace.WriteLine(ex);

                return new SynapseOutput()
                {
                    MethodOutputJson = errorJson
                };
            }
        }

        internal async Task<SynapseOutput> DoRevitAsync(SynapseRequest request)
        {
            try
            {
                return await revitRunner.DoRevitAsync(request);
            }
            catch (Exception ex)
            {
                string errorJson = JsonConvert.SerializeObject(ex, Formatting.Indented);

                Trace.WriteLine(ex);

                return new SynapseOutput()
                {
                    MethodOutputJson = errorJson
                };
            }
        }
    }
}
