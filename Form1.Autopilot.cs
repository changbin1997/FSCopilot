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
    /// Form1.Autopilot.cs - Form1 的部分类定义。
    /// </summary>
    public partial class Form1
    {
        /// <summary>
        /// 处理自动驾驶状态变化，当自动驾驶开启或关闭时输出语音提示。
        /// </summary>
        private void ProcessAutopilot()
        {
            try
            {
                double currentAutopilotState = currentData.AUTOPILOT_MASTER;

                // 首次获取数据时，根据当前状态设置 label11 颜色
                if (lastAutopilotState < 0)
                {
                    BeginInvoke(new Action(() =>
                    {
                        label11.ForeColor = currentAutopilotState > 0.5
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                // 检测状态变化
                if (lastAutopilotState >= 0 && lastAutopilotState != currentAutopilotState)
                {
                    if (currentAutopilotState > 0.5)
                    {
                        Speak("已接通自动驾驶");
                        BeginInvoke(new Action(() =>
                        {
                            label11.ForeColor = System.Drawing.Color.FromArgb(241, 217, 81);
                        }));
                    }
                    else
                    {
                        Speak("已断开自动驾驶");
                        BeginInvoke(new Action(() =>
                        {
                            label11.ForeColor = System.Drawing.Color.FromArgb(153, 153, 153);
                        }));
                    }
                }

                lastAutopilotState = currentAutopilotState;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理自动驾驶错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理自动驾驶高度保持 (ALT) 状态变化，根据状态修改 label12 文字颜色。
        /// </summary>
        private void ProcessAutopilotAltHold()
        {
            try
            {
                double currentState = currentData.AUTOPILOT_ALTITUDE_LOCK;

                if (lastApAltLockState < 0)
                {
                    BeginInvoke(new Action(() =>
                    {
                        label12.ForeColor = currentState > 0.5
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                if (lastApAltLockState >= 0 && lastApAltLockState != currentState)
                {
                    bool isOn = currentState > 0.5;
                    BeginInvoke(new Action(() =>
                    {
                        label12.ForeColor = isOn
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                lastApAltLockState = currentState;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理自动驾驶高度保持错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理自动驾驶航向保持 (HDG) 状态变化，根据状态修改 label13 文字颜色。
        /// </summary>
        private void ProcessAutopilotHdg()
        {
            try
            {
                double currentState = currentData.AUTOPILOT_HEADING_LOCK;

                if (lastApHdgLockState < 0)
                {
                    BeginInvoke(new Action(() =>
                    {
                        label13.ForeColor = currentState > 0.5
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                if (lastApHdgLockState >= 0 && lastApHdgLockState != currentState)
                {
                    bool isOn = currentState > 0.5;
                    BeginInvoke(new Action(() =>
                    {
                        label13.ForeColor = isOn
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                lastApHdgLockState = currentState;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理自动驾驶航向保持错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理自动驾驶垂直速度保持 (VS) 状态变化，根据状态修改 label14 文字颜色。
        /// </summary>
        private void ProcessAutopilotVs()
        {
            try
            {
                double currentState = currentData.AUTOPILOT_VERTICAL_HOLD;

                if (lastApVsHoldState < 0)
                {
                    BeginInvoke(new Action(() =>
                    {
                        label14.ForeColor = currentState > 0.5
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                if (lastApVsHoldState >= 0 && lastApVsHoldState != currentState)
                {
                    bool isOn = currentState > 0.5;
                    BeginInvoke(new Action(() =>
                    {
                        label14.ForeColor = isOn
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                lastApVsHoldState = currentState;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理自动驾驶垂直速度保持错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理自动驾驶导航保持 (NAV) 状态变化，根据状态修改 label15 文字颜色。
        /// </summary>
        private void ProcessAutopilotNav()
        {
            try
            {
                double currentState = currentData.AUTOPILOT_NAV1_LOCK;

                if (lastApNavLockState < 0)
                {
                    BeginInvoke(new Action(() =>
                    {
                        label15.ForeColor = currentState > 0.5
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                if (lastApNavLockState >= 0 && lastApNavLockState != currentState)
                {
                    bool isOn = currentState > 0.5;
                    BeginInvoke(new Action(() =>
                    {
                        label15.ForeColor = isOn
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                lastApNavLockState = currentState;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理自动驾驶导航保持错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理自动驾驶进近保持 (APR) 状态变化，根据状态修改 label16 文字颜色。
        /// </summary>
        private void ProcessAutopilotApr()
        {
            try
            {
                double currentState = currentData.AUTOPILOT_APPROACH_HOLD;

                if (lastApAprHoldState < 0)
                {
                    BeginInvoke(new Action(() =>
                    {
                        label16.ForeColor = currentState > 0.5
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                if (lastApAprHoldState >= 0 && lastApAprHoldState != currentState)
                {
                    bool isOn = currentState > 0.5;
                    BeginInvoke(new Action(() =>
                    {
                        label16.ForeColor = isOn
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                lastApAprHoldState = currentState;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理自动驾驶进近保持错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理模拟速率变化，更新 label23 显示当前模拟速率。
        /// </summary>
        private void ProcessSimulationRate()
        {
            try
            {
                double currentRate = currentData.SIMULATION_RATE;

                // 首次获取数据时，设置 label23 的文本
                if (lastSimulationRate < 0)
                {
                    double rate = currentRate;
                    BeginInvoke(new Action(() =>
                    {
                        label23.Text = $"速率：{rate}x";
                    }));
                }

                // 检测速率变化
                if (lastSimulationRate >= 0 && Math.Abs(lastSimulationRate - currentRate) > 0.001)
                {
                    double rate = currentRate;
                    BeginInvoke(new Action(() =>
                    {
                        label23.Text = $"速率：{rate}x";
                    }));
                }

                lastSimulationRate = currentRate;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理模拟速率错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理自动油门状态变化，根据状态修改 label17 文字颜色并输出语音提示。
        /// </summary>
        private void ProcessAutoThrottle()
        {
            try
            {
                double currentState = currentData.AUTOPILOT_THROTTLE_ARM;

                // 首次获取数据时，根据当前状态设置 label17 颜色
                if (lastAutoThrottleState < 0)
                {
                    bool isOn = currentState > 0.5;
                    BeginInvoke(new Action(() =>
                    {
                        label17.ForeColor = isOn
                            ? System.Drawing.Color.FromArgb(241, 217, 81)
                            : System.Drawing.Color.FromArgb(153, 153, 153);
                    }));
                }

                // 检测状态变化
                if (lastAutoThrottleState >= 0 && lastAutoThrottleState != currentState)
                {
                    bool isOn = currentState > 0.5;
                    if (isOn)
                    {
                        Speak("已开启自动油门");
                        BeginInvoke(new Action(() =>
                        {
                            label17.ForeColor = System.Drawing.Color.FromArgb(241, 217, 81);
                        }));
                    }
                    else
                    {
                        Speak("已关闭自动油门");
                        BeginInvoke(new Action(() =>
                        {
                            label17.ForeColor = System.Drawing.Color.FromArgb(153, 153, 153);
                        }));
                    }
                }

                lastAutoThrottleState = currentState;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理自动油门错误: {ex.Message}");
            }
        }

    }
}
