using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Addons.EmojiTools;
using Discord.Commands;
using Discord.WebSocket;

namespace BooBot
{
	public partial class SoundBoardCommand : ModuleBase<SocketCommandContext>
	{
		class SoundBoardMenu : IReactionMenu
		{
			static readonly Random rng = new Random();
			IUserMessage message;
			ReactionMenuService service;

			SoundBoardClient soundClient;

			static readonly string emoteClose = UnicodeEmoji.FromText(":x:");
			static readonly string emoteRandom = UnicodeEmoji.FromText(":game_die:");

			// Todo: convert into an array of types that contain emoji + soundfile
			static (string shorthand, string soundFile)[] sounds =
				{
					//(":rotating_light:", "airhorn.wav"),
					//":nine:",
					//":rocket:",
					//":alarm_clock:",
				};

			// Todo: join audio channel here as well
			public async Task<IMessage> SetupInitialMessage(ReactionMenuService service, IMessageChannel channel)
			{
				this.service = service;
				message = await channel.SendMessageAsync("__**SoundBoard**__").ConfigureAwait(false);

				// Random emoji
				message.AddReactionAsync(emoteRandom);
				// All sounds
				await Task.WhenAll(sounds.Select(r => message.AddReactionAsync(UnicodeEmoji.FromText(r.shorthand)))).ConfigureAwait(false);
				// Plus the close emoji
				message.AddReactionAsync(emoteClose);

				return message;
			}

			public async Task OnReaction(ReactionChangeType changeType, SocketReaction reaction)
			{
				if (reaction.User.IsSpecified && reaction.User.Value.IsBot)
					return;

				switch (changeType)
				{
				case ReactionChangeType.Cleared:
				case ReactionChangeType.Added when reaction.Emoji.Name == emoteClose:
					soundClient?.Dispose();
					await message.DeleteAsync().ConfigureAwait(false);
					return;

				case ReactionChangeType.Added:
					message.RemoveReactionAsync(reaction.Emoji, reaction.User.Value);
					await message.ModifyAsync(p => p.Content = $"Playing: {reaction.Emoji.Name} *at* volume factor {int.MaxValue}.0x");

					if (soundClient == null)
					{
						soundClient = new SoundBoardClient();
						await soundClient.Start(((IGuildUser)reaction.User.Value)?.VoiceChannel).ConfigureAwait(false);
					}

					var fileName = EmojiToSoundFile(reaction.Emoji);
					if (fileName != null)
						soundClient.PlaySound(fileName);

					fileName = EmojiToSoundFile(reaction.Emoji);
					if (fileName != null)
						soundClient.PlaySound(fileName);

					break;
				}
			}

			static string EmojiToSoundFile(Emoji emoji)
			{
				const string soundsFolder = "sounds";

				string fileName = null;
				if (emoji.Name == emoteRandom)
				{
					var files = Directory.GetFiles(soundsFolder, "*.*");
					fileName = files[rng.Next(files.Length)];
				}
				else
				{
					var reactionShorthand = UnicodeEmoji.GetShorthand(emoji.Name);
					var sound = sounds.FirstOrDefault(s => s.shorthand == reactionShorthand);
					if (sound.soundFile != null)
						fileName = Path.Combine(soundsFolder, sound.soundFile);
				}

				return fileName;
			}
		}

	}
}
