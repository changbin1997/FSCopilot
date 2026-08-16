using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FSCopilot
{
    /// <summary>
    /// Form1.GPWS.cs - Form1 的部分类定义。
    /// </summary>
    public partial class Form1
    {
        /// <summary>
        /// 处理 GPWS (Ground Proximity Warning System) 逻辑，根据高度和垂直速度提供语音警告。
        /// 智能重置逻辑：
        /// 1. 落地后重置所有高度提示
        /// 2. 爬升超过阈值一定高度后，重置该阈值（允许再次触发）
        /// 3. 添加冷却时间防止短时间内重复触发
        /// </summary>
        private void ProcessGPWS()
        {
            try
            {
                double h = currentData.RADIO_HEIGHT;
                double vs = currentData.VERTICAL_SPEED;
                bool isOnGround = currentData.SIM_ON_GROUND > 0.5;

                // === 1. 检测落地事件：从空中到地面，重置所有状态 ===
                if (isOnGround && !wasOnGround)
                {
                    // 刚刚落地，重置所有高度提示状态
                    spokenThresholds.Clear();
                    maxHeightSinceTakeoff = 0;
                    minHeightSincePeak = double.MaxValue;
                    System.Diagnostics.Debug.WriteLine("GPWS: 检测到落地，重置所有高度提示状态");
                }

                // === 2. 检测起飞事件：从地面到空中 ===
                if (!isOnGround && wasOnGround)
                {
                    // 刚刚起飞，重置状态
                    spokenThresholds.Clear();
                    maxHeightSinceTakeoff = h;
                    minHeightSincePeak = h;
                    System.Diagnostics.Debug.WriteLine("GPWS: 检测到起飞，重置高度提示状态");
                }

                // 更新地面状态跟踪
                wasOnGround = isOnGround;

                // 如果在地面上，不处理高度提示
                if (isOnGround)
                {
                    previousRadioHeight = h;
                    return;
                }

                // === 3. 跟踪飞行中的高度变化 ===
                if (h > maxHeightSinceTakeoff)
                {
                    maxHeightSinceTakeoff = h;
                    minHeightSincePeak = h;
                }

                // === 4. 智能重置逻辑：当爬升超过某个阈值一定高度后，重置该阈值 ===
                // 这样可以处理地形起伏导致的提前触发问题
                if (previousRadioHeight > 0 && h > previousRadioHeight)
                {
                    // 正在爬升，检查是否需要重置某些阈值
                    List<double> thresholdsToReset = new List<double>();
                    foreach (var t in spokenThresholds)
                    {
                        // 如果当前高度比阈值高出 HEIGHT_RESET_THRESHOLD，则重置该阈值
                        if (h > t + HEIGHT_RESET_THRESHOLD)
                        {
                            thresholdsToReset.Add(t);
                        }
                    }
                    foreach (var t in thresholdsToReset)
                    {
                        spokenThresholds.Remove(t);
                        System.Diagnostics.Debug.WriteLine($"GPWS: 爬升后重置阈值 {t}，当前高度 {h:F0}");
                    }
                }

                // === 5. 高度超过2600英尺时完全重置（保留原有逻辑作为兜底） ===
                if (h > 2600)
                {
                    if (spokenThresholds.Count > 0)
                    {
                        spokenThresholds.Clear();
                        System.Diagnostics.Debug.WriteLine("GPWS: 高度超过2600，完全重置");
                    }
                }

                // === 6. 下降时触发高度提示 ===
                if (vs < -100)
                {
                    // 检查冷却时间，防止短时间内多次触发
                    TimeSpan timeSinceLastCallout = DateTime.Now - lastAltitudeCalloutTime;
                    if (timeSinceLastCallout.TotalMilliseconds < ALTITUDE_CALLOUT_COOLDOWN_MS)
                    {
                        previousRadioHeight = h;
                        return;
                    }

                    // 查找应该播报的高度
                    foreach (var t in thresholds)
                    {
                        if (h <= t && !spokenThresholds.Contains(t))
                        {
                            // 验证：确保不会播报比当前高度高太多的值
                            // 例如：当前高度450，不应该播报500
                            if (t > h + 50)
                            {
                                continue;
                            }

                            // 额外验证：确保是合理的下降顺序
                            // 找出比当前阈值更大且已经播报过的阈值
                            bool hasHigherSpoken = false;
                            foreach (var higher in thresholds)
                            {
                                if (higher > t && spokenThresholds.Contains(higher))
                                {
                                    hasHigherSpoken = true;
                                    break;
                                }
                            }

                            // 如果没有更高的阈值被播报过，且当前高度显著低于第一个阈值，
                            // 说明可能是中途开始下降，直接播报当前阈值
                            if (!hasHigherSpoken && t < 2500 && h < t - 10)
                            {
                                // 跳过，等待更低的阈值
                                continue;
                            }

                            Speak(t.ToString());
                            spokenThresholds.Add(t);
                            lastAltitudeCalloutTime = DateTime.Now;
                            System.Diagnostics.Debug.WriteLine($"GPWS: 播报高度 {t}，当前高度 {h:F0}，垂直速度 {vs:F0}");
                            break;
                        }
                    }
                }

                previousRadioHeight = h;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理 GPWS 逻辑错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理地面滑行速度警告，当飞机在地面上时根据速度阈值提供语音提示。
        /// </summary>
        private void ProcessTaxiSpeeds()
        {
            try
            {
                bool isOnGround = currentData.SIM_ON_GROUND > 0.5;
                double currentSpeed = Math.Round(currentData.AIRSPEED_INDICATED);

                if (!isOnGround)
                {
                    spokenTaxiSpeeds.Clear();
                    lastSpokenTaxiSpeed = -1;
                    return;
                }

                if (currentSpeed < 40)
                {
                    return;
                }

                if (Math.Abs(currentSpeed - lastSpokenTaxiSpeed) < 0.1)
                {
                    return;
                }

                foreach (var threshold in taxiSpeedThresholds)
                {
                    if (Math.Abs(currentSpeed - threshold) <= 0.5 && !spokenTaxiSpeeds.Contains(threshold))
                    {
                        Speak(threshold.ToString());
                        spokenTaxiSpeeds.Add(threshold);
                        lastSpokenTaxiSpeed = currentSpeed;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理滑行速度错误: {ex.Message}");
            }
        }

    }
}
