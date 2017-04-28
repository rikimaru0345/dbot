using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System;

namespace LogProvider
{
	[Flags]
	public enum LogLevel
	{
		Debug = 1 << 0,
		Info = 1 << 1,
		Warning = 1 << 2,
		Error = 1 << 3,

		GuiOnly = 1 << 30,
		FileOnly = 1 << 31,
	}

	public abstract class LogMessageHandler
	{
		public abstract void Process(LogMessage msg);
	}

	public abstract class LogMessageBuffer : LogMessageHandler
	{
		public abstract int BufferSize { get; set; }
		public abstract void EmitAll(bool andClear);
		public abstract void ClearBuffer();
	}

	public class FuncProcessor : LogMessageHandler
	{
		readonly Func<LogMessage, LogMessage> processorFunc;

		public FuncProcessor(Func<LogMessage, LogMessage> processorFunc)
		{
			this.processorFunc = processorFunc;
		}

		public override void Process(LogMessage msg)
		{
			processorFunc(msg);
		}
	}

	public class Logger
	{
		/// <summary>
		/// Creates a new logger instance with a pre-set category
		/// </summary>
		public static Logger Create(string category = null, string prefix = null)
		{
			var logger = new Logger
			{
				Category = category ?? ""
			};

			if (prefix != null)
				logger.MessageProcessors.Add(new FuncProcessor(m =>
				{
					m.Message = prefix + m.Message;
					return m;
				}));

			return logger;
		}

		/// <summary>
		/// Every message can belong to a category, all messages send through this logger will have the category applied
		/// </summary>
		public string Category { get; private set; } = string.Empty;
		/// <summary>
		/// Every message will be processed by each message processor before getting sent to the buffer or outputs
		/// </summary>
		public ImmutableList<LogMessageHandler> MessageProcessors { get; private set; } = ImmutableList<LogMessageHandler>.Empty;
		/// <summary>
		/// A buffer caches all messages (up to a limit). You need a buffer when you have an UI but generate log messages before the UI is initialized.
		/// When a new output is added, the buffer will be flushed into the outputs.
		/// </summary>
		public LogMessageBuffer LogMessageBuffer { get; private set; } = null;
		public ImmutableList<LogMessageHandler> LogOutputs { get; private set; } = ImmutableList<LogMessageHandler>.Empty;


		Logger()
		{
		}

		// Copy from an existing instance
		Logger(Logger existing)
		{
			Category = existing.Category;
			MessageProcessors = existing.MessageProcessors;
			LogMessageBuffer = existing.LogMessageBuffer;
			LogOutputs = existing.LogOutputs;
		}


		public Logger SetCategory(string category) => new Logger(this) { Category = category ?? "" };
		public Logger SetProcessors(params LogMessageHandler[] handlers) => new Logger(this) { MessageProcessors = ImmutableList<LogMessageHandler>.Empty.AddRange(handlers) };
		public Logger SetBuffer(LogMessageBuffer buffer) => new Logger(this) { LogMessageBuffer = buffer };
		public Logger SetOutput(LogMessageHandler output, bool flushBuffer = true, bool disableBuffer = true) => SetOutputs(new[] { output }, flushBuffer, disableBuffer);
		public Logger AddOutput(LogMessageHandler output, bool flushBuffer = true, bool disableBuffer = true) => SetOutputs(LogOutputs.Add(output), flushBuffer, disableBuffer);
		public Logger SetOutputs(params LogMessageHandler[] outputs) => SetOutputs(outputs.AsEnumerable());
		public Logger SetOutputs(IEnumerable<LogMessageHandler> outputs, bool flushBuffer = true, bool disableBuffer = true)
		{
			var changed = new Logger(this);

			changed.LogOutputs = ImmutableList<LogMessageHandler>.Empty.AddRange(outputs);

			var buffer = changed.LogMessageBuffer;
			if (buffer != null)
			{
				if (flushBuffer)
					buffer.EmitAll(false);
				if (disableBuffer)
					changed.LogMessageBuffer = null;
			}

			return changed;
		}

		public void Write(LogLevel logLevel, string text, object tag = null)
		{
			var m = new LogMessage(logLevel, text, Category, tag);

			// Process the message, maybe apply prefixes and whatnot
			foreach (var processor in MessageProcessors)
				processor.Process(m);

			// Let the buffer know if one is set
			LogMessageBuffer?.Process(m);

			// Give the message to all the outputs
			foreach (var output in LogOutputs)
				output.Process(m);
		}
	}

	public static class LoggerEx
	{
		public static void Debug(this Logger logger, string text, object tag = null) => logger.Write(LogLevel.Debug, text, tag);
		public static void DebugWarn(this Logger logger, string text, object tag = null) => logger.Write(LogLevel.Debug | LogLevel.Warning, text, tag);
		public static void DebugError(this Logger logger, string text, object tag = null) => logger.Write(LogLevel.Debug | LogLevel.Error, text, tag);

		public static void Info(this Logger logger, string text, object tag = null) => logger.Write(LogLevel.Info, text, tag);
		public static void Warning(this Logger logger, string text, object tag = null) => logger.Write(LogLevel.Warning, text, tag);
		public static void Error(this Logger logger, string text, object tag = null) => logger.Write(LogLevel.Error, text, tag);
	}


	public class LogMessage
	{
		public DateTime Timestamp { get; set; }
		public LogLevel LogLevel { get; set; }
		public string Category { get; set; }
		public string Message { get; set; }
		public object Tag { get; set; }

		public string DateTimeFormattedWithMs => $"{Timestamp.Hour:00}:{Timestamp.Minute:00}:{Timestamp.Second:00}.{Timestamp.Millisecond:000}";
		public string DateTimeFormatted => $"{Timestamp.Hour:00}:{Timestamp.Minute:00}:{Timestamp.Second:00}";

		internal LogMessage(LogLevel loglevel, string message, string category = null, object tag = null)
		{
			Message = message;
			Category = category;
			Timestamp = DateTime.Now;
			LogLevel = loglevel;
			Tag = tag;
		}
	}


	class TextFileOutput : LogMessageHandler
	{
		TextWriter streamWriter;

		public static string GenerateDateTimeString()
		{
			var now = DateTime.UtcNow;
			return $"{now.Day}-{now.Month}-{now.Year}  {now.Hour}-{now.Minute}-{now.Second}";
		}

		public static string GenerateDefaultLogFileName()
		{
			return $"LogFile {GenerateDateTimeString()}.txt";
		}

		public TextFileOutput() : this(GenerateDefaultLogFileName())
		{
		}

		public TextFileOutput(string logFileName)
		{
			var fileStream = File.OpenWrite(logFileName);
			streamWriter = new StreamWriter(fileStream) { AutoFlush = true };

			// Force Windows-Newline
			streamWriter.NewLine = "\r\n";
		}

		public override void Process(LogMessage msg)
		{
			if ((msg.LogLevel & LogLevel.GuiOnly) != 0)
				return;

			var ex = msg.Tag as Exception;
			if (ex == null)
				streamWriter.WriteLine($"[{msg.DateTimeFormattedWithMs}][{msg.LogLevel}][{msg.Category}] {msg.Message}");
			else
				streamWriter.WriteLine($"[{msg.DateTimeFormattedWithMs}][{msg.LogLevel}][{msg.Category}] {msg.Message}\r\nType: {ex.GetType().FullName} Method: {ex.TargetSite.Name}\r\nTrace:\r\n{ex.StackTrace}");
		}
	}

	class ConsoleOutput : LogMessageHandler
	{
		TextWriter streamWriter;
		object colorLock = new object();

		public ConsoleOutput()
		{
			streamWriter = Console.Out;
		}

		public override void Process(LogMessage msg)
		{
			if ((msg.LogLevel & LogLevel.GuiOnly) != 0)
				return;


			lock (colorLock)
			{
				var previousColor = Console.ForegroundColor;
				var color = GetColorFromLogLevel(msg.LogLevel);
				Console.ForegroundColor = color;

				var ex = msg.Tag as Exception;
				if (ex == null)
					streamWriter.WriteLine($"[{msg.DateTimeFormattedWithMs}][{msg.LogLevel}][{msg.Category}] {msg.Message}");
				else
					streamWriter.WriteLine($"[{msg.DateTimeFormattedWithMs}][{msg.LogLevel}][{msg.Category}] {msg.Message}\r\nType: {ex.GetType().FullName} Method: {ex.TargetSite.Name}\r\nTrace:\r\n{ex.StackTrace}");

				Console.ForegroundColor = previousColor;
			}
		}

		ConsoleColor GetColorFromLogLevel(LogLevel level)
		{
			if ((level & LogLevel.Debug) != 0)
				return ConsoleColor.DarkGray;

			if ((level & LogLevel.Error) != 0)
				return ConsoleColor.Red;

			if ((level & LogLevel.Warning) != 0)
				return ConsoleColor.Yellow;

			return ConsoleColor.Gray;
		}
	}

	class CyclicBuffer<T> : IEnumerable<T>
	{
		T[] array;
		int curIndex = 0;
		int count = 0;

		public CyclicBuffer(int size)
		{
			array = new T[size];
		}

		public void Add(T item)
		{
			curIndex++;
			if (curIndex >= array.Length)
				curIndex = 0;

			array[curIndex] = item;
			count++;
		}

		public void Clear()
		{
			Array.Clear(array, 0, array.Length);
			curIndex = 0;
			count = 0;
		}

		public IEnumerator<T> GetEnumerator()
		{
			int arrayLength = array.Length;
			int localCount = Math.Min(count, arrayLength);
			for (int i = 0; i < localCount; i++)
				yield return array[i % arrayLength];
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}

	public class LogBuffer : LogMessageBuffer
	{
		public override int BufferSize { get; set; }
		CyclicBuffer<LogMessage> cycleBuffer;

		public LogBuffer(int bufferSize = 500)
		{
			BufferSize = bufferSize;
			cycleBuffer = new CyclicBuffer<LogMessage>(bufferSize);
		}

		public override void Process(LogMessage msg)
		{
			cycleBuffer.Add(msg);
		}

		public override void EmitAll(bool andClear)
		{
			throw new NotImplementedException();
		}

		public override void ClearBuffer()
		{
			cycleBuffer.Clear();
		}
	}
}