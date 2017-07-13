using Spreads.IPC.Network;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace UDPThroughput
{
    public class IpcThroughput
    {
        private static readonly int MessageLength = 1024;

        private static volatile bool running = true;

        public static void Main()
        {
            using (Process p = Process.GetCurrentProcess())
            {
                p.PriorityClass = ProcessPriorityClass.Normal;
            }

            var subscriber = new Subscriber();
            var publisher = new Publisher();
            var subscriberThread = new Thread(subscriber.Run) { Name = "subscriber" };
            var publisherThread = new Thread(publisher.Run) { Name = "publisher" };
            var rateReporterThread = new Thread(new RateReporter(subscriber, publisher).Run) { Name = "rate-reporter" };

            rateReporterThread.Start();
            subscriberThread.Start();
            publisherThread.Start();

            Console.WriteLine("Press any key to stop...");
            Console.Read();

            running = false;

            subscriberThread.Join();
            publisherThread.Join();
            rateReporterThread.Join();
        }

        public class RateReporter
        {
            internal readonly Subscriber Subscriber;
            internal readonly Publisher Publisher;
            private readonly Stopwatch _stopwatch;

            public RateReporter(Subscriber subscriber, Publisher publisher)
            {
                Subscriber = subscriber;
                Publisher = publisher;
                _stopwatch = Stopwatch.StartNew();
            }

            public void Run()
            {
                var lastReceivedTotalBytes = Subscriber.TotalBytes();
                var lastSentTotalBytes = Publisher.TotalBytes();

                while (running)
                {
                    Thread.Sleep(1000);

                    var duration = _stopwatch.ElapsedMilliseconds;

                    var newSentTotalBytes = Publisher.TotalBytes();
                    var bytesSent = newSentTotalBytes - lastSentTotalBytes;

                    var newTotalReceivedBytes = Subscriber.TotalBytes();
                    var bytesReceived = newTotalReceivedBytes - lastReceivedTotalBytes;

                    Console.WriteLine(
                        $"Duration {duration:N0}ms - {bytesReceived / MessageLength:N0} in msgs - {bytesReceived:N0} in bytes - {bytesSent / MessageLength:N0} out msg - {bytesSent:N0} out bytes - {(bytesSent - bytesReceived) / MessageLength} lost, GC0 {GC.CollectionCount(0)}, GC1 {GC.CollectionCount(1)}, GC2 {GC.CollectionCount(2)}");

                    _stopwatch.Restart();
                    lastReceivedTotalBytes = newTotalReceivedBytes;
                    lastSentTotalBytes = newSentTotalBytes;
                }
            }
        }

        public sealed class Publisher
        {
            public readonly UDPConnection Connection;

            public Publisher()
            {
                Connection = new UDPConnection(IPAddress.Loopback, 51311, IPAddress.Loopback, 51310);
            }

            public long TotalBytes()
            {
                return Connection.SendCounter * MessageLength;
            }

            public void Run()
            {
                var buffer = new byte[MessageLength];
                while (running)
                {
                    Connection.Send(buffer, 0, buffer.Length);
                }
            }
        }

        public class Subscriber
        {
            public readonly UDPConnection Connection;

            private long _totalBytes;

            public Subscriber()
            {
                Connection = new UDPConnection(IPAddress.Loopback, 51310, IPAddress.Loopback, 51311);
            }

            public long TotalBytes()
            {
                return Connection.ReceiveCounter * MessageLength;
            }

            public void Run()
            {
                Connection.StartReceive();
            }
        }
    }
}