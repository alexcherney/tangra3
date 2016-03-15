﻿/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Tangra.Model.Config;
using Tangra.Helpers;

namespace Tangra.Config.SettingPannels
{
	public partial class ucGeneralVideo : SettingsPannel
	{
		public ucGeneralVideo()
		{
			InitializeComponent();
		}

		private bool m_GammaWillChange;
		private bool m_CameraResponseReverseWillChange;

		public override void LoadSettings()
		{
			nudGamma.SetNUDValue((decimal)TangraConfig.Settings.Photometry.RememberedEncodingGammaNotForDirectUse);
			cbxGammaTheFullFrame.Checked = TangraConfig.Settings.Generic.ReverseGammaCorrection;		
	
			cbxKnownResponse.SetCBXIndex((int)TangraConfig.Settings.Photometry.KnownCameraResponse);
			cbxCameraResponseFullFrame.Checked = TangraConfig.Settings.Generic.ReverseCameraResponse;

			m_GammaWillChange = false;
			m_CameraResponseReverseWillChange = false;

			UpdateControlState();
		}

		public override void SaveSettings()
		{
			TangraConfig.Settings.Photometry.RememberedEncodingGammaNotForDirectUse = (float)nudGamma.Value;			

			if (cbxGammaTheFullFrame.Checked)
			{
				m_GammaWillChange = 
					!TangraConfig.Settings.Generic.ReverseGammaCorrection || 
					Math.Round(Math.Abs(TangraConfig.Settings.Photometry.EncodingGamma - TangraConfig.Settings.Photometry.RememberedEncodingGammaNotForDirectUse)) >= 0.01;

				TangraConfig.Settings.Generic.ReverseGammaCorrection = true;
				TangraConfig.Settings.Photometry.EncodingGamma = TangraConfig.Settings.Photometry.RememberedEncodingGammaNotForDirectUse;
			}
			else
			{
				m_GammaWillChange = TangraConfig.Settings.Generic.ReverseGammaCorrection;
				TangraConfig.Settings.Generic.ReverseGammaCorrection = false;
				TangraConfig.Settings.Photometry.EncodingGamma = 1;
			}

			if (cbxCameraResponseFullFrame.Checked)
			{
				m_CameraResponseReverseWillChange = (int)TangraConfig.Settings.Photometry.KnownCameraResponse != cbxKnownResponse.SelectedIndex;
				TangraConfig.Settings.Generic.ReverseCameraResponse = true;
				TangraConfig.Settings.Photometry.KnownCameraResponse = (TangraConfig.KnownCameraResponse)cbxKnownResponse.SelectedIndex;
			}
			else
			{
				m_CameraResponseReverseWillChange = false;
				TangraConfig.Settings.Generic.ReverseCameraResponse = false;
			}
		}

		public override void OnPostSaveSettings()
		{
			if (m_GammaWillChange)
				NotificationManager.Instance.NotifyGammaChanged();

			if (m_CameraResponseReverseWillChange)
				NotificationManager.Instance.CameraResponseReverseChanged();
		}

		private void cbxGammaTheFullFrame_CheckedChanged(object sender, EventArgs e)
		{
			UpdateControlState();
		}

		private void UpdateControlState()
		{
			pnlEnterGammaValue.Enabled = cbxGammaTheFullFrame.Checked;
			pnlChooseKnownResponse.Enabled = cbxCameraResponseFullFrame.Checked;
		}

		public override void Reset()
		{		
			NotificationManager.Instance.NotifyGammaChanged();
			NotificationManager.Instance.CameraResponseReverseChanged();
		}

		private void cbxCameraResponseFullFrame_CheckedChanged(object sender, EventArgs e)
		{
			UpdateControlState();
		}
	}
}
