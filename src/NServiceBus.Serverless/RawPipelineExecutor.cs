namespace NServiceBus
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using ObjectBuilder;
    using Pipeline;
    using Transport;

    class RawPipelineExecutor : IPipelineExecutor
    {
        public RawPipelineExecutor(IBuilder builder, IEventAggregator eventAggregator, IPipelineCache pipelineCache, IPipeline<ITriggerReceiveContext> mainPipeline)
        {
            this.builder = builder;
            this.eventAggregator = eventAggregator;
            this.pipelineCache = pipelineCache;
            this.mainPipeline = mainPipeline;
        }

        public async Task Invoke(MessageContext messageContext)
        {
            var pipelineStartedAt = DateTime.UtcNow;

            using (var childBuilder = builder.CreateChildBuilder())
            {
                var rootContext = new RootContext(childBuilder, pipelineCache, eventAggregator);

                var message = new IncomingMessage(messageContext.MessageId, messageContext.Headers, messageContext.Body);
                var context = new TriggerReceiveContext(message, messageContext.TransportTransaction, messageContext.ReceiveCancellationTokenSource, rootContext);

                context.Extensions.Merge(messageContext.Extensions);

                await mainPipeline.Invoke(context).ConfigureAwait(false);

                await context.RaiseNotification(new ReceivePipelineCompleted(message, pipelineStartedAt, DateTime.UtcNow)).ConfigureAwait(false);
            }
        }

        IEventAggregator eventAggregator;
        IBuilder builder;
        IPipelineCache pipelineCache;
        IPipeline<ITriggerReceiveContext> mainPipeline;
    }

    interface ITriggerReceiveContext : IBehaviorContext
    { }

    class TriggerReceiveContext : BehaviorContext, ITriggerReceiveContext
    {
        public TriggerReceiveContext(IBehaviorContext parentContext) : base(parentContext)
        {
        }

        public TriggerReceiveContext(IncomingMessage receivedMessage, TransportTransaction transportTransaction, CancellationTokenSource cancellationTokenSource, IBehaviorContext parentContext)
            : base(parentContext)
        {
            this.cancellationTokenSource = cancellationTokenSource;
            Message = receivedMessage;
            Set(Message);
            Set(transportTransaction);
        }

        /// <summary>
        /// The physical message being processed.
        /// </summary>
        public IncomingMessage Message { get; }

        /// <summary>
        /// Allows the pipeline to flag that it has been aborted and the receive operation should be rolled back.
        /// </summary>
        public void AbortReceiveOperation()
        {
            cancellationTokenSource.Cancel();
        }

        CancellationTokenSource cancellationTokenSource;
    }
}