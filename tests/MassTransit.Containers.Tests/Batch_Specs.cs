namespace MassTransit.Containers.Tests
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using Middleware.InMemoryOutbox;
    using NUnit.Framework;
    using TestFramework;
    using Testing;


    [TestFixture]
    public class When_batch_limit_is_reached
    {
        [Test]
        public async Task Should_deliver_the_batch_to_the_consumer()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<TestBatchConsumer>();
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(2).Count(), Is.EqualTo(2));

            Assert.That(await harness.GetConsumerHarness<TestBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.IsTrue(await harness.Published.Any<BatchResult>(x => x.Context.Message.Count == 2 && x.Context.Message.Mode == BatchCompletionMode.Time));
        }
    }


    [TestFixture]
    public class When_retry_and_in_memory_outbox_are_used_with_batch_consumers
    {
        [Test]
        public async Task Should_deliver_the_batch_to_the_consumer()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<TestOutboxBatchConsumer>();

                    x.AddConfigureEndpointsCallback((_, cfg) =>
                    {
                        cfg.UseMessageRetry(r => r.Immediate(2));
                        cfg.UseInMemoryOutbox();
                    });
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(2).Count(), Is.EqualTo(2));

            Assert.That(await harness.GetConsumerHarness<TestOutboxBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.IsTrue(await harness.Published.Any<BatchResult>(x => x.Context.Message.Count == 2 && x.Context.Message.Mode == BatchCompletionMode.Time));
        }

        [Test]
        public async Task Should_deliver_the_batch_to_the_consumer_with_message()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<TestOutboxBatchConsumer>(c => c.Message<Batch<BatchItem>>(m => m.UseInMemoryOutbox()));
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(2).Count(), Is.EqualTo(2));

            Assert.That(await harness.GetConsumerHarness<TestOutboxBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.IsTrue(await harness.Published.Any<BatchResult>(x => x.Context.Message.Count == 2 && x.Context.Message.Mode == BatchCompletionMode.Time));
        }

        [Test]
        public async Task Should_deliver_the_batch_to_the_consumer_after_retry()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<TestRetryOutboxBatchConsumer>();

                    x.AddConfigureEndpointsCallback((_, cfg) =>
                    {
                        cfg.UseMessageRetry(r => r.Immediate(2));
                        cfg.UseInMemoryOutbox();
                    });
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(2).Count(), Is.EqualTo(2));

            Assert.That(await harness.GetConsumerHarness<TestRetryOutboxBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.IsTrue(await harness.Published.Any<BatchResult>(x => x.Context.Message.Count == 2 && x.Context.Message.Mode == BatchCompletionMode.Time));
        }


        class TestOutboxBatchConsumer :
            IConsumer<Batch<BatchItem>>
        {
            public Task Consume(ConsumeContext<Batch<BatchItem>> context)
            {
                if (context.TryGetPayload<InMemoryOutboxConsumeContext>(out _))
                {
                    context.Respond(new BatchResult
                    {
                        Count = context.Message.Length,
                        Mode = context.Message.Mode
                    });
                }
                else
                    throw new InvalidOperationException("Outbox context is not available at this point");

                return Task.CompletedTask;
            }
        }


        class TestRetryOutboxBatchConsumer :
            IConsumer<Batch<BatchItem>>
        {
            public Task Consume(ConsumeContext<Batch<BatchItem>> context)
            {
                if (context.TryGetPayload<InMemoryOutboxConsumeContext>(out _))
                {
                    if (context.GetRetryCount() == 0)
                        throw new IntentionalTestException("First time is not the charm");

                    context.Respond(new BatchResult
                    {
                        Count = context.Message.Length,
                        Mode = context.Message.Mode
                    });
                }
                else
                    throw new InvalidOperationException("Outbox context is not available at this point");

                return Task.CompletedTask;
            }
        }
    }


    [TestFixture]
    public class When_a_batch_limit_is_configured
    {
        [Test]
        public async Task Should_deliver_the_batch_to_the_consumer()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<TestBatchConsumer>(c =>
                            c.Options<BatchOptions>(o => o.SetMessageLimit(5).SetTimeLimit(1000)))
                        .Endpoint(e => e.ConcurrentMessageLimit = 16);
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem(), new BatchItem(), new BatchItem(), new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(5).Count(), Is.EqualTo(5));

            Assert.That(await harness.GetConsumerHarness<TestBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.IsTrue(await harness.Published.Any<BatchResult>(x => x.Context.Message.Count == 5 && x.Context.Message.Mode == BatchCompletionMode.Size));
            Assert.IsTrue(await harness.Published.Any<BatchResult>(x => x.Context.Message.Count == 1 && x.Context.Message.Mode == BatchCompletionMode.Time));
        }
    }


    [TestFixture]
    public class When_a_big_batch_limit_is_configured
    {
        [Test]
        public async Task Should_deliver_the_batch_to_the_consumer()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<TestBatchConsumer>(c =>
                            c.Options<BatchOptions>(o => o.SetMessageLimit(100).SetTimeLimit(10000)))
                        .Endpoint(e => e.ConcurrentMessageLimit = 101);
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(Enumerable.Range(0, 100).Select(_ => new BatchItem()));

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(100).Count(), Is.EqualTo(100));

            Assert.That(await harness.GetConsumerHarness<TestBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.IsTrue(await harness.Published.Any<BatchResult>(x => x.Context.Message.Count == 100 && x.Context.Message.Mode == BatchCompletionMode.Size));
        }
    }


    [TestFixture]
    public class When_a_batch_limit_is_configured_using_a_definition
    {
        [Test]
        public async Task Should_deliver_the_batch_to_the_consumer()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<TestBatchConsumer, TestBatchConsumerDefinition>()
                        .Endpoint(e => e.ConcurrentMessageLimit = 16);
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem(), new BatchItem(), new BatchItem(), new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(5).Count(), Is.EqualTo(5));

            Assert.That(await harness.GetConsumerHarness<TestBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.IsTrue(await harness.Published.Any<BatchResult>(x => x.Context.Message.Count == 5 && x.Context.Message.Mode == BatchCompletionMode.Size));
            Assert.IsTrue(await harness.Published.Any<BatchResult>(x => x.Context.Message.Count == 1 && x.Context.Message.Mode == BatchCompletionMode.Time));
        }
    }


    [TestFixture]
    public class When_a_batch_consumer_faults
    {
        [Test]
        public async Task Should_fault_once_for_each_message_in_the_batch()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<FailingBatchConsumer>(c => c.Options<BatchOptions>(o => o.SetMessageLimit(2)));
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(2).Count(), Is.EqualTo(2));

            Assert.That(await harness.GetConsumerHarness<FailingBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.That(await harness.Published.SelectAsync<Fault<BatchItem>>().Take(2).Count(), Is.EqualTo(2));
        }
    }


    [TestFixture]
    public class When_a_batch_consumer_faults_and_retries
    {
        [Test]
        public async Task Should_fault_once_for_each_message_in_the_batch()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<FailingBatchConsumer>(c => c.Options<BatchOptions>(o => o.SetMessageLimit(2)));

                    x.AddConfigureEndpointsCallback((_, cfg) => cfg.UseMessageRetry(r => r.Immediate(1)));
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(2).Count(), Is.EqualTo(2));

            Assert.That(await harness.GetConsumerHarness<FailingBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.That(await harness.Published.SelectAsync<Fault<BatchItem>>().Take(2).Count(), Is.EqualTo(2));
        }

        [Test]
        public async Task Should_fault_once_for_each_message_in_the_batch_with_in_memory_outbox()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<FailingBatchConsumer>(c => c.Options<BatchOptions>(o => o.SetMessageLimit(2)));

                    x.AddConfigureEndpointsCallback((_, cfg) =>
                    {
                        cfg.UseMessageRetry(r => r.Immediate(2));
                        cfg.UseInMemoryOutbox();
                    });
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(2).Count(), Is.EqualTo(2));

            Assert.That(await harness.GetConsumerHarness<FailingBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.That(await harness.Published.SelectAsync<Fault<BatchItem>>().Take(2).Count(), Is.EqualTo(2));
        }

        [Test]
        public async Task Should_fault_once_for_each_message_in_the_batch_at_the_consumer_retry()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<FailingBatchConsumer>(c =>
                    {
                        c.UseMessageRetry(r => r.Immediate(1));
                        c.Options<BatchOptions>(o => o.SetMessageLimit(2));
                    });
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(2).Count(), Is.EqualTo(2));

            Assert.That(await harness.GetConsumerHarness<FailingBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.That(await harness.Published.SelectAsync<Fault<BatchItem>>().Take(2).Count(), Is.EqualTo(2));
        }

        [Test]
        public async Task Should_fault_once_for_each_message_in_the_batch_with_delayed_redelivery()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<FailingBatchConsumer>(c => c.Options<BatchOptions>(o => o.SetMessageLimit(2)));

                    x.AddConfigureEndpointsCallback((_, cfg) =>
                    {
                        cfg.UseDelayedRedelivery(r => r.Intervals(10));
                        cfg.UseMessageRetry(r => r.Immediate(1));
                    });
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(2).Count(), Is.EqualTo(2));

            Assert.That(await harness.GetConsumerHarness<FailingBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.That(await harness.Published.SelectAsync<Fault<BatchItem>>().Take(2).Count(), Is.EqualTo(2));
        }

        [Test]
        public async Task Should_fault_once_for_each_message_in_the_batch_with_scheduled_redelivery()
        {
            await using var provider = new ServiceCollection()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<FailingBatchConsumer>(c => c.Options<BatchOptions>(o => o.SetMessageLimit(2)));

                    x.AddConfigureEndpointsCallback((_, cfg) =>
                    {
                        cfg.UseScheduledRedelivery(r => r.Intervals(10));
                        cfg.UseMessageRetry(r => r.Immediate(1));
                    });

                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.UseDelayedMessageScheduler();

                        cfg.ConfigureEndpoints(context);
                    });
                })
                .BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            await harness.Bus.PublishBatch(new[] { new BatchItem(), new BatchItem() });

            Assert.That(await harness.Consumed.SelectAsync<BatchItem>().Take(2).Count(), Is.EqualTo(2));

            Assert.That(await harness.GetConsumerHarness<FailingBatchConsumer>().Consumed.Any<Batch<BatchItem>>(), Is.True);

            Assert.That(await harness.Published.SelectAsync<Fault<BatchItem>>().Take(2).Count(), Is.EqualTo(2));
        }
    }


    public class BatchItem
    {
    }


    public class BatchResult
    {
        public int Count { get; set; }
        public BatchCompletionMode Mode { get; set; }
    }


    public class TestBatchConsumerDefinition :
        ConsumerDefinition<TestBatchConsumer>
    {
        protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
            IConsumerConfigurator<TestBatchConsumer> consumerConfigurator)
        {
            endpointConfigurator.UseInMemoryOutbox();
            consumerConfigurator.Options<BatchOptions>(o => o.SetMessageLimit(5).SetTimeLimit(1000));
        }
    }


    public class TestBatchConsumer :
        IConsumer<Batch<BatchItem>>
    {
        public Task Consume(ConsumeContext<Batch<BatchItem>> context)
        {
            context.Respond(new BatchResult
            {
                Count = context.Message.Length,
                Mode = context.Message.Mode
            });

            return Task.CompletedTask;
        }
    }


    class FailingBatchConsumer :
        IConsumer<Batch<BatchItem>>
    {
        int _attempts;

        public int Attempts => _attempts;

        public Task Consume(ConsumeContext<Batch<BatchItem>> context)
        {
            Interlocked.Increment(ref _attempts);

            throw new IntentionalTestException("Failing Batch Consumer");
        }
    }
}
