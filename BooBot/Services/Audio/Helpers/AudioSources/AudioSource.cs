using System;
using System.IO;
using System.Threading.Tasks;

namespace BooBot.Services.Audio.HelperStreams
{
	partial class PcmMixer
	{
		// todo, volt: should this be nested? maybe not anymore because there are multiple implementations now
		// A source from where we get delicious audio such as "JohnCena.mp3", "MLG_Airhorn.wav" and "Allahu Akbar.ogg" 
		private abstract unsafe class AudioSource
		{
			/// <summary>
			/// Used to let the user user know when this source is complete
			/// </summary>
			public TaskCompletionSource<int> SourceState { get; } = new TaskCompletionSource<int>();

			protected readonly Stream _sourceStream;

			// todo, volt: we want to avoid both:
			// 1.) exposing an internal buffer like this because its just terrible design
			// 2.) having a Read(...) method, which would essentially force us to provide a buffer, which would cause yet another data-copy
			// Number 2 is definitely not an option since we're dealing with real-time data.
			protected readonly byte[] _buffer;
			protected int _bytesInBuffer;

			/// <summary>
			/// Tells the mixer how much data this source can safely mix into the egress stream right now
			/// </summary>
			public int Available => _bytesInBuffer;
			public MixerSource AsMixerSource => new MixerSource(this);
			public int TotalBytesProcessed { get; protected set; }
			public float Volume { get; set; } = 1;

			public bool IsDone { get; protected set; }


			protected AudioSource(Stream sourceStream, int bufferSize)
			{
				_sourceStream = sourceStream;
				_buffer = new byte[bufferSize];
			}

			// Tell this source to start buffering / read from its internal source
			public abstract void FillBuffer();

			public virtual void OnMixing(int bytesToMix) { }
			public virtual void OnMixingDone(int bytesDrained) { }
			
			internal void MixInto(float[] accumulatorBuffer, int bytesToMix)
			{
				OnMixing(bytesToMix);

				var v = Volume;
				var samples = bytesToMix / 2;

				fixed (byte* ptr = _buffer)
				{
					short* shortBuffer = (short*)ptr;

					// It's worth factoring out the multiplication step here.
					// Volume changed by less than 1% ? Then we do no adjustment
					if ((Math.Abs(1 - v) * 100) < 1)
						for (int i = 0; i < samples; i++)
							accumulatorBuffer[i] += shortBuffer[i];
					else
						for (int i = 0; i < samples; i++)
							accumulatorBuffer[i] += shortBuffer[i] * v;
				}

				OnMixingDone(bytesToMix);
			}

			public abstract void Cancel();
		}
	}
}
