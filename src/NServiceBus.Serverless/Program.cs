namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using DelayedDelivery;
    using Extensibility;
    using Features;
    using Microsoft.Build.Utilities;
    using Performance.TimeToBeReceived;
    using Pipeline;
    using Routing;
    using Routing.MessageDrivenSubscriptions;
    using Settings;
    using Transport;
    using Task = System.Threading.Tasks.Task;

    /// <summary></summary>
    public class Program
    {
        /// <summary></summary>
        public static async Task Main()
        {
            var builder = new CommonObjectBuilder(new LightInjectObjectBuilder());
            var eventAggregator = new EventAggregator(new NotificationSubscriptions());
            var settingsHolder = new SettingsHolder();

            // wire convention
            var conventionsBuilder = new ConventionsBuilder(settingsHolder);
            var conventions = conventionsBuilder.Conventions;
            settingsHolder.SetDefault<Conventions>(conventions);

            var pipelineCache = new PipelineCache(builder, settingsHolder);

            var pipelineConfiguration = new PipelineConfiguration();
            var pipelineSettings = new PipelineSettings(pipelineConfiguration.Modifications, settingsHolder);


            var routingComponent = new RoutingComponent(
                settingsHolder.GetOrCreate<UnicastRoutingTable>(),
                settingsHolder.GetOrCreate<DistributionPolicy>(),
                settingsHolder.GetOrCreate<EndpointInstances>(),
                settingsHolder.GetOrCreate<Publishers>());
            routingComponent.Initialize(settingsHolder, new DummyTransportInfrastructure(), pipelineSettings);

            //var messageSession = new MessageSession(new RootContext(builder, pipelineCache, eventAggregator));

            var featureTypes = new List<Type>
            {
                //typeof(Audit),
                typeof(MessageCausation),
                typeof(MessageCorrelation),
                //typeof(ForwardReceivedMessages),
                typeof(ReceiveFeature)
            };

            var featureActivator = new FeatureActivator(settingsHolder);
            foreach (var type in featureTypes)
            {
                featureActivator.Add(type.Construct<Feature>());
            }

            var featureStats = featureActivator.SetupFeatures(builder, pipelineSettings, routingComponent);

            DisplayDiagnosticsForFeatures(featureStats);

            //var receiveFeature = new ReceiveFeature();
            //var featureConfigurationContext = new FeatureConfigurationContext(settingsHolder, builder, pipelineSettings, routingComponent);
            //receiveFeature.Setup(featureConfigurationContext);

            pipelineConfiguration.RegisterBehaviorsInContainer(settingsHolder, builder);

            var pipeline = new Pipeline<ITransportReceiveContext>(builder, settingsHolder, pipelineConfiguration.Modifications);
            var mainPipelineExecutor = new MainPipelineExecutor(builder, eventAggregator, pipelineCache, pipeline);

            var messageContext = new MessageContext("123", new Dictionary<string, string>(), new byte[] { 1, 2, 3 }, new TransportTransaction(), new CancellationTokenSource(), new ContextBag());
            await mainPipelineExecutor.Invoke(messageContext).ConfigureAwait(false);
        }

        static void DisplayDiagnosticsForFeatures(FeaturesReport report)
        {
            var statusText = new StringBuilder();

            statusText.AppendLine("------------- FEATURES ----------------");

            foreach (var diagnosticData in report.Features)
            {
                statusText.AppendLine($"Name: {diagnosticData.Name}");
                statusText.AppendLine($"Version: {diagnosticData.Version}");
                statusText.AppendLine($"Enabled by Default: {(diagnosticData.EnabledByDefault ? "Yes" : "No")}");
                statusText.AppendLine($"Status: {(diagnosticData.Active ? "Enabled" : "Disabled")}");
                if (!diagnosticData.Active)
                {
                    statusText.Append("Deactivation reason: ");
                    if (diagnosticData.PrerequisiteStatus != null && !diagnosticData.PrerequisiteStatus.IsSatisfied)
                    {
                        statusText.AppendLine("Did not fulfill its Prerequisites:");

                        foreach (var reason in diagnosticData.PrerequisiteStatus.Reasons)
                        {
                            statusText.AppendLine("   -" + reason);
                        }
                    }
                    else if (!diagnosticData.DependenciesAreMet)
                    {
                        statusText.AppendLine($"Did not meet one of the dependencies: {string.Join(",", diagnosticData.Dependencies.Select(t => "[" + string.Join(",", t.Select(t1 => t1)) + "]"))}");
                    }
                    else
                    {
                        statusText.AppendLine("Not explicitly enabled");
                    }
                }
                else
                {
                    statusText.AppendLine($"Dependencies: {(diagnosticData.Dependencies.Count == 0 ? "Default" : string.Join(",", diagnosticData.Dependencies.Select(t => "[" + string.Join(",", t.Select(t1 => t1)) + "]")))}");
                    statusText.AppendLine($"Startup Tasks: {(diagnosticData.StartupTasks.Count == 0 ? "Default" : string.Join(",", diagnosticData.StartupTasks.Select(t => t)))}");
                }

                statusText.AppendLine();
            }

            Console.WriteLine(statusText.ToString());
        }
    }



#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public class DummyTransportInfrastructure : TransportInfrastructure
    {
        public override IEnumerable<Type> DeliveryConstraints { get; } = new List<Type>
        {
            typeof(DiscardIfNotReceivedBefore),
            typeof(DelayDeliveryWith),
            typeof(DoNotDeliverBefore)
        };
        public override TransportTransactionMode TransactionMode { get; } = TransportTransactionMode.ReceiveOnly;
        public override OutboundRoutingPolicy OutboundRoutingPolicy { get; } = new OutboundRoutingPolicy(OutboundRoutingType.Unicast, OutboundRoutingType.Multicast, OutboundRoutingType.Unicast);
        public override TransportReceiveInfrastructure ConfigureReceiveInfrastructure()
        {
            throw new NotImplementedException();
        }

        public override TransportSendInfrastructure ConfigureSendInfrastructure()
        {
            return new TransportSendInfrastructure(() => new DummyDispatcher(), () => Task.FromResult(StartupCheckResult.Success));
        }

        public override TransportSubscriptionInfrastructure ConfigureSubscriptionInfrastructure()
        {
            return new TransportSubscriptionInfrastructure(() => new DummySubscriptionManager());
        }

        public override EndpointInstance BindToLocalEndpoint(EndpointInstance instance)
        {
            return instance;
        }

        public override string ToTransportAddress(LogicalAddress logicalAddress)
        {
            return logicalAddress.ToString();
        }
    }

    public class DummySubscriptionManager : IManageSubscriptions
    {
        public Task Subscribe(Type eventType, ContextBag context)
        {
            Console.WriteLine("Subscribing to event");
            return TaskEx.CompletedTask;
        }

        public Task Unsubscribe(Type eventType, ContextBag context)
        {
            Console.WriteLine("Unsubscribing to event");
            return TaskEx.CompletedTask;
        }
    }

    public class DummyDispatcher : IDispatchMessages
    {
        public Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, ContextBag context)
        {
            Console.WriteLine("Dispatching a message");
            return TaskEx.CompletedTask;
        }
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
