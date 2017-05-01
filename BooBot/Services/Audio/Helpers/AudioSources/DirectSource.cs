using System;
using System.IO;

namespace BooBot.Services.Audio.HelperStreams
{
	partial class PcmMixer
	{
		// Direct sources have literally zero overhead. We would need a byte array anyway at some point.
		sealed class DirectSource : AudioSource
		{
			public DirectSource(Stream sourceStream, int bufferSize) : base(sourceStream, bufferSize)
			{
			}

			// Direct sources (from a file) just do static reads, they are fast enough to never cause us any trouble.
			public override void FillBuffer()
			{
				if (IsDone)
					return;

				int spaceLeft = _buffer.Length - _bytesInBuffer;

				if (spaceLeft == 0)
					// buffer is full at the moment
					return;

				int read;
				try
				{
					read = _sourceStream.Read(_buffer, _bytesInBuffer, spaceLeft);
				}
				catch (Exception ex)
				{
					SourceState.SetException(ex);
					IsDone = true;
					_sourceStream.Dispose();
					return;
				}

				if (read == 0)
				{
					// We've reached the end
					IsDone = true;
					SourceState.SetResult(0);
					_sourceStream.Dispose();
				}

				_bytesInBuffer += read;
			}
			
			public override void OnMixingDone(int bytesDrained)
			{
				Buffer.BlockCopy(_buffer, bytesDrained, _buffer, 0, _buffer.Length - bytesDrained);
				_bytesInBuffer -= bytesDrained;
				TotalBytesProcessed += bytesDrained;
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
