using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using Discord.Commands;
using LogProvider;
using System.Linq;
using BooBot.Services.Audio.HelperStreams;

namespace BooBot
{
	public partial class SoundBoardCommand : ModuleBase<SocketCommandContext>
	{
		class SoundBoardClient : IDisposable
		{
			static Logger log = Logger.Create("SoundBoardClient").AddOutput(new ConsoleOutput());

			IVoiceChannel channel;
			IAudioClient audioClient;

			AudioOutStream outStream; // final output
			PcmMixer mixer;

			int currentlyPlaying;
			public int CurrentlyPlayingCount => currentlyPlaying;

			public async Task Start(IVoiceChannel channel)
			{
				audioClient = await channel.ConnectAsync().ConfigureAwait(false);
				audioClient.Disconnected += e =>
				{
					Console.WriteLine("Audio Exception: " + e);
					Dispose();
					return Task.CompletedTask;
				};

				outStream = audioClient.CreatePCMStream(AudioApplication.Mixed, bitrate: 48 * 1024, bufferMillis: 1000);

				mixer = new PcmMixer(outStream);
			}

			Random rng = new Random();

			public async Task PlaySound(string fileName)
			{
				Interlocked.Increment(ref currentlyPlaying);

				var soundStream = Debug_TranscodeFileToPcm(fileName);

				try
				{
					var source = mixer.AddSource(soundStream, readAsync: true);
					await source.WaitForFinish();
				}
				catch (Exception ex)
				{
					log.Error("Exception while playing audio " + Path.GetFileName(fileName) + ": " + ex);
				}

				Interlocked.Decrement(ref currentlyPlaying);
				Console.WriteLine($"Audio Done: {fileName}");
			}

			Stream Debug_TranscodeFileToPcm(string fileName)
			{
				var p = Process.Start(new ProcessStartInfo
				{
					FileName = "ffmpeg",
					Arguments = "-i pipe:0 -f s16le -ar 48000 -ac 2 pipe:1",
					UseShellExecute = false,
					//RedirectStandardError = relayErrors,
					RedirectStandardOutput = true,
					RedirectStandardInput = true,
					CreateNoWindow = true
				});

				Task.Run(() =>
				{
					try
					{
						// 2byte * 2channels => 4byte per sample * 48000 samples per second => 192KB/s / 1000 => 1920bytes per millisecond
						using (var fileData = new FileStream(fileName, FileMode.Open, FileAccess.Read))
							fileData.CopyTo(p.StandardInput.BaseStream, 1920 * 10);
					}
					catch (Exception ex)
					{
						log.Error("Exception while writing data from file to ffmpeg: " + ex);
						throw;
					}
					finally
					{
						p.Kill();
					}
				});

				return p.StandardOutput.BaseStream;
			}

			public void Dispose()
			{
				audioClient?.Dispose();
				outStream?.Dispose();
			}
		}

	}
}
