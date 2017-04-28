using System.Linq;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace BooBot
{
	[Service]
	public class HelpService
	{
		static readonly Regex _removeRedundantSpaces = new Regex(@"(?: {2,}|(?:[\r\n\t\f\v])+)", RegexOptions.Compiled);
		static readonly Embed _noMatchFoundEmbed = new EmbedBuilder().WithColor(new Color(186, 44, 44)).WithDescription("No command or command group matched your search");
		
		public Embed HelpLookup(string query, CommandService commandService)
		{
			var searchString = _removeRedundantSpaces.Replace(query, " ");

			return formatModule(findModule()) ?? formatCommand(findCommand()) ?? _noMatchFoundEmbed;

			ModuleInfo findModule() => commandService.Modules.FirstOrDefault(m => m.Aliases.First() == searchString);
			CommandInfo findCommand() => commandService.Commands.FirstOrDefault(c => c.Aliases.First() == searchString);

			Embed formatModule(ModuleInfo module)
			{
				if (module == null)
					return null;

				var embedBuilder = new EmbedBuilder();

				embedBuilder.WithTitle($"Command Group: {module.Name}");

				var description = new StringBuilder();

				if (module.Summary != null)
					description.AppendLine(module.Summary);
				if (module.Remarks != null)
					description.AppendLine(module.Remarks);

				if (description.Length > 0)
					embedBuilder.WithDescription(description.ToString());

				foreach (var c in module.Commands)
				{
					var cmd = new EmbedFieldBuilder()
							.WithIsInline(false)
							.WithName(c.Name);

					if (c.Summary != null)
						cmd.WithValue(c.Summary);
					else
						cmd.WithValue("\x200b");

					embedBuilder.AddField(cmd);
				}

				return embedBuilder.Build();
			}

			Embed formatCommand(CommandInfo command)
			{
				if (command == null)
					return null;

				var embedBuilder = new EmbedBuilder();

				embedBuilder.WithTitle($"Command: {command.Name}");

				var description = new StringBuilder();

				if (command.Summary != null)
					description.AppendLine(command.Summary);
				if (command.Remarks != null)
					description.AppendLine(command.Remarks);

				if (description.Length > 0)
					description.AppendLine();

				description.AppendLine(formatSyntax(command));

				if (description.Length > 0)
					embedBuilder.WithDescription(description.ToString());

				foreach (var p in command.Parameters)
				{
					var fieldBuilder = new EmbedFieldBuilder()
						.WithName($"{p.Name} ({p.Type.Name})");

					var fieldValue = new StringBuilder();
					if (p.Summary != null)
						fieldValue.AppendLine(p.Summary);

					fieldValue.Append(p.IsOptional ? "Optional" : "Required");

					if (p.IsMultiple)
						fieldValue.Append(", Multiple");

					fieldBuilder.WithValue(fieldValue.ToString());
				}

				return embedBuilder.Build();
			}

			string formatSyntax(CommandInfo cmd) => $"{cmd.Aliases.First()} {string.Join(" ", cmd.Parameters.Select(p => formatParameter(p)))}";

			string formatParameter(ParameterInfo param)
			{
				var formattedParam = param.Name;

				if (param.IsOptional)
					formattedParam = $"[{formattedParam}]";
				else
					formattedParam = $"<{formattedParam}>";

				if (param.IsMultiple)
					formattedParam = $"{formattedParam}...";

				return formattedParam;
			}
		}
	}
}
