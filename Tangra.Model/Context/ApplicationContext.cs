﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tangra.Model.Context
{
	public class TangraContext
	{
		public static TangraContext Current = new TangraContext();

		private bool m_HasVideoLoaded;

		public bool HasVideoLoaded
		{
			get { return m_HasVideoLoaded; }
			set
			{
				m_HasVideoLoaded = value;
				CanScrollFrames = value;
				CanPlayVideo = value;
			}
		}
		public bool HasImageLoaded;
		public bool CanChangeTool = true;
		public bool CanPlayVideo = true;

		public bool CanScrollFrames;

		public bool IsCalibrating = false;

		public bool HasConfigurationChosen = false;
		public bool HasConfigurationSolved = false;

		public bool UsingAviSynth = false;
		public bool UsingADV = false;
		public bool UsingDirectShow = false;
		public bool RecordingDebugSession = false;
		//internal PreProcessingInfo PreProcessingInfo = null;

		public bool UsingIntegration = false;
		public int NumberFramesToIntegrate = 0;

		public bool OSDExcludeToolDisabled = false;

		public bool CanLoadDarkFrame;

		public bool HasAnyFileLoaded
		{
			get { return HasVideoLoaded || HasImageLoaded; }
		}

		public int FrameWidth = 0;
		public int FrameHeight = 0;
		public int FirstFrame = -1;
		public int LastFrame = 0;

		public string FileName;
		public string FileFormat;

		private bool m_RestartRequest = false;
		public bool HasRestartRequest()
		{
			return m_RestartRequest;
		}

		public void Reset()
		{
			HasVideoLoaded = false;
			HasImageLoaded = false;
			CanChangeTool = false;
			CanChangeTool = false;

			CanScrollFrames = false;
			CanPlayVideo = false;

			UsingAviSynth = false;
			UsingADV = false;
			UsingDirectShow = false;
			RecordingDebugSession = false;
			UsingIntegration = false;

			HasConfigurationChosen = false;
			HasConfigurationSolved = false;
			IsCalibrating = false;

			OSDExcludeToolDisabled = false;

			FrameWidth = 0;
			FrameHeight = 0;
			FirstFrame = -1;
			LastFrame = 0;

			FileName = null;
			FileFormat = null;
		}
	}
}
