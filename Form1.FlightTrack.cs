using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace FSCopilot
{
    /// <summary>
    /// Form1.FlightTrack.cs - Form1 的部分类定义。
    /// 飞行轨迹记录功能：每隔 5 秒采样一次飞机经纬度与海拔高度，
    /// 停止记录时生成 KML 文件（Google Earth 格式，高度单位为米）。
    /// </summary>
    public partial class Form1
    {
        // === 飞行轨迹记录 ===
        // 轨迹采样间隔（毫秒）：每 5 秒获取一次飞机位置
        private const int TRACK_SAMPLE_INTERVAL_MS = 5000;

        // 轨迹采样定时器
        private System.Windows.Forms.Timer trackTimer = null;

        // 已记录的轨迹点（格式：经度,纬度,高度(米)），停止记录后写入 KML 文件
        private List<string> trackPoints = new List<string>();

        // 是否正在记录飞行轨迹
        private bool isRecordingTrack = false;

        // 是否已写入过轨迹点（用于判断首个采样点）
        private bool hasTrackData = false;

        // 上次写入的轨迹点（用于去重，与本次完全相同则丢弃）
        private string lastTrackPoint = null;

        /// <summary>
        /// button8 点击事件处理器：开始或停止飞行轨迹记录。
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void button8_Click(object sender, EventArgs e)
        {
            if (isRecordingTrack)
            {
                // 正在记录：停止并保存
                StopFlightTrackRecording();
            }
            else
            {
                // 未在记录：开始记录
                StartFlightTrackRecording();
            }
        }

        /// <summary>
        /// 开始记录飞行轨迹：立即采样第一个点，然后每 5 秒采样一次。
        /// </summary>
        private void StartFlightTrackRecording()
        {
            // 已在记录中则忽略重复点击
            if (isRecordingTrack)
            {
                return;
            }

            // 清空上一次记录的数据
            trackPoints.Clear();
            hasTrackData = false;
            lastTrackPoint = null;

            // 立即采样第一个点，避免刚开始记录时还要等待 5 秒
            SampleFlightPosition();

            // 创建并启动采样定时器
            trackTimer = new System.Windows.Forms.Timer();
            trackTimer.Interval = TRACK_SAMPLE_INTERVAL_MS;
            trackTimer.Tick += TrackTimer_Tick;
            trackTimer.Start();

            isRecordingTrack = true;
            button8.Text = "停止记录";
            System.Diagnostics.Debug.WriteLine("开始记录飞行轨迹");
        }

        /// <summary>
        /// 停止记录飞行轨迹并保存 KML 文件。
        /// </summary>
        private void StopFlightTrackRecording()
        {
            // 未在记录则直接返回
            if (!isRecordingTrack)
            {
                return;
            }

            // 停止并释放采样定时器
            if (trackTimer != null)
            {
                trackTimer.Stop();
                trackTimer.Tick -= TrackTimer_Tick;
                trackTimer.Dispose();
                trackTimer = null;
            }

            isRecordingTrack = false;
            button8.Text = "轨迹记录";

            // 将记录的轨迹点保存为 KML 文件
            SaveFlightTrackToKml();
        }

        /// <summary>
        /// 若正在记录飞行轨迹，则先停止记录并保存 KML 文件。
        /// 用于关闭窗体、重启程序前的清理，确保轨迹数据不丢失。
        /// </summary>
        private void SaveFlightTrackIfRecording()
        {
            if (isRecordingTrack)
            {
                StopFlightTrackRecording();
            }
        }

        /// <summary>
        /// 采样定时器事件处理器：每隔 5 秒获取一次飞机位置。
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void TrackTimer_Tick(object sender, EventArgs e)
        {
            SampleFlightPosition();
        }

        /// <summary>
        /// 获取当前飞机位置并写入轨迹点。
        /// 仅在与模拟器连接且已收到数据时采样，避免记录无效的 (0,0,0) 点；
        /// 若与上次写入的数据完全相同，则丢弃该点。
        /// </summary>
        private void SampleFlightPosition()
        {
            // 未连接或尚未收到数据时不采样
            if (!isConnected || !hasReceivedData)
            {
                return;
            }

            // 格式化当前飞机位置：经度,纬度,高度(米)
            string point = FormatTrackPoint(
                currentData.PLANE_LONGITUDE,
                currentData.PLANE_LATITUDE,
                currentData.PLANE_ALTITUDE);

            // 与上次写入的轨迹点完全相同则丢弃，不写入文件
            if (hasTrackData && point == lastTrackPoint)
            {
                return;
            }

            trackPoints.Add(point);
            lastTrackPoint = point;
            hasTrackData = true;
            System.Diagnostics.Debug.WriteLine($"记录轨迹点: {point}");
        }

        /// <summary>
        /// 将经纬度和海拔高度格式化为 KML 坐标点字符串。
        /// KML 坐标顺序固定为：经度,纬度,高度(米)；
        /// 使用 InvariantCulture 保证小数点和精度在任意区域设置下一致。
        /// </summary>
        /// <param name="longitude">经度（度）</param>
        /// <param name="latitude">纬度（度）</param>
        /// <param name="altitude">海拔高度（米）</param>
        /// <returns>KML 坐标点字符串</returns>
        private static string FormatTrackPoint(double longitude, double latitude, double altitude)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:F6},{1:F6},{2:F2}",
                longitude,
                latitude,
                altitude);
        }

        /// <summary>
        /// 将记录的轨迹点保存为 KML 文件。
        /// 文件生成在程序所在目录，文件名使用当前日期时间（如 2026-08-16-18-25-30.kml）。
        /// KML 标准强制要求高度单位为米，因此直接使用 SimConnect 的 PLANE ALTITUDE（米）。
        /// </summary>
        private void SaveFlightTrackToKml()
        {
            try
            {
                // 没有记录到有效轨迹点时不生成空文件
                if (trackPoints.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("未记录到有效轨迹点，不生成 KML 文件");
                    return;
                }

                // 文件名示例：2026-08-16-18-25-30.kml，对应 2026年08月16日 18:25:30
                string fileName = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss") + ".kml";
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

                // 按固定模板拼接 KML 内容
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                sb.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
                sb.AppendLine("  <Document>");
                sb.AppendLine("    <name>My MSFS Flight</name>");
                sb.AppendLine("    <description>VFR Flight Track</description>");
                sb.AppendLine("    ");
                sb.AppendLine("    <Placemark>");
                sb.AppendLine("      <name>Flight Path</name>");
                sb.AppendLine("      <Style>");
                sb.AppendLine("        <LineStyle>");
                sb.AppendLine("          <color>ff0000ff</color> <!-- 红色线条 (格式是 aabbggrr) -->");
                sb.AppendLine("          <width>4</width>        <!-- 线条宽度 -->");
                sb.AppendLine("        </LineStyle>");
                sb.AppendLine("      </Style>");
                sb.AppendLine("      <LineString>");
                sb.AppendLine("        <extrude>1</extrude> <!-- 设为 1 会在轨迹和地面之间画一道阴影幕墙，非常有立体感 -->");
                sb.AppendLine("        <tessellate>1</tessellate>");
                sb.AppendLine("        <altitudeMode>absolute</altitudeMode> <!-- 必须是 absolute -->");
                sb.AppendLine("        <coordinates>");
                sb.AppendLine("          <!-- 格式: 经度,纬度,高度(米) -->");

                // 追加所有轨迹点
                foreach (string point in trackPoints)
                {
                    sb.AppendLine("          " + point);
                }

                sb.AppendLine("        </coordinates>");
                sb.AppendLine("      </LineString>");
                sb.AppendLine("    </Placemark>");
                sb.AppendLine("  </Document>");
                sb.AppendLine("</kml>");

                // 以 UTF-8（无 BOM）写入，与 XML 声明保持一致
                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
                System.Diagnostics.Debug.WriteLine($"飞行轨迹已保存: {filePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存飞行轨迹错误: {ex.Message}");
                MessageBox.Show($"保存飞行轨迹失败: {ex.Message}");
            }
        }
    }
}
