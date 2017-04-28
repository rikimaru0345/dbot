using System;
using System.IO;

namespace BooBot
{
	partial class MixStream : Stream
	{
		// A ChannelStream is a proxy that represents one channel of a MixStream.
		private class ChannelStream : Stream
		{
			readonly MixStream mixStream;
			internal int BufferPosition { get; private set; }
			internal readonly byte[] channelBuffer;

			public override bool CanWrite => true;

			public override bool CanRead => false;
			public override bool CanSeek => false;
			public override long Length => throw new NotSupportedException();
			public override long Position
			{
				get => throw new NotSupportedException();
				set => throw new NotSupportedException();
			}

			internal ChannelStream(MixStream mixStream, int bufferSize)
			{
				this.mixStream = mixStream;
				this.channelBuffer = new byte[bufferSize];
			}

			public override void Write(byte[] incomingBuffer, int offset, int count)
			{
				if ((count % 4) != 0)
					throw new InvalidDataException("Can only write modulo 4 sizes. (2bytes per sample * 2 channels)");

				// Ok, we need to copy in whatever we can, and if we hit the limit we'll flush
				// After the flush we have space again so we can copy over the rest.
				// todo: improve this by using a circular buffer of fixed size instead.

				lock (channelBuffer) // we need to lock here because MixAndFlush also locks on channelBuffer to make sure nobody is writing to any channel while we're mixing
				{

					int totalWritten = 0;
					while (totalWritten < count)
					{
						int currentSpaceLeft = channelBuffer.Length - BufferPosition;
						int leftToWrite = count - totalWritten;
						int goingToWrite = Math.Min(currentSpaceLeft, leftToWrite);

						// do the write
						Buffer.BlockCopy(incomingBuffer, totalWritten, channelBuffer, BufferPosition, goingToWrite);
						BufferPosition += goingToWrite;
						totalWritten += goingToWrite;

						// If we're full now, flush! (or maybe we just wrote some left over bytes)
						if (BufferPosition == channelBuffer.Length)
							mixStream.MixAndFlush();
					}
				}
			}

			internal void OnFlushed(int bytesFlushed)
			{
				// The mixing routine flushed out some bytes,
				// which means we need to copy what we have left to the start (would be easier with a circular buffer - where easier means no need to do anything)
				Buffer.BlockCopy(channelBuffer, bytesFlushed, channelBuffer, 0, channelBuffer.Length - bytesFlushed);
				BufferPosition -= bytesFlushed;
			}

			public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
			public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
			public override void Flush() => throw new NotSupportedException();
			public override void SetLength(long value) => throw new NotImplementedException();
		}
	}
}
