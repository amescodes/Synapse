using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using Grpc.Core;

using Newtonsoft.Json;

namespace Synapse
{
    public class SynapseClient
    {
        private const int Port = 8278;

        private Channel channel;
        private RevitRunner.RevitRunnerClient revitRunner;

        private SynapseClient() { }

        public static SynapseClient StartSynapseClient()
        {
            SynapseClient synapseClient = new SynapseClient();

            synapseClient.channel = new Channel($"127.0.0.1:{Port}", ChannelCredentials.Insecure);
            synapseClient.revitRunner = new RevitRunner.RevitRunnerClient(synapseClient.channel);

            return synapseClient;
        }

        public TOut DoRevit<TOut>(string methodId, params object[] inputs)
        {
            string inputAsJsonString = JsonConvert.SerializeObject(inputs);

            SynapseOutput response = DoRevit(new SynapseRequest() { MethodId = methodId, MethodInputJson = inputAsJsonString });

            TOut deserializeObject = JsonConvert.DeserializeObject<TOut>(response.MethodOutputJson);
            if (deserializeObject == null)
            {
                throw new SynapseException($"Couldn't deserialize Revit response to type {typeof(TOut)}.");
            }

            return deserializeObject;
        }

        public SynapseOutput DoRevit(string methodId, params object[] inputs)
        {
            string inputAsJsonString = JsonConvert.SerializeObject(inputs);

            SynapseOutput response;
            try
            {
                response = DoRevit(new SynapseRequest() { MethodId = methodId, MethodInputJson = inputAsJsonString });
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);

                //todo make error class
                response = new SynapseOutput()
                {
                    MethodOutputJson = JsonConvert.SerializeObject(ex, Formatting.Indented)
                };
            }

            return response;
        }

        /// <summary>
        /// Sends a request to the Revit Server to run a method.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public SynapseOutput DoRevit(SynapseRequest request)
        {
            return revitRunner.DoRevit(request);
        }

        public async Task<TOut> DoRevitAsync<TOut>(string methodId, params object[] inputs)
        {
            string inputAsJsonString = JsonConvert.SerializeObject(inputs);

            SynapseOutput response = await DoRevitAsync(new SynapseRequest() { MethodId = methodId, MethodInputJson = inputAsJsonString });

            TOut deserializeObject = JsonConvert.DeserializeObject<TOut>(response.MethodOutputJson);
            if (deserializeObject == null)
            {
                throw new SynapseException($"Couldn't deserialize Revit response to type {typeof(TOut)}.");
            }

            return deserializeObject;
        }

        public async Task<SynapseOutput> DoRevitAsync(string methodId, params object[] inputs)
        {
            string inputAsJsonString = JsonConvert.SerializeObject(inputs);

            SynapseOutput response;
            try
            {
                response = await DoRevitAsync(new SynapseRequest() { MethodId = methodId, MethodInputJson = inputAsJsonString });
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                //todo make error class
                response = new SynapseOutput()
                {
                    MethodOutputJson = JsonConvert.SerializeObject(ex, Formatting.Indented)
                };
            }

            return response;
        }

        /// <summary>
        /// Sends a request to the Revit Server to run a method.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<SynapseOutput> DoRevitAsync(SynapseRequest request)
        {
            return await revitRunner.DoRevitAsync(request);
        }

        /// <summary>
        /// Shuts down the channel that the client is using to communicate with the Revit Server.
        /// </summary>
        /// <returns></returns>
        public async Task Shutdown()
        {
            await channel.ShutdownAsync();
        }
    }
}
