using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using Discord.Commands;
using LogProvider;

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
			VolumeControlPcmStream volumeControlStream; // ^ adjusts volume (and writes to out)
			MixStream mixStream; // ^ merges multiple channels into one (and writes to volume)

			int nextFreeChannel = 0; // todo: this is just for debugging: completely rework this, error out when there're no free channels

			public async Task Start(IVoiceChannel channel)
			{
				audioClient = await channel.ConnectAsync().ConfigureAwait(false);
				audioClient.Disconnected += e =>
				{
					Console.WriteLine("Audio Exception: " + e);
					Dispose();
					return Task.CompletedTask;
				};

				outStream = audioClient.CreatePCMStream(AudioApplication.Mixed, bufferMillis: 1000);

				volumeControlStream = new VolumeControlPcmStream(outStream);
				volumeControlStream.Volume = 1;

				mixStream = new MixStream(volumeControlStream, 5, 1920 * 10); // 10ms
			}

			Random rng = new Random();

			public async Task PlaySound(string fileName)
			{
				var c = (nextFreeChannel++) % mixStream.ChannelCount;
				var channelForThisSound = mixStream[c];

				var soundStream = Debug_TranscodeFileToPcm(fileName);

				await soundStream.CopyToAsync(channelForThisSound).ConfigureAwait(false);
				await outStream.FlushAsync().ConfigureAwait(false);
				Console.WriteLine($"Audio Done: {fileName} (mixChannel #{c + 1})");
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

				Task.Run(async () =>
				{
					// 2byte * 2channels => 4byte per sample * 48000 samples per second => 192KB/s / 1000 => 1920bytes per millisecond
					using (var fileData = new FileStream(fileName, FileMode.Open, FileAccess.Read))
						await fileData.CopyToAsync(p.StandardInput.BaseStream, 1920 * 10);
					p.Kill();
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
