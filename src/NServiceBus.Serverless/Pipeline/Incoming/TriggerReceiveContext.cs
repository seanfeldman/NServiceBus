namespace NServiceBus
{
    using System.Threading;
    using Pipeline;
    using Transport;

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