﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tangra.Model.Config;

namespace Tangra.OCR
{
	public class OcrExtensionManager
	{
		public static ITimestampOcr GetCurrentOCR()
		{
			if (TangraConfig.Settings.Generic.OcrEngine == "IOTA-VTI")
				return new IotaVtiOrcManaged();

			return null;
		}
	}
}
