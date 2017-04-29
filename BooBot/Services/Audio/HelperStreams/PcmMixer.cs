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
	// !! Warning: This class is only intended for the common use case:
	// !!          - high-performance
	// !!          - easy to use audio signal mixing for discord
	// !!          This class assumes PCM 48KHz Stereo throughout for performance reasons, which
	// !!          should be perfectly fine most of the time, if you need something else you
	// !!          will know anyway (and use a dedicated library anyway!)
	partial class PcmMixer
	{
		private const int SamplesPerSecond = 48 * 1000; // 48KHz - 48000 samples per second (for one channel)
		private const int Channels = 2; // Stereo
		private const int BytesPerSecond = (SamplesPerSecond * 2) * Channels; // One sample consists of 2 bytes.
		private const int BytesPerMs = BytesPerSecond / 1000;

		Logger log = Logger.Create("PcmAudioMixer").AddOutput(new ConsoleOutput());

		// Where we write out mixed output to
		readonly Stream egressStream;
		readonly List<AudioSource> sources = new List<AudioSource>();

		readonly int bufferSize;
		readonly float[] accumulatorBuffer;
		readonly byte[] finalMixBuffer; // kingdom hearts hehe

		Task mixerTask;

		public float Volume { get; set; } = 1f;


		public PcmMixer(Stream outputStream, int bufferSize = BytesPerMs * 1000)
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
		public IMixerSource AddSource(Stream sourcePcmStream, bool readAsync)
		{
			if (!sourcePcmStream.CanRead)
				throw new InvalidOperationException("Source stream must be readable");

			if (readAsync)
				throw new NotImplementedException("not yet done and tested... use blocking reads for now");

			AudioSource audioSource = readAsync ?
				(AudioSource)new AutoFillingSource(sourcePcmStream, bufferSize) :
				(AudioSource)new DirectSource(sourcePcmStream, bufferSize);

			lock (sources)
			{
				sources.Add(audioSource);

				if (mixerTask == null || mixerTask.IsCompleted)
					mixerTask = Task.Run(() => MixProcessingTask());
			}

			return new MixerSource(audioSource);
		}



		void MixProcessingTask()
		{
			List<AudioSource> activeSources = new List<AudioSource>();

			while (true)
			{
				// Get a view we can work with in a thread-safe manner
				(int availableBytes, bool stop) = UpdateActiveSources(activeSources);

				if (stop)
					// todo: a race-condition is possible here.
					// While we're checking our copy-list, a new instance could have been added just now.
					// The thread that just added a source saw us still active, but we're in the
					// process of exiting.
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

		(int bytesToMix, bool stopProcessing) UpdateActiveSources(List<AudioSource> activeSources)
		{
			activeSources.Clear();
			int commonData = int.MaxValue;
			bool viableSourcesRemaining = false;

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

					viableSourcesRemaining = true;

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
				return (0, !viableSourcesRemaining);

			return (commonData, viableSourcesRemaining);
		}

		// todo, volt: should this be nested? maybe not anymore because there are multiple implementations now
		// A source from where we get delicious audio such as "JohnCena.mp3", "MLG_Airhorn.wav" and "Allahu Akbar.ogg" 
		private abstract unsafe class AudioSource
		{
			public TaskCompletionSource<int> SourceState { get; } = new TaskCompletionSource<int>();

			protected readonly Stream sourceStream;

			// todo, volt: we want to avoid both:
			// 1.) exposing an internal buffer like this because its just terrible design
			// 2.) having a Read(...) method, which would essentially force us to provide a buffer, which would cause yet another data-copy
			// Number 2 is definitely not an option since we're dealing with real-time data.
			internal readonly byte[] buffer;
			protected int bytesInBuffer;

			public int Available => bytesInBuffer;
			public MixerSource AsMixerSource => new MixerSource(this);
			public int TotalBytesProcessed { get; protected set; }

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

			public abstract void Cancel();
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
				if (isDone)
					return;

				int spaceLeft = buffer.Length - bytesInBuffer;

				if (spaceLeft == 0)
					// buffer is full at the moment
					return;

				int read;
				try
				{
					read = sourceStream.Read(buffer, bytesInBuffer, spaceLeft);
				}
				catch (Exception ex)
				{
					SourceState.SetException(ex);
					isDone = true;
					sourceStream.Dispose();
					return;
				}

				if (read == 0)
				{
					// We've reached the end
					isDone = true;
					SourceState.SetResult(0);
					sourceStream.Dispose();
				}

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
				TotalBytesProcessed += bytesDrained;
			}

			public override void Cancel()
			{
				isDone = true;
				sourceStream.Dispose();
				SourceState.SetResult(0);
			}
		}

		// Uses a task to keep filling its buffer
		sealed class AutoFillingSource : AudioSource
		{
			bool isDone;
			public override bool IsDone => isDone;

			volatile int isReading = 0;
			volatile int isDraining = 0;

			// When we're dealing with multiple threads we have to do some double buffering (at least to keep it simple)
			byte[] localReadBuffer;

			public AutoFillingSource(Stream sourceStream, int bufferSize) : base(sourceStream, bufferSize)
			{
				localReadBuffer = new byte[bufferSize];
			}

			// Direct sources (from a file) just do static reads, they are fast enough to never cause us any trouble.
			public override void FillBuffer()
			{
				if (isDone)
					// Nothing left to read
					return;

				if (buffer.Length - bytesInBuffer == 0)
					// No space to read at the moment
					// This check alone is not enough, we have to check again inside the task to prevent race-conditions
					return;

				if (Interlocked.CompareExchange(ref isReading, 1, 0) == 1)
					// Last read is still pending
					return;

				// Using async/await for reading from the stream would massively complicate things (if done correctly)
				// todo: eventually we want to have one task reading from the source asynchronously with some good doublebuffering
				// that could improve performance a bit, not sure if its needed anyway though
				Task.Run((Action)ReadTask);
			}

			void ReadTask()
			{
				int spaceLeft = buffer.Length - bytesInBuffer;

				if (spaceLeft == 0)
				{
					Interlocked.Exchange(ref isReading, 0);
					return;
				}

				int read;
				try
				{
					read = sourceStream.Read(localReadBuffer, 0, spaceLeft);
				}
				catch (Exception ex)
				{
					SourceState.SetException(ex);
					isDone = true;
					// no need to "release" isReading here
					return;
				}

				if (spaceLeft > 0 && read == 0)
				{
					isDone = true;
					sourceStream.Dispose();
					SourceState.SetResult(0);
				}

				bytesInBuffer += read;

				Interlocked.Exchange(ref isReading, 0);
			}

			public override void EnterDrainMode()
			{
				// In "auto fill mode" the buffer will get used by the filling task as well as the mixer.
				// We have to synchronize access.
				// A simple lock() will be perfectly fine in this situation 
				Monitor.Enter(buffer);
			}

			public override void ExitDrainMode(int bytesDrained)
			{
				Buffer.BlockCopy(buffer, bytesDrained, buffer, 0, buffer.Length - bytesDrained);
				bytesInBuffer -= bytesDrained;
				TotalBytesProcessed += bytesDrained;
			}

			public override void Cancel()
			{
				isDone = true;
				sourceStream.Dispose();
				SourceState.SetResult(0);
			}
		}
	}
}
