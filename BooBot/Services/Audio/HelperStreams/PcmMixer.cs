using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LogProvider;

namespace BooBot.Services.Audio.HelperStreams
{
	// Takes an egress stream that it writes to.
	// Can take multiple source streams that it will read from.
	// The mixer will take audio frames from all source streams, mix them together, and write to them
	class PcmMixer
	{
		Logger log = Logger.Create("PcmAudioMixer").AddOutput(new ConsoleOutput());

		// Where we write out mixed output to
		readonly Stream egressStream;
		readonly List<AudioSource> sources = new List<AudioSource>();

		readonly int bufferSize;
		readonly float[] accumulatorBuffer;
		readonly byte[] finalMixBuffer; // lol kingdom hearts

		Task mixerTask;
		int isMixerTaskActive;

		public float Volume { get; set; } = 1f;


		public PcmMixer(Stream outputStream, int bufferSize = 1920 * 50)
		{
			this.egressStream = outputStream;
			this.bufferSize = bufferSize;
			this.accumulatorBuffer = new float[bufferSize / 2]; // 2 bytes => 1 sample
			this.finalMixBuffer = new byte[bufferSize];
		}


		/// <summary>
		/// Adds a source to the mixer.
		/// Pay close attention to the <paramref name="streamMayBlock"/> parameter if you read from a network source instead of a local file.
		/// If ingress streams block Reads the whole mixer will become stuck.
		/// Set <paramref name="streamMayBlock"/> to true to prevent this by paying a small performance overhead.
		/// </summary>
		/// <param name="sourcePcmStream">the stream to read audio from</param>
		/// <param name="streamMayBlock">set this to true if you think it's possible that this source could block (provide audio data in less than realtime)</param>
		public void AddSource(Stream sourcePcmStream, bool streamMayBlock)
		{
			if (!sourcePcmStream.CanRead)
				throw new InvalidOperationException("Source stream must be readable");

			if (streamMayBlock)
				throw new NotSupportedException("Not yet available, report this please to make sure we didn't just forget about it");

			lock (sources)
				sources.Add(new DirectSource(sourcePcmStream, bufferSize));

			if (Interlocked.CompareExchange(ref isMixerTaskActive, 1, 0) == 0)
				mixerTask = Task.Run(() => MixerTask());
		}


		void MixerTask()
		{
			List<AudioSource> activeSources = new List<AudioSource>();

			while (true)
			{
				// Get a view we can work with in a thread-safe manner
				int availableBytes = UpdateActiveSources(activeSources);

				if (activeSources.Count == 0)
					// todo, volt: a race-condition is possible here.
					// While we're checking our copy-list, a new instance could have been added just now.
					// The thread that just added a source saw us still active, but we're in the
					// process of exiting. How could we prevent this?
					break;

				
				// Gives sources a chance to lock their internal buffers
				for (int s = 0; s < activeSources.Count; s++)
					activeSources[s].EnterDrainMode();

				// Merge all pcm data we have so far
				MixIntoEgressBuffer(activeSources, availableBytes);

				// Let all sources know how much data we have consumed
				// And give them a chance to unlock their buffers, start their tasks, ...
				for (int s = 0; s < activeSources.Count; s++)
					activeSources[s].ExitDrainMode(availableBytes);

				// Write our final data to egress.
				egressStream.Write(finalMixBuffer, 0, availableBytes);
			}

			Interlocked.CompareExchange(ref isMixerTaskActive, 0, 1);
		}

		void MixIntoEgressBuffer(List<AudioSource> sources, int bytesToMix)
		{
			// It is important that this function does not write to
			// egress directly because it will block most likely.
			// This time can be used to read/process source data already which
			// would be impossible if we don't unlock the source buffers.

			int samples = bytesToMix / 2;

			//
			// 1.) Reset the accumulator buffer
			//
			for (int i = 0; i < samples; i++)
				accumulatorBuffer[i] = 0;

			unsafe
			{
				//
				// 2.) Accumulate data from all channels
				//
				for (int c = 0; c < sources.Count; c++)
				{
					var source = sources[c];
					fixed (byte* ptr = source.buffer)
					{
						short* shortBuffer = (short*)ptr;

						for (int i = 0; i < samples; i++)
							accumulatorBuffer[i] += shortBuffer[i];
					}
				}

				//
				// 3.) Adjust volume and clip, writing to finalMix
				//
				fixed (byte* finalMixPtr = finalMixBuffer)
				{
					float v = Volume;
					short* finalMixShort = (short*)finalMixPtr;
					for (int i = 0; i < samples; i++)
					{
						var f = accumulatorBuffer[i] * v;

						if (f < short.MinValue)
							f = short.MinValue;
						else if (f > short.MaxValue)
							f = short.MaxValue;

						finalMixShort[i] = (short)f;
					}
				}
			}
		}

		int UpdateActiveSources(List<AudioSource> activeSources)
		{
			activeSources.Clear();
			int commonData = int.MaxValue;

			lock (sources)
			{
				for (int i = 0; i < sources.Count; i++)
				{
					var source = sources[i];

					source.FillBuffer();

					if (source.IsDone)
					{
						sources.RemoveAt(i--);
						continue;
					}

					var available = source.Available;
					if (available == 0)
						continue;

					if ((available % 4) != 0)
						continue; // We only operate on full sample-pairs (2 samples (one per channel), each 2 bytes)

					commonData = Math.Min(commonData, available);
					activeSources.Add(source);
				}
			}

			// Just to make sure we got at least one source with available data
			if (commonData == int.MaxValue)
				return 0;

			return commonData;
		}

		// todo, volt: should this be nested? maybe not anymore because there are multiple implementations now
		// A source from where we get delicious audio such as "JohnCena.mp3", "MLG_Airhorn.wav" and "Allahu Akbar.ogg" 
		private abstract unsafe class AudioSource
		{
			protected readonly Stream sourceStream;

			// todo, volt: we want to avoid both:
			// 1.) exposing an internal buffer like this because its just terrible design
			// 2.) having a Read(...) method, which would essentially force us to provide a buffer, which would cause yet another data-copy
			// Number 2 is definitely not an option since we're dealing with real-time data.
			internal readonly byte[] buffer;
			protected int bytesInBuffer;

			public int Available => bytesInBuffer;
			public abstract bool IsDone { get; }
			

			protected AudioSource(Stream sourceStream, int bufferSize)
			{
				this.sourceStream = sourceStream;
				buffer = new byte[bufferSize];
			}

			// Tell this source to read form its internal source
			public abstract void FillBuffer();

			public abstract void EnterDrainMode();
			public abstract void ExitDrainMode(int bytesDrained);
		}

		// Direct sources have literally zero overhead. We would need a byte array anyway at some point.
		sealed class DirectSource : AudioSource
		{
			bool isDone;
			public override bool IsDone => isDone;

			public DirectSource(Stream sourceStream, int bufferSize) : base(sourceStream, bufferSize)
			{
			}

			// Direct sources (from a file) just do static reads, they are fast enough to never cause us any trouble.
			public override void FillBuffer()
			{
				int spaceLeft = buffer.Length - bytesInBuffer;
				int read = sourceStream.Read(buffer, bytesInBuffer, spaceLeft);

				if (spaceLeft > 0 && read == 0)
					isDone = true;

				bytesInBuffer += read;
			}

			public override void EnterDrainMode()
			{
				// There's nothing for us to do.
				// Nobody will contend the buffer in direct mode.
			}

			public override void ExitDrainMode(int bytesDrained)
			{
				Buffer.BlockCopy(buffer, bytesDrained, buffer, 0, buffer.Length - bytesDrained);
				bytesInBuffer -= bytesDrained;
			}
		}

		// Uses a task to keep filling its buffer
		sealed class AutoFillingSource : AudioSource
		{
			public override bool IsDone => throw new NotImplementedException();

			public AutoFillingSource(Stream sourceStream, int bufferSize) : base(sourceStream, bufferSize)
			{
				Task.Run(FillingTask);
				throw new NotImplementedException();
			}

			async Task FillingTask()
			{
				//while()
			}

			public override void EnterDrainMode()
			{
				throw new NotImplementedException();
			}

			public override void ExitDrainMode(int bytesDrained)
			{
				throw new NotImplementedException();
			}

			public override void FillBuffer()
			{
				throw new NotImplementedException();
			}
		}
	}
}
