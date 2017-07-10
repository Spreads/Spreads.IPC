// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.IPC.Logbuffer;
using Spreads.IPC.Protocol;
using Spreads.Utils;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Spreads.IPC
{
    // Append log is based on Aeron LogBuffers
    // LogBuffers are permament, we always start from current position
    // Spreads protocol ensures that if a slow-producing series
    // has non-flushed mutations that are cleared from the LogBuffers,
    // then on next subscription to the series will force flushing
    // of chunks, after which subscribers will reveive chunk commands
    // and then Flush event, after which they will have syncronized data.

    // TODO slowest subscriber position

    public interface IAppendLog : IDisposable
    {
        void Append<T>(T message);

        long Claim(int length, out BufferClaim claim);

        event OnAppendHandler OnAppend;
    }

    public sealed class AppendLog : IAppendLog
    {
        private readonly LogBuffers _logBuffers;

        private static readonly WaitCallback CleanerCallback = AppendLog.ClearLogBuffer;
        private static readonly TimerCallback TimerCallback = AppendLog.SendStatusMessage;

        //private static int _counter;
        private static int _pid = Process.GetCurrentProcess().Id;

        // subscriber id
        private readonly long _sid;

        private Timer _smTimer;

        private long _subscriberPosition;
        private readonly HeaderWriter _dataHeaderWriter;
        private readonly HeaderWriter _smHeaderWriter;

        private readonly TermAppender[] _termAppenders = new TermAppender[LogBufferDescriptor.PARTITION_COUNT];
        private readonly int _initialTermId;
        private readonly int _positionBitsToShift;

        private Task _poller;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private int _termLengthMask;

        public AppendLog(string filepath, int bufferSizeMb = 100)
        {
            var startTime = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
            _sid = (_pid << 32) & startTime;

            var bufferSizeInBytes = BitUtil.FindNextPositivePowerOfTwo(bufferSizeMb * 1024 * 1024);

            _logBuffers = new LogBuffers(filepath, bufferSizeInBytes);

            for (int i = 0; i < LogBufferDescriptor.PARTITION_COUNT; i++)
            {
                _termAppenders[i] = new TermAppender(_logBuffers.Buffers[i], _logBuffers.Buffers[i + LogBufferDescriptor.PARTITION_COUNT]);
            }
            _termLengthMask = _logBuffers.TermLength - 1;
            _positionBitsToShift = IntUtil.NumberOfTrailingZeros(_logBuffers.TermLength);
            _initialTermId = LogBufferDescriptor.InitialTermId(_logBuffers.LogMetaData);
            var defaultHeader = DataHeaderFlyweight.CreateDefaultHeader(0, 0, _initialTermId);
            var smHeader = new DataHeader
            {
                Header =
                {
                    Version = HeaderFlyweight.CURRENT_VERSION,
                    Flags = (byte) DataHeaderFlyweight.BEGIN_AND_END_FLAGS,
                    Type = (short) HeaderFlyweight.HDR_TYPE_SM
                },
                SessionID = 0,
                StreamID = 0,
                TermID = _initialTermId,
                ReservedValue = DataHeaderFlyweight.DEFAULT_RESERVE_VALUE
            };
            _dataHeaderWriter = new HeaderWriter(defaultHeader);
            _smHeaderWriter = new HeaderWriter(smHeader);

            // Send status message every 100 milliseconds
            _smTimer = new Timer(TimerCallback, this, 0, 100);
        }

        public void StartPolling()
        {
            _subscriberPosition = Position;
            Trace.Assert(_subscriberPosition == Position);

            _poller = Task.Factory.StartNew(() =>
                {
                    try { }
                    finally
                    {
                        while (!_cts.IsCancellationRequested)
                        {
                            // NB catch exception outside spin loop and restart
                            // the loop - this give substantial performance gain
                            // due to inlining of the Read method
                            try
                            {
                                var sw = new SpinWait();
                                while (!_cts.IsCancellationRequested)
                                {
                                    var fragments = Poll();
                                    if (fragments > 0)
                                    {
                                        sw.Reset();
                                    }
                                    else
                                    {
                                        sw.SpinOnce();
                                    }
                                }
                            }
                            catch (Exception t)
                            {
                                OnError?.Invoke(t);
                            }
                        }
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                .ContinueWith(task =>
                {
                    Console.WriteLine("AppendLog Poll should never throw exceptions" + Environment.NewLine + task.Exception);
                    Environment.FailFast("AppendLog Poll should never throw exceptions", task.Exception);
                }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Subscriber (session) Id
        /// </summary>
        public long Sid => _sid;

        /// <summary>
        /// Process Id
        /// </summary>
        public int Pid => (int)(_sid >> 32);

        public event OnAppendHandler OnAppend;

        public event ErrorHandler OnError;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long Claim(int length, out BufferClaim claim)
        {
            while (true)
            {
                var partitionIndex = LogBufferDescriptor.ActivePartitionIndex(_logBuffers.LogMetaData);
                var termAppender = _termAppenders[partitionIndex];
                long rawTail = termAppender.RawTailVolatile;
                long termOffset = rawTail & 0xFFFFFFFFL;

                long position = LogBufferDescriptor.ComputeTermBeginPosition(LogBufferDescriptor.TermId(rawTail), _positionBitsToShift, _initialTermId) + termOffset;

                long result = termAppender.Claim(_dataHeaderWriter, length, out claim);

                if (result < 0)
                {
                }

                long newPosition = NewPosition(partitionIndex, (int)termOffset, position, result);

                if (newPosition < 0) { continue; }
                return newPosition;
            }
        }

        /// <summary>
        /// Get the current position to which the publication has advanced for this stream.
        /// </summary>
        /// <returns> the current position to which the publication has advanced for this stream. </returns>
        public long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                long rawTail = _termAppenders[LogBufferDescriptor.ActivePartitionIndex(_logBuffers.LogMetaData)].RawTailVolatile;
                int termOffset = LogBufferDescriptor.TermOffset(rawTail, _logBuffers.TermLength);
                return LogBufferDescriptor.ComputePosition(LogBufferDescriptor.TermId(rawTail), termOffset, _positionBitsToShift, _initialTermId);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long NewPosition(int index, int currentTail, long position, long result)
        {
            long newPosition = TermAppender.TRIPPED;
            int termOffset = TermAppender.TermOffset(result);
            if (termOffset > 0)
            {
                newPosition = (position - currentTail) + termOffset;
            }
            else if (termOffset == TermAppender.TRIPPED)
            {
                //int nextIndex = NextPartitionIndex(index);
                //int nextNextIndex = NextPartitionIndex(nextIndex);
                //_termAppenders[nextIndex].TailTermId(TermAppender.TermId(result) + 1);
                //_termAppenders[nextNextIndex].StatusOrdered(LogBufferDescriptor.NEEDS_CLEANING);
                //ActivePartitionIndex(_logBuffers.LogMetaData, nextIndex);
                // TODO why we rotate log here and not in term appender?
                RotateAndClean(index, result);
            }
            return newPosition;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RotateAndClean(int index, long result)
        {
            LogBufferDescriptor.RotateLog(_logBuffers.Partitions, _logBuffers.LogMetaData, index,
                TermAppender.TermId(result) + 1);
            ThreadPool.QueueUserWorkItem(CleanerCallback, this);
        }

        /// <summary>
        /// Poll messages starting from _subscriberPosition and invoke OnAppendHandlerOld event
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long Poll()
        {
            var subscriberIndex = LogBufferDescriptor.IndexByPosition(_subscriberPosition, _positionBitsToShift);
            int termOffset = (int)_subscriberPosition & _termLengthMask;
            var termBuffer = _logBuffers.Buffers[subscriberIndex];

            // ,ErrorHandler errorHandler //OnError
            long outcome = TermReader.Read(termBuffer, termOffset, OnAppend, 10);

            UpdatePosition(termOffset, TermReader.Offset(outcome));
            return outcome & 0xFFFFFFFFL;
        }

        /// <summary>
        /// TODO inline
        /// </summary>
        /// <param name="offsetBefore"></param>
        /// <param name="offsetAfter"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdatePosition(int offsetBefore, int offsetAfter)
        {
            _subscriberPosition = _subscriberPosition + (offsetAfter - offsetBefore);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append<T>(T message)
        {
            throw new NotImplementedException();
            //MemoryStream stream;
            //var sizeOf = BinarySerializer.SizeOf<T>(message, out stream);
            //BufferClaim claim;
            //Claim(sizeOf, out claim);
            //var buffer = claim.Buffer;
            //BinarySerializer.Write<T>(message, ref buffer, (uint)claim.Offset, stream);
            //claim.Commit();
        }

        /// <summary>
        /// Called on a thread pool in background when a term appender returns TRIPPED result
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ClearLogBuffer(object state)
        {
            try
            {
                var al = (AppendLog)state;
                foreach (var partition in al._logBuffers.Partitions)
                {
                    if (partition.Status == LogBufferDescriptor.NEEDS_CLEANING)
                    {
                        partition.Clean();
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError("AppendLog.ClearLogBuffer should never throw exceptions" + Environment.NewLine + ex);
                Environment.FailFast("AppendLog.ClearLogBuffer should never throw exceptions", ex);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SendStatusMessage(object state)
        {
            try
            {
                var al = (AppendLog)state;
            }
            catch (Exception ex)
            {
                Trace.TraceError("AppendLog.SendStatusMessage should never throw exceptions" + Environment.NewLine + ex);
                Environment.FailFast("AppendLog.SendStatusMessage should never throw exceptions", ex);
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public void Dispose(bool disposing)
        {
            _cts.Cancel();
            //_poller.Wait();
            _logBuffers.Dispose();
            _smTimer.Dispose();
            if (disposing)
            {
                GC.SuppressFinalize(this);
            }
        }

        ~AppendLog()
        {
            Dispose(false);
        }
    }
}