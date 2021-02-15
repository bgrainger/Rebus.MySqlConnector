using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Logging;
using Rebus.MySql.Tests;
using Rebus.Tests.Contracts;
using Rebus.Tests.Contracts.Utilities;

namespace PerformanceTest
{
	class Program
	{
        const string QueueName = "perftest";

        static readonly string TableName = TestConfig.GetName("perftest");

        static async Task Main()
		{
            MySqlTestHelper.DropTable(TableName);

            await CheckReceivePerformance(2000, false);
        }

        public static async Task CheckReceivePerformance(int messageCount, bool useLeaseBasedTransport)
        {
            using var adapter = new BuiltinHandlerActivator();

            Configure.With(adapter)
                .Logging(l => l.ColoredConsole(LogLevel.Warn))
                .Transport(t =>
                {
                    if (useLeaseBasedTransport)
                    {
                        Console.WriteLine("*** Using LEASE-BASED SQL transport ***");
                        t.UseMySqlInLeaseMode(new MySqlLeaseTransportOptions(MySqlTestHelper.ConnectionString), QueueName);
                    }
                    else
                    {
                        Console.WriteLine("*** Using NORMAL SQL transport ***");
                        t.UseMySql(new MySqlTransportOptions(MySqlTestHelper.ConnectionString), QueueName);
                    }
                })
                .Options(o =>
                {
                    o.SetNumberOfWorkers(0);
                    o.SetMaxParallelism(20);
                })
                .Start();

            Console.WriteLine($"Sending {messageCount} messages...");

            var stopwatch = Stopwatch.StartNew();

            await Task.WhenAll(Enumerable.Range(0, messageCount)
                .Select(i => adapter.Bus.SendLocal($"THIS IS MESSAGE {i}")));

            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;

            Console.WriteLine($"Inserted {messageCount} messages in {elapsedSeconds:0.0} s - that's {messageCount / elapsedSeconds:0.0} msg/s");

            using var counter = new SharedCounter(messageCount);

            adapter.Handle<string>(async message => counter.Decrement());

            Console.WriteLine("Waiting for messages to be received...");

            stopwatch = Stopwatch.StartNew();

            adapter.Bus.Advanced.Workers.SetNumberOfWorkers(3);

            counter.WaitForResetEvent(timeoutSeconds: messageCount / 100 + 5);

            elapsedSeconds = stopwatch.Elapsed.TotalSeconds;

            Console.WriteLine($"{messageCount} messages received in {elapsedSeconds:0.0} s - that's {messageCount / elapsedSeconds:0.0} msg/s");
        }
    }
}
