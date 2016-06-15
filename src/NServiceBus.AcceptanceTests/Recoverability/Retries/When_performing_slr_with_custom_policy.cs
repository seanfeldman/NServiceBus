﻿namespace NServiceBus.AcceptanceTests.Recoverability.Retries
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using EndpointTemplates;
    using Features;
    using NUnit.Framework;

    public class When_performing_slr_with_custom_policy : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_expose_error_context_to_policy()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b =>
                    b.When(bus => bus.SendLocal(new MessageToBeRetried()))
                     .DoNotFailOnErrorMessages())
                .Done(c => c.FailedMessages.Any())
                .Run();

            Assert.That(context.HeaderValues.Count, Is.EqualTo(2), "because the custom policy should have been invoked twice");
            Assert.That(context.HeaderValues[0].Message, Is.Not.Null);
            Assert.That(context.HeaderValues[0].Exception, Is.TypeOf<SimulatedException>());
            Assert.That(context.HeaderValues[0].SecondLevelRetryAttempt, Is.EqualTo(1));
            Assert.That(context.HeaderValues[1].Message, Is.Not.Null);
            Assert.That(context.HeaderValues[1].Exception, Is.TypeOf<SimulatedException>());
            Assert.That(context.HeaderValues[1].SecondLevelRetryAttempt, Is.EqualTo(2));
        }

        class Context : ScenarioContext
        {
            public bool MessageMovedToErrorQueue { get; set; }
            public List<SecondLevelRetryContext> HeaderValues { get; } = new List<SecondLevelRetryContext>();
        }

        class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>((config, context) =>
                {
                    var testContext = context.ScenarioContext as Context;

                    config.EnableFeature<TimeoutManager>();
                    config.EnableFeature<FirstLevelRetries>();
                    config.EnableFeature<SecondLevelRetries>();
                    config.SecondLevelRetries().CustomRetryPolicy(new CustomPolicy(testContext).GetDelay);
                });
            }

            class CustomPolicy
            {
                public CustomPolicy(Context context)
                {
                    this.context = context;
                }

                public TimeSpan GetDelay(SecondLevelRetryContext slrRetryContext)
                {
                    context.HeaderValues.Add(slrRetryContext);

                    if (slrRetries++ == 1)
                    {
                        return TimeSpan.MinValue;
                    }
                    return TimeSpan.FromMilliseconds(1);
                }

                Context context;
                int slrRetries;
            }

            class Handler : IHandleMessages<MessageToBeRetried>
            {
                public Task Handle(MessageToBeRetried message, IMessageHandlerContext context)
                {
                    throw new SimulatedException();
                }
            }
        }

        class MessageToBeRetried : IMessage
        {
        }
    }
}