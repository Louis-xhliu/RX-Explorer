﻿using SharedLibrary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using Vanara.PInvoke;
using Timer = System.Timers.Timer;

namespace MonitorTrustProcess
{
    class Program
    {
        private static Process ExplorerProcess;

        private static ManualResetEvent ExitLocker;

        private static NamedPipeMonitorCommunicationBaseController PipeCommunicationBaseController;

        private static NamedPipeWriteController PipeCommandWriteController;

        private static NamedPipeReadController PipeCommandReadController;

        private static Timer RespondingTimer;

        private static bool IsDebuggerAttachedToMonitorProcess;

        private static string RecoveryData;

        private static readonly string ExplorerPackageFamilyName = "36186RuoFan.USB_q3e6crc0w375t";

        private static readonly Dictionary<MonitorFeature, bool> FeatureStatusMapping = new Dictionary<MonitorFeature, bool>
        {
            { MonitorFeature.CrashMonitor, false },
            { MonitorFeature.FreezeMonitor, false }
        };

        private static bool IsMonitorEnabled { get; set; }

        private static bool IsRegisterRestartRequest { get; set; }

        private static bool IsCrashMonitorEnabled => IsMonitorEnabled && FeatureStatusMapping[MonitorFeature.CrashMonitor];

        private static bool IsFreezeMonitorEnabled => IsMonitorEnabled && FeatureStatusMapping[MonitorFeature.FreezeMonitor];

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                ExitLocker = new ManualResetEvent(false);

                PipeCommunicationBaseController = new NamedPipeMonitorCommunicationBaseController(ExplorerPackageFamilyName);
                PipeCommunicationBaseController.OnDataReceived += PipeCommunicationBaseController_OnDataReceived;

                RespondingTimer = new Timer(15000)
                {
                    AutoReset = true,
                    Enabled = true
                };
                RespondingTimer.Elapsed += RespondingTimer_Elapsed;

                if (PipeCommunicationBaseController.WaitForConnectionAsync(10000).Result)
                {
                    ExitLocker.WaitOne();
                }
                else
                {
                    LogTracer.Log($"Could not connect to the explorer. PipeCommunicationBaseController connect timeout. Exiting...");
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "An unexpected exception was threw in starting MonitorTrustProcess");
            }
            finally
            {
                ExitLocker?.Dispose();
                RespondingTimer?.Dispose();
                PipeCommandWriteController?.Dispose();
                PipeCommandReadController?.Dispose();
                PipeCommunicationBaseController?.Dispose();

                LogTracer.MakeSureLogIsFlushed(2000);
            }
        }

        private static void RespondingTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (IsFreezeMonitorEnabled && !IsDebuggerAttachedToMonitorProcess)
            {
                RespondingTimer.Enabled = false;

                try
                {
                    ExplorerProcess?.Refresh();

                    if (!(ExplorerProcess?.HasExited).GetValueOrDefault(true))
                    {
                        if (Helper.GetWindowInformationFromUwpApplication(ExplorerPackageFamilyName, Convert.ToUInt32((ExplorerProcess?.Id).GetValueOrDefault())) is WindowInformation UwpInfo)
                        {
                            if (UwpInfo.IsValidInfomation)
                            {
                                IntPtr Result = IntPtr.Zero;

                                if (!UwpInfo.CoreWindowHandle.IsNull)
                                {
                                    if (User32.SendMessageTimeout(UwpInfo.CoreWindowHandle, (uint)User32.WindowMessage.WM_NULL, fuFlags: User32.SMTO.SMTO_ABORTIFHUNG, uTimeout: 15000, lpdwResult: ref Result) == IntPtr.Zero)
                                    {
                                        CloseAndRestartApplication(RestartReason.Freeze);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, "Could not finish the responding check");
                }
                finally
                {
                    RespondingTimer.Enabled = true;
                }
            }
        }

        private static void PipeCommunicationBaseController_OnDataReceived(object sender, NamedPipeDataReceivedArgs e)
        {
            if (e.ExtraException is Exception Ex)
            {
                LogTracer.Log(Ex, "Could not receive pipe data");
            }
            else
            {
                try
                {
                    IDictionary<string, string> Package = JsonSerializer.Deserialize<IDictionary<string, string>>(e.Data);

                    if (Package.TryGetValue("LogRecordFolderPath", out string LogRecordPath))
                    {
                        LogTracer.SetLogRecordFolderPath(LogRecordPath);
                    }

                    if (Package.TryGetValue("ProcessId", out string ProcessId))
                    {
                        if ((ExplorerProcess?.Id).GetValueOrDefault() != Convert.ToInt32(ProcessId))
                        {
                            ExplorerProcess = Process.GetProcessById(Convert.ToInt32(ProcessId));
                            ExplorerProcess.EnableRaisingEvents = true;
                            ExplorerProcess.Exited += ExplorerProcess_Exited;
                            IsDebuggerAttachedToMonitorProcess = Helper.CheckIfDebuggerIsAttached(ExplorerProcess.Handle);
                        }
                    }

                    if (PipeCommandReadController != null)
                    {
                        PipeCommandReadController.OnDataReceived -= PipeCommandReadController_OnDataReceived;
                    }

                    if (Package.TryGetValue("PipeCommandWriteId", out string PipeCommandWriteId))
                    {
                        PipeCommandReadController?.Dispose();
                        PipeCommandReadController = new NamedPipeReadController(ExplorerPackageFamilyName, PipeCommandWriteId);
                        PipeCommandReadController.OnDataReceived += PipeCommandReadController_OnDataReceived;
                    }

                    if (Package.TryGetValue("PipeCommandReadId", out string PipeCommandReadId))
                    {
                        PipeCommandWriteController?.Dispose();
                        PipeCommandWriteController = new NamedPipeWriteController(ExplorerPackageFamilyName, PipeCommandReadId);
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, $"An exception was threw in get data in {nameof(PipeCommunicationBaseController_OnDataReceived)}");
                }
            }
        }

        private static void PipeCommandReadController_OnDataReceived(object sender, NamedPipeDataReceivedArgs e)
        {
            try
            {
                if (e.ExtraException is Exception Ex)
                {
                    LogTracer.Log(Ex, "Could not receive pipe data");
                }
                else
                {
                    IDictionary<string, string> Request = JsonSerializer.Deserialize<IDictionary<string, string>>(e.Data);
                    IDictionary<string, string> Response = HandleCommand(Request);
                    PipeCommandWriteController?.SendData(JsonSerializer.Serialize(Response));
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "An exception was threw in responding pipe message");
            }
        }

        private static IDictionary<string, string> HandleCommand(IDictionary<string, string> CommandValue)
        {
#if DEBUG
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }
                else
                {
                    Debugger.Launch();
                }

                throw new Exception("Not allowed to execute this function in modes rather than STA");
            }
#endif

            IDictionary<string, string> Value = new Dictionary<string, string>();

            try
            {
                switch (Enum.Parse<MonitorCommandType>(CommandValue["CommandType"]))
                {
                    case MonitorCommandType.RegisterRestartRequest:
                        {
                            IsRegisterRestartRequest = true;
                            RecoveryData = CommandValue["RecoveryData"];
                            break;
                        }
                    case MonitorCommandType.SetRecoveryData:
                        {
                            RecoveryData = CommandValue["RecoveryData"];
                            break;
                        }
                    case MonitorCommandType.StartMonitor:
                        {
                            IsMonitorEnabled = true;
                            break;
                        }
                    case MonitorCommandType.StopMonitor:
                        {
                            IsMonitorEnabled = false;
                            break;
                        }
                    case MonitorCommandType.EnableFeature:
                        {
                            FeatureStatusMapping[Enum.Parse<MonitorFeature>(CommandValue["Feature"])] = true;
                            break;
                        }
                    case MonitorCommandType.DisableFeature:
                        {
                            FeatureStatusMapping[Enum.Parse<MonitorFeature>(CommandValue["Feature"])] = false;
                            break;
                        }
                }

                Value.Add("Success", string.Empty);
            }
            catch (Exception ex)
            {
                Value.Clear();
                Value.Add("Error", ex.Message);
            }

            return Value;
        }

        private static void ExplorerProcess_Exited(object sender, EventArgs e)
        {
            if (IsRegisterRestartRequest)
            {
                CloseAndRestartApplication(RestartReason.Restart);
            }
            else if (IsCrashMonitorEnabled && !IsDebuggerAttachedToMonitorProcess)
            {
                CloseAndRestartApplication(RestartReason.Crash);
            }
            else
            {
                ExitLocker.Set();
            }
        }

        private static void CloseAndRestartApplication(RestartReason Reason)
        {
            try
            {
                if (ExplorerProcess != null)
                {
                    switch (Reason)
                    {
                        case RestartReason.Freeze:
                            {
                                ExplorerProcess.EnableRaisingEvents = false;
                                ExplorerProcess.Exited -= ExplorerProcess_Exited;
                                ExplorerProcess.Kill();

                                Helper.LaunchApplicationFromPackageFamilyName(ExplorerPackageFamilyName, "--RecoveryReason", Enum.GetName(RestartReason.Freeze), "--RecoveryData", Convert.ToBase64String(Encoding.UTF8.GetBytes(RecoveryData)));
                                break;
                            }
                        case RestartReason.Crash:
                            {
                                Helper.LaunchApplicationFromPackageFamilyName(ExplorerPackageFamilyName, "--RecoveryReason", Enum.GetName(RestartReason.Crash), "--RecoveryData", Convert.ToBase64String(Encoding.UTF8.GetBytes(RecoveryData)));
                                break;
                            }
                        case RestartReason.Restart:
                            {
                                if (string.IsNullOrEmpty(RecoveryData))
                                {
                                    Helper.LaunchApplicationFromPackageFamilyName(ExplorerPackageFamilyName, "--RecoveryReason", Enum.GetName(RestartReason.Restart));
                                }
                                else
                                {
                                    Helper.LaunchApplicationFromPackageFamilyName(ExplorerPackageFamilyName, "--RecoveryReason", Enum.GetName(RestartReason.Restart), "--RecoveryData", Convert.ToBase64String(Encoding.UTF8.GetBytes(RecoveryData)));
                                }

                                break;
                            }
                    }
                }
            }
            catch (Exception)
            {
                //No need to handle this exception
            }
            finally
            {
                ExitLocker.Set();
            }
        }
    }
}