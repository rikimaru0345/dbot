using System;
using System.Threading.Tasks;

namespace BooBot.Services.Audio.HelperStreams
{
	public interface IMixerSource
	{
		/// <summary>
		/// Gets or sets the volume for this source
		/// </summary>
		float Volume { get; set; }

		/// <summary>
		/// Same as <seealso cref="TimePlayed"/> but in samples instead of time.
		/// </summary>
		int SamplesPlayed { get; }

		/// <summary>
		/// Returns the processed data as time (if you want to display the "current" time of the sound, this is what you want)
		/// </summary>
		TimeSpan TimePlayed { get; }

		/// <summary>
		/// Returns a task that completes when the source was processed completely
		/// </summary>
		Task WaitForFinish();

		/// <summary>
		/// Cancels the processing of a source immediately
		/// </summary>
		void Stop();
	}

	partial class PcmMixer
	{
		private struct MixerSource : IMixerSource
		{
			readonly AudioSource source;

			public int SamplesPlayed => (source.TotalBytesProcessed / 2) / Channels;
			public TimeSpan TimePlayed => TimeSpan.FromSeconds((double)SamplesPlayed / SamplesPerSecond);

			public float Volume { get => throw new NotImplementedException("todo"); set => throw new NotImplementedException("todo"); }

			public Task WaitForFinish() => source.SourceState.Task; // todo, volt: is this correct? how would we best expose awaiting the completion of a mixer source??
			public void Stop() => source.Cancel();
			

			public MixerSource(AudioSource source) => this.source = source;
		}
	}
}
