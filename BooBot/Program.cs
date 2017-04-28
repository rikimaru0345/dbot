using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Net.Providers.WS4Net;
using Discord.WebSocket;
using Discord.Commands;
using System.Reflection;
using System.Numerics;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ComponentModel;
using LogProvider;

namespace BooBot
{

	class Program
	{
		public static void Main(string[] args) => new Program().Start().GetAwaiter().GetResult();

		static Logger coreLogger = Logger.Create("Main").AddOutput(new ConsoleOutput());

		DiscordSocketClient client;		

		public async Task Start()
		{
			LoadAudioLibraries();


			client = new DiscordSocketClient(new DiscordSocketConfig
			{
				WebSocketProvider = WS4NetProvider.Instance,
				MessageCacheSize = 10,
				AlwaysDownloadUsers = false,
				LogLevel = LogSeverity.Info,
			});

			client.Log += (msg) =>
			{
				coreLogger.Info(msg.ToString());
				return Task.CompletedTask;
			};
			
			coreLogger.Info("Initializing Service Manager");
			var serviceDepMap = new ServiceDependencyMap();
			serviceDepMap.Add(client);
						
			coreLogger.Info("Initializing Command Handler");
			var cmdHandler = new CommandHandler(client, "~", serviceDepMap);
			await cmdHandler.AddAllCommands();

			serviceDepMap.InitializeServices();

			
			coreLogger.Info("Logging in");
			await client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("BooBotToken", EnvironmentVariableTarget.Machine));
			await client.StartAsync();


			cmdHandler.StartListening();

			await Task.Delay(-1);
		}

		void LoadAudioLibraries()
		{
			bool x86 = IntPtr.Size == 4;
			var bitnessFolderName = x86 ? "win32" : "win64";

			var root = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);

			var opusLib = Path.Combine(root, "opus",  "opus.dll");
			var sodiumLib = Path.Combine(root, "libsodium",  "libsodium.dll");

			Load(sodiumLib);
			Load(opusLib);

			void Load(string fileName)
			{
				if (!File.Exists(fileName))
					throw new FileNotFoundException("Missing: " + fileName);
				var ptr = LoadLibrary(fileName);
				if (ptr == IntPtr.Zero)
				{
					var lastError = Marshal.GetLastWin32Error();
					var ex= new Win32Exception(lastError);
					throw ex;
				}
			}
		}

		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
		static extern IntPtr LoadLibrary([MarshalAs(UnmanagedType.LPStr)]string lpFileName);
	}
}
