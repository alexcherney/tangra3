﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tangra.VideoOperations.LightCurves
{
    public interface IDisplayBitmapConverter
    {
        byte ToDisplayBitmapByte(uint pixel);
        string GetConfig();
        void SetConfig(string config);
    }

    public class DisplayBitmapConverter : IDisplayBitmapConverter
    {
        public byte ToDisplayBitmapByte(uint pixel)
        {
            return 0;
        }

        public static IDisplayBitmapConverter Default = new DefaultDisplayBitmapConverter();

        public static IDisplayBitmapConverter Default12Bit = new TwelveBitDisplayBitmapConverter();


        internal class DefaultDisplayBitmapConverter : IDisplayBitmapConverter
        {

            public byte ToDisplayBitmapByte(uint pixel)
            {
                return (byte)(pixel & 0xFF);
            }

            public string GetConfig()
            {
                return string.Empty;
            }

            public void SetConfig(string config)
            { }
        }

        internal class TwelveBitDisplayBitmapConverter : IDisplayBitmapConverter
        {

            public byte ToDisplayBitmapByte(uint pixel)
            {
                return (byte)Math.Max(0, Math.Min(255, Math.Round(0xFF * (pixel * 1.0f / 0xFFF))));
            }

            public string GetConfig()
            {
                return string.Empty;
            }

            public void SetConfig(string config)
            { }
        }

		internal class FourteenBitDisplayBitmapConverter : IDisplayBitmapConverter
        {

            public byte ToDisplayBitmapByte(uint pixel)
            {
                return (byte)Math.Max(0, Math.Min(255, Math.Round(0xFF * (pixel * 1.0f / 0x3FFF))));
            }

            public string GetConfig()
            {
                return string.Empty;
            }

            public void SetConfig(string config)
            { }
        }

        public string GetConfig()
        {
            throw new NotImplementedException();
        }

        public void SetConfig(string config)
        {
            throw new NotImplementedException();
        }

        public static IDisplayBitmapConverter ConstructConverter(int bitPix, string config)
        {
            if (string.IsNullOrEmpty(config))
            {
                if (bitPix == 8)
                    return new DefaultDisplayBitmapConverter();
                else if (bitPix == 12)
                    return new TwelveBitDisplayBitmapConverter();
				else if (bitPix == 14)
					return new FourteenBitDisplayBitmapConverter();
            }

            throw new NotImplementedException();
        }
    }
}
