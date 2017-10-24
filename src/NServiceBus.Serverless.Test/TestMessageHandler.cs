namespace NServiceBus
{
    using System.Threading.Tasks;

    public class TestMessageHandler : IHandleMessages<TestMessage>
    {
        public Task Handle(TestMessage message, IMessageHandlerContext context)
        {
            return Task.FromResult(0);
        }
    }
}