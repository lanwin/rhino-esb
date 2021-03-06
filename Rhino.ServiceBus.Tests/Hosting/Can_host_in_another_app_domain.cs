using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Castle.MicroKernel.Registration;
using Castle.Windsor;
using Castle.Windsor.Configuration.Interpreters;
using Rhino.ServiceBus.Hosting;
using Rhino.ServiceBus.Impl;
using Xunit;
using System.Linq;

namespace Rhino.ServiceBus.Tests.Hosting
{
    public class Can_host_in_another_app_domain : MsmqTestBase, OccasionalConsumerOf<StringMsg>
    {
        readonly RemoteAppDomainHost host = new RemoteAppDomainHost(
            Path.Combine(Environment.CurrentDirectory, "Rhino.ServiceBus.Tests.dll"), typeof(TestBootStrapper));
        private string reply;

        private readonly ManualResetEvent resetEvent = new ManualResetEvent(false);

        private readonly IWindsorContainer container;

        public Can_host_in_another_app_domain()
        {
            container = new WindsorContainer(new XmlInterpreter(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AnotherBus.config")
                ));
            container.Kernel.AddFacility("rhino.esb", new RhinoServiceBusFacility());
        }

        [Fact]
        public void Components_are_registered_using_name_only()
        {
            var windsorContainer = new WindsorContainer();
            new SimpleBootStrapper().InitializeContainer(windsorContainer);
            var handler = windsorContainer.Kernel.GetHandler(typeof(TestRemoteHandler).Name);
            Assert.NotNull(handler);
        }

        [Fact]
        public void And_accept_messages_from_there()
        {
            host.Start();

            using (var bus = container.Resolve<IStartableServiceBus>())
            {
                bus.Start();

                using (bus.AddInstanceSubscription(this))
                {
                    bus.Send(new Uri("msmq://localhost/test_queue").ToEndpoint(), new StringMsg
                    {
                        Value = "hello"
                    });

                    Assert.True(resetEvent.WaitOne(TimeSpan.FromSeconds(10), false));

                    Assert.Equal("olleh", reply);
                }
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            host.Close();
        }

        public void Consume(StringMsg message)
        {
            reply = message.Value;
            resetEvent.Set();
        }
    }

    public class SimpleBootStrapper : AbstractBootStrapper
    {

    }

    public class TestBootStrapper : AbstractBootStrapper
    {
        protected override void ConfigureContainer()
        {
            container.Register(Component.For<TestRemoteHandler>());
        }
    }

    public class TestRemoteHandler : ConsumerOf<StringMsg>
    {
        private readonly IServiceBus bus;

        public TestRemoteHandler(IServiceBus bus)
        {
            this.bus = bus;
        }

        public void Consume(StringMsg message)
        {
            bus.Reply(new StringMsg
            {
                Value = new String(message.Value.Reverse().ToArray())
            });
        }
    }

    public class StringMsg
    {
        public string Value { get; set; }
    }
}