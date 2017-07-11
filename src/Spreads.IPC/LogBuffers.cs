// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.Buffers;
using Spreads.IPC.Logbuffer;
using System;
using System.IO;

namespace Spreads.IPC
{
    /// <summary>
    /// Takes a log file name and maps the file into memory and wraps it with <seealso cref="DirectBuffer"/>s as appropriate.
    /// </summary>
    /// <seealso> cref="LogBufferDescriptor" </seealso>
    public class LogBuffers : IDisposable
    {
        private readonly int _termLength;
        private DirectFile _df;
        private readonly DirectBuffer[] _buffers = new DirectBuffer[(LogBufferDescriptor.PARTITION_COUNT * 2) + 1];
        private readonly LogBufferPartition[] _partitions = new LogBufferPartition[LogBufferDescriptor.PARTITION_COUNT];

        public LogBuffers(string logFileName, int termLength = LogBufferDescriptor.TERM_MIN_LENGTH)
        {
            try
            {
                long logLength = LogBufferDescriptor.PARTITION_COUNT *
                                 (LogBufferDescriptor.TERM_META_DATA_LENGTH + (long)termLength) +
                                 LogBufferDescriptor.LOG_META_DATA_LENGTH;
                termLength = LogBufferDescriptor.ComputeTermLength(logLength);
                LogBufferDescriptor.CheckTermLength(termLength);
                _df = new DirectFile(logFileName, logLength);
                _termLength = termLength;

                // if log length exceeds MAX_INT we need multiple mapped buffers, (see FileChannel.map doc).
                if (logLength < int.MaxValue)
                {
                    int metaDataSectionOffset = termLength * LogBufferDescriptor.PARTITION_COUNT;

                    for (int i = 0; i < LogBufferDescriptor.PARTITION_COUNT; i++)
                    {
                        int metaDataOffset = metaDataSectionOffset + (i * LogBufferDescriptor.TERM_META_DATA_LENGTH);

                        _buffers[i] = new DirectBuffer(termLength, _df.Buffer.Data + i * termLength);
                        _buffers[i + LogBufferDescriptor.PARTITION_COUNT] = new DirectBuffer(LogBufferDescriptor.TERM_META_DATA_LENGTH, _df.Buffer.Data + metaDataOffset);
                        _partitions[i] = new LogBufferPartition(_buffers[i], _buffers[i + LogBufferDescriptor.PARTITION_COUNT]);
                    }

                    _buffers[_buffers.Length - 1] = new DirectBuffer(LogBufferDescriptor.LOG_META_DATA_LENGTH,
                        _df.Buffer.Data + (int)(logLength - LogBufferDescriptor.LOG_META_DATA_LENGTH));
                }
                else
                {
                    throw new NotImplementedException("TODO Check .NET mapping limit");
                }
            }
            catch (IOException ex)
            {
                throw new AggregateException(ex);
            }

            foreach (var buffer in _buffers)
            {
                buffer.VerifyAlignment(8);
            }
        }

        public DirectBuffer[] Buffers => _buffers;

        public DirectBuffer LogMetaData => _buffers[_buffers.Length - 1];

        public DirectFile DirectFile => _df;

        public void Dispose(bool disposing)
        {
            _df.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~LogBuffers()
        {
            Dispose(false);
        }

        public int TermLength => _termLength;

        public LogBufferPartition[] Partitions => _partitions;
    }
}