using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BooBot.Preconditions;
using Discord;
using Discord.Commands;

namespace BooBot
{
	public partial class SoundBoardCommand : ModuleBase<SocketCommandContext>
	{
		ReactionMenuService _reactionMenuService;

		public SoundBoardCommand(ReactionMenuService reactionMenuService) => _reactionMenuService = reactionMenuService;

		[Command("sb"), RequireUserInVoice]
		public async Task CreateSoundBoard()
		{
			var menu = new SoundBoardMenu();
			await _reactionMenuService.StartMenu(Context.Channel, menu);
		}
	}
}
