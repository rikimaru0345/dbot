using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Discord.Commands;

namespace BooBot
{
	partial class MixStream : Stream
	{
		readonly Stream baseStream;
		ChannelStream[] channels;

		public int ChannelCount => channels.Length;
		public Stream this[int channelIndex]
		{
			get => channels[channelIndex];
		}

		readonly float[] accMixBuffer; // accumulates samples while mixing
		readonly byte[] finalMixBuffer; // lol kingdom hearts
		readonly List<ChannelStream> currentlyMixing = new List<ChannelStream>();

		public override bool CanWrite => false;
		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override long Length => throw new NotSupportedException();
		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		// baseStream is where the data goes after mixing,
		// channels is the amount of channels that can play together,
		// bytesPerFlush says how many bytes one channel needs to write in order to trigger a flush
		public MixStream(Stream baseStream, int channels, int bytesPerFlush)
		{
			this.baseStream = baseStream;
			this.channels = new ChannelStream[channels];
			for (int i = 0; i < channels; i++)
				this.channels[i] = new ChannelStream(this, bytesPerFlush);

			// todo: sadly we can't help it, we have to make some assumptions about the pcm format we're dealing with
			// we assume 2bytes per sample (short), and 2 channels. so 4 bytes in total
			accMixBuffer = new float[bytesPerFlush / 2]; // We want to keep the amount of samples, so we only divide by "bytes per sample"
			finalMixBuffer = new byte[bytesPerFlush];
		}

		// One of the channels is full!
		void MixAndFlush()
		{
			// Ok, so what is the least amount of data we can get from all the channels?

			try
			{
				for (int i = 0; i < channels.Length; i++)
					Monitor.Enter(channels[i].channelBuffer);


				currentlyMixing.Clear();
				for (int i = 0; i < channels.Length; i++)
					if (channels[i].BufferPosition > 0)
						currentlyMixing.Add(channels[i]);

				// Early out: just one channel doesn't need mixing and can directly stream to the output
				if (currentlyMixing.Count == 1)
				{
					var channel = currentlyMixing[0];
					baseStream.Write(channel.channelBuffer, 0, channel.BufferPosition);
					channel.OnFlushed(channel.BufferPosition);
					return;
				}

				// We want to mix the lowest common amount of data
				// That will ensure that at least one channel will become completely drained.
				int leastData = currentlyMixing.Min(c => c.BufferPosition);

				int samplesToTake = leastData / 2;

				// Zero the accumulator buffer
				for (int i = 0; i < samplesToTake; i++)
					accMixBuffer[i] = 0;

				// Lets do some mixing, we will first add everything and then clip it later
				unsafe
				{
					for (int c = 0; c < currentlyMixing.Count; c++)
					{
						var channel = currentlyMixing[c];
						fixed (byte* channelData = channel.channelBuffer)
						{
							short* shortBuffer = (short*)channelData;

							for (int i = 0; i < samplesToTake; i++)
								accMixBuffer[i] += shortBuffer[i];
						}
					}

					// We're done accumulating
					// Now convert back to bytes while at the same time clipping the values that went out of bounds!
					fixed (byte* finalMixPtr = finalMixBuffer)
					{
						short* finalMixShort = (short*)finalMixPtr;
						for (int i = 0; i < samplesToTake; i++)
						{
							var f = accMixBuffer[i];

							if (f < short.MinValue)
								f = short.MinValue;
							else if (f > short.MaxValue)
								f = short.MaxValue;

							finalMixShort[i] = (short)f;
						}
					}
				}

				// finalMixBuffer contains the data we want to send to the baseStream
				baseStream.Write(finalMixBuffer, 0, leastData);

				for (int i = 0; i < currentlyMixing.Count; i++)
					currentlyMixing[i].OnFlushed(leastData);

			}
			finally
			{
				for (int i = 0; i < channels.Length; i++)
					Monitor.Exit(channels[i].channelBuffer);
			}
		}

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Write to one of the channels instead!");
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void Flush() => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotImplementedException();
	}
}
