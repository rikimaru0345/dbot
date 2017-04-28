using System;
using System.IO;
using Discord.Commands;

namespace BooBot
{
	class VolumeControlPcmStream : Stream
		{
			public float Volume { get; set; } = 1;
			readonly Stream baseStream;

			public override bool CanRead => false;
			public override bool CanSeek => false;
			public override bool CanWrite => true;
			public override long Length => throw new NotSupportedException();
			public override long Position
			{
				get => throw new NotSupportedException();
				set => throw new NotSupportedException();
			}


			public VolumeControlPcmStream(Stream baseStream)
			{
				this.baseStream = baseStream;
			}


			public override void Write(byte[] buffer, int offset, int count)
			{
				// We always need full samples: 2bytes per sample (1short)
				// Later we'll automatically remember the last "odd" byte and merge it into the stream correctly.
				if ((count % 2) != 0)
					throw new InvalidDataException("Must always write a multiple of 2 bytes, given: " + count);

				unsafe
				{
					int sampleCount = count / 2;
					float v = Volume;
					fixed (byte* ptr = buffer)
					{
						byte* byteBuffer = ptr + offset;
						short* shortBuffer = (short*)byteBuffer;

						for (int i = 0; i < sampleCount; i++)
						{
							// Get and scale
							float f = shortBuffer[i] * v;

							// Clip
							if (f < short.MinValue)
								f = short.MinValue;
							else if (f > short.MaxValue)
								f = short.MaxValue;

							// Write
							shortBuffer[i] = (short)f;
						}
					}
				}

				baseStream.Write(buffer, offset, count);
			}


			public override int Read(byte[] buffer, int offset, int count)
			{
				throw new NotSupportedException();
			}

			public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
			public override void Flush() => throw new NotSupportedException();
			public override void SetLength(long value) => throw new NotImplementedException();
		}
}
