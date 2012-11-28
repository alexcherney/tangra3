﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Tangra.Model.Image;
using Tangra.Model.Video;

namespace Tangra.Video
{
	public class FramePlayer : IFramePlayer, IDisposable
	{
		private IFrameStream m_VideoStream;

		private int m_MillisecondsPerFrame;
		private bool m_IsRunning;
		private uint m_Step = 1;

		private IVideoFrameRenderer m_FrameRenderer;

		private Thread m_PlayerThread;

		public delegate void SimpleDelegate();
		
		public IFrameStream Video
		{
			get { return m_VideoStream; }
		}

		/// <summary>Returns the current playback status</summary>
		public bool IsRunning
		{
			get { return m_IsRunning; }
		}

		public void DisposeResources()
		{
			Dispose();
		}

		public void SetFrameRenderer(IVideoFrameRenderer frameRenderer)
		{
			m_FrameRenderer = frameRenderer;
		}

		public void OpenVideo(IFrameStream frameStream)
		{

			EnsureClosed();

			m_VideoStream = frameStream;

			this.m_IsRunning = false;

			m_MillisecondsPerFrame = (int)m_VideoStream.MillisecondsPerFrame;
			m_CurrentFrameIndex = m_VideoStream.FirstFrame - 1;
		}

		public void CloseVideo()
		{
			EnsureClosed();
		}

		public int FrameStep
		{
			get { return (int)m_Step; }
		}

		/// <summary>Start the video playback</summary>
		public void Start(FramePlaySpeed mode, uint step)
		{
			m_Step = step;
			m_IsRunning = true;

			int bufferSize = m_VideoStream.RecommendedBufferSize;

			if (mode == FramePlaySpeed.Fastest)
				m_MillisecondsPerFrame = 0;
			else if (mode == FramePlaySpeed.Slower)
				m_MillisecondsPerFrame = m_MillisecondsPerFrame * 2;

			if (bufferSize < 2)
			{
				m_PlayerThread = new Thread(new ThreadStart(Run));
			}
			else
			{
				m_PlayerThread = new Thread(new ThreadStart(RunBufferred));
				m_PlayerThread.IsBackground = true;
				m_PlayerThread.SetApartmentState(ApartmentState.MTA);

				m_BufferNextFrameThread = new Thread(new ParameterizedThreadStart(BufferNextFrame));
				m_BufferNextFrameThread.IsBackground = true;
				m_BufferNextFrameThread.SetApartmentState(ApartmentState.MTA);

				m_BufferNextFrameThread.Start(new FrameBufferContext()
				{
					BufferSize = bufferSize,
					FirstFrameNo = m_CurrentFrameIndex
				});
			}

			m_PlayerThread.Start();
		}

		internal class FrameBufferContext
		{
			public int BufferSize;
			public int FirstFrameNo;
		}

		internal class BufferedFrame
		{
			public Pixelmap Image;
			public int FrameNo;
		}

		private int m_CurrentFrameIndex = -1;

		public void StepForward()
		{
			m_CurrentFrameIndex++;
			if (m_CurrentFrameIndex >= m_VideoStream.LastFrame) m_CurrentFrameIndex = m_VideoStream.LastFrame;

			DisplayCurrentFrame(MovementType.Step);
		}

		public void StepForward(int secondsForward)
		{
			m_CurrentFrameIndex += (int)Math.Round(secondsForward * m_VideoStream.FrameRate);
			if (m_CurrentFrameIndex >= m_VideoStream.LastFrame) m_CurrentFrameIndex = m_VideoStream.LastFrame;

			DisplayCurrentFrame(MovementType.Jump);
		}

		public void RefreshCurrentFrame()
		{
			DisplayCurrentFrame(MovementType.Refresh);
		}

		public void MoveToFrame(int frameId)
		{
			m_CurrentFrameIndex = frameId;
			if (m_CurrentFrameIndex >= m_VideoStream.LastFrame) m_CurrentFrameIndex = m_VideoStream.LastFrame;
			if (m_CurrentFrameIndex < m_VideoStream.FirstFrame) m_CurrentFrameIndex = m_VideoStream.FirstFrame;

			DisplayCurrentFrame(MovementType.Jump);
		}

		public Pixelmap GetFrame(int frameNo, bool noIntegrate)
		{
			if (frameNo >= m_VideoStream.LastFrame) frameNo = m_VideoStream.LastFrame;
			if (frameNo < m_VideoStream.FirstFrame) frameNo = m_VideoStream.FirstFrame;

			Pixelmap currentBitmap = null;

			try
			{
				currentBitmap = m_VideoStream.GetPixelmap(frameNo);
			}
			catch (Exception ex)
			{
				Trace.WriteLine(ex.ToString());
			}

			return currentBitmap;
		}

		public void StepBackward()
		{
			m_CurrentFrameIndex--;
			if (m_CurrentFrameIndex < m_VideoStream.FirstFrame) m_CurrentFrameIndex = m_VideoStream.FirstFrame;

			DisplayCurrentFrame(MovementType.StepBackwards);
		}

		public void StepBackward(int secondsBackward)
		{
			m_CurrentFrameIndex -= (int)Math.Round(secondsBackward * m_VideoStream.FrameRate);

			if (m_CurrentFrameIndex < m_VideoStream.FirstFrame) m_CurrentFrameIndex = m_VideoStream.FirstFrame;

			DisplayCurrentFrame(MovementType.Jump);
		}

		private void DisplayCurrentFrame(MovementType movementType)
		{
			if (m_VideoStream != null)
			{
				Pixelmap currentBitmap = m_VideoStream.GetPixelmap(m_CurrentFrameIndex);
				DisplayCurrentFrameInternal(movementType, currentBitmap);
			}
		}

		private void DisplayCurrentFrameInternal(MovementType movementType, Pixelmap currentPixelmap)
		{
			if (m_CurrentFrameIndex >= m_VideoStream.FirstFrame &&
				m_CurrentFrameIndex <= m_VideoStream.LastFrame)
			{
				m_FrameRenderer.RenderFrame(m_CurrentFrameIndex, currentPixelmap, movementType, false, 0);			
			}
		}

		private Thread m_BufferNextFrameThread;
		private object m_FrameBitmapLock = new object();
		private Queue<BufferedFrame> m_FramesBufferQueue = new Queue<BufferedFrame>();

		private void BufferNextFrame(object state)
		{
			FrameBufferContext context = (FrameBufferContext)state;
			context.BufferSize = Math.Min(context.BufferSize, 64);

			Trace.WriteLine(string.Format("Frame Player: Bufferring {0} frames starting from {1}", context.BufferSize, context.FirstFrameNo));

			if (m_FramesBufferQueue.Count > 0)
			{
				lock (m_FrameBitmapLock)
				{
					m_FramesBufferQueue.Clear();
				}
			}

			int nextFrameIdToBuffer = context.FirstFrameNo;

			while (m_IsRunning)
			{
				if (nextFrameIdToBuffer > -1 &&
					m_FramesBufferQueue.Count < context.BufferSize)
				{
					if (nextFrameIdToBuffer < m_VideoStream.LastFrame)
					{
						BufferNonIntegratedFrame(nextFrameIdToBuffer);
						nextFrameIdToBuffer += (int)m_Step;
					}
				}

				Thread.Sleep(1);
			}
		}

		private void BufferNonIntegratedFrame(int nextFrameIdToBuffer)
		{
			Pixelmap bmp = m_VideoStream.GetPixelmap(nextFrameIdToBuffer);

			lock (m_FrameBitmapLock)
			{
				BufferedFrame bufferedFrame = new BufferedFrame();
				bufferedFrame.FrameNo = nextFrameIdToBuffer;
				bufferedFrame.Image = bmp;
				m_FramesBufferQueue.Enqueue(bufferedFrame);				
			}
		}

		/// <summary>Extract and display the frames</summary>
		private void RunBufferred()
		{
			m_FramesBufferQueue.Clear();
			try
			{
				m_FrameRenderer.PlayerStarted();

				Stopwatch sw = new Stopwatch();
				for (; (m_CurrentFrameIndex < m_VideoStream.LastFrame) && m_IsRunning; m_CurrentFrameIndex += (int)m_Step)
				{
					if (m_CurrentFrameIndex >= m_VideoStream.LastFrame)
						break;

					if (m_MillisecondsPerFrame != 0)
						sw.Start();

					BufferedFrame currentFrame = null;

					while (m_FramesBufferQueue.Count == 0)
					{
						Thread.Sleep(1);
					}

					bool taken = false;
					lock (m_FrameBitmapLock)
					{
						try
						{
							currentFrame = m_FramesBufferQueue.Dequeue();
						}
						catch (InvalidOperationException)
						{
							// Queue is empty
							currentFrame = null;
						}						
					}
					
					if (currentFrame == null) continue;
					if (currentFrame.FrameNo < m_CurrentFrameIndex) continue;
					if (currentFrame.FrameNo > m_CurrentFrameIndex)
					{
						Trace.WriteLine(string.Format("Frame Player: {0} frame(s) dropped by the rendering engine.", currentFrame.FrameNo - m_CurrentFrameIndex));

						// This will potentially skip a frame
						m_CurrentFrameIndex = currentFrame.FrameNo;
					}

					Pixelmap currentPixelmap =
						currentFrame.FrameNo > m_CurrentFrameIndex ?
						null : currentFrame.Image;

					int msToWait = -1;
					if (m_MillisecondsPerFrame != 0)
					{
						sw.Stop();
						msToWait = m_MillisecondsPerFrame - (int)sw.ElapsedMilliseconds;
						sw.Reset();
					}

					//show frame
					m_FrameRenderer.RenderFrame(m_CurrentFrameIndex, currentPixelmap, MovementType.Step, m_CurrentFrameIndex + m_Step >= m_VideoStream.LastFrame, msToWait);

					Thread.Sleep(1);
				}
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			finally
			{
				m_IsRunning = false;
			}

			m_FrameRenderer.PlayerStopped();
		}

		/// <summary>Extract and display the frames</summary>
		private void Run()
		{
			try
			{
				m_FrameRenderer.PlayerStarted();

				Stopwatch sw = new Stopwatch();
				for (; (m_CurrentFrameIndex < m_VideoStream.LastFrame) && m_IsRunning; m_CurrentFrameIndex += (int)m_Step)
				{
					if (m_CurrentFrameIndex >= m_VideoStream.LastFrame)
						break;

					if (m_MillisecondsPerFrame != 0)
						sw.Start();

					Pixelmap currentPixelmap = m_VideoStream.GetPixelmap(m_CurrentFrameIndex);

					int msToWait = -1;
					if (m_MillisecondsPerFrame != 0)
					{
						sw.Stop();
						msToWait = m_MillisecondsPerFrame - (int)sw.ElapsedMilliseconds;
						sw.Reset();
					}

					//show frame
					m_FrameRenderer.RenderFrame(
								m_CurrentFrameIndex, 
								currentPixelmap, 
								MovementType.Step,
								m_CurrentFrameIndex + m_Step >= m_VideoStream.LastFrame,
								msToWait);
				}
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			catch (Exception ex)
			{
				Trace.WriteLine("FramePlayer:Run() -> " + ex.ToString());
			}
			finally
			{
				m_IsRunning = false;
			}

			m_FrameRenderer.PlayerStopped();
		}

		/// <summary>Stop the video playback</summary>
		public void Stop()
		{
			m_IsRunning = false;
		}

		public void Dispose()
		{
			EnsureClosed();
		}

		private void EnsureClosed()
		{
			if (m_VideoStream != null)
			{
				IDisposable disp = m_VideoStream as IDisposable;
				if (disp != null) disp.Dispose();

				m_VideoStream = null;
			}
		}

		public bool IsAstroDigitalVideo
		{
			get
			{
				return m_VideoStream is AstroDigitalVideoStream;
			}
		}
	}
}
