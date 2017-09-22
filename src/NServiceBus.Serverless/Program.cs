namespace NServiceBus
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Extensibility;
    using Pipeline;
    using Settings;
    using Transport;

    /// <summary></summary>
    public class Program
    {
        /// <summary></summary>
        public static async Task Main()
        {
            var builder = new CommonObjectBuilder(new LightInjectObjectBuilder());
            var eventAggregator = new EventAggregator(new NotificationSubscriptions());
            var settingsHolder = new SettingsHolder();
            var pipelineCache = new PipelineCache(builder, settingsHolder);

            var pipelineConfiguration = new PipelineConfiguration();
            var pipelineSettings = new PipelineSettings(pipelineConfiguration.Modifications, settingsHolder);

            var pipeline = new Pipeline<ITransportReceiveContext>(builder, settingsHolder, pipelineConfiguration.Modifications);

            var mainPipelineExecutor = new MainPipelineExecutor(builder, eventAggregator, pipelineCache, pipeline);

            var messageContext = new MessageContext("123", new Dictionary<string, string>(), new byte[] { 1, 2, 3 }, new TransportTransaction(), new CancellationTokenSource(), new ContextBag());
            await mainPipelineExecutor.Invoke(messageContext).ConfigureAwait(false);
        }
    }
}
