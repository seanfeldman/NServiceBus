namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Extensibility;
    using Features;
    using Hosting.Helpers;
    using MessageInterfaces.MessageMapper.Reflection;
    using ObjectBuilder;
    using ObjectBuilder.Common;
    using Pipeline;
    using Routing;
    using Routing.MessageDrivenSubscriptions;
    using Settings;
    using Transport;
    using Unicast.Messages;

    /// <summary></summary>
    public class ServerlessRunner
    {
        /// <summary></summary>
        public static async Task Run(List<Type> typesToAdd, Dictionary<string, string> headers, object message)
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
            //var scannedTypes = new List<Type>
            //{
            //    typeof(TestMessage),
            //    typeof(TestMessageHandler)
            //};

            var scanner = new ServerlessAssemblyScanner();
            var scannedTypes = scanner.ScanServerlessAssembly().Types;

            scannedTypes.AddRange(typesToAdd);

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
                typeof(SerializationFeature),
                typeof(RegisterHandlersInOrder),
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
            routingComponent.Initialize(settingsHolder, address => address.ToString(), pipelineSettings);

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

            var defaultSerializerAndDefinition = settingsHolder.GetMainSerializer();

            var definition = defaultSerializerAndDefinition.Item1;
            var deserializerSettings = defaultSerializerAndDefinition.Item2;
            deserializerSettings.Merge(settingsHolder);
            deserializerSettings.PreventChanges();

            var serializerFactory = definition.Configure(deserializerSettings);
            var serializer = serializerFactory(new MessageMapper());

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(message, stream);

                var messageContext = new MessageContext("messageId", headers, stream.ToArray(), new TransportTransaction(), new CancellationTokenSource(), new ContextBag());
                await mainPipelineExecutor.Invoke(messageContext).ConfigureAwait(false);
            }
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
}