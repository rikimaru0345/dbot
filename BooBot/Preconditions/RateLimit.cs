using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;

namespace BooBot.Preconditions
{
	/// <summary>
	/// A ratelimit precondition in its simplest form.
	/// It does not offer exceptions for specific users or roles.
	/// There's no way to specify how often we should respond with a warning that the ratelimit is in effect.
	/// The user of this attribute is expected to format 
	/// </summary>
	class RateLimitAttribute : PreconditionAttribute
	{
		readonly TimeSpan minimumGuildInterval;
		readonly TimeSpan minimumChannelInterval;
		readonly TimeSpan minimumUserInterval;

		readonly Dictionary<ulong, DateTime> nextAllowedForGuild = new Dictionary<ulong, DateTime>();
		readonly Dictionary<ulong, DateTime> nextAllowedForChannel = new Dictionary<ulong, DateTime>();
		readonly Dictionary<ulong, DateTime> nextAllowedForUser = new Dictionary<ulong, DateTime>();

		readonly Task<PreconditionResult> taskResultSuccess = Task.FromResult(PreconditionResult.FromSuccess());


		public RateLimitAttribute(int guildSeconds = 0, int channelSeconds = 0, int userSeconds = 0)
		{
			minimumGuildInterval = TimeSpan.FromSeconds(guildSeconds);
			minimumChannelInterval = TimeSpan.FromSeconds(channelSeconds);
			minimumUserInterval = TimeSpan.FromSeconds(userSeconds);
		}


		public override Task<PreconditionResult> CheckPermissions(ICommandContext context, CommandInfo command, IDependencyMap map)
		{
			return CheckCategory(nextAllowedForGuild, context.Guild.Id, minimumGuildInterval)
				?? CheckCategory(nextAllowedForChannel, context.Channel.Id, minimumChannelInterval)
				?? CheckCategory(nextAllowedForUser, context.User.Id, minimumUserInterval)
				?? taskResultSuccess;

			Task<PreconditionResult> CheckCategory(Dictionary<ulong, DateTime> nextAllowed, ulong id, TimeSpan banTime)
			{
				if (banTime.Ticks > 0)
				{
					var remainingTime = CheckAndTrigger(nextAllowed, id, banTime);

					if (remainingTime.Ticks > 0)
						return Task.FromResult(PreconditionResult.FromError(RateLimitResult.FromError(RateLimitGroup.Guild, remainingTime)));
				}

				return null;
			}
		}

		// Returns the remaining ban time. If positive the ratelimit is still preventing execution of the command.
		static TimeSpan CheckAndTrigger(Dictionary<ulong, DateTime> banExpireTimes, ulong id, TimeSpan rateLimitTime)
		{
			var now = DateTime.UtcNow;
			DateTime nextAllowed;

			lock (banExpireTimes) // Monitor based locking is exactly what we want here because we expect minimal lock contention
			{
				// When does the ban expire?
				if (!banExpireTimes.TryGetValue(id, out nextAllowed))
					nextAllowed = now;

				// How much longer is that?
				var remainingBanTime = nextAllowed - now;

				// If its expired, trigger it now
				if (remainingBanTime.Ticks <= 0)
					banExpireTimes[id] = now + rateLimitTime;

				// We always return the last ban time. (even if we trigger it now)
				return remainingBanTime;
			}
		}
	}

	public struct RateLimitResult : IResult
	{
		public RateLimitGroup RateLimitGroup { get; }
		public TimeSpan RemainingTime { get; }

		public RateLimitResult(RateLimitGroup group, TimeSpan remainingTime)
		{
			RateLimitGroup = group;
			RemainingTime = remainingTime;
		}

		public CommandError? Error => CommandError.UnmetPrecondition;
		public string ErrorReason => $"Command is rate-limited for '{RateLimitGroup}' for {RemainingTime.TotalSeconds:0} seconds";
		public bool IsSuccess => false;

		public static RateLimitResult FromError(RateLimitGroup group, TimeSpan remainingTime) =>
			new RateLimitResult(group, remainingTime);
	}

	public enum RateLimitGroup
	{
		Guild,
		Channel,
		User,
		// todo: based on role?
	}
}
