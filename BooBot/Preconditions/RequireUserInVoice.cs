using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace BooBot.Preconditions
{
	class RequireUserInVoice : PreconditionAttribute
	{
		static readonly PreconditionResult errorResult = PreconditionResult.FromError("You must be in a voice channel I can join and speak in.");

		public override async Task<PreconditionResult> CheckPermissions(ICommandContext context, CommandInfo command, IDependencyMap map)
		{
			// The user must be in a voice channel
			// We must be able to join and speak there

			// Todo: whats the difference between  IUSer.GetDmChannelAsync and CreateDMChannelAsync ? both say they'd create it, why do they have different options?

			var self = await context.Guild.GetCurrentUserAsync();

			var voiceChannel = (context.User as IGuildUser)?.VoiceChannel;
			if (voiceChannel == null)
				return errorResult;

			var vcPerms = self.GetPermissions(voiceChannel);
			if (!vcPerms.Connect || !vcPerms.Speak)
				return errorResult;

			return PreconditionResult.FromSuccess();
		}
	}
}
