using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace BooBot.Preconditions
{
	class LastMessageMustContainPreconditionAttribute : PreconditionAttribute
	{
		string neededText;
		int backlog;

		public LastMessageMustContainPreconditionAttribute(string text, int messageBacklog = 20)
		{
			neededText = text;
			backlog = messageBacklog;
		}

		public override async Task<PreconditionResult> CheckPermissions(ICommandContext context, CommandInfo command, IDependencyMap map)
		{
			var messages = context.Channel.GetMessagesAsync(backlog, CacheMode.AllowDownload);
			foreach (var m in await messages.Flatten())
				if (m.Content.IndexOf(neededText, StringComparison.OrdinalIgnoreCase) != -1)
					return PreconditionResult.FromSuccess();

			return PreconditionResult.FromError($"The phrase '{neededText}' must appear anywhere in the last {backlog} messages you sent to this channel.");

		}
	}
}
