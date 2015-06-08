﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Tangra.Model.Video;
using Tangra.VideoOperations.Spectroscopy.Helpers;

namespace Tangra.VideoOperations.Spectroscopy
{
	public partial class frmRunMultiFrameSpectroscopy : Form
	{
		public int NumberOfMeasurements { get; private set; }
        public int MeasurementAreaWing { get; private set; }
	    public PixelCombineMethod BackgroundMethod { get; private set; }

		public frmRunMultiFrameSpectroscopy()
		{
			InitializeComponent();
		}

        public frmRunMultiFrameSpectroscopy(IFramePlayer framePlayer, VideoSpectroscopyOperation videoOperation)
			: this()
        {
			nudNumberMeasurements.Maximum = framePlayer.Video.LastFrame - framePlayer.CurrentFrameIndex;
			nudNumberMeasurements.Value = Math.Min(200, nudNumberMeasurements.Maximum);
            nudAreaWing.Value = (int)Math.Ceiling(videoOperation.SelectedStarFWHM);
		    cbxCombineMethod.SelectedIndex = 0;
		}

		private void btnNext_Click(object sender, EventArgs e)
		{
			NumberOfMeasurements = (int)nudNumberMeasurements.Value;
            MeasurementAreaWing = (int)nudAreaWing.Value;
            BackgroundMethod = (PixelCombineMethod)cbxCombineMethod.SelectedIndex;

			DialogResult = DialogResult.OK;
			Close();
		}
	}
}
