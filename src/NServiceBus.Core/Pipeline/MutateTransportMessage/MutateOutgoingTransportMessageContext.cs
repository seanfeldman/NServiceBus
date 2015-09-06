namespace NServiceBus.MessageMutator
{
    using System.Collections.Generic;

    /// <summary>
    /// Context class for <see cref="IMutateOutgoingTransportMessages"/>.
    /// </summary>
    public class MutateOutgoingTransportMessageContext
    {
        byte[] body;

        /// <summary>
        /// Initializes a new instance of <see cref="MutateOutgoingTransportMessageContext"/>.
        /// </summary>
        public MutateOutgoingTransportMessageContext(byte[] body, Dictionary<string, string> headers)
        {
            Guard.AgainstNull("headers", headers);
            Guard.AgainstNull("body", body);
            Headers = headers;
            Body = body;
        }

        /// <summary>
        /// The body of the message.
        /// </summary>
        public byte[] Body
        {
            get { return body; }
            set
            {
                Guard.AgainstNull("value",value);
                body = value;
            }
        }

        /// <summary>
        /// The current outgoing headers.
        /// </summary>
        public Dictionary<string, string> Headers { get; private set; }


    }
}