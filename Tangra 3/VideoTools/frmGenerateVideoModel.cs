﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Tangra.Model.Config;
using Tangra.Model.Image;
using Tangra.Model.Numerical;
using Tangra.PInvoke;
using Tangra.Video.AstroDigitalVideo;

namespace Tangra.VideoTools
{
	public partial class frmGenerateVideoModel : Form
	{
		internal class ModelConfig
		{
			public string FileName;
			public int TotalFrames;
			public int NoiseMean;
			public int NoiseStdDev;
			public int FlickeringStdDev;
			public double StandardStarMag;
			public int StandardStarIntensity;
			public double FWHM;
			public double StarMag2;
			public double StarMag3;
			public double StarMag4;
			public double StarMag5;
			public bool SimulateStar2;
			public bool SimulateStar3;
			public bool SimulateStar4;
			public bool SimulateStar5;
			public bool SimulatePassBy;
			public double PassByDist;
			public double PassByMag1; 
			public double PassByMag2;
			public string InfoLine1;
			public string InfoLine2;
			public string InfoLine3;
			public string InfoLine4;
			public double Gamma;
			public bool SimulateMovingBackground;
			public int PolyBgOrder;
			public int PolyBgFreq;
			public double PolyBgShift;
			public double PolyBgDepth;
		}

		public frmGenerateVideoModel()
		{
			InitializeComponent();
		}

		private delegate void UpdateUIDelegate(int pbarId, int percent, bool show);

		private void UpdateUI(int pbarId, int percent, bool show)
		{
			pbar.Value = percent;

			if (show && !pbar.Visible)
			{
				pbar.Visible = true;
				pnlConfig.Enabled = false;
			}
			else if (!show && pbar.Visible)
			{
				pbar.Visible = false;
				pnlConfig.Enabled = true;
			}

			pbar.Update();

			Update();
			Application.DoEvents();
		}

		private void InvokeUpdateUI(int pbarId, int percentDone, bool show)
		{
			try
			{
				Invoke(new UpdateUIDelegate(UpdateUI), new object[] { pbarId, percentDone, show });
			}
			catch (InvalidOperationException)
			{ }
		}

		private void GenerateSimulatedVideo(object state)
		{
			InvokeUpdateUI(2, 0, true);

			try
			{
				ModelConfig modelConfig = (ModelConfig) state;

				TangraVideo.CloseAviFile();
				TangraVideo.StartNewAviFile(modelConfig.FileName, 300, 200, 8, 25, false);
				try
				{
					using (Bitmap bmp = new Bitmap(300, 200, PixelFormat.Format24bppRgb))
					{
						AddOnScreenText(bmp, modelConfig, "The simulated video stars from the next frame");
						Pixelmap pixmap = Pixelmap.ConstructFromBitmap(bmp, TangraConfig.ColourChannel.Red);
						TangraVideo.AddAviVideoFrame(pixmap, modelConfig.Gamma, null);
					}

					if (modelConfig.SimulateMovingBackground)
						GenerateBackgroundModelParameters(modelConfig.PolyBgOrder, modelConfig.PolyBgDepth, 100, 100, 40);

					for (int i = 1; i <= modelConfig.TotalFrames; i++)
					{
						using (Pixelmap pixmap = GenerateFrame(i * 1.0 / modelConfig.TotalFrames, modelConfig))
						{
							TangraVideo.AddAviVideoFrame(pixmap, modelConfig.Gamma, null);
						}

						InvokeUpdateUI(2, (int)(100.0 * i / modelConfig.TotalFrames), true);
					}
				}
				finally
				{
					TangraVideo.CloseAviFile();
				}
			}
			finally
			{
				InvokeUpdateUI(2, 100, false);
			}
		}

		private static Font s_SmallFont = new Font(FontFamily.GenericSansSerif, 7);
		private const double FWHM_GAIN_PER_MAG = 0.15;

		private double m_A;
		private double m_B;
		private double m_C;
		private double m_D;
		private double m_E;
		private double m_F;

		private int[,] GenerateBackground(int order, int frequency, double shift, double framesDone, int x0, int y0, int side)
		{
			int[,] rv = new int[300, 200];
			double deltaX = -shift;
			double step = 2 * shift / frequency;

			for (int x = 0; x < framesDone - 1; x++)
			{
				if ((x / frequency) % 2 == 0)
					deltaX += step;
				else
					deltaX -= step;
			}
			Trace.WriteLine(string.Format("framesDone:{0}, deltaX = {1}", framesDone, deltaX));

			for (int x = 0; x < 300; x++)
			for (int y = 0; y < 200; y++)
			{
				if (x > x0 - side && x < x0 + side && y > y0 - side && y < y0 + side)
				{
					if (order == 1)
					{
						rv[x, y] = (int)(m_A * (x + deltaX) + m_B * y + m_C);
					}
					else if (order == 2)
					{
						rv[x, y] = (int)(m_A * (x + deltaX) * (x + deltaX) + m_B * (x + deltaX) * y + m_C * y * y + m_D * (x + deltaX) * m_E * y + m_F);
					}
				}
			}

			return rv;
		}

		private void GenerateBackgroundModelParameters(int order, double depth, int x0, int y0, int radius)
		{
			Random rnd = new Random((int) DateTime.Now.Ticks);

			if (order == 1)
			{
				// z = ax + by + c

				int dist = rnd.Next(x0, x0 + (int)(1.2 * radius));
				int d2 = rnd.Next((int)depth / 2, (int)depth);

				SafeMatrix A = new SafeMatrix(3, 3);
				SafeMatrix X = new SafeMatrix(3, 1);

				A[0, 0] = x0; A[0, 1] = y0; A[0, 2] = 1; X[0, 0] = depth;
				A[1, 0] = x0 + dist / 2; A[1, 1] = y0 + dist / 3; A[1, 2] = 1; X[1, 0] = d2;
				A[2, 0] = x0 + dist; A[2, 1] = y0 + dist / 2; A[2, 2] = 1; X[2, 0] = 0;

				SafeMatrix a_T = A.Transpose();
				SafeMatrix aa = a_T * A;
				SafeMatrix aa_inv = aa.Inverse();
				SafeMatrix bx = (aa_inv * a_T) * X;

				m_A = bx[0, 0];
				m_B = bx[1, 0];
				m_C = bx[2, 0];
			}
			else if (order == 2)
			{
				// z = axx + bxy + cyy + dx + ey + f

				int[] xArr = new int[6];
				int[] yArr = new int[6];
				double[] zArr = new double[6];

				xArr[0] = x0; yArr[0] = y0; zArr[0] = depth;
				xArr[1] = x0 + radius; yArr[1] = y0 + radius / 3; zArr[1] = 0;
				for (int i = 2; i < 6; i++)
				{
					xArr[i] = rnd.Next(x0, x0 + (int)(1.2 * radius));
					yArr[i] = rnd.Next(y0, y0 + (int)(0.6 * radius));
					zArr[i] = rnd.Next(0, (int)depth);
				}

				// Start with an approximation
				// z = axx +     + cyy +         + f

				SafeMatrix A = new SafeMatrix(3, 3);
				SafeMatrix X = new SafeMatrix(3, 1);

				for (int i = 0; i < 3; i++)
				{
					A[i, 0] = xArr[i] * xArr[i];
					A[i, 1] = yArr[i] * yArr[i];
					A[i, 2] = 1;
					X[i, 0] = zArr[i];
				}

				SafeMatrix a_T = A.Transpose();
				SafeMatrix aa = a_T * A;
				SafeMatrix aa_inv = aa.Inverse();
				SafeMatrix bx = (aa_inv * a_T) * X;

				m_A = bx[0, 0];
				m_C = bx[1, 0];
				m_F = bx[2, 0];

				m_B = 0;
				m_D = 0;
				m_E = 0;

				/*
				A = new SafeMatrix(6, 6);
				X = new SafeMatrix(6, 1);

				for (int i = 0; i < 6; i++)
				{
					A[i, 0] = xArr[i] * xArr[i];
					A[i, 1] = xArr[i] * yArr[i];
					A[i, 2] = yArr[i] * yArr[i];
					A[i, 3] = xArr[i];
					A[i, 4] = yArr[i]; 
					A[i, 5] = 1;
					X[i, 0] = zArr[i];
				}

				a_T = A.Transpose();
				aa = a_T * A;
				aa_inv = aa.Inverse();
				bx = (aa_inv * a_T) * X;

				m_A = bx[0, 0];
				m_B = bx[1, 0];
				m_C = bx[2, 0];
				m_D = bx[3, 0];
				m_E = bx[4, 0];
				m_F = bx[5, 0];
				 */
			}
			else if (order == 3)
			{
				// z = axxx + bxxy + cxyy + dyyy + exx + fxy + gyy + hx + iy + j
			}
		}

		private Pixelmap GenerateFrame(double percentDone, ModelConfig modelConfig)
		{
			int I1 = (int)Math.Round((double)modelConfig.StandardStarIntensity);
			int I2 = (int)Math.Round((double)modelConfig.StandardStarIntensity / Math.Pow(10, (modelConfig.StarMag2 - modelConfig.StandardStarMag) / 2.5));
			int I3 = (int)Math.Round((double)modelConfig.StandardStarIntensity / Math.Pow(10, (modelConfig.StarMag3 - modelConfig.StandardStarMag) / 2.5));
			int I4 = (int)Math.Round((double)modelConfig.StandardStarIntensity / Math.Pow(10, (modelConfig.StarMag4 - modelConfig.StandardStarMag) / 2.5));
			int I5 = (int)Math.Round((double)modelConfig.StandardStarIntensity / Math.Pow(10, (modelConfig.StarMag5 - modelConfig.StandardStarMag) / 2.5));
			int IPB1 = (int)Math.Round((double)modelConfig.StandardStarIntensity / Math.Pow(10, (modelConfig.PassByMag1 - modelConfig.StandardStarMag) / 2.5));
			int IPB2 = (int)Math.Round((double)modelConfig.StandardStarIntensity / Math.Pow(10, (modelConfig.PassByMag2 - modelConfig.StandardStarMag) / 2.5));
			float fwhm1 = (float) modelConfig.FWHM;
			float fwhm2 = (float)(modelConfig.FWHM + (modelConfig.StandardStarMag - modelConfig.StarMag2) * FWHM_GAIN_PER_MAG);
			float fwhm3 = (float)(modelConfig.FWHM + (modelConfig.StandardStarMag - modelConfig.StarMag3) * FWHM_GAIN_PER_MAG);
			float fwhm4 = (float)(modelConfig.FWHM + (modelConfig.StandardStarMag - modelConfig.StarMag4) * FWHM_GAIN_PER_MAG);
			float fwhm5 = (float)(modelConfig.FWHM + (modelConfig.StandardStarMag - modelConfig.StarMag5) * FWHM_GAIN_PER_MAG);
			float fwhm_pb1 = (float)(modelConfig.FWHM + (modelConfig.StandardStarMag - modelConfig.PassByMag1) * FWHM_GAIN_PER_MAG);
			float fwhm_pb2 = (float)(modelConfig.FWHM + (modelConfig.StandardStarMag - modelConfig.PassByMag2) * FWHM_GAIN_PER_MAG);

			if (modelConfig.FlickeringStdDev > 0)
			{
				I1 = (int)Math.Round(Random(I1, modelConfig.FlickeringStdDev));
				I2 = (int)Math.Round(Random(I2, modelConfig.FlickeringStdDev));
				I3 = (int)Math.Round(Random(I3, modelConfig.FlickeringStdDev));
				I4 = (int)Math.Round(Random(I4, modelConfig.FlickeringStdDev));
				I5 = (int)Math.Round(Random(I5, modelConfig.FlickeringStdDev));
			}

			int[,] simulatedBackground = new int[300,200];
			for (int x = 0; x < 300; x++)
			for (int y = 0; y < 200; y++)
			{
				simulatedBackground[x, y] = 0;
			}

			using (Bitmap bmp = new Bitmap(300, 200, PixelFormat.Format24bppRgb))
			{
				
				if (modelConfig.SimulateMovingBackground)
				{
					simulatedBackground = GenerateBackground(modelConfig.PolyBgOrder, modelConfig.PolyBgFreq, modelConfig.PolyBgShift, modelConfig.TotalFrames * percentDone, 110, 100, 35);
				}

				GenerateNoise(bmp, simulatedBackground, modelConfig.NoiseMean, modelConfig.NoiseStdDev);

				GenerateStar(bmp, 25, 160, (float)fwhm1, I1);
				if (modelConfig.SimulateStar2) GenerateStar(bmp, 75, 160, (float)fwhm2, I2);
				if (modelConfig.SimulateStar3) GenerateStar(bmp, 125, 160, (float)fwhm3, I3);
				if (modelConfig.SimulateStar4) GenerateStar(bmp, 175, 160, (float)fwhm4, I4);
				if (modelConfig.SimulateStar5) GenerateStar(bmp, 225, 160, (float)fwhm5, I5);

				if (modelConfig.SimulatePassBy)
				{
					GenerateStar(bmp, 110, 100, fwhm_pb1, IPB1);
					GenerateStar(bmp, 110 + (float)modelConfig.PassByDist, (float)(100 - 3 + 6 * percentDone), fwhm_pb2, IPB2);					
				}

				AddOnScreenText(bmp, modelConfig);

				return Pixelmap.ConstructFromBitmap(bmp, TangraConfig.ColourChannel.Red);
			}
		}

		private void AddOnScreenText(Bitmap bmp, ModelConfig modelConfig, string instructions = null)
		{
			using (Graphics g = Graphics.FromImage(bmp))
			{
				g.DrawString(modelConfig.InfoLine1, s_SmallFont, Brushes.LightGray, 0, 5);
				g.DrawString(modelConfig.InfoLine2, s_SmallFont, Brushes.LightGray, 0, 17);
				if (modelConfig.InfoLine3 != null) g.DrawString(modelConfig.InfoLine3, s_SmallFont, Brushes.LightGray, 0, 29);
				if (modelConfig.InfoLine4 != null) g.DrawString(modelConfig.InfoLine4, s_SmallFont, Brushes.LightGray, 0, 41);
				if (instructions != null) g.DrawString(instructions, s_SmallFont, Brushes.LightGray, 0, 100);
				g.Save();
			}	
		}

		private RNGCryptoServiceProvider cryptoRand = new RNGCryptoServiceProvider();		

		private double Random(double mean, double stdDev)
		{
			byte[] twoBytes = new byte[2];
			cryptoRand.GetBytes(twoBytes);
			double u1 = twoBytes[0] * 1.0 / 0xFF; //these are uniform(0,1) random doubles
			double u2 = twoBytes[1] * 1.0 / 0xFF;
			double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
			double randNormal = mean + stdDev * randStdNormal; //random normal(mean,stdDev^2)
			return randNormal;
		}

		private void GenerateNoise(Bitmap bmp, int[,] simulatedBackground, int mean, int stdDev)
		{
			BitmapData bmData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

			int stride = bmData.Stride;
			System.IntPtr Scan0 = bmData.Scan0;
			unsafe
			{
				byte* p = (byte*)(void*)Scan0;

				int nOffset = stride - bmp.Width * 3;

				for (int y = 0; y < bmp.Height; ++y)
				{
					for (int x = 0; x < bmp.Width; ++x)
					{
						byte val = (byte)Math.Min(255, Math.Max(0, (int)Math.Abs(simulatedBackground[x, y] + Random(mean, stdDev))));

						p[0] = val;
						p[1] = val;
						p[2] = val;

						p += 3;
					}
					p += nOffset;
				}
			}

			bmp.UnlockBits(bmData);
		}

		private void GenerateStar(Bitmap bmp, float x0, float y0, float fwhm, float iMax)
		{
			double r0 = fwhm / (2 * Math.Sqrt(Math.Log(2)));
			int maxPsfModelDist = (int)(6 * fwhm);

			// GDI+ still lies to us - the return format is BGR, NOT RGB.
			BitmapData bmData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

			int stride = bmData.Stride;
			System.IntPtr Scan0 = bmData.Scan0;
			unsafe
			{
				byte* p = (byte*)(void*)Scan0;

				int nOffset = stride - bmp.Width * 3;

				for (int y = 0; y < bmp.Height; ++y)
				{
					for (int x = 0; x < bmp.Width; ++x)
					{
						if (Math.Abs(x - x0) < maxPsfModelDist && Math.Abs(y - y0) < maxPsfModelDist)
						{
							int counter = 0;
							double sum = 0;
							for (double dx = -0.5; dx < 0.5; dx += 0.1)
							{
								for (double dy = -0.5; dy < 0.5; dy += 0.1)
								{
									double thisVal = Math.Min(255, Math.Max(0, iMax * Math.Exp(-((x + dx - x0) * (x + dx - x0) + (y + dy - y0) * (y + dy - y0)) / (r0 * r0))));
									sum += thisVal;
									counter++;
								}
							}

							int val = (int)Math.Round(sum / counter);

							int p1 = p[0] + val;
							int p2 = p[1] + val;
							int p3 = p[2] + val;

							p[0] = (byte)Math.Min(255, Math.Max(0, p1));
							p[1] = (byte)Math.Min(255, Math.Max(0, p2));
							p[2] = (byte)Math.Min(255, Math.Max(0, p3));
						}

						p += 3;
					}
					p += nOffset;
				}
			}

			bmp.UnlockBits(bmData);
		}

		private void btnGenerateVideo_Click(object sender, EventArgs e)
		{
			if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
			{
				var config = new ModelConfig()
				{
					FileName = saveFileDialog.FileName,
					TotalFrames = (int)nudTotalFrames.Value,
					NoiseMean = (int)nudNoiseMean.Value,
					NoiseStdDev = (int)nudNoiseStdDev.Value,
					FlickeringStdDev = (int)nudStarFlickering.Value,
					StandardStarIntensity = (int)nudStar1Intensity.Value - (int)nudNoiseMean.Value,
					StandardStarMag = (double)nudStar1Mag.Value,
					FWHM = (double)nudStar1FWHM.Value,
					StarMag2 = (double)nudStar2Mag.Value,
					StarMag3 = (double)nudStar3Mag.Value,
					StarMag4 = (double)nudStar4Mag.Value,
					StarMag5 = (double)nudStar5Mag.Value,
					SimulateStar2 = cbxStar2.Checked,
					SimulateStar3 = cbxStar3.Checked,
					SimulateStar4 = cbxStar4.Checked,
					SimulateStar5 = cbxStar5.Checked,
					SimulatePassBy = cbClosePassBySim.Checked,
					PassByDist = (double)nudPassByDist.Value,
					PassByMag1= (double)nudPassByMag1.Value,
					PassByMag2 = (double)nudPassByMag2.Value,
					Gamma = (double)nudGamma.Value,
					SimulateMovingBackground = cbxPolyBackground.Checked,
					PolyBgOrder = (int)nudPolyOrder.Value,
					PolyBgFreq = (int)nudPolyFreq.Value,
					PolyBgShift = (double)nudPolyShift.Value,
					PolyBgDepth = (double)nudPolyDepth.Value,
				};

				config.InfoLine1 = string.Format("Model Video Generated by Tangra v.{0}", ((AssemblyFileVersionAttribute)Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyFileVersionAttribute), true)[0]).Version);
				config.InfoLine2 = string.Format("Noise: {0} +/- {1}, Flickering: {2}, FWHM: {3}, {4} = {5} mag", 
					config.NoiseMean, config.NoiseStdDev, config.FlickeringStdDev, config.FWHM.ToString("0.0"), config.StandardStarIntensity, config.StandardStarMag.ToString("0.00"));

				var modelConfigStr = new StringBuilder("Stars: ");
				if (config.SimulateStar2) modelConfigStr.AppendFormat("m2={0}; ", config.StarMag2.ToString("0.00"));
				if (config.SimulateStar3) modelConfigStr.AppendFormat("m3={0}; ", config.StarMag3.ToString("0.00"));
				if (config.SimulateStar4) modelConfigStr.AppendFormat("m4={0}; ", config.StarMag4.ToString("0.00"));
				if (config.SimulateStar5) modelConfigStr.AppendFormat("m5={0}; ", config.StarMag5.ToString("0.00"));
				config.InfoLine3 = modelConfigStr.ToString();

				modelConfigStr = new StringBuilder("Pass-By: ");
				if (config.SimulatePassBy) modelConfigStr.AppendFormat("{0}pix, m'={1}, m\"={2}", config.PassByDist.ToString("0.0"), config.PassByMag1.ToString("0.00"), config.PassByMag2.ToString("0.00"));
				config.InfoLine4 = modelConfigStr.ToString();

				ThreadPool.QueueUserWorkItem(new WaitCallback(GenerateSimulatedVideo), config);
			}
		}
	}
}
