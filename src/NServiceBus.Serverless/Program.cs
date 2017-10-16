namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using DelayedDelivery;
    using Extensibility;
    using Features;
    using ObjectBuilder;
    using ObjectBuilder.Common;
    using Performance.TimeToBeReceived;
    using Pipeline;
    using Routing;
    using Routing.MessageDrivenSubscriptions;
    using Settings;
    using Transport;
    using Unicast.Messages;
    using Task = System.Threading.Tasks.Task;

    /// <summary></summary>
    public class Program
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

        public static async Task Main()
        {
            await NormalMain().ConfigureAwait(false);
            await ServerlessMain().ConfigureAwait(false);
        }
        public static async Task NormalMain()
        {
            var configuration = new EndpointConfiguration("Dummy");
            configuration.UseTransport<DummyTransport>();
            configuration.SendFailedMessagesTo("error");

            var instance = await Endpoint.Start(configuration).ConfigureAwait(false);

            var headers = new Dictionary<string, string>
            {
                {Headers.EnclosedMessageTypes, typeof(TestMessage).FullName}
            };
            var body = new byte[]
            {
                1,
                2,
                3
            };
            var messageContext = new MessageContext("messageId", headers, body, new TransportTransaction(), new CancellationTokenSource(), new ContextBag());

            await DummyMessagePump.Pump(messageContext).ConfigureAwait(false);

            await instance.Stop().ConfigureAwait(false);
        }

        /// <summary></summary>
        public static async Task ServerlessMain()
        {
            var settingsHolder = new SettingsHolder();

            // EndpointConfiguration.ctor()
            settingsHolder.Set("NServiceBus.Routing.EndpointName", "Dummy");

            var pipelineConfiguration = new PipelineConfiguration();
            settingsHolder.Set<PipelineConfiguration>(pipelineConfiguration);

            var pipelineSettings = new PipelineSettings(pipelineConfiguration.Modifications, settingsHolder);

            settingsHolder.Set<QueueBindings>(new QueueBindings());

            var notifications = new Notifications();
            settingsHolder.Set<Notifications>(notifications);
            settingsHolder.Set<NotificationSubscriptions>(new NotificationSubscriptions());

            var conventionsBuilder = new ConventionsBuilder(settingsHolder);

            // EndpointConfiguration.Build()
            var scannedTypes = new List<Type>
            {
                typeof(TestMessage),
                typeof(TestMessageHandler)
            };
            settingsHolder.SetDefault("TypesToScan", scannedTypes);
            IContainer container = new LightInjectObjectBuilder();

            var conventions = conventionsBuilder.Conventions;
            settingsHolder.SetDefault<Conventions>(conventions);

            var messageMetadataRegistry = new MessageMetadataRegistry(conventions);
            messageMetadataRegistry.RegisterMessageTypesFoundIn(settingsHolder.GetAvailableTypes());
            settingsHolder.SetDefault<MessageMetadataRegistry>(messageMetadataRegistry);

            // InitializableEndpoint.ctor()
            var builder = new CommonObjectBuilder(container);
            builder.ConfigureComponent<IBuilder>(_ => builder, DependencyLifecycle.SingleInstance);
            builder.RegisterSingleton<ReadOnlySettings>(settingsHolder);

            // InitializableEndpoint.Initialize()
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

            var routingComponent = new RoutingComponent(
                settingsHolder.GetOrCreate<UnicastRoutingTable>(),
                settingsHolder.GetOrCreate<DistributionPolicy>(),
                settingsHolder.GetOrCreate<EndpointInstances>(),
                settingsHolder.GetOrCreate<Publishers>());
            routingComponent.Initialize(settingsHolder, new DummyTransportInfrastructure(), pipelineSettings);

            var featureStats = featureActivator.SetupFeatures(builder, pipelineSettings, routingComponent);
            pipelineConfiguration.RegisterBehaviorsInContainer(settingsHolder, builder);
            DisplayDiagnosticsForFeatures(featureStats);

            builder.ConfigureComponent(b => settingsHolder.Get<Notifications>(), DependencyLifecycle.SingleInstance);

            // StartableEndpoint.ctor()
            var pipelineCache = new PipelineCache(builder, settingsHolder);

            // StartableEndpoint.Start()
            var pipeline = new Pipeline<ITransportReceiveContext>(builder, settingsHolder, pipelineConfiguration.Modifications);

            var eventAggregator = new EventAggregator(new NotificationSubscriptions());
            var messageSession = new MessageSession(new RootContext(builder, pipelineCache, eventAggregator));
            var featureRunner = new FeatureRunner(featureActivator);
            await featureRunner.Start(builder, messageSession).ConfigureAwait(false);

            var mainPipelineExecutor = new MainPipelineExecutor(builder, eventAggregator, pipelineCache, pipeline);

            var headers = new Dictionary<string, string>
            {
                {Headers.EnclosedMessageTypes, typeof(TestMessage).FullName}
            };
            var body = new byte[]
            {
                1,
                2,
                3
            };
            var messageContext = new MessageContext("messageId", headers, body, new TransportTransaction(), new CancellationTokenSource(), new ContextBag());
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

    public class TestMessage : ICommand
    {
    }

    public class TestMessageHandler : IHandleMessages<TestMessage>
    {
        public Task Handle(TestMessage message, IMessageHandlerContext context)
        {
            return TaskEx.CompletedTask;
        }
    }

    public class DummyTransport : TransportDefinition
    {
        public override string ExampleConnectionStringForErrorMessage { get; } = string.Empty;

        public override bool RequiresConnectionString { get; } = false;

        public override TransportInfrastructure Initialize(SettingsHolder settings, string connectionString)
        {
            return new DummyTransportInfrastructure();
        }
    }

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
            return new TransportReceiveInfrastructure(() => new DummyMessagePump(), () => new DummyQueueCreator(), () => Task.FromResult(StartupCheckResult.Success));
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

    public class DummyMessagePump : IPushMessages{
        public Task Init(Func<MessageContext, Task> onMessage, Func<ErrorContext, Task<ErrorHandleResult>> onError, CriticalError criticalError, PushSettings settings)
        {
            DummyMessagePump.onMessage = onMessage;

            return TaskEx.CompletedTask;
        }

        static Func<MessageContext, Task> onMessage;

        public static Task Pump(MessageContext context)
        {
            return onMessage(context);
        }

        public void Start(PushRuntimeSettings limitations)
        {

        }

        public Task Stop()
        {
            return TaskEx.CompletedTask;
        }
    }

    public class DummyQueueCreator : ICreateQueues
    {
        public Task CreateQueueIfNecessary(QueueBindings queueBindings, string identity)
        {
            return TaskEx.CompletedTask;
        }
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
