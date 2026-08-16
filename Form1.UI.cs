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
    /// Form1.UI.cs - Form1 的部分类定义。
    /// </summary>
    public partial class Form1
    {
        /// <summary>
        /// 更新连接状态显示，根据连接和数据接收状态更新 UI 标签。
        /// </summary>
        private void UpdateConnectionStatus()
        {
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(UpdateConnectionStatus));
                    return;
                }

                if (!isConnected)
                {
                    // 已断开连接
                    label1.Text = "已断开连接MSFS";
                    label1.ForeColor = System.Drawing.Color.Red;
                }
                else if (!hasReceivedData)
                {
                    // 已连接但无法获取数据
                    label1.Text = "已连接MSFS，但无法获取数据！";
                    label1.ForeColor = System.Drawing.Color.Red;
                }
                else
                {
                    // 已成功连接
                    label1.Text = "已成功连接MSFS";
                    label1.ForeColor = System.Drawing.Color.Green;
                }

                UpdateWeatherDisplay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新状态错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新天气显示，将真风向按当前磁偏角转换为可直接输入飞机的磁风向。
        /// </summary>
        private void UpdateWeatherDisplay()
        {
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(UpdateWeatherDisplay));
                    return;
                }

                if (!isConnected || !hasReceivedData)
                {
                    label2.Text = "风向：---";
                    label9.Text = "风速：--";
                    label10.Text = "气温：--℃";
                    return;
                }

                int magneticWindDirection = ConvertTrueToMagneticHeading(currentData.AMBIENT_WIND_DIRECTION, currentData.MAGVAR);
                int windSpeed = RoundToWholeNumber(currentData.AMBIENT_WIND_VELOCITY);
                int temperature = RoundToWholeNumber(currentData.AMBIENT_TEMPERATURE);

                label2.Text = $"风向：{magneticWindDirection:000}";
                label9.Text = $"风速：{windSpeed}";
                label10.Text = $"气温：{temperature}℃";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新天气显示错误: {ex.Message}");
            }
        }

    }
}
