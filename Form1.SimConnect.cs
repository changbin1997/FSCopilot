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
    /// Form1.SimConnect.cs - Form1 的部分类定义。
    /// </summary>
    public partial class Form1
    {
        // === 2. SimConnect 数据结构 ===
        /// <summary>
        /// SimConnect 数据结构，包含从模拟器接收的飞行数据。
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        struct SimData
        {
            public double RADIO_HEIGHT;        // 无线电高度 (AGL)
            public double INDICATED_ALTITUDE;  // 指示高度 (MSL)
            public double VERTICAL_SPEED;      // 垂直速度 (FPM)
            public double AIRSPEED_INDICATED;  // 指示空速 (KTS)
            public double SIM_ON_GROUND;       // 飞机是否在地面 (1=是, 0=否)
            public double PARKING_BRAKE;       // 停机刹车 (1=已设置, 0=已解除)
            public double SPOILER_AVAILABLE;   // 飞机是否有扰流板 (1=有, 0=无)
            public double SPOILERS_HANDLE_POSITION;  // 扰流板手柄位置 (百分比)
            public double AUTOPILOT_MASTER;          // 自动驾驶主开关 (1=开启, 0=关闭)
            public double FLAPS_HANDLE_INDEX;        // 襟翼手柄当前挡位索引
            public double FLAPS_NUM_HANDLE_POSITIONS; // 襟翼手柄总挡位数量
            public double PLANE_HEADING_DEGREES_MAGNETIC; // 飞机磁航向 (度)
            public double GEAR_TOTAL_PCT_EXTENDED;   // 起落架总伸展百分比 (0=完全收起, 1=完全放下)
            public double AMBIENT_WIND_DIRECTION;    // 环境风向，基于真北 (度)
            public double AMBIENT_WIND_VELOCITY;     // 环境风速 (KT)
            public double AMBIENT_TEMPERATURE;       // 环境温度 (℃)
            public double MAGVAR;                    // 磁偏角，东偏为负，西偏为正 (度)
            public double AUTOPILOT_ALTITUDE_LOCK;   // 自动驾驶高度保持 (1=开启, 0=关闭)
            public double AUTOPILOT_HEADING_LOCK;    // 自动驾驶航向保持 (1=开启, 0=关闭)
            public double AUTOPILOT_VERTICAL_HOLD;   // 自动驾驶垂直速度保持 (1=开启, 0=关闭)
            public double AUTOPILOT_NAV1_LOCK;       // 自动驾驶导航保持 (1=开启, 0=关闭)
            public double AUTOPILOT_APPROACH_HOLD;   // 自动驾驶进近保持 (1=开启, 0=关闭)
            public double SIMULATION_RATE;            // 模拟速率 (1=正常, 2=2倍速, 等)
            public double AUTOPILOT_THROTTLE_ARM;     // 自动油门 (1=开启, 0=关闭)
            public double SPOILERS_ARMED;             // 扰流板预位 (1=已预位, 0=未预位)
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct ApAltitudeSetData
        {
            public double ALTITUDE;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct ApHeadingSetData
        {
            public double HEADING;
        }

        /// <summary>
        /// 数据定义枚举，用于定义 SimConnect 数据结构。
        /// </summary>
        enum DEFINITIONS
        {
            SimDataDef,
            ApAltitudeSetDef,
            ApHeadingSetDef
        };

        /// <summary>
        /// 数据请求枚举，用于请求 SimConnect 数据。
        /// </summary>
        enum REQUESTS { SimDataReq };

        /// <summary>
        /// 客户端事件枚举，用于向模拟器发送控制事件。
        /// </summary>
        enum EVENTS
        {
            Nav1RadioSetHz,
            Nav1StbySetHz,
            Nav1RadioSetBcd16,
            Nav1StbySetBcd16,
            Nav1RadioSwap,
            Vor1Set,
            Com1RadioSetHz,
            Com1StbyRadioSetHz,
            Com1RadioSwap
        };

        enum GROUPS
        {
            Highest = 1
        };

        /// <summary>
        /// 后台消息循环：持续处理 SimConnect 消息（高效方案）
        /// </summary>
        private async Task MessageLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && isConnected && simconnect != null)
                {
                    try
                    {
                        // 每次检查是否有新消息，不主动请求数据
                        // 数据通过 OnRecvSimobjectData 事件驱动到达
                        simconnect.ReceiveMessage();
                        
                        // 减少 CPU 占用，保持足够的消息处理频率
                        await Task.Delay(10, ct);
                    }
                    catch (COMException)
                    {
                        // SimConnect 连接已断开
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"消息循环错误: {ex.Message}");
                        break;
                    }
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("消息循环已结束");
            }
        }

        /// <summary>
        /// 重连定时器事件处理器，每隔指定时间尝试重新连接到模拟器。
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void ReconnectTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!isConnected)
                {
                    TryConnectToSimulator();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"重连定时器错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 尝试连接到模拟器
        /// </summary>
        private void TryConnectToSimulator()
        {
            try
            {
                if (simconnect != null)
                {
                    simconnect.Dispose();
                    simconnect = null;
                }

                // 连接到 SimConnect
                simconnect = new SimConnect("GPWS_Final", this.Handle, WM_USER_SIMCONNECT, null, 0);

                // 定义数据 - 使用 PERIOD 自动更新而不是主动请求
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "RADIO HEIGHT", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "INDICATED ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "VERTICAL SPEED", "feet/minute", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "AIRSPEED INDICATED", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "SIM ON GROUND", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "BRAKE PARKING INDICATOR", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "SPOILER AVAILABLE", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "SPOILERS HANDLE POSITION", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "AUTOPILOT MASTER", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "FLAPS HANDLE INDEX", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "FLAPS NUM HANDLE POSITIONS", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "PLANE HEADING DEGREES MAGNETIC", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "GEAR TOTAL PCT EXTENDED", "percent over 100", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "AMBIENT WIND DIRECTION", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "AMBIENT WIND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "AMBIENT TEMPERATURE", "celsius", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "MAGVAR", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "AUTOPILOT ALTITUDE LOCK", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "AUTOPILOT HEADING LOCK", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "AUTOPILOT VERTICAL HOLD", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "AUTOPILOT NAV1 LOCK", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "AUTOPILOT APPROACH HOLD", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "SIMULATION RATE", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "AUTOPILOT THROTTLE ARM", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.SimDataDef, "SPOILERS ARMED", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                simconnect.RegisterDataDefineStruct<SimData>(DEFINITIONS.SimDataDef);
                // 写入自动驾驶目标高度（feet）
                simconnect.AddToDataDefinition(DEFINITIONS.ApAltitudeSetDef, "AUTOPILOT ALTITUDE LOCK VAR", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.RegisterDataDefineStruct<ApAltitudeSetData>(DEFINITIONS.ApAltitudeSetDef);

                // 写入自动驾驶目标航向（degrees）
                simconnect.AddToDataDefinition(DEFINITIONS.ApHeadingSetDef, "AUTOPILOT HEADING LOCK DIR", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.RegisterDataDefineStruct<ApHeadingSetData>(DEFINITIONS.ApHeadingSetDef);

                // NAV1 事件映射：Hz 与 BCD16 双路径，提升不同机模兼容性
                simconnect.MapClientEventToSimEvent(EVENTS.Nav1RadioSetHz, "NAV1_RADIO_SET_HZ");
                simconnect.MapClientEventToSimEvent(EVENTS.Nav1StbySetHz, "NAV1_STBY_SET_HZ");
                simconnect.MapClientEventToSimEvent(EVENTS.Nav1RadioSetBcd16, "NAV1_RADIO_SET");
                simconnect.MapClientEventToSimEvent(EVENTS.Nav1StbySetBcd16, "NAV1_STBY_SET");
                simconnect.MapClientEventToSimEvent(EVENTS.Nav1RadioSwap, "NAV1_RADIO_SWAP");
                simconnect.MapClientEventToSimEvent(EVENTS.Vor1Set, "VOR1_SET");
                simconnect.MapClientEventToSimEvent(EVENTS.Com1RadioSetHz, "COM_RADIO_SET_HZ");
                simconnect.MapClientEventToSimEvent(EVENTS.Com1StbyRadioSetHz, "COM_STBY_RADIO_SET_HZ");
                simconnect.MapClientEventToSimEvent(EVENTS.Com1RadioSwap, "COM1_RADIO_SWAP");

                // 注册事件处理器
                simconnect.OnRecvSimobjectData += Simconnect_OnRecvSimobjectData;
                simconnect.OnRecvQuit += Simconnect_OnRecvQuit;
                simconnect.OnRecvException += Simconnect_OnRecvException;

                isConnected = true;
                hasReceivedData = false;
                lastParkingBrakeState = -1; // 重置停机刹车状态跟踪变量
                lastSpoilerState = -1;      // 重置扰流板状态跟踪变量
                lastAutopilotState = -1;    // 重置自动驾驶状态跟踪变量
                lastFlapsIndex = -1;        // 重置襟翼挡位跟踪变量
                lastGearState = -1;         // 重置起落架状态跟踪变量
                lastApAltLockState = -1;    // 重置自动驾驶高度保持状态跟踪变量
                lastApHdgLockState = -1;    // 重置自动驾驶航向保持状态跟踪变量
                lastApVsHoldState = -1;     // 重置自动驾驶垂直速度保持状态跟踪变量
                lastApNavLockState = -1;    // 重置自动驾驶导航保持状态跟踪变量
                lastApAprHoldState = -1;    // 重置自动驾驶进近保持状态跟踪变量
                lastSimulationRate = -1;        // 重置模拟速率跟踪变量
                lastAutoThrottleState = -1;     // 重置自动油门状态跟踪变量
                lastSpoilersArmedState = -1;    // 重置扰流板预位状态跟踪变量

                // 关键改进：设置周期自动更新，而不是定时器主动请求
                // 模拟器会按 SIMCONNECT_PERIOD.SIM_FRAME 自动推送数据
                simconnect.RequestDataOnSimObject(REQUESTS.SimDataReq, DEFINITIONS.SimDataDef,
                    SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SIM_FRAME, 
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

                // 启动后台消息循环
                messageLoopCts = new CancellationTokenSource();
                messageLoopTask = MessageLoop(messageLoopCts.Token);

                UpdateConnectionStatus();
                Speak("GPWS 已连接");
                System.Diagnostics.Debug.WriteLine("SimConnect 连接成功");
            }
            catch (COMException comEx)
            {
                isConnected = false;
                hasReceivedData = false;
                UpdateConnectionStatus();
                System.Diagnostics.Debug.WriteLine($"SimConnect 连接失败: {comEx.Message}");
            }
            catch (Exception ex)
            {
                isConnected = false;
                hasReceivedData = false;
                UpdateConnectionStatus();
                System.Diagnostics.Debug.WriteLine($"连接错误: {ex.Message}");
            }
        }

        /// <summary>
        /// SimConnect 数据接收事件处理器，处理从模拟器接收到的飞行数据。
        /// </summary>
        /// <param name="sender">SimConnect 实例</param>
        /// <param name="data">接收到的数据</param>
        private void Simconnect_OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            try
            {
                if (data.dwRequestID == (uint)REQUESTS.SimDataReq)
                {
                    currentData = (SimData)data.dwData[0];
                    hasReceivedData = true;
                    UpdateConnectionStatus();
                    ProcessGPWS();
                    ProcessTaxiSpeeds();
                    ProcessParkingBrake();
                    ProcessSpoilers();
                    ProcessAutopilot();
                    ProcessFlaps();
                    ProcessLandingGear();
                    ProcessAutopilotAltHold();
                    ProcessAutopilotHdg();
                    ProcessAutopilotVs();
                    ProcessAutopilotNav();
                    ProcessAutopilotApr();
                    ProcessSimulationRate();
                    ProcessAutoThrottle();
                    ProcessSpoilersArmed();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理数据错误: {ex.Message}");
            }
        }

        /// <summary>
        /// SimConnect 退出事件处理器，当模拟器退出时断开连接。
        /// </summary>
        /// <param name="sender">SimConnect 实例</param>
        /// <param name="data">接收到的数据</param>
        private void Simconnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            try
            {
                DisconnectFromSimulator();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理退出事件错误: {ex.Message}");
            }
        }

        /// <summary>
        /// SimConnect 异常事件处理器，处理连接异常并断开连接。
        /// </summary>
        /// <param name="sender">SimConnect 实例</param>
        /// <param name="data">异常数据</param>
        private void Simconnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"SimConnect 异常: {data.dwException}");
                DisconnectFromSimulator();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理异常事件错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开与模拟器的连接，清理资源并停止后台任务。
        /// </summary>
        private void DisconnectFromSimulator()
        {
            try
            {
                if (isConnected)
                {
                    wasConnectedBefore = true;
                }

                isConnected = false;
                hasReceivedData = false;
                currentData = new SimData();

                // 停止后台消息循环
                if (messageLoopCts != null)
                {
                    messageLoopCts.Cancel();
                    try
                    {
                        messageLoopTask?.Wait(TimeSpan.FromSeconds(2));
                    }
                    catch (AggregateException) { }
                    finally
                    {
                        messageLoopCts.Dispose();
                        messageLoopCts = null;
                    }
                }

                if (simconnect != null)
                {
                    try
                    {
                        simconnect.OnRecvSimobjectData -= Simconnect_OnRecvSimobjectData;
                        simconnect.OnRecvQuit -= Simconnect_OnRecvQuit;
                        simconnect.OnRecvException -= Simconnect_OnRecvException;
                        simconnect.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"释放 SimConnect 错误: {ex.Message}");
                    }

                    simconnect = null;
                }

                UpdateConnectionStatus();

                if (wasConnectedBefore)
                {
                    Speak("GPWS 已断开");
                    wasConnectedBefore = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"断开连接错误: {ex.Message}");
            }
        }

        private void SendSimEvent(EVENTS evt, uint data)
        {
            simconnect.TransmitClientEvent(
                SimConnect.SIMCONNECT_OBJECT_ID_USER,
                evt,
                data,
                GROUPS.Highest,
                SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        }

    }
}
