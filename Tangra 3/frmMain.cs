﻿/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Tangra.Addins;
using Tangra.Config;
using Tangra.Controller;
using Tangra.Helpers;
using Tangra.ImageTools;
using Tangra.Model.Config;
using Tangra.Model.Context;
using Tangra.Model.Helpers;
using Tangra.Model.Image;
using Tangra.Model.Video;
using Tangra.Model.VideoOperations;
using Tangra.PInvoke;
using Tangra.Properties;
using Tangra.SDK;
using Tangra.Video;
using Tangra.Video.AstroDigitalVideo;
using Tangra.VideoOperations.LightCurves;
using Tangra.VideoOperations.MakeDarkFlatField;
using Tangra.VideoTools;
using Tangra.View;
using nom.tam.fits;
using nom.tam.util;

namespace Tangra
{
	public partial class frmMain : Form, IVideoFrameRenderer
	{
		private VideoController m_VideoController;
	    private LightCurveController m_LightCurveController;
		private DarkFlatFrameController m_MakeDarkFlatController;
		private AutoUpdatesController m_AutoUpdatesController;
		private AddinsController m_AddinsController;

		private VideoFileView m_VideoFileView;
		private ImageToolView m_ImageToolView;
        private ZoomedImageView m_ZoomedImageView;
		private bool m_FormLoaded = false;

		public frmMain()
		{
			InitializeComponent();

			TangraConfig.Load(ApplicationSettingsSerializer.Instance);

			m_VideoFileView = new VideoFileView(this);
			m_ImageToolView = new ImageToolView(this);
		    m_ZoomedImageView = new ZoomedImageView(zoomedImage, this);

			m_VideoController = new VideoController(this, m_VideoFileView, m_ZoomedImageView, m_ImageToolView, pnlControlerPanel);
			m_AddinsController = new AddinsController(this, m_VideoController);

			m_LightCurveController = new LightCurveController(this, m_VideoController, m_AddinsController);
			m_MakeDarkFlatController = new DarkFlatFrameController(this, m_VideoController);
			m_AutoUpdatesController = new AutoUpdatesController(this, m_VideoController);

			NotificationManager.Instance.SetVideoController(m_VideoController);

			m_VideoController.SetLightCurveController(m_LightCurveController);

			BuildRecentFilesMenu();

			m_AddinsController.LoadAddins();

#if !WIN32
			miVideoModelling.Visible = false;
#endif
			m_AutoUpdatesController.CheckForUpdates(false);
		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}

			base.Dispose(disposing);

			try
			{
				m_VideoController.Dispose();
			}
			catch (InvalidOperationException)
			{ }

			try
			{
				m_AddinsController.Dispose();
			}
			catch (InvalidOperationException)
			{ }			
		}

        public override object InitializeLifetimeService()
        {
            // The lifetime of the object is managed by Tangra
            return null;
        }

		#region Frame Rendering

		private RenderFrameContext m_LastFrameContext = RenderFrameContext.Empty;
		private VideoContext m_VideoContext = new VideoContext();

		public void PlayerStarted()
		{
			m_VideoController.UpdateViews();
		}

		public void PlayerStopped(int lastDisplayedFrame, bool userStopRequest)
		{
			m_VideoController.UpdateViews();
		}

		public void RenderFrame(
			int currentFrameIndex,
			Pixelmap currentPixelmap,
			MovementType movementType,
			bool isLastFrame,
			int msToWait,
            int firstFrameInIntegrationPeriod)
		{
			m_LastFrameContext = new RenderFrameContext()
			{
				CurrentFrameIndex = currentFrameIndex,
				MovementType = movementType,
				IsLastFrame = isLastFrame,
				MsToWait = msToWait,
                FirstFrameInIntegrationPeriod = firstFrameInIntegrationPeriod
			};

			RenderFrame(m_LastFrameContext, currentPixelmap);
		}

		private int m_CurrentFrameId = -1;
		
		private Stopwatch m_sw = new Stopwatch();
		private volatile int m_RefreshFrameLockOwner;
		private bool m_RefreshAtTheEndOfRenderFrame = false;
		private SpinLock m_ReentrancyGuard = new SpinLock(true);

		internal void RenderFrame(RenderFrameContext frameContext, Pixelmap currentPixelmap)
		{
			bool taken = false;

			if (frameContext.MsToWait > -1)
			{
				m_sw.Reset();
				m_sw.Start();
			}

			if (m_ReentrancyGuard.IsHeldByCurrentThread)
			{
				while ((Interlocked.CompareExchange(ref m_RefreshFrameLockOwner, 2, 0) == 0))
				{
					Thread.Sleep(1);
				}
				try
				{
					if (m_ReentrancyGuard.IsHeldByCurrentThread)
					{
						m_RefreshAtTheEndOfRenderFrame = true;
						return;
					}
				}
				finally
				{
					this.m_RefreshFrameLockOwner = 0;
				}
			}

			m_ReentrancyGuard.Enter(ref taken);

			try
			{
				m_RefreshAtTheEndOfRenderFrame = false;

				DoRenderFrame(currentPixelmap, frameContext);

				if (frameContext.MsToWait > -1)
				{
					m_sw.Stop();
					int msToWaitReal = frameContext.MsToWait - (int)m_sw.ElapsedMilliseconds;
					if (msToWaitReal > 0)
						Thread.Sleep(msToWaitReal);
				}

				while ((Interlocked.CompareExchange(ref m_RefreshFrameLockOwner, 1, 0) == 0))
				{
					Thread.Sleep(1);
				}
				try
				{
					if (m_RefreshAtTheEndOfRenderFrame)
						m_VideoController.RefreshCurrentFrame();
				}
				finally
				{
					m_RefreshFrameLockOwner = 0;
				}

				//m_TangraApplicationImpl.SetCurrentFrame(frameContext.CurrentFrameIndex);
			}
			catch (OutOfMemoryException)
			{
				MessageBox.Show(
					"There was not enough free memory to complete the operation. Please stop other running applications and try again.",
					"Tangra",
					MessageBoxButtons.OK, MessageBoxIcon.Stop);

				Application.Exit();
			}
			catch (Exception ex)
			{
				Trace.WriteLine(ex.ToString());

#if !DEBUG
                frmUnhandledException.HandleExceptionNoRestart(this, ex);
#else
				Debugger.Break();
#endif
			}
			finally
			{
				if (taken)
					m_ReentrancyGuard.Exit();
			}
		}

		private int m_FPSLastFrameNo;
		private long m_FPSLastSavedTicks;

		private void DoRenderFrame(Pixelmap currentPixelmap, RenderFrameContext frameContext)
		{
			// NOTE: Disposing the images may happen before they have been rendered and this would cause the following error:
			// Parameter is not valid.
			//  at System.Drawing.Image.get_Width()
			//

			if (m_VideoContext.Pixelmap != null)
			{
				m_VideoContext.Pixelmap.Dispose();
				m_VideoContext.Pixelmap = null;
			}

			m_VideoContext.Pixelmap = currentPixelmap;
			m_VideoContext.RenderFrameContext = frameContext;

			if (currentPixelmap == null)
				return;	

			scrollBarFrames.Value = frameContext.CurrentFrameIndex;

			if (TangraConfig.Settings.Generic.ShowProcessingSpeed)
			{
			    if (m_FPSLastFrameNo != -1)
			    {
			        double totalSec = (new TimeSpan(DateTime.Now.Ticks - m_FPSLastSavedTicks)).TotalSeconds;
			        if (totalSec >= 1.0)
			        {
						int totalFrames = frameContext.CurrentFrameIndex - m_FPSLastFrameNo;
						ssFPS.Text = string.Format("{0} fps", ((double)totalFrames / (totalSec /* m_FramePlayer.FrameStep*/)).ToString("0.0"));

						m_FPSLastFrameNo = frameContext.CurrentFrameIndex;
			            m_FPSLastSavedTicks = DateTime.Now.Ticks;
			        }
			    }
			    else
			    {
					m_FPSLastFrameNo = frameContext.CurrentFrameIndex;
			        m_FPSLastSavedTicks = DateTime.Now.Ticks;
			    }
			}


            bool isNewFrame = m_CurrentFrameId != frameContext.CurrentFrameIndex;
			m_CurrentFrameId = frameContext.CurrentFrameIndex;

			m_VideoController.SetImage(currentPixelmap, frameContext, !isNewFrame && frameContext.MovementType == MovementType.Refresh);

			ssFrameNo.Text = string.Format("Frame: {0}", frameContext.CurrentFrameIndex);

            m_VideoController.NewFrameDisplayed();

			CompleteRenderFrame();

			Update();

			m_VideoController.UpdateViews();

            if (isNewFrame)
                NotificationManager.Instance.NotifyCurrentFrameChanged(m_CurrentFrameId);
		}

		private void CompleteRenderFrame()
		{
			pictureBox.Image = m_VideoContext.Pixelmap.DisplayBitmap;

			if (m_VideoController.HasAstroImageState)
			{
				m_VideoController.OverlayStateForFrame(m_VideoContext.Pixelmap.DisplayBitmap, m_CurrentFrameId);
			}						         

            using (Graphics g = Graphics.FromImage(m_VideoContext.Pixelmap.DisplayBitmap))
            {
                m_VideoController.CompleteRenderFrame(g);

                g.Save();
            }

			m_VideoController.ApplyDisplayModeAdjustments(m_VideoContext.Pixelmap.DisplayBitmap, true, m_VideoContext.Pixelmap);	
		}

		#endregion

		private void miAbout_Click(object sender, EventArgs e)
		{
			bool hasNtpTime = m_VideoController.IsAstroAnalogueVideo && !m_VideoController.AstroAnalogueVideoHasOcrOrNtpData;
			var frmAbout = new frmAbout();
			frmAbout.ShowDialog(this);
		}

		private void miOpenVideo_Click(object sender, EventArgs e)
		{
            if (CurrentOS.IsWindows)
            {
				openVideoFileDialog.Filter = "All Supported Files (*.avi;*.adv;*.aav;*.ser)|*.avi;*.adv;*.aav;*.ser";
                openVideoFileDialog.DefaultExt = "avi";
            }
            else
            {
                // On Non-Windows OS currently only ADV/AAV files are supported
				openVideoFileDialog.Filter = "All Supported Files (*.adv;*.aav;*.ser)|*.adv;*.aav;*.ser";
                openVideoFileDialog.DefaultExt = "adv";
            }

		    if (openVideoFileDialog.ShowDialog(this) == DialogResult.OK)
			{
				OpenTangraFile(openVideoFileDialog.FileName);
			}
		}

		private void frmMain_Load(object sender, EventArgs e)
		{
			m_VideoController.InitVideoSystem(new PlayerContext(this));

			m_FormLoaded = true;

			if (Environment.GetCommandLineArgs().Length > 1)
			{
				string fileName = Environment.GetCommandLineArgs()[1];
				if (File.Exists(fileName))
				{
					timerCommandArgs.Tag = fileName;
					timerCommandArgs.Enabled = true;
				}
			}
		}

		private void timerCommandArgs_Tick(object sender, EventArgs e)
		{
			timerCommandArgs.Enabled = false;
			string fileName = timerCommandArgs.Tag as string;
			if (fileName != null && File.Exists(fileName))
			{
				OpenTangraFile(fileName, false);
			}
		}

		private void frmMain_Resize(object sender, EventArgs e)
		{
			if (m_FormLoaded &&
				FormWindowState.Normal == WindowState)
			{
				//TODO: Implement remembering form position into the IsolatedFileStorage
				//PositionMemento.SaveControlPosition(this);
			}

			//ConfigureImageScrollbars();
		}

		public bool SelectVideoOperation()
        {
            if (TangraConfig.Settings.Generic.OnOpenOperation == TangraConfig.OnOpenOperation.StartLightCurveReduction)
            {
				
				if (m_VideoController.ActivateOperation<ReduceLightCurveOperation>(m_LightCurveController, false))
				{
					m_VideoController.RefreshCurrentFrame();
					return true;
				}
            }

            return false;
        }

		public void OpenTangraFile(string fileName, bool runDefaultAction = true)
		{
			m_VideoController.CloseOpenedVideoFile();

			string fileExt = Path.GetExtension(fileName);

			if (fileExt == ".lc")
			{				
				m_LightCurveController.OpenLcFile(fileName);
			}
			else
			{
				try
				{
					if (m_VideoController.OpenVideoFile(fileName))
					{
						if (runDefaultAction && !SelectVideoOperation())
						{
							// NOTE: If no operation is selected, then set the default Arrow tool
							m_VideoController.SelectImageTool<ArrowTool>();
						}
					}
				}
				catch (InvalidVideoFileException ex)
				{
					MessageBox.Show(
						this,
						"Tangra is unable to open this file. Make sure that it is a valid video file and that you have all necessary codecs installed.\r\n\r\n" + ex.Message,
						"Error",
						MessageBoxButtons.OK,
						MessageBoxIcon.Error);
				}
			}
		}

		//private void ConfigureImageScrollbars()
		//{
		//	if (FormWindowState.Maximized == WindowState)
		//	{
		//		pictureBox.Dock = DockStyle.None;
		//		pictureBox.SizeMode = PictureBoxSizeMode.AutoSize;
		//	}
		//	else
		//	{
		//		pictureBox.Dock = DockStyle.Fill;
		//		pictureBox.SizeMode = PictureBoxSizeMode.Normal;
		//	}
		//}

		private void btnPlay_Click(object sender, EventArgs e)
		{
			m_VideoController.PlayVideo();
		}

		private void btnStop_Click(object sender, EventArgs e)
		{
			m_VideoController.StopVideo();
		}

		private void miSettings_Click(object sender, EventArgs e)
		{
			IAddinContainer lightCurveAddinContainer = m_LightCurveController.LightCurveFormAddinContainer;
			IAddinContainer[] addinContainers = lightCurveAddinContainer != null
				    ? new IAddinContainer[] {lightCurveAddinContainer}
				    : new IAddinContainer[] {};

			var frmSettings = new frmTangraSettings(
                null, 
                m_VideoController.AdvStatusPopupFormCustomizer, 
                m_VideoController.AavStatusPopupFormCustomizer,
                m_AddinsController,
				addinContainers);

			frmSettings.StartPosition = FormStartPosition.CenterParent;
			if (frmSettings.ShowDialog(this) == DialogResult.OK)
			{
				m_VideoController.RefreshCurrentFrame();
			}
		}

		private void scrollBarFrames_Scroll(object sender, ScrollEventArgs e)
		{
			if (!TangraContext.Current.CanScrollFrames)
				return;

			if (e.Type == ScrollEventType.EndScroll)
			{
				if (!m_VideoController.IsRunning)
				{
					m_VideoController.MoveToFrame(e.NewValue);
				}
			}
			else
			{
				displayFrameTimer.Tag = e;
				displayFrameTimer.Enabled = false;
				displayFrameTimer.Enabled = true;
			}
		}

		private void displayFrameTimer_Tick(object sender, EventArgs e)
		{
			displayFrameTimer.Enabled = false;

			ScrollEventArgs se = displayFrameTimer.Tag as ScrollEventArgs;

			if (se != null)
			{
				if (!m_VideoController.IsRunning)
				{
					m_VideoController.MoveToFrame(se.NewValue);
				}
			}
		}

		private void btn1FrMinus_Click(object sender, EventArgs e)
		{
			if (!m_VideoController.IsRunning)
			{
				m_VideoController.StepBackward();
			}
		}

		private void btn1FrPlus_Click(object sender, EventArgs e)
		{
			if (!m_VideoController.IsRunning)
			{
				m_VideoController.StepForward();
			}
		}

		private void btn1SecMinus_Click(object sender, EventArgs e)
		{
			if (!m_VideoController.IsRunning)
			{
				m_VideoController.StepBackward(1);
			}
		}

		private void btn1SecPlus_Click(object sender, EventArgs e)
		{
			if (!m_VideoController.IsRunning)
			{
				m_VideoController.StepForward(1);
			}
		}

		private void btn10SecMinus_Click(object sender, EventArgs e)
		{
			if (!m_VideoController.IsRunning)
			{
				m_VideoController.StepBackward(10);
			}
		}

		private void btn10SecPlus_Click(object sender, EventArgs e)
		{
			if (!m_VideoController.IsRunning)
			{
				m_VideoController.StepForward(10);
			}
		}

		private void btnJumpTo_Click(object sender, EventArgs e)
		{
			if (!m_VideoController.IsRunning)
			{
                var frm = new frmJumpToFrame();
                frm.nudFrameToJumpTo.Minimum = m_VideoController.VideoFirstFrame;
                frm.nudFrameToJumpTo.Maximum= m_VideoController.VideoLastFrame;
                frm.nudFrameToJumpTo.Value = m_CurrentFrameId;

                if (frm.ShowDialog(this) == DialogResult.OK)
                    m_VideoController.MoveToFrame((int)frm.nudFrameToJumpTo.Value);
			}
		}

		private void miTools_DropDownOpening(object sender, EventArgs e)
		{
			miFrameStatusData.Checked = m_VideoController.IsAdvStatusFormVisible || m_VideoController.IsAavStatusFormVisible;
		    miTargetPSFViewer.Checked = m_VideoController.IsTargetPSFViewerFormVisible;
		}

		private void miADVStatusData_Click(object sender, EventArgs e)
		{
			if (m_VideoController.IsAstroDigitalVideo || m_VideoController.IsAstroAnalogueVideo)
			{
				m_VideoController.ToggleAstroVideoStatusForm();
			}
			else if (m_VideoController.IsSerVideo)
			{
				m_VideoController.ToggleSerStatusForm();
			}
		}

        private void miTargetPSFViewer_Click(object sender, EventArgs e)
        {
            m_VideoController.TogglePSFViewerForm();
        }

		private void frmMain_Move(object sender, EventArgs e)
		{
			m_VideoController.NotifyMainFormMoved();
		}

        internal void BuildRecentFilesMenu()
        {
            miRecentVideos.DropDownItems.Clear();

            foreach (string recentFilePath in TangraConfig.Settings.RecentFiles.Lists[(int)RecentFileType.Video])
            {
                if (File.Exists(recentFilePath))
                {
                    ToolStripMenuItem miRecentFile = (ToolStripMenuItem)miRecentVideos.DropDownItems.Add(recentFilePath);
                    miRecentFile.Tag = recentFilePath;
                    miRecentFile.Click += new EventHandler(miRecentFileMenuItemClick);
                }
            }

            miRecentVideos.Enabled = miRecentVideos.DropDownItems.Count > 0;

			miRecentLightCurves.DropDownItems.Clear();

			foreach (string recentFilePath in TangraConfig.Settings.RecentFiles.Lists[(int)RecentFileType.LightCurve])
			{
				if (File.Exists(recentFilePath))
				{
					ToolStripMenuItem miRecentFile = (ToolStripMenuItem)miRecentLightCurves.DropDownItems.Add(recentFilePath);
					miRecentFile.Tag = recentFilePath;
					miRecentFile.Click += new EventHandler(miRecentFileMenuItemClick);
				}
			}

			miRecentLightCurves.Enabled = miRecentLightCurves.DropDownItems.Count > 0;
        }		

		private void miRecentFileMenuItemClick(object sender, EventArgs e)
		{
			ToolStripMenuItem mi = (sender as ToolStripMenuItem);
			if (mi != null && mi.Tag is string)
			{
				string filePath = mi.Tag as string;
				if (!string.IsNullOrEmpty(filePath) &&
					File.Exists(filePath))
				{
					OpenTangraFile(filePath);
				}
			}
		}

        private void miExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void miOnlineHelp_Click(object sender, EventArgs e)
        {
			UsageStats.Instance.OnlineHelpMenuUsed++;
			UsageStats.Instance.Save();

            Process.Start("http://www.hristopavlov.net/Tangra3");
        }

        #region Picture Box Events
        private void pictureBox_MouseClick(object sender, MouseEventArgs e)
        {
	        Point loc = pictureBox.GetImageLocation(e.Location);
			m_VideoController.MouseClick(loc);
        }

        private void pictureBox_MouseDoubleClick(object sender, MouseEventArgs e)
        {
			Point loc = pictureBox.GetImageLocation(e.Location);
			m_VideoController.MouseDoubleClick(loc);
        }

        private void pictureBox_MouseDown(object sender, MouseEventArgs e)
        {
			Point loc = pictureBox.GetImageLocation(e.Location);
			m_VideoController.MouseDown(loc);
        }

        private void pictureBox_MouseLeave(object sender, EventArgs e)
        {
            m_VideoController.MouseLeave();
        }

        private void pictureBox_MouseMove(object sender, MouseEventArgs e)
        {
			Point loc = pictureBox.GetImageLocation(e.Location);
			m_VideoController.MouseMove(loc);
        }

        private void pictureBox_MouseUp(object sender, MouseEventArgs e)
        {
			Point loc = pictureBox.GetImageLocation(e.Location);
			m_VideoController.MouseUp(loc);
        }
        #endregion

		private void miFSTSFileViewer_Click(object sender, EventArgs e)
		{
			m_VideoController.ShowFSTSFileViewer();
		}

		private void miRepairAdvFile_Click(object sender, EventArgs e)
		{
			if (openAdvFileDialog.ShowDialog(this) == DialogResult.OK)
			{
				m_VideoController.RepairAdvFile(openAdvFileDialog.FileName);
			}
		}

        private void miOpenLightCurve_Click(object sender, EventArgs e)
        {
            m_LightCurveController.LoadLightCurve();
        }

		private void miExportToBMP_Click(object sender, EventArgs e)
		{
			if (m_VideoContext.Pixelmap != null)
			{
				saveFrameDialog.Filter = "BMP Image (*.bmp)|*.bmp";
				saveFrameDialog.DefaultExt = "bmp";

				if (saveFrameDialog.ShowDialog(this) == DialogResult.OK)
				{
					UsageStats.Instance.ExportToBMPUsed++;
					UsageStats.Instance.Save();

					m_VideoContext.Pixelmap.CreateNewDisplayBitmap().Save(saveFrameDialog.FileName, ImageFormat.Bmp);
				}
			}
		}

		private void miExportToCSV_Click(object sender, EventArgs e)
		{
			if (m_VideoContext.Pixelmap != null)
			{
				saveFrameDialog.Filter = "Comma separated values (*.csv)|*.csv";
				saveFrameDialog.DefaultExt = "csv";

				if (saveFrameDialog.ShowDialog(this) == DialogResult.OK)
				{
					UsageStats.Instance.ExportToCSVUsed++;
					UsageStats.Instance.Save();

					StringBuilder output = new StringBuilder();

					for (int y = 0; y < m_VideoContext.Pixelmap.Height; y++)
					{
						for (int x = 0; x < m_VideoContext.Pixelmap.Width; x++)
						{
							output.Append(m_VideoContext.Pixelmap[x, y]);
							if (x != m_VideoContext.Pixelmap.Width - 1) output.Append(",");
						}

						if (y != m_VideoContext.Pixelmap.Height - 1) output.Append("\r\n");
					}

					File.WriteAllText(saveFrameDialog.FileName, output.ToString());
				}
			}
		}

		private void miExportToFits_Click(object sender, EventArgs e)
		{
			if (m_VideoContext.Pixelmap != null)
                m_VideoController.ExportToFits(m_VideoContext.Pixelmap);
		}

		private void DisplayIntensifyModeClicked(object sender, EventArgs e)
		{
			var currItem = sender as ToolStripMenuItem;
			if (currItem != null && !currItem.Checked)
			{
				tsmiOff.Checked = false;
				tsmiLo.Checked = false;
				tsmiHigh.Checked = false;
				tsmiDynamic.Checked = false;

				currItem.Checked = true;

				DisplayIntensifyMode newMode = DisplayIntensifyMode.Off;
				if (tsmiHigh.Checked)
					newMode = DisplayIntensifyMode.Hi;
				else if (tsmiLo.Checked)
					newMode = DisplayIntensifyMode.Lo;
				else if (tsmiDynamic.Checked)
					newMode = DisplayIntensifyMode.Dynamic;

				m_VideoController.SetDisplayIntensifyMode(newMode, null, null);
			}

			if (tsmiDynamic.Checked)
			{
				var frm = new frmDefineDisplayDynamicRange(m_VideoController);
				frm.ShowDialog(this);
			}
		}

		private void DisplayInvertedClicked(object sender, EventArgs e)
		{
			m_VideoController.SetDisplayInvertMode(tsmiInverted.Checked);
		}

        private void tsmiHueIntensity_Click(object sender, EventArgs e)
        {
            m_VideoController.SetDisplayHueMode(tsmiHueIntensity.Checked);
        }


		private void miJupiterGlow_Click(object sender, EventArgs e)
		{
			m_VideoController.SetDisplayHueBackgroundMode(miJupiterGlow.Checked);
		}

		private void miMakeDarkFlat_Click(object sender, EventArgs e)
		{
			m_VideoController.ActivateOperation<MakeDarkFlatOperation>(m_MakeDarkFlatController);
		}

		private void miLoadDark_Click(object sender, EventArgs e)
		{
			Cursor = Cursors.WaitCursor;
			try
			{
				m_MakeDarkFlatController.LoadDarkFrame();
			}
			finally
			{
				Cursor = Cursors.Default;
			}			
		}

		private void miLoadFlat_Click(object sender, EventArgs e)
		{
			Cursor = Cursors.WaitCursor;
			try
			{
				m_MakeDarkFlatController.LoadFlatFrame();
			}
			finally
			{
				Cursor = Cursors.Default;
			}			
		}

        private void miShowFields_Click(object sender, EventArgs e)
        {
            m_VideoController.RedrawCurrentFrame(true);
        }

		private void miCheckForUpdates_Click(object sender, EventArgs e)
		{
			m_AutoUpdatesController.CheckForUpdates(true);
		}

		private void pnlNewVersionAvailable_Click(object sender, EventArgs e)
		{
			PlatformID platform = Environment.OSVersion.Platform;
			if (platform == PlatformID.Win32Windows || platform == PlatformID.Win32NT || platform == PlatformID.Win32S)
			{				
				pnlNewVersionAvailable.Enabled = false;
				pnlNewVersionAvailable.IsLink = false;
				pnlNewVersionAvailable.Tag = pnlNewVersionAvailable.Text;
				pnlNewVersionAvailable.Text = "Update started ...";
				statusStrip.Update();

				m_AutoUpdatesController.RunTangra3UpdaterForWindows();

				Close();
			}
			else
				Process.Start("http://www.hristopavlov.net/Tangra3");
        }

		private void miLoadedAddins_Click(object sender, EventArgs e)
		{
			
		}

        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            m_AddinsController.FinaliseAddins();
        }

		private void miReduceLightCurve_Click(object sender, EventArgs e)
		{
			// Done by MouseDown
		}

		private void miReduceLightCurve_MouseDown(object sender, MouseEventArgs e)
		{
			bool debugMode = e.Button == MouseButtons.Middle;
			m_VideoController.ActivateOperation<ReduceLightCurveOperation>(m_LightCurveController, debugMode);
		}

		private void FileSystemFileDragDrop(object sender, DragEventArgs e)
		{
			try
			{
				var filedata = e.Data.GetData(DataFormats.FileDrop, false) as string[];
				if (filedata != null)
				{
					var filename = filedata.FirstOrDefault();
					if (filename != null)
					{
						// MessageBox.Show(filename);
						// TODO: Make the drag-drop of files to work
					}
				}
			}
			catch (Exception ex)
			{
				Trace.WriteLine(ex.GetFullStackTrace());
			}
		}

		private void FileSystemFileDragEnter(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
				e.Effect = DragDropEffects.Copy;
		}

		private void miVideoModelling_Click(object sender, EventArgs e)
		{
			var frm = new frmGenerateVideoModel();
			frm.ShowDialog(this);
		}

		private void miFileInfo_Click(object sender, EventArgs e)
		{
			m_VideoController.ShowFileInformation();
		}

        private void miOpenFitsSequence_Click(object sender, EventArgs e)
        {
	        var frm = new frmChooseFitsFolder();
	        frm.tbxFolderPath.Text = TangraConfig.Settings.LastUsed.FitsSeqenceLastFolderLocation;
			if (frm.ShowDialog(this) == DialogResult.OK)
            {
                if (m_VideoController.OpenFitsFileSequence(frm.tbxFolderPath.Text) ||
					(Control.ModifierKeys == Keys.Control && m_VideoController.OpenBitmapFileSequence(frm.tbxFolderPath.Text)))
                {
					TangraConfig.Settings.LastUsed.FitsSeqenceLastFolderLocation = frm.tbxFolderPath.Text;

					if (!SelectVideoOperation())
					{
						// NOTE: If no operation is selected, then set the default Arrow tool
						m_VideoController.SelectImageTool<ArrowTool>();
					}
                }
            }
        }

		private void zoomedImage_MouseMove(object sender, MouseEventArgs e)
		{
			Point imagePos = m_VideoController.GetImageCoordinatesFromZoomedImage(e.Location);
			m_VideoController.DisplayCursorImageCoordinates(imagePos);
		}

		private void frmMain_Shown(object sender, EventArgs e)
		{
			if (!Settings.Default.LicenseDialogAcknowledged && DateTime.Now < new DateTime(2015, 06, 30))
			{
				var frm = new frmContributorsCall();
				frm.StartPosition = FormStartPosition.CenterParent;
				frm.ShowDialog(this);
				Settings.Default.LicenseDialogAcknowledged = true;
				Settings.Default.Save();
			}
		}

		private void miCallForContributions_Click(object sender, EventArgs e)
		{
			var frm = new frmContributorsCall();
			frm.StartPosition = FormStartPosition.CenterParent;
			frm.ShowDialog(this);
		}

        private void miYahooGroup_Click(object sender, EventArgs e)
        {
            Process.Start("https://groups.yahoo.com/neo/groups/Tangra/info");
        }

	}
}
