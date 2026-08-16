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
    /// 主窗体类，用于实现 GPWS (Ground Proximity Warning System) 系统。
    /// 该类负责与 Microsoft Flight Simulator 连接，监控飞行数据，并提供语音警告。
    /// </summary>
    public partial class Form1 : Form
    {
        // === 1. Windows API 声明 ===
        private const int WM_HOTKEY = 0x0312;

        private const int WM_USER_SIMCONNECT = 0x0402;

        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 快捷键 ID 
        const int ID_HOTKEY_Z = 100;

        const int ID_HOTKEY_X = 101;

        const int ID_HOTKEY_C = 102;

        const int ID_HOTKEY_V = 104;

        // 全局快捷键当前是否已注册，用于避免重复注册/注销
        private bool hotkeysRegistered = false;

        private SimConnect simconnect = null;

        private SpeechSynthesizer speaker = new SpeechSynthesizer();

        private SimData currentData = new SimData();

        private HashSet<double> spokenThresholds = new HashSet<double>();

        private readonly double[] thresholds = { 2500, 1000, 500, 400, 300, 200, 100, 50, 40, 30, 20, 10 };

        private double lastParkingBrakeState = -1; // 跟踪前一个停机刹车状态

        private double lastSpoilerState = -1;      // 跟踪前一个扰流板状态

        private double lastAutopilotState = -1;     // 跟踪前一个自动驾驶状态

        private double lastFlapsIndex = -1;           // 跟踪前一个襟翼挡位

        private int lastGearState = -1;               // 跟踪前一个起落架状态 (0=收起, 1=放下, 2=过渡中)

        private double lastApAltLockState = -1;        // 跟踪前一个自动驾驶高度保持状态

        private double lastApHdgLockState = -1;        // 跟踪前一个自动驾驶航向保持状态

        private double lastApVsHoldState = -1;         // 跟踪前一个自动驾驶垂直速度保持状态

        private double lastApNavLockState = -1;        // 跟踪前一个自动驾驶导航保持状态

        private double lastApAprHoldState = -1;        // 跟踪前一个自动驾驶进近保持状态

        private double lastSimulationRate = -1;           // 跟踪前一个模拟速率

        private double lastAutoThrottleState = -1;       // 跟踪前一个自动油门状态

        private double lastSpoilersArmedState = -1;      // 跟踪前一个扰流板预位状态

        // === GPWS 智能状态跟踪 ===
        private double previousRadioHeight = -1;           // 上一帧的无线电高度

        private bool wasOnGround = true;                   // 上一帧是否在地面

        private double maxHeightSinceTakeoff = 0;          // 起飞后达到的最大高度

        private double minHeightSincePeak = double.MaxValue; // 从最高点下降后的最低高度

        private DateTime lastAltitudeCalloutTime = DateTime.MinValue;  // 上次高度提示时间

        private const int ALTITUDE_CALLOUT_COOLDOWN_MS = 800;  // 高度提示冷却时间（毫秒）

        private const double HEIGHT_RESET_THRESHOLD = 100;     // 爬升多少英尺后重置对应阈值

        // 地面滑行速度阈值
        private readonly int[] taxiSpeedThresholds = { 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160 };

        private HashSet<int> spokenTaxiSpeeds = new HashSet<int>();

        private double lastSpokenTaxiSpeed = -1;

        // 状态跟踪变量
        private bool isConnected = false;

        private bool hasReceivedData = false;

        private bool wasConnectedBefore = false;

        // 后台消息循环
        private Task messageLoopTask = null;

        private CancellationTokenSource messageLoopCts = null;

        private System.Windows.Forms.Timer reconnectTimer = null;

        private const int RECONNECT_INTERVAL = 5000; // 每5秒尝试重连

        /// <summary>
        /// 构造函数，初始化窗体组件并配置语音合成器。
        /// </summary>
        public Form1()
        {
            InitializeComponent();
            speaker.Rate = 6;
            speaker.SetOutputToDefaultAudioDevice();
        }

        /// <summary>
        /// 窗体加载事件处理器，根据 checkBox1 状态注册全局快捷键，初始化重连定时器，并尝试连接到模拟器。
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void Form1_Load(object sender, EventArgs e)
        {
            // 为 checkBox1 添加选中状态变化事件处理，用于动态注册/注销全局快捷键
            checkBox1.CheckedChanged += CheckBox1_CheckedChanged;

            // 为 button8 添加点击事件处理（开始/停止飞行轨迹记录）
            button8.Click += button8_Click;

            // 窗体加载时 checkBox1 已处于选中状态，事件不会因初始赋值而触发，因此需要主动注册一次
            if (checkBox1.Checked)
            {
                RegisterGlobalHotkeys();
            }

            // 加载可用的语音声音
            LoadAvailableVoices();

            // 为 listBox2 添加选择变化事件处理
            listBox2.SelectedIndexChanged += ListBox2_SelectedIndexChanged;

            // 初始化重连定时器
            reconnectTimer = new System.Windows.Forms.Timer();
            reconnectTimer.Interval = RECONNECT_INTERVAL;
            reconnectTimer.Tick += ReconnectTimer_Tick;
            reconnectTimer.Start();

            // 尝试连接
            TryConnectToSimulator();
        }

        /// <summary>
        /// 注册全局语音播报快捷键（Z / X / C / V）。
        /// 仅当快捷键尚未注册时才执行，避免重复注册导致失败。
        /// </summary>
        private void RegisterGlobalHotkeys()
        {
            // 已注册则直接返回，防止重复注册（同一快捷键重复注册会失败）
            if (hotkeysRegistered)
            {
                return;
            }

            // 依次注册四个快捷键
            bool zRegistered = RegisterHotKey(this.Handle, ID_HOTKEY_Z, MOD_NOREPEAT, (uint)Keys.Z);
            bool xRegistered = RegisterHotKey(this.Handle, ID_HOTKEY_X, MOD_NOREPEAT, (uint)Keys.X);
            bool cRegistered = RegisterHotKey(this.Handle, ID_HOTKEY_C, MOD_NOREPEAT, (uint)Keys.C);
            bool vRegistered = RegisterHotKey(this.Handle, ID_HOTKEY_V, MOD_NOREPEAT, (uint)Keys.V);

            // 全部注册成功，记录状态
            if (zRegistered && xRegistered && cRegistered && vRegistered)
            {
                hotkeysRegistered = true;
                System.Diagnostics.Debug.WriteLine("全局快捷键已注册（Z/X/C/V）");
                return;
            }

            // 部分注册失败：撤销已成功注册的快捷键，避免残留无效注册
            if (zRegistered) UnregisterHotKey(this.Handle, ID_HOTKEY_Z);
            if (xRegistered) UnregisterHotKey(this.Handle, ID_HOTKEY_X);
            if (cRegistered) UnregisterHotKey(this.Handle, ID_HOTKEY_C);
            if (vRegistered) UnregisterHotKey(this.Handle, ID_HOTKEY_V);

            hotkeysRegistered = false;
            MessageBox.Show("快捷键注册失败，请检查是否被其他程序占用。");
        }

        /// <summary>
        /// 注销全局语音播报快捷键（Z / X / C / V）。
        /// 仅当快捷键当前已注册时才执行注销。
        /// </summary>
        private void UnregisterGlobalHotkeys()
        {
            // 未注册则直接返回
            if (!hotkeysRegistered)
            {
                return;
            }

            UnregisterHotKey(this.Handle, ID_HOTKEY_Z);
            UnregisterHotKey(this.Handle, ID_HOTKEY_X);
            UnregisterHotKey(this.Handle, ID_HOTKEY_C);
            UnregisterHotKey(this.Handle, ID_HOTKEY_V);

            hotkeysRegistered = false;
            System.Diagnostics.Debug.WriteLine("全局快捷键已注销（Z/X/C/V）");
        }

        /// <summary>
        /// checkBox1 选中状态变化事件处理器。
        /// 选中时注册全局快捷键，取消选中时注销全局快捷键。
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void CheckBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                RegisterGlobalHotkeys();
            }
            else
            {
                UnregisterGlobalHotkeys();
            }
        }

        /// <summary>
        /// 加载所有可用的语音声音并显示在 listBox2 中
        /// </summary>
        private void LoadAvailableVoices()
        {
            try
            {
                listBox2.Items.Clear();

                // 获取所有可用的语音声音
                var voices = speaker.GetInstalledVoices();

                if (voices.Count == 0)
                {
                    MessageBox.Show("系统中未找到可用的语音声音。");
                    return;
                }

                // 将所有声音名称添加到 listBox2
                foreach (var voice in voices)
                {
                    listBox2.Items.Add(voice.VoiceInfo.Name);
                }

                // 默认选中第一个声音
                if (listBox2.Items.Count > 0)
                {
                    listBox2.SelectedIndex = 0;
                    SetVoice(speaker.GetInstalledVoices()[0].VoiceInfo.Name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载语音声音错误: {ex.Message}");
                MessageBox.Show($"加载语音声音时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// listBox2 选择项改变事件处理器
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void ListBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (listBox2.SelectedIndex >= 0)
                {
                    string selectedVoiceName = listBox2.SelectedItem.ToString();
                    SetVoice(selectedVoiceName);
                    // 使用选中的语音输出语音名称
                    Speak(selectedVoiceName);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"选择语音错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置语音合成器使用的声音
        /// </summary>
        /// <param name="voiceName">声音名称</param>
        private void SetVoice(string voiceName)
        {
            try
            {
                speaker.SelectVoice(voiceName);
                System.Diagnostics.Debug.WriteLine($"语音已切换为: {voiceName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置语音错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理 Windows 消息，包括全局快捷键和 SimConnect 通信。
        /// </summary>
        /// <param name="m">消息引用</param>
        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    if (id == ID_HOTKEY_Z) Speak(Math.Round(currentData.AIRSPEED_INDICATED).ToString());
                    if (id == ID_HOTKEY_X) Speak(Math.Round(currentData.INDICATED_ALTITUDE).ToString());
                    if (id == ID_HOTKEY_C) Speak(Math.Round(currentData.RADIO_HEIGHT).ToString());
                    if (id == ID_HOTKEY_V) 
                    {
                        int heading = (int)Math.Round(currentData.PLANE_HEADING_DEGREES_MAGNETIC);
                        if (heading == 0) heading = 360;
                        Speak(heading.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WndProc 错误: {ex.Message}");
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// 异步语音输出指定的文本。
        /// </summary>
        /// <param name="text">要语音输出的文本</param>
        private void Speak(string text)
        {
            try
            {
                speaker.SpeakAsyncCancelAll();
                speaker.SpeakAsync(text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"语音输出错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 窗体关闭事件处理器，清理所有资源，包括定时器、快捷键和连接。
        /// </summary>
        /// <param name="e">关闭事件参数</param>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (reconnectTimer != null)
                {
                    reconnectTimer.Stop();
                    reconnectTimer.Dispose();
                }

                // 注销全局快捷键（若已注册）
                UnregisterGlobalHotkeys();

                // 若正在记录飞行轨迹，先停止记录并保存 KML 文件，避免轨迹丢失
                SaveFlightTrackIfRecording();

                DisconnectFromSimulator();

                if (speaker != null)
                {
                    speaker.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"窗体关闭错误: {ex.Message}");
            }

            base.OnFormClosing(e);
        }

        /// <summary>
        /// 重启应用程序
        /// </summary>
        private void RestartApplication()
        {
            try
            {
                // 若正在记录飞行轨迹，先停止记录并保存 KML 文件，再重启程序
                SaveFlightTrackIfRecording();

                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = System.Reflection.Assembly.GetExecutingAssembly().Location;
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
                
                // 关闭当前应用程序
                Application.Exit();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"重启应用程序错误: {ex.Message}");
                MessageBox.Show($"重启应用程序失败: {ex.Message}");
            }
        }

    }
}
