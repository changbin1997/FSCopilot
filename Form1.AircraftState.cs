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
    /// Form1.AircraftState.cs - Form1 的部分类定义。
    /// </summary>
    public partial class Form1
    {
        /// <summary>
        /// 处理停机刹车状态变化，当停机刹车状态改变时输出语音提示。
        /// </summary>
        private void ProcessParkingBrake()
        {
            try
            {
                double currentBrakeState = currentData.PARKING_BRAKE;

                // 检测状态变化
                if (lastParkingBrakeState >= 0 && lastParkingBrakeState != currentBrakeState)
                {
                    if (currentBrakeState > 0.5)
                    {
                        Speak("已设置停机刹车");
                    }
                    else
                    {
                        Speak("已解除停机刹车");
                    }
                }

                lastParkingBrakeState = currentBrakeState;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理停机刹车错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理扰流板状态变化，当扰流板展开或收起时输出语音提示。
        /// 只有当飞机有扰流板时才进行监听。
        /// </summary>
        private void ProcessSpoilers()
        {
            try
            {
                // 检查飞机是否有扰流板
                if (currentData.SPOILER_AVAILABLE < 0.5)
                {
                    // 飞机没有扰流板，重置状态并跳过
                    lastSpoilerState = -1;
                    return;
                }

                double currentSpoilerPosition = currentData.SPOILERS_HANDLE_POSITION;
                // 判断扰流板是否展开（位置大于5%视为展开）
                bool isSpoilerDeployed = currentSpoilerPosition > 5.0;
                double currentState = isSpoilerDeployed ? 1.0 : 0.0;

                // 首次获取数据时，根据当前状态设置 label21 颜色
                if (lastSpoilerState < 0)
                {
                    bool deployed = isSpoilerDeployed;
                    BeginInvoke(new Action(() =>
                    {
                        label21.ForeColor = deployed
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                // 检测状态变化
                if (lastSpoilerState >= 0 && lastSpoilerState != currentState)
                {
                    if (currentState > 0.5)
                    {
                        Speak("已展开扰流板");
                        BeginInvoke(new Action(() =>
                        {
                            label21.ForeColor = System.Drawing.Color.FromArgb(241, 217, 81);
                        }));
                    }
                    else
                    {
                        Speak("已收起扰流板");
                        BeginInvoke(new Action(() =>
                        {
                            label21.ForeColor = System.Drawing.Color.FromArgb(153, 153, 153);
                        }));
                    }
                }

                lastSpoilerState = currentState;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理扰流板错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理扰流板预位状态变化，根据状态修改 label22 文字颜色。
        /// </summary>
        private void ProcessSpoilersArmed()
        {
            try
            {
                double currentState = currentData.SPOILERS_ARMED;

                // 首次获取数据时，根据当前状态设置 label22 颜色
                if (lastSpoilersArmedState < 0)
                {
                    bool isArmed = currentState > 0.5;
                    BeginInvoke(new Action(() =>
                    {
                        label22.ForeColor = isArmed
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                // 检测状态变化
                if (lastSpoilersArmedState >= 0 && lastSpoilersArmedState != currentState)
                {
                    bool isArmed = currentState > 0.5;
                    BeginInvoke(new Action(() =>
                    {
                        label22.ForeColor = isArmed
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                lastSpoilersArmedState = currentState;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理扰流板预位错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理襟翼挡位变化，当襟翼挡位改变时输出语音提示。
        /// 格式为 "襟翼 X/Y"，最大挡位时额外输出 "最大襟翼"，完全收起时输出 "已收起襟翼"。
        /// </summary>
        private void ProcessFlaps()
        {
            try
            {
                double currentFlapsIndex = currentData.FLAPS_HANDLE_INDEX;
                double flapsNumPositions = currentData.FLAPS_NUM_HANDLE_POSITIONS;

                // 总挡位数不包含 0 位（收起），所以可用挡位数 = flapsNumPositions - 1
                int maxFlapsIndex = (int)(flapsNumPositions);
                int currentIndex = (int)currentFlapsIndex;

                // 首次获取数据时，设置 label19 文本和颜色
                if (lastFlapsIndex < 0 && maxFlapsIndex > 0)
                {
                    int idx = currentIndex;
                    int max = maxFlapsIndex;
                    BeginInvoke(new Action(() =>
                    {
                        label19.Text = $"襟翼：{idx}/{max}";
                        label19.ForeColor = idx > 0
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                // 检测状态变化
                if (lastFlapsIndex >= 0 && lastFlapsIndex != currentFlapsIndex && maxFlapsIndex > 0)
                {
                    if (currentIndex == 0)
                    {
                        // 襟翼完全收起
                        Speak($"襟翼 0/{maxFlapsIndex}，已收起襟翼");
                        BeginInvoke(new Action(() =>
                        {
                            label19.Text = $"襟翼：0/{maxFlapsIndex}";
                            label19.ForeColor = System.Drawing.Color.FromArgb(153, 153, 153);
                        }));
                    }
                    else if (currentIndex >= maxFlapsIndex)
                    {
                        // 最大挡位
                        Speak($"襟翼 {currentIndex}/{maxFlapsIndex}，最大襟翼");
                        BeginInvoke(new Action(() =>
                        {
                            label19.Text = $"襟翼：{currentIndex}/{maxFlapsIndex}";
                            label19.ForeColor = System.Drawing.Color.FromArgb(241, 217, 81);
                        }));
                    }
                    else
                    {
                        Speak($"襟翼 {currentIndex}/{maxFlapsIndex}");
                        BeginInvoke(new Action(() =>
                        {
                            label19.Text = $"襟翼：{currentIndex}/{maxFlapsIndex}";
                            label19.ForeColor = System.Drawing.Color.FromArgb(241, 217, 81);
                        }));
                    }
                }

                lastFlapsIndex = currentFlapsIndex;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理襟翼错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理起落架状态变化，当起落架完全收起或完全放下时输出语音提示。
        /// GEAR TOTAL PCT EXTENDED: 0 = 完全收起, 1 = 完全放下
        /// </summary>
        private void ProcessLandingGear()
        {
            try
            {
                double gearPct = currentData.GEAR_TOTAL_PCT_EXTENDED;

                // 判断当前起落架状态: 0=完全收起, 1=完全放下, 2=过渡中
                int currentGearState;
                if (gearPct <= 0)
                {
                    currentGearState = 0; // 完全收起
                }
                else if (gearPct >= 0.01)
                {
                    currentGearState = 1; // 完全放下
                }
                else
                {
                    currentGearState = 2; // 过渡中
                }

                // 首次获取数据时，根据当前状态设置 label20 颜色
                if (lastGearState < 0)
                {
                    bool gearDeployed = currentGearState == 1;
                    BeginInvoke(new Action(() =>
                    {
                        label20.ForeColor = gearDeployed
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                // 检测状态变化（仅在完全收起或完全放下时播报）
                if (lastGearState >= 0 && lastGearState != currentGearState)
                {
                    if (currentGearState == 0)
                    {
                        Speak("已收起起落架");
                        BeginInvoke(new Action(() =>
                        {
                            label20.ForeColor = System.Drawing.Color.FromArgb(153, 153, 153);
                        }));
                    }
                    else if (currentGearState == 1)
                    {
                        Speak("已放下起落架");
                        BeginInvoke(new Action(() =>
                        {
                            label20.ForeColor = System.Drawing.Color.FromArgb(241, 217, 81);
                        }));
                    }
                }

                lastGearState = currentGearState;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理起落架错误: {ex.Message}");
            }
        }

    }
}
