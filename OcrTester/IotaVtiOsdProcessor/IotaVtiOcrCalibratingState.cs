﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tangra.Model.Image;

namespace OcrTester.IotaVtiOsdProcessor
{
    public class CalibratedBlockPosition
    {
        public int BlockWidth;
        public int BlockHeight;
        public int BlockOffsetX;
        public int BlockOffsetY;

        public Pixelmap Image;

        public uint[] LastFrameNoDigit;
        public uint[] SecondLastFrameNoDigit;

        public int GetLastFrameNoDigitPosition(IotaVtiOcrProcessor stateManager)
        {
            for (int i = IotaVtiOcrProcessor.FIRST_FRAME_NO_DIGIT_POSITIONS; i <= IotaVtiOcrProcessor.MAX_POSITIONS; i++)
            {
                uint[] pixels = stateManager.GetBlockAtPosition(Image, i);
                int nuberSignalPixels = pixels.Count(x => x < 127);
                if (10 * nuberSignalPixels < stateManager.BlockWidth * stateManager.BlockHeight)
                    return i;
            }

            return -1;
        }

        public void PrepareLastTwoDigitsFromTheFrameNumber(IotaVtiOcrProcessor stateManager)
        {
            LastFrameNoDigit = stateManager.GetBlockAtPosition(Image, stateManager.LastFrameNoDigitPosition);
            SecondLastFrameNoDigit = stateManager.GetBlockAtPosition(Image, stateManager.LastFrameNoDigitPosition - 1);
        }
    }


    public class IotaVtiOcrCalibratingState : IotaVtiOcrState
    {
        private List<CalibratedBlockPosition> m_CalibratedPositons = new List<CalibratedBlockPosition>();

        private int RateBlock(Pixelmap image, Rectangle block)
        {
            int rv = 0;
            bool leftOk = (block.Left - 1) > 0 && (block.Left - 1) < image.Width;
            bool left2Ok = block.Left < image.Width;
            bool bottomOk = (block.Bottom - 1) < image.Height;
            bool bottom2Ok = block.Bottom < image.Height;
            bool topOk = block.Top - 1 >= 0;

            int totalWhiteWhite = 0;
            for (int y = block.Top; y < block.Bottom; y++)
            {
                if (y >= image.Height) continue;
                if (leftOk && left2Ok)
                {
                    if (image[block.Left - 1, y] > 127 && image[block.Left, y] < 127) rv += 2;
                    else if (image[block.Left - 1, y] > 127 && image[block.Left, y] > 127)
                        totalWhiteWhite++;
                }
            }
            if (totalWhiteWhite == block.Bottom - block.Top)
                rv += 2 * (block.Bottom - block.Top);

            for (int x = block.Left; x < block.Right; x++)
            {
                if (x >= image.Width) continue;
                if (bottomOk && image[x, block.Bottom - 1] < 127) rv++;
                if (bottom2Ok && image[x, block.Bottom] > 127) rv++;
                if (topOk && image[x, block.Top - 1] > 127) rv++;
                if (image[x, block.Top] < 127) rv++;
            }

            return rv;
        }

        private void CalibrateBlockPositons(IotaVtiOcrProcessor stateManager)
        {
            int count = 0;
            int maxRating = -1;

            int bestXOffs = -1;
            int bestYOffs = -1;
            int bestWidth = -1;
            int bestHeight = -1;

            for (int xOffs = 0; xOffs < 8; xOffs++)
            {
                for (int yOffs = 0; yOffs < 4; yOffs++)
                {
                    for (int x = m_MinBlockWidth; x <= m_MaxBlockWidth; x++)
                    {
                        for (int y = m_MinBlockHeight; y <= m_MaxBlockHeight; y++)
                        {
                            int totalRating = 0;
                            Rectangle rect;

                            for (int i = 0; i < IotaVtiOcrProcessor.MAX_POSITIONS; i++)
                            {
                                rect = new Rectangle(xOffs + i * x, yOffs, x, y);
                                totalRating += RateBlock(stateManager.CurrentImage, rect);
                            }

                            if (totalRating > maxRating)
                            {
                                maxRating = totalRating;
                                bestXOffs = xOffs;
                                bestYOffs = yOffs;
                                bestWidth = x;
                                bestHeight = y;
                            }

                            count++;
                        }
                    }
                }
            }

            stateManager.BlockWidth = bestWidth;
            stateManager.BlockHeight = bestHeight;
            stateManager.BlockOffsetX = bestXOffs;
            stateManager.BlockOffsetY = bestYOffs;

            m_CalibratedPositons.Add(
                new CalibratedBlockPosition()
                {
                    BlockWidth = bestWidth,
                    BlockHeight = bestHeight,
                    BlockOffsetX = bestXOffs,
                    BlockOffsetY = bestYOffs,
                    Image = stateManager.CurrentImage
                });
        }

        private void Reinitialise(int width, int height)
        {
            m_Width = width;
            m_Height = height;

            m_MaxBlockWidth = m_Width / 27;
            m_MinBlockWidth = m_Width / 29;
            m_MinBlockHeight = (int)Math.Round(0.66 * m_Height);
            m_MaxBlockHeight = m_Height - 2;

            m_CalibratedPositons.Clear();
        }

        public override void Process(IotaVtiOcrProcessor stateManager, Graphics g)
        {
            if (m_Width != stateManager.CurrentImage.Width || m_Height != stateManager.CurrentImage.Height)
            {
                Reinitialise(stateManager.CurrentImage.Width, stateManager.CurrentImage.Height);
            }

            if (m_CalibratedPositons.Count >= 10)
            {
                if (CheckBlockPositions(stateManager) &&
                    DigitPatternsRecognized(stateManager))
                {
                    // TODO: Change the state to calibrated or working
                    m_CalibratedPositons.RemoveAt(0);
                }
                else
                    m_CalibratedPositons.RemoveAt(0);
            }

            CalibrateBlockPositons(stateManager);

            for (int i = 0; i < IotaVtiOcrProcessor.MAX_POSITIONS; i++)
            {
                g.DrawRectangle(Pens.Chartreuse, i * stateManager.BlockWidth + stateManager.BlockOffsetX, stateManager.BlockOffsetY, stateManager.BlockWidth, stateManager.BlockHeight);
            }
        }

        private bool CheckBlockPositions(IotaVtiOcrProcessor stateManager)
        {
            int widthMedian = m_CalibratedPositons.Select(x => x.BlockWidth).ToList().Median();
            int heightMedian = m_CalibratedPositons.Select(x => x.BlockHeight).ToList().Median();
            int offsetXMedian = m_CalibratedPositons.Select(x => x.BlockOffsetX).ToList().Median();
            int offsetYMedian = m_CalibratedPositons.Select(x => x.BlockOffsetY).ToList().Median();

            int numItemsWithDifferentValues = m_CalibratedPositons
                .Count(x =>
                       x.BlockWidth != widthMedian ||
                       x.BlockHeight != heightMedian ||
                       x.BlockOffsetX != offsetXMedian ||
                       x.BlockOffsetY != offsetYMedian);

            if (numItemsWithDifferentValues <= 3)
            {
                stateManager.BlockWidth = widthMedian;
                stateManager.BlockHeight = heightMedian;
                stateManager.BlockOffsetX = offsetXMedian;
                stateManager.BlockOffsetY = offsetYMedian;
            }

            int[] arrLastDigitPos  = m_CalibratedPositons.Select(x => x.GetLastFrameNoDigitPosition(stateManager)).ToArray();
            Array.Sort(arrLastDigitPos);

            if (arrLastDigitPos[0] == arrLastDigitPos[arrLastDigitPos.Length - 1] && // All positions in all frames must be the same ...
                arrLastDigitPos[0] != -1 && // recognized position ...
                arrLastDigitPos[0] > IotaVtiOcrProcessor.FIRST_FRAME_NO_DIGIT_POSITIONS + 2) // and should have at least 2 digits in the frame number
            {
                stateManager.LastFrameNoDigitPosition = arrLastDigitPos[0] - 1;
                return true;
            }
            else
                return false;
        }

        private int GetDiffSignature(uint[] probe, uint[] etalon)
        {
            int rv = 0;

            for (int i = 0; i < probe.Length; i++)
            {
                if (etalon[i] != probe[i])
                    rv++;
            }

            return rv;
        }

        private void BinirizeDiffArray(List<int> diffs)
        {
            double averageValue = diffs.Average();
            for (int i = 0; i < diffs.Count; i++)
            {
                diffs[i] = diffs[i] < averageValue ? 0 : 1;
            }
        }

        private bool DigitPatternsRecognized(IotaVtiOcrProcessor stateManager)
        {
            Trace.Assert(m_CalibratedPositons.Count == 10);

            foreach (CalibratedBlockPosition blockPosition in m_CalibratedPositons)
                blockPosition.PrepareLastTwoDigitsFromTheFrameNumber(stateManager);

            List<int> forwardDiffs = m_CalibratedPositons
                .Select(x => GetDiffSignature(x.SecondLastFrameNoDigit, m_CalibratedPositons[0].SecondLastFrameNoDigit))
                .ToList();

            List<int> backwardDiffs = m_CalibratedPositons
                .Select(x => GetDiffSignature(x.SecondLastFrameNoDigit, m_CalibratedPositons[m_CalibratedPositons.Count - 1].SecondLastFrameNoDigit))
                .ToList();

            BinirizeDiffArray(forwardDiffs);
            BinirizeDiffArray(backwardDiffs);

            int forwardChangeIndex = forwardDiffs.FindIndex(x => x == 1);
            int backwardChangeIndex = backwardDiffs.FindIndex(x=> x == 0);

            if (forwardChangeIndex == backwardChangeIndex &&
                forwardDiffs.FindIndex(forwardChangeIndex, x => x == 0) == -1 &&
                backwardDiffs.FindIndex(backwardChangeIndex, x => x == 1) == -1)
            {
                int indexPixelPrev = forwardChangeIndex > 0 ? forwardChangeIndex - 1 : 9;
                int indexPixelNext = forwardChangeIndex < m_CalibratedPositons.Count - 1 ? forwardChangeIndex + 1 : forwardChangeIndex - 9;
                int signalChange = m_CalibratedPositons[forwardChangeIndex].LastFrameNoDigit.Count(x => x < 127);
                int signalPixelsPrev = m_CalibratedPositons[indexPixelPrev].LastFrameNoDigit.Count(x => x < 127);
                int signalPixelsNext = m_CalibratedPositons[indexPixelNext].LastFrameNoDigit.Count(x => x < 127);

                // OneAfterZero: 9 - 0 - 1
                // ZeroAfterOne: 8 - 1 - 0

                if (signalChange < signalPixelsPrev && signalChange < signalPixelsNext)
                {
                    // One before Zero
                    stateManager.LearnDigitPattern(m_CalibratedPositons[indexPixelNext].LastFrameNoDigit, 0);
                    stateManager.LearnDigitPattern(m_CalibratedPositons[forwardChangeIndex].LastFrameNoDigit, 1);
                    stateManager.LearnDigitPattern(m_CalibratedPositons[indexPixelPrev].LastFrameNoDigit, 8);

                    // 3 - 2 - 5 - 4 - 7 - 6 - 9 - (8 - 1 - 0) - 3 - 2 - 5 - 4 - 7 - 6  - 9 - 8

                    int walkBackIndex = indexPixelPrev - 1;
                    if (walkBackIndex >= 0) stateManager.LearnDigitPattern(m_CalibratedPositons[walkBackIndex].LastFrameNoDigit, 9);
                    walkBackIndex--;
                    if (walkBackIndex >= 0) stateManager.LearnDigitPattern(m_CalibratedPositons[walkBackIndex].LastFrameNoDigit, 6);
                    walkBackIndex--;
                    if (walkBackIndex >= 0) stateManager.LearnDigitPattern(m_CalibratedPositons[walkBackIndex].LastFrameNoDigit, 7);
                    walkBackIndex--;
                    if (walkBackIndex >= 0) stateManager.LearnDigitPattern(m_CalibratedPositons[walkBackIndex].LastFrameNoDigit, 4);
                    walkBackIndex--;
                    if (walkBackIndex >= 0) stateManager.LearnDigitPattern(m_CalibratedPositons[walkBackIndex].LastFrameNoDigit, 5);
                    walkBackIndex--;
                    if (walkBackIndex >= 0) stateManager.LearnDigitPattern(m_CalibratedPositons[walkBackIndex].LastFrameNoDigit, 2);
                    walkBackIndex--;
                    if (walkBackIndex >= 0) stateManager.LearnDigitPattern(m_CalibratedPositons[walkBackIndex].LastFrameNoDigit, 3);

                    int walkForwardIndex = indexPixelNext + 1;
                    if (walkForwardIndex < 10) stateManager.LearnDigitPattern(m_CalibratedPositons[walkForwardIndex].LastFrameNoDigit, 3);
                    walkForwardIndex++;
                    if (walkForwardIndex < 10) stateManager.LearnDigitPattern(m_CalibratedPositons[walkForwardIndex].LastFrameNoDigit, 2);
                    walkForwardIndex++;
                    if (walkForwardIndex < 10) stateManager.LearnDigitPattern(m_CalibratedPositons[walkForwardIndex].LastFrameNoDigit, 5);
                    walkForwardIndex++;
                    if (walkForwardIndex < 10) stateManager.LearnDigitPattern(m_CalibratedPositons[walkForwardIndex].LastFrameNoDigit, 4);
                    walkForwardIndex++;
                    if (walkForwardIndex < 10) stateManager.LearnDigitPattern(m_CalibratedPositons[walkForwardIndex].LastFrameNoDigit, 7);
                    walkForwardIndex++;
                    if (walkForwardIndex < 10) stateManager.LearnDigitPattern(m_CalibratedPositons[walkForwardIndex].LastFrameNoDigit, 6);
                    walkForwardIndex++;
                    if (walkForwardIndex < 10) stateManager.LearnDigitPattern(m_CalibratedPositons[walkForwardIndex].LastFrameNoDigit, 9);
                }
                else if (signalPixelsNext < signalChange && signalPixelsNext < signalPixelsPrev)
                {
                    stateManager.LearnDigitPattern(m_CalibratedPositons[forwardChangeIndex].LastFrameNoDigit, 0);

                    // One after Zero
                    int nextDigitIndex = indexPixelNext;
                    for (int i = 0; i < 9; i++)
                    {
                        stateManager.LearnDigitPattern(m_CalibratedPositons[nextDigitIndex].LastFrameNoDigit, 1 + i);

                        nextDigitIndex++;
                        if (nextDigitIndex > 9) nextDigitIndex -= 10;
                    }
                }

                return true;
            }

            return false;
        }
    }
}
