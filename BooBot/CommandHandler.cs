using System.Threading.Tasks;
using Discord.WebSocket;
using Discord.Commands;
using System.Reflection;
using System;
using LogProvider;

namespace BooBot
{
	public class CommandHandler
	{
		private DiscordSocketClient _client;
		private CommandService _commandService;
		private ServiceDependencyMap _dependencyMap;
		private Logger _logger = Logger.Create("CommandHandler").AddOutput(new ConsoleOutput());
		private string _prefix;
		
		public CommandService CommandService => _commandService;


		public CommandHandler(DiscordSocketClient client, string prefix, ServiceDependencyMap dependencyMap)
		{
			_client = client;
			_prefix = prefix;
			_dependencyMap = dependencyMap;

			_commandService = new CommandService(new CommandServiceConfig
			{
				DefaultRunMode = RunMode.Async,
				CaseSensitiveCommands = false,
			});			
		}


		public void StartListening() => _client.MessageReceived += Handler;
		
		public async Task AddAllCommands() => await _commandService.AddModulesAsync(Assembly.GetEntryAssembly());

		async Task Handler(SocketMessage arg)
		{
			var msg = arg as SocketUserMessage;

			if (msg == null || msg.Author.IsBot)
				return;
			
			int argPos = 0;
			if (!msg.HasStringPrefix(_prefix, ref argPos))
				return;

			var context = new SocketCommandContext(_client, msg);
			var result = await _commandService.ExecuteAsync(context, argPos, _dependencyMap, MultiMatchHandling.Best);
			
			_logger.Info($"Command ran by {context.User} in {context.Channel.Name} - {context.Message.Content}");

			string response = null;

			switch (result)
			{
			case SearchResult searchResult:
				// "Commnd not found"
				break;

			case ParseResult parseResult:
				response = $":warning: There was an error parsing your command: `{parseResult.ErrorReason}`";
				break;

			case PreconditionResult preconditionResult:
				response = $":warning: A precondition of your command failed: `{preconditionResult.ErrorReason}`";

				//switch (preconditionResult)
				//{
				//case RateLimitResult rateLimitResponse:

				//	break;
				//}

				break;
			case ExecuteResult executeResult when !executeResult.IsSuccess:
				response = $":warning: Sorry I screwed up somewhere (Report that please)";
				_logger.Error(executeResult.Exception.ToString());
				break;
			}

			if (response != null)
				await context.ReplyAsync(response);
		}
	}
}
