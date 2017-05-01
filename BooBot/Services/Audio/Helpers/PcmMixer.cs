using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LogProvider;

namespace BooBot.Services.Audio.HelperStreams
{
	/// <summary>
	/// Takes an egress stream that it writes to.
	/// Can take multiple source streams that it will read from.
	/// The mixer will take audio frames from all source streams, mix them together, and write to them
	/// </summary>
	/// <remarks>
	/// Warning:
	/// This class is only intended for the common use case:
	/// - high-performance
	/// - easy to use audio signal mixing for discord
	/// This class assumes PCM 48KHz Stereo throughout for performance reasons, which
	/// should be perfectly fine most of the time, if you need something else you
	/// will know anyway (and use a dedicated library anyway!)
	/// </remarks>
	unsafe partial class PcmMixer : IDisposable
	{
		const int SamplesPerSecond = 48 * 1000; // 48KHz - 48000 samples per second (for one channel)
		const int Channels = 2; // Stereo
		const int BytesPerSecond = (SamplesPerSecond * 2) * Channels; // One sample consists of 2 bytes.
		const int BytesPerMs = BytesPerSecond / 1000;

		Logger log = Logger.Create("PcmAudioMixer").AddOutput(new ConsoleOutput());

		// Where we write out mixed output to
		readonly Stream _egressStream;
		readonly List<AudioSource> _sources = new List<AudioSource>();

		readonly int _bufferSize;

		readonly float[] _accumulatorBuffer;
		readonly GCHandle _accumulatorHandle;
		readonly float* _accumulatorPtr;

		readonly byte[] _finalMixBuffer; // kingdom hearts hehe

		bool _sourcesStale;
		CancellationTokenSource _cancelToken;
		Task _mixerTask;


		public float Volume { get; set; } = 1f;


		public PcmMixer(Stream outputStream, int bufferSize = BytesPerMs * 1000)
		{
			_egressStream = outputStream;
			_bufferSize = bufferSize;

			_accumulatorBuffer = new float[bufferSize / 2];
			_accumulatorHandle = GCHandle.Alloc(_accumulatorBuffer, GCHandleType.Pinned);
			_accumulatorPtr = (float*)_accumulatorHandle.AddrOfPinnedObject();

			_finalMixBuffer = new byte[bufferSize];
		}


		/// <summary>
		/// Adds a source to the mixer.
		/// Pay close attention to the <paramref name="readAsync"/> parameter if you read from a network source instead of a local file.
		/// If ingress streams block Reads the whole mixer will become stuck.
		/// Set <paramref name="streamMayBlock"/> to true to prevent this by paying a small performance overhead.
		/// </summary>
		/// <param name="sourcePcmStream">the stream to read audio from</param>
		/// <param name="readAsync">set this to true if you think it's possible that this source could block (provide audio data in less than realtime)</param>
		public IMixerSource AddSource(Stream sourcePcmStream, bool readAsync)
		{
			if (!sourcePcmStream.CanRead)
				throw new InvalidOperationException("Source stream must be readable");

			//if (readAsync)
			//	throw new NotImplementedException("not yet done and tested... use blocking reads for now");

			AudioSource audioSource = readAsync ?
				(AudioSource)new AutoFillingSource(sourcePcmStream, _bufferSize) :
				(AudioSource)new DirectSource(sourcePcmStream, _bufferSize);

			lock (_sources)
			{
				bool needToStartMixer = _sources.Count == 0;
				_sources.Add(audioSource);

				if (needToStartMixer)
				{
					_cancelToken = new CancellationTokenSource();
					_mixerTask = Task.Run(() =>
					{
						try
						{
							MixProcessingTask();
						}
						catch (Exception ex)
						{
							log.Error("Exception in mixer: " + ex);
						}
					});
				}

				_sourcesStale = true;
			}

			return new MixerSource(audioSource);
		}

		bool RemoveSource(AudioSource source)
		{
			lock (_sources)
			{
				bool removed = _sources.Remove(source);

				if (_sources.Count == 0)
					_cancelToken.Cancel();

				_sourcesStale = true;
				return removed;
			}
		}


		void MixProcessingTask()
		{
			log.Debug("PcmMixerTask started");
			// local copy of all active sources
			// even the ones that are still active but are currently not providing any data
			// (network buffering, HDD spinning up, ffmpeg have a bad day, ...)
			List<AudioSource> localSources = new List<AudioSource>();
			List<AudioSource> workingSources = new List<AudioSource>(); // sources that have data available *right now*

			while (true)
			{
				// Get a view we can work with in a thread-safe manner
				if (_sourcesStale)
					CopySources(localSources);

				int bytesToMix = UpdateWorkingSources(localSources, workingSources);

				if (_cancelToken.IsCancellationRequested)
					break;

				if (bytesToMix == 0)
					// We are (or at least want to be) always on a dedicated thread, what else to do?
					// Spinning would be a lot worse than this.
					// How can we wait for source data to become available?
					// Should we even aim to remove this as it only happens in exceptional circumstances anyway?
					Thread.Sleep(10);

				// Merge all pcm data we have so far
				MixIntoEgressBuffer(workingSources, bytesToMix);

				// Write our final data to egress.
				_egressStream.Write(_finalMixBuffer, 0, bytesToMix);
			}

			log.Debug("PcmMixerTask stopped");
		}

		void MixIntoEgressBuffer(List<AudioSource> workingSources, int bytesToMix)
		{
			// It is important that this function does not write to
			// egress directly because it will block most likely.
			// This time can be used to read/process source data already which
			// would be impossible if we don't unlock the source buffers.

			int samples = bytesToMix / 2;

			//
			// 1.) Reset the accumulator buffer
			//
			Array.Clear(_accumulatorBuffer, 0, _accumulatorBuffer.Length);

			unsafe
			{
				//
				// 2.) Accumulate data from all channels
				//
				for (int c = 0; c < workingSources.Count; c++)
					workingSources[c].MixInto(_accumulatorPtr, bytesToMix);

				//
				// 3.) Adjust volume and clip, writing to finalMix
				//
				fixed (byte* finalMixPtr = _finalMixBuffer)
				{
					float v = Volume;
					short* finalMixShort = (short*)finalMixPtr;
					for (int i = 0; i < samples; i++)
					{
						var f = _accumulatorBuffer[i] * v;

						if (f < short.MinValue)
							f = short.MinValue;
						else if (f > short.MaxValue)
							f = short.MaxValue;

						finalMixShort[i] = (short)f;
					}
				}
			}
		}

		void CopySources(List<AudioSource> localSources)
		{
			localSources.Clear();

			lock (_sources)
			{
				for (int i = 0; i < _sources.Count; i++)
					localSources.Add(_sources[i]);
				_sourcesStale = false;
			}
		}

		// Determines what sources we want to process this frame, as well as how many bytes we should mix
		int UpdateWorkingSources(List<AudioSource> allSources, List<AudioSource> workingSources)
		{
			int bytesToMix = int.MaxValue;

			workingSources.Clear();
			for (int i = 0; i < allSources.Count; i++)
			{
				var s = allSources[i];

				if (!s.IsDone)
					s.FillBuffer();

				int a = s.Available;
				if (a > 0 && (a % 4) == 0) // We only use full stereo samples.
				{
					// Enough data available
					workingSources.Add(s);
					bytesToMix = Math.Min(bytesToMix, s.Available);
				}
				else if (a == 0 && s.IsDone)
				{
					// Source is done and fully drained
					if (RemoveSource(s))
						i--;
				}
			}

			if (bytesToMix == int.MaxValue)
				return 0;
			return bytesToMix;
		}



		void Dispose(bool disposing)
		{
			if (disposing)
			{
				bool lockTaken = false;
				Monitor.TryEnter(_sources, 100, ref lockTaken);
				// We'd like to release memory right now, but 
				// if we cannot take the lock right now for whatever reason
				// we'll just leave it to the GC + Finalizer :ok_hand:
				if (lockTaken)
				{
					for (int i = 0; i < _sources.Count; i++)
						_sources[i].Dispose();
					_sources.Clear();
				}
				Monitor.Exit(_sources);
			}

			_accumulatorHandle.Free();

			GC.SuppressFinalize(this);
		}

		public void Dispose() => Dispose(true);
		~PcmMixer() => Dispose(false);
	}
}
