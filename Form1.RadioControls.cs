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
    /// Form1.RadioControls.cs - Form1 的部分类定义。
    /// </summary>
    public partial class Form1
    {
        private static uint ToBcd16(int value)
        {
            uint bcd = 0;
            int shift = 0;
            int working = value;
            while (working > 0 && shift < 16)
            {
                bcd |= (uint)(working % 10) << shift;
                working /= 10;
                shift += 4;
            }
            return bcd;
        }

        private static uint ConvertNavMhzToBcd16(double navMhz)
        {
            // NAV1_RADIO_SET / NAV1_STBY_SET 的 BCD16 格式通常使用去掉百位后的 4 位数字：
            // 109.10 -> 0910, 117.95 -> 1795
            int whole = (int)Math.Floor(navMhz);
            int frac = (int)Math.Round((navMhz - whole) * 100.0, MidpointRounding.AwayFromZero);
            int fourDigits = ((whole - 100) * 100) + frac;
            if (fourDigits < 0) fourDigits = 0;
            if (fourDigits > 9999) fourDigits = 9999;
            return ToBcd16(fourDigits);
        }

        private static int ConvertTrueToMagneticHeading(double trueHeading, double magVar)
        {
            return NormalizeHeading(trueHeading + magVar);
        }

        private static int NormalizeHeading(double heading)
        {
            if (double.IsNaN(heading) || double.IsInfinity(heading))
            {
                return 360;
            }

            double normalized = heading % 360.0;
            if (normalized < 0)
            {
                normalized += 360.0;
            }

            int rounded = (int)Math.Round(normalized, MidpointRounding.AwayFromZero);
            if (rounded <= 0)
            {
                return 360;
            }

            if (rounded > 360)
            {
                rounded -= 360;
            }

            return rounded;
        }

        private static int RoundToWholeNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (!isConnected || simconnect == null)
            {
                return;
            }

            if (!int.TryParse(textBox1.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int altitudeInput))
            {
                return;
            }

            // 常见自动驾驶预选高度范围：0~100000 英尺
            if (altitudeInput < 0 || altitudeInput > 100000)
            {
                return;
            }

            try
            {
                var data = new ApAltitudeSetData
                {
                    ALTITUDE = altitudeInput
                };

                simconnect.SetDataOnSimObject(DEFINITIONS.ApAltitudeSetDef, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, data);
                Speak($"目标高度: {altitudeInput}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置目标高度错误: {ex.Message}");
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (!isConnected || simconnect == null)
            {
                return;
            }

            if (!int.TryParse(textBox2.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int headingInput))
            {
                return;
            }

            if (headingInput < 1 || headingInput > 360)
            {
                return;
            }

            // UI/仪表输入 1~360；若底层按 0~359 处理，360 需要映射为 0
            int apiHeading = (headingInput == 360) ? 0 : headingInput;

            try
            {
                var data = new ApHeadingSetData
                {
                    HEADING = apiHeading
                };

                simconnect.SetDataOnSimObject(DEFINITIONS.ApHeadingSetDef, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, data);
                Speak($"目标航向: {headingInput}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置目标航向错误: {ex.Message}");
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            if (!isConnected || simconnect == null)
            {
                return;
            }

            string inputText = textBox4.Text?.Trim();
            if (string.IsNullOrWhiteSpace(inputText))
            {
                return;
            }

            if (!double.TryParse(inputText, NumberStyles.Float, CultureInfo.InvariantCulture, out double navInputMhz) &&
                !double.TryParse(inputText, NumberStyles.Float, CultureInfo.CurrentCulture, out navInputMhz))
            {
                return;
            }

            // NAV 范围：108.00 ~ 117.95 MHz
            if (navInputMhz < 108.00 || navInputMhz > 117.95)
            {
                return;
            }

            // NAV 频率通常按 50kHz（0.05 MHz）步进，自动转换到可用值
            double convertedNavMhz = Math.Round(navInputMhz * 20.0, MidpointRounding.AwayFromZero) / 20.0;
            if (convertedNavMhz < 108.00 || convertedNavMhz > 117.95)
            {
                return;
            }

            try
            {
                uint navHz = (uint)Math.Round(convertedNavMhz * 1_000_000.0, MidpointRounding.AwayFromZero);
                uint navBcd16 = ConvertNavMhzToBcd16(convertedNavMhz);

                // 路径 1：直接设置激活频率（Hz）
                SendSimEvent(EVENTS.Nav1RadioSetHz, navHz);
                // 路径 2：写入备用（Hz）并交换到激活
                SendSimEvent(EVENTS.Nav1StbySetHz, navHz);
                SendSimEvent(EVENTS.Nav1RadioSwap, 0);
                // 路径 3：BCD16 兜底（兼容部分机型）
                SendSimEvent(EVENTS.Nav1RadioSetBcd16, navBcd16);
                SendSimEvent(EVENTS.Nav1StbySetBcd16, navBcd16);
                SendSimEvent(EVENTS.Nav1RadioSwap, 0);

                Speak($"NAV1频率: {inputText}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置 NAV1 频率错误: {ex.Message}");
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (!isConnected || simconnect == null)
            {
                return;
            }

            if (!int.TryParse(textBox3.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int courseInput))
            {
                return;
            }

            if (courseInput < 1 || courseInput > 360)
            {
                return;
            }

            // 仪表输入范围是 1~360；VOR1_SET 事件通常使用 0~359，360 映射为 0
            uint apiCourse = (uint)((courseInput == 360) ? 0 : courseInput);

            try
            {
                SendSimEvent(EVENTS.Vor1Set, apiCourse);
                Speak($"航向道: {courseInput}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置航向道错误: {ex.Message}");
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            if (!isConnected || simconnect == null)
            {
                return;
            }

            string inputText = textBox5.Text?.Trim();
            if (string.IsNullOrWhiteSpace(inputText))
            {
                return;
            }

            // 频率格式要求：类似 124.850（允许用逗号作为小数点）
            string normalizedInput = inputText.Replace(',', '.');
            if (!Regex.IsMatch(normalizedInput, @"^\d{3}\.\d{3}$"))
            {
                return;
            }

            if (!double.TryParse(normalizedInput, NumberStyles.Float, CultureInfo.InvariantCulture, out double comInputMhz))
            {
                return;
            }

            // 航空通信常用 COM 频率范围：118.000 ~ 136.975 MHz
            if (comInputMhz < 118.000 || comInputMhz > 136.975)
            {
                return;
            }

            // COM 频率转为 Hz；并按 5kHz 网格归一化（兼容 25kHz / 8.33kHz 机型）
            uint comHz = (uint)Math.Round(comInputMhz * 1_000_000.0, MidpointRounding.AwayFromZero);
            comHz = (uint)(Math.Round(comHz / 5000.0, MidpointRounding.AwayFromZero) * 5000.0);
            if (comHz < 118_000_000 || comHz > 136_975_000)
            {
                return;
            }

            try
            {
                // 路径 1：直接设置 COM1 激活频率（Hz）
                SendSimEvent(EVENTS.Com1RadioSetHz, comHz);
                // 路径 2：写入备用（Hz）并交换到激活
                SendSimEvent(EVENTS.Com1StbyRadioSetHz, comHz);
                SendSimEvent(EVENTS.Com1RadioSwap, 0);

                Speak($"COM1频率: {(comHz / 1_000_000.0).ToString("F3", CultureInfo.InvariantCulture)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置 COM1 频率错误: {ex.Message}");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            RestartApplication();
        }

    }
}
