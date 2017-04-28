using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace BooBot
{
	[Service]
	public class ReactionMenuService
	{
		readonly Dictionary<ulong, IReactionMenu> messageIdToMenus = new Dictionary<ulong, IReactionMenu>();

		public ReactionMenuService(DependencyMap di)
		{
			var client = di.Get<DiscordSocketClient>();
			client.ReactionAdded += OnReactionAdded;
			client.ReactionRemoved += OnReactionRemoved;
			client.ReactionsCleared += OnReactionsCleared;
		}

		Task OnReactionAdded(Cacheable<IUserMessage, ulong> message, ISocketMessageChannel channel, SocketReaction reaction)
		{
			GetOrDefault(message.Id)?.OnReaction(ReactionChangeType.Added, reaction);
			return Task.CompletedTask;
		}

		Task OnReactionRemoved(Cacheable<IUserMessage, ulong> message, ISocketMessageChannel channel, SocketReaction reaction)
		{
			GetOrDefault(message.Id)?.OnReaction(ReactionChangeType.Removed, reaction);
			return Task.CompletedTask;
		}

		Task OnReactionsCleared(Cacheable<IUserMessage, ulong> message, ISocketMessageChannel channel)
		{
			GetOrDefault(message.Id)?.OnReaction(ReactionChangeType.Cleared, null);
			return Task.CompletedTask;
		}

		IReactionMenu GetOrDefault(ulong messageId)
		{
			IReactionMenu menu;
			if (messageIdToMenus.TryGetValue(messageId, out menu))
				return menu;
			return null;
		}

		public async Task StartMenu(IMessageChannel channel, IReactionMenu reactionMenu)
		{
			var msg = await reactionMenu.SetupInitialMessage(this, channel);

			messageIdToMenus.Add(msg.Id, reactionMenu);
		}
	}

	// To not bloat the interface unnecessarily we merge Added+Removed+Cleared into one method.
	public enum ReactionChangeType
	{
		Added,
		Removed,
		Cleared
	}

	// A reaction menu should have a way to tell the ReactionMenuService that its done now
	// todo:
	// 1.) Maybe giving the service just the type + argument values and letting it instantiate the menu would be good here?
	// It would be in line with how depenency injection stuff happens all around in dnet.
	// 2.) Or would it be better to only let the menu communicate with the menuservice through return values in OnReaction?
	// 3.) Maybe the reaction can delete its own message that it created and the service would pick up on it? But that would be trusting that we stay connected, sorta indirect..,
	public interface IReactionMenu
	{
		Task<IMessage> SetupInitialMessage(ReactionMenuService service, IMessageChannel channel);

		Task OnReaction(ReactionChangeType changeType, SocketReaction reaction);
	}
}
