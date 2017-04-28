using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BooBot.Services.Audio.HelperStreams
{
	/*
	internal class CircularBuffer
	{
		public int Capacity { get; }

		public int Size
		{
			get
			{
				return (int)(this.totalWrite - this.totalRead);
			}
		}

		public int ReadPos
		{
			get
			{
				return this.readCursor;
			}
		}

		public int WritePos
		{
			get
			{
				return this.writeCursor;
			}
		}

		public RingBuffer(int size)
		{
			this.Capacity = size;
			this.internalBuffer = new byte[this.Capacity];
			this.notOverflowEvent = new ManualResetEventSlim();
			this.asyncLock = new AsyncLock();
		}

		public void Push(byte[] buffer, int offset, int count)
		{
			bool flag = count > this.Capacity;
			if (flag)
			{
				throw new Exception("Trying to Push more data than there is capacity");
			}
			while (this.Capacity - this.Size < count)
			{
				Thread.Sleep(100);
			}
			object obj = this.staticLock;
			lock (obj)
			{
				while (count > 0)
				{
					int remainingCapacity = this.Capacity - this.Size;
					bool flag3 = count <= remainingCapacity;
					if (flag3)
					{
						this.Write(buffer, offset, count);
						count = 0;
					}
					else
					{
						bool flag4 = remainingCapacity > 0;
						if (flag4)
						{
							this.Write(buffer, offset, remainingCapacity);
							offset += remainingCapacity;
							count -= remainingCapacity;
						}
					}
				}
			}
		}

		public bool Pop(byte[] buffer, int offset, int count)
		{
			bool flag = this.totalRead == this.totalWrite;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				bool flag2 = count > this.Size;
				if (flag2)
				{
					result = false;
				}
				else
				{
					object obj = this.staticLock;
					lock (obj)
					{
						if (count > 0)
						{
							int remainingReadable = this.Size;
							bool flag4 = count <= remainingReadable;
							if (flag4)
							{
								this.Read(buffer, offset, count);
								count = 0;
								this.notOverflowEvent.Set();
								return true;
							}
							return false;
						}
					}
					result = true;
				}
			}
			return result;
		}

		private void Write(byte[] buffer, int offset, int count)
		{
			int remainingTillWrap = this.internalBuffer.Length - this.writeCursor;
			int initialWrite = Math.Min(remainingTillWrap, count);
			Buffer.BlockCopy(buffer, offset, this.internalBuffer, this.writeCursor, initialWrite);
			offset += initialWrite;
			count -= initialWrite;
			this.writeCursor += initialWrite;
			this.totalWrite += (ulong)((long)initialWrite);
			bool flag = this.writeCursor >= this.Capacity;
			if (flag)
			{
				this.writeCursor = 0;
			}
			bool flag2 = count == 0;
			if (!flag2)
			{
				Buffer.BlockCopy(buffer, offset, this.internalBuffer, this.writeCursor, count);
				this.writeCursor += count;
				this.totalWrite += (ulong)((long)count);
			}
		}

		private void Read(byte[] buffer, int offset, int count)
		{
			int remainingTillWrap = this.internalBuffer.Length - this.readCursor;
			int initialRead = Math.Min(remainingTillWrap, count);
			Buffer.BlockCopy(this.internalBuffer, this.readCursor, buffer, offset, initialRead);
			offset += initialRead;
			count -= initialRead;
			this.readCursor += initialRead;
			this.totalRead += (ulong)((long)initialRead);
			bool flag = this.readCursor >= this.Capacity;
			if (flag)
			{
				this.readCursor = 0;
			}
			bool flag2 = count == 0;
			if (!flag2)
			{
				Buffer.BlockCopy(this.internalBuffer, this.readCursor, buffer, offset, count);
				this.readCursor += count;
				this.totalRead += (ulong)((long)count);
			}
		}

		public void Clear()
		{
			using (this.asyncLock.Lock())
			{
				this.writeCursor = 0;
				this.readCursor = 0;
			}
		}

		public void Wait(CancellationToken cancelToken)
		{
			bool flag;
			do
			{
				this.notOverflowEvent.Wait(cancelToken);
				flag = (this.writeCursor == this.readCursor);
			}
			while (!flag);
		}

		private readonly byte[] internalBuffer;

		private ulong totalRead;

		private ulong totalWrite;

		private int readCursor;

		private int writeCursor;

		private ManualResetEventSlim notOverflowEvent;

		private AsyncLock asyncLock;

		private object staticLock = new object();
	}
	*/
}
