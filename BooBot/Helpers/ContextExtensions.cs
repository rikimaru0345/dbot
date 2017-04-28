using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace BooBot
{
	public static class ContextExtensions
	{
		public static async Task ReplyAsync(this ICommandContext context, string message, bool mention = true)
			=> await context.Channel.SendMessageAsync($"{(mention ? context.User.Mention + ", " : "")}{message}");

		public static async Task ReplyAsync(this ICommandContext context, Embed embed)
			=> await context.Channel.SendMessageAsync(String.Empty, embed: embed);
	}

}
