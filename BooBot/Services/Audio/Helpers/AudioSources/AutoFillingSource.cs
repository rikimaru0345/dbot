using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BooBot.Services.Audio.HelperStreams
{
	partial class PcmMixer
	{
		// Uses a task to keep filling its buffer
		sealed class AutoFillingSource : AudioSource
		{
			AutoResetEvent dataDrainedEvent = new AutoResetEvent(true);
			int isReading = 0;

			// When we're dealing with multiple threads we have to do some double buffering (at least to keep it simple)
			byte[] localReadBuffer;

			public AutoFillingSource(Stream sourceStream, int bufferSize) : base(sourceStream, bufferSize)
			{
				localReadBuffer = new byte[bufferSize];
			}

			// Direct sources (from a file) just do static reads, they are fast enough to never cause us any trouble.
			public override void FillBuffer()
			{
				if (IsDone)
					// Nothing left to read
					return;

				if (Interlocked.CompareExchange(ref isReading, 1, 0) == 1)
					// Already reading
					return;

				// Using async/await for reading from the stream would massively complicate things (if done correctly)
				// todo: eventually we want to have one task reading from the source asynchronously
				// that could improve performance a bit, not sure if its needed anyway though
				Task.Run((Action)ReadTask);
			}

			void ReadTask()
			{
				while (true)
				{
					int spaceLeft = _buffer.Length - _bytesInBuffer;

					if (spaceLeft == 0)
					{
						// Wait for the "we got more space" signal
						dataDrainedEvent.WaitOne();
						continue;
					}

					int read;
					try
					{
						read = _sourceStream.Read(localReadBuffer, 0, spaceLeft);
					}
					catch (Exception ex)
					{
						SourceState.SetException(ex);
						IsDone = true;
						// no need to "release" isReading here
						return;
					}

					if (spaceLeft > 0 && read == 0)
					{
						IsDone = true;
						_sourceStream.Dispose();
						SourceState.SetResult(0);
						break;
					}

					// Copy over the data we just read, no way around the lock here...
					lock (_buffer)
					{
						Buffer.BlockCopy(localReadBuffer, 0, _buffer, _bytesInBuffer, read);
						_bytesInBuffer += read;
					}
				}

				// Signal we're not reading anymore
				Interlocked.Exchange(ref isReading, 0);
			}

			public override void OnMixing(int bytesToMix)
			{
				// In "auto fill mode" the buffer will get used by the filling task as well as the mixer.
				// We have to synchronize access.
				// A simple lock() will be perfectly fine in this situation 
				Monitor.Enter(_buffer);
			}

			public override void OnMixingDone(int bytesDrained)
			{
				Buffer.BlockCopy(_buffer, bytesDrained, _buffer, 0, _buffer.Length - bytesDrained);
				_bytesInBuffer -= bytesDrained;
				TotalBytesProcessed += bytesDrained;
				dataDrainedEvent.Set();

				Monitor.Exit(_buffer);
			}

			public override void Cancel()
			{
				IsDone = true;
				_sourceStream.Dispose();
				SourceState.SetResult(0);
			}
		}
	}
}
