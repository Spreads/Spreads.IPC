using Spreads.Buffers;
using Spreads.IPC;
using Spreads.Utils;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace IPCThroughput
{
    public class IpcThroughput
    {
        private static readonly int MessageLength = 32;
        private const int BurstLength = 1000000;

        private static volatile bool running = true;

        public static void Main()
        {
            using (Process p = Process.GetCurrentProcess())
            {
                p.PriorityClass = ProcessPriorityClass.Normal;
            }

            var subscriber = new Subscriber();
            var subscriberThread = new Thread(subscriber.Run) { Name = "subscriber" };
            var publisherThread = new Thread(new Publisher().Run) { Name = "publisher" };
            var publisherThread2 = new Thread(new Publisher().Run) { Name = "publisher2" };
            var publisherThread3 = new Thread(new Publisher().Run) { Name = "publisher3" };
            var rateReporterThread = new Thread(new RateReporter(subscriber).Run) { Name = "rate-reporter" };

            rateReporterThread.Start();
            subscriberThread.Start();
            publisherThread.Start();
            publisherThread2.Start();
            publisherThread3.Start();

            Console.WriteLine("Press any key to stop...");
            Console.Read();

            running = false;

            subscriberThread.Join();
            publisherThread.Join();
            publisherThread2.Join();
            publisherThread3.Join();
            rateReporterThread.Join();
        }

        public class RateReporter
        {
            internal readonly Subscriber Subscriber;
            private readonly Stopwatch _stopwatch;

            public RateReporter(Subscriber subscriber)
            {
                Subscriber = subscriber;
                _stopwatch = Stopwatch.StartNew();
            }

            public void Run()
            {
                var lastTotalBytes = Subscriber.TotalBytes();

                while (running)
                {
                    Thread.Sleep(1000);

                    var newTotalBytes = Subscriber.TotalBytes();
                    var duration = _stopwatch.ElapsedMilliseconds;
                    var bytesTransferred = newTotalBytes - lastTotalBytes;
                    Console.WriteLine(
                        $"Duration {duration:N0}ms - {bytesTransferred / MessageLength:N0} messages - {bytesTransferred:N0} bytes, GC0 {GC.CollectionCount(0)}, GC1 {GC.CollectionCount(1)}, GC2 {GC.CollectionCount(2)}");

                    _stopwatch.Restart();
                    lastTotalBytes = newTotalBytes;
                }
            }
        }

        public sealed class Publisher
        {
            private AppendLog _log;

            public Publisher()
            {
                _log = new AppendLog("IPC_throughput", 500);
            }

            public unsafe void Run()
            {
                long totalMessageCount = 0;
                var handle = Marshal.AllocHGlobal(MessageLength);
                var source = new DirectBuffer(MessageLength, handle);
                while (running)
                {
                    for (var i = 0; i < BurstLength; i++)
                    {
                        _log.Claim(MessageLength, out var claim);
                        ByteUtil.VectorizedCopy((byte*)(claim.Data), (byte*)(source.Data), (uint)MessageLength);
                        //claim.Buffer.WriteBytes(claim.Offset, source, 0, MessageLength);
                        claim.Commit();
                        ++totalMessageCount;
                    }
                }
            }
        }

        public class Subscriber
        {
            private AppendLog _log;

            private long _totalBytes;

            public Subscriber()
            {
                _log = new AppendLog("IPC_throughput", 500);
                _log.StartPolling();
            }

            public long TotalBytes()
            {
                return _totalBytes;
            }

            public void Run()
            {
                _log.OnAppend += OnAppend;
            }

            public void OnAppend(DirectBuffer buffer)
            {
                _totalBytes += buffer.Length;
            }
        }
    }
}