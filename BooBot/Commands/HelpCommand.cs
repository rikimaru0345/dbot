using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using System;
using System.Collections.Generic;
using BooBot.Preconditions;

namespace BooBot
{
	public class HelpCommand : ModuleBase<SocketCommandContext>
	{
		readonly HelpService _helpService;
		readonly CommandService _commandService;

		public HelpCommand(HelpService helpService, CommandService commandService)
		{
			_helpService = helpService;
			_commandService = commandService;
		}

		[Command("help"), Priority(2), RateLimit(userSeconds: 1)]
		public async Task Help([Remainder]string Query)
		{
			var embed = _helpService.HelpLookup(Query, _commandService);
			await ReplyAsync("", embed: embed);
		}
	}
}
