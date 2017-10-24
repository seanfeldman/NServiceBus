namespace NServiceBus.Serverless.Test
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary></summary>
    public class Program
    {
        public static void Main()
        {
            ServerlessMain().GetAwaiter().GetResult();
        }

        /// <summary></summary>
        public static async Task ServerlessMain()
        {
            var headers = new Dictionary<string, string>
            {
                {Headers.EnclosedMessageTypes, typeof(TestMessage).FullName}
            };

            var message = new TestMessage();

            await ServerlessRunner.Run(new List<Type>
            {
                typeof(TestMessage),
                typeof(TestMessageHandler)
            }, headers, message).ConfigureAwait(false);
        }
    }
}
