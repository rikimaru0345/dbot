using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using Discord.Commands;
using System.Reflection;
using LogProvider;

namespace BooBot
{
	public class ServiceDependencyMap : DependencyMap
	{
		private Logger _logger = Logger.Create("ServiceManager").AddOutput(new ConsoleOutput());

		public void InitializeServices()
		{
			// First we discover all types inheriting from ServiceBase
			// We could do additional filtering based on attributes here as well
			var serviceTypes = Assembly
				.GetEntryAssembly()
				.ExportedTypes
				.Where(x => x.GetTypeInfo().GetCustomAttributes<ServiceAttribute>().Count() > 0);

			foreach (var type in serviceTypes)
			{
				object instance = InstantiateService(type);

				// Todo: send snippy remark + mean blob-emoji at whoever made this exclusively generic
				// Add(instance);

				this.GetType()
					.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance)
					.MakeGenericMethod(type)
					.Invoke(this, new[] { instance });


				_logger.Info($"Registered Service '{type.FullName}'");
			}
		}

		object InstantiateService(Type type)
		{
			var ctors = type
				.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
				.Select(c => (ConstructorInfo: c, Parameters: c.GetParameters()))
				.ToArray(); // We're enumerating multiple times

			// For now, we do no set-intersection with available types/values
			// because the DependencyMap should already contain everything a service could need.
			// However if there's an additional dependency that for whatever reason doesn't belong to the di container, 
			// we leave room for improvement here.

			// Single argument: DependencyMap
			var diCtor = ctors
				.Where(c => c.Parameters.Length == 1)
				.FirstOrDefault(c => typeof(DependencyMap).IsAssignableFrom(c.Parameters[0].ParameterType));

			if (diCtor.ConstructorInfo != null)
				return diCtor.ConstructorInfo.Invoke(new object[] { this });

			// Default case: no arguments
			var noParamsCtor = ctors.FirstOrDefault(c => c.Parameters.Length == 0);
			if (noParamsCtor.ConstructorInfo != null)
				return noParamsCtor.ConstructorInfo.Invoke(new object[] { });

			// Error if no matches can be found
			throw new MissingMemberException($"Service '{type.FullName}' should provide either a parameterless consturctor or a constructor taking a '{typeof(DependencyMap).FullName}'. No matching public constructor could be found ({ctors.Length} candidate constructors)");
		}
	}

	class ServiceAttribute : Attribute
	{
	}
}
