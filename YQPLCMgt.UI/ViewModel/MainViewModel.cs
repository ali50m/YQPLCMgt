﻿using MyLogLib;
using Newtonsoft.Json;
using RabbitMQ;
using RabbitMQ.YQMsg;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using YQPLCMgt.Helper;

namespace YQPLCMgt.UI.ViewModel
{
    public class MainViewModel : ObservableObject, IDisposable
    {

        #region public property
        public DataSource Source { get => _Source; set => _Source = value; }
        public bool PLC1_Status { get => _PLC1_Status; set => Set(ref _PLC1_Status, value); }
        public bool PLC2_Status { get => _PLC2_Status; set => Set(ref _PLC2_Status, value); }
        public bool PLC3_Status { get => _PLC3_Status; set => Set(ref _PLC3_Status, value); }
        public bool InitCompleted { get => _InitCompleted; set => Set(ref _InitCompleted, value); }

        public bool[] PLC_Status { get => _PLC_Status; set => Set(ref _PLC_Status, value); }
        #endregion

        #region private field
        private bool _PLC1_Status = false;
        private bool _PLC2_Status = false;
        private bool _PLC3_Status = false;

        private bool[] _PLC_Status = new bool[3] { true, true, true };

        private DataSource _Source;
        private CancellationTokenSource cancelToken;
        private ClientMQ mqClient;

        private bool _InitCompleted = false;
        /// <summary>
        /// PLC
        /// </summary>
        private PLCHelper[] plcs;


        /// <summary>
        /// 扫码枪
        /// </summary>
        private List<ScannerHelper> scanHelpers;
        #endregion

        public MainViewModel()
        {
            Source = new DataSource();
        }

        #region 初始化
        public void Init()
        {
            InitMQ();
            InitPLC();
            //InitScan();
            InitCompleted = true;
        }

        /// <summary>
        /// 初始化MQ Client
        /// <para>MQ需要手动关闭，否则后台线程不退出</para>
        /// </summary>
        public void InitMQ()
        {
            ShowMsg("初始化MQ...");
            mqClient?.Close();
            try
            {
                mqClient = new ClientMQ();
                mqClient.singleArrivalEvent += MqClient_singleArrivalEvent;
                mqClient.ReceiveMessage();
                ShowMsg("初始化MQ完毕！");
            }
            catch (Exception ex)
            {
                MyLog.WriteLog("初始化MQ失败！", ex);
                ShowMsg("初始化MQ失败！");
            }
        }

        /// <summary>
        /// 初始化扫码枪
        /// </summary>
        private void InitScan()
        {
            ShowMsg("初始化扫码枪...");
            if (scanHelpers != null)
            {
                scanHelpers.ForEach(p => p.DisConnect());
            }
            scanHelpers = new List<ScannerHelper>();
            string errComNames = "";
            foreach (var item in _Source.ScanDevices)
            {
                ScannerHelper scanHelper;
                scanHelper = new SocketScannerHelper(item);
                //if (item.IOType == ScannerIO.Socket)
                //{
                //    scanHelper = new SocketScannerHelper(item);
                //}
                //else
                //{
                //    scanHelper = new SerialScannerHelper(item);
                //}
                scanHelper.OnScanned += ScannedCallback;
                scanHelper.OnError += ShowMsg;
                if (!scanHelper.Connect())
                {
                    errComNames += item.IP + ",";
                }
                scanHelpers.Add(scanHelper);
            }

            if (!string.IsNullOrEmpty(errComNames))
            {
                errComNames = errComNames.Remove(errComNames.Length - 1, 1);
                ShowMsg("条码枪串口初始化失败——" + errComNames);
            }
        }
        private bool IsAllPLCConnected = true;
        /// <summary>
        /// 初始化PLC
        /// </summary>
        private void InitPLC()
        {
            ShowMsg("初始化PLC...");
            string[] plc_ips = new string[]
            {
                "192.168.0.10"//,"192.168.0.20",//"192.168.0.30"
            };
            IsAllPLCConnected = true;
            plcs?.ToList().ForEach(p => p.DisConnect());
            plcs = new PLCHelper[plc_ips.Length];
            PLC_Status = new bool[plc_ips.Length];
            for (int i = 0; i < plcs.Length; i++)
            {
                plcs[i] = new PLCHelper(plc_ips[i], 8501, true);
                plcs[i].OnShowMsg += ShowMsg;
                PLC_Status[i] = plcs[i].Connect();
                IsAllPLCConnected &= PLC_Status[i];
            }
            RaisePropertyChanged("PLC_Status");
            ShowMsg("初始化PLC完毕！");
        }
        #endregion

        /// <summary>
        /// 扫码枪接收回调
        /// </summary>
        /// <param name="scan"></param>
        /// <param name="strCodes"></param>
        private void ScannedCallback(ScanDevice scan, string data)
        {
            ShowMsg("扫码:" + scan.IP + " -- " + data);
            //格式条码+\r
            string[] codes = data.Split('\r');
            foreach (var barcode in codes)
            {
                if (string.IsNullOrEmpty(barcode) || barcode.Length < 4)//TODO:条码长度过滤非法数据
                {
                    continue;
                }
                try
                {
                    BarcodeMsg msg = new BarcodeMsg(scan.NO);
                    msg.BAR_CODE = barcode;
                    string strJson = JsonConvert.SerializeObject(msg);
                    mqClient?.SentMessage(strJson);
                }
                catch (Exception ex)
                {
                    string errMsg = "条码上报MQ服务器失败！";
                    MyLog.WriteLog(errMsg, ex);
                    OnShowMsg(errMsg);
                }
            }
        }

        /// <summary>
        /// MQ消息接收回调
        /// </summary>
        /// <param name="data"></param>
        private void MqClient_singleArrivalEvent(string data)
        {
            ShowMsg(data);
            MsgBase msg = null;
            try
            {
                msg = JsonConvert.DeserializeObject<MsgBase>(data);
            }
            catch (Exception ex)
            {
                string errMsg = "协议格式错误！";
                MyLog.WriteLog(errMsg, ex);
                ShowMsg(errMsg);
                return;
            }

            if (msg.MESSAGE_TYPE == "control")
            {
                if (msg is ControlMsg ctlMsg)
                {
                    var stop = _Source.StopDevices.FirstOrDefault(p => p.NO == ctlMsg.NO);
                    var machine = _Source.MachineDevices.FirstOrDefault(p => p.NO == ctlMsg.NO);
                    if (stop != null)
                    {
                        var plc = plcs.FirstOrDefault(p => p.IP == stop.PLCIP);
                        var resp = plc.SetOnePoint(stop.DMAddr_Status, ctlMsg.COMMAND_ID);
                        if (!resp.HasError)
                        {
                            ResponseServer(stop.DEVICE_TYPE, stop.NO, ctlMsg.COMMAND_ID.ToString());
                        }
                        else
                        {
                            ShowMsg(resp.ErrorMsg);
                        }
                    }
                    if (machine != null)
                    {
                        var plc = plcs.FirstOrDefault(p => p.IP == machine.PLCIP);
                        var resp = plc.SetOnePoint(machine.DMAddr_Status, ctlMsg.COMMAND_ID);
                        if (!resp.HasError)
                        {
                            ResponseServer(machine.DEVICE_TYPE, machine.NO, ctlMsg.COMMAND_ID.ToString());
                        }
                        else
                        {
                            ShowMsg(resp.ErrorMsg);
                        }
                    }
                }
            }
            else if (msg.MESSAGE_TYPE == "task")
            {

            }
            else if (msg.MESSAGE_TYPE == "data")
            {

            }
            else
            {

            }
        }

        private void ResponseServer(string DEVICE_TYPE, string NO, string STATUS)
        {
            HeartBeatMsg respMsg = new HeartBeatMsg();
            respMsg.DEVICE_TYPE = DEVICE_TYPE;
            respMsg.NO = NO;
            respMsg.STATUS = STATUS;
            mqClient?.SentMessage(JsonConvert.SerializeObject(respMsg));
        }

        public event Action<string> OnShowMsg;

        #region command

        #region StartCmd
        private RelayCommand _StartCmd;
        public RelayCommand StartCmd
        {
            get
            {
                if (_StartCmd == null)
                {
                    _StartCmd = new RelayCommand(Start, CanStart);
                }
                return _StartCmd;
            }
        }

        private void Start()
        {
            cancelToken = new CancellationTokenSource();
            Task.Run(new Action(MonitorDevice), cancelToken.Token);
            //Task.Run(new Action(MonitorStopDevice), cancelToken.Token);
            //Task.Run(new Action(MonitorMachine), cancelToken.Token);
        }

        private bool CanStart()
        {
            return InitCompleted && IsAllPLCConnected;
        }

        private void MonitorDevice()
        {
            int start = 100;
            int count = 200;
            while (cancelToken != null && !cancelToken.IsCancellationRequested)
            {
                try
                {
                    foreach (var plc in plcs)
                    {
                        PLCResponse response = plc.Send($"RDS DM{start}.U {count}\r");
                        if (response.HasError)
                        {
                            ShowMsg(response.ErrorMsg);
                        }
                        else
                        {
                            if (_Source.ErrorMsg.Keys.Contains(response.Text))
                            {
                                ShowMsg(_Source.ErrorMsg[response.Text]);
                            }
                            else
                            {
                                string[] getStrs = response.Text.Split(' ').ToArray();
                                int[] getValues = new int[getStrs.Length];
                                for (int i = 0; i < getValues.Length; i++)
                                {
                                    string dmAddr = "DM" + (start + i);
                                    getValues[i] = Convert.ToInt32(getStrs[i]);
                                    #region 上报状态至MQ服务器
                                    //获取专机
                                    var machine = _Source.MachineDevices.FirstOrDefault(p => p.DMAddr_Status == dmAddr && p.PLCIP == plc.IP);
                                    if (machine != null)//专机状态PLC
                                    {
                                        PLCMsg plcMsg = new PLCMsg();
                                        plcMsg.DEVICE_TYPE = machine.DEVICE_TYPE;
                                        plcMsg.NO = machine.NO;
                                        plcMsg.STATUS = getValues[i];
                                        if (!string.IsNullOrEmpty(machine.DMAddr_Pallet))//有单独的托盘DM
                                        {
                                            int idx = Convert.ToInt32(machine.DMAddr_Pallet.Replace("DM", "")) - start;
                                            plcMsg.PALLET_COUNT = getValues[idx];
                                        }
                                        else
                                        {
                                            plcMsg.PALLET_COUNT = plcMsg.STATUS > 0 ? 1 : 0;
                                        }
                                        string strJson = JsonConvert.SerializeObject(plcMsg);
                                        mqClient?.SentMessage(strJson);
                                        Thread.Sleep(50);
                                    }

                                    //获取挡停
                                    var stop = _Source.StopDevices.FirstOrDefault(p => p.DMAddr_Status == dmAddr && plc.IP == p.PLCIP);
                                    if (stop != null)//专机状态PLC
                                    {
                                        #region 根据挡停状态触发扫码枪
                                        if (getValues[i] == 1 && !string.IsNullOrEmpty(stop.Scan_Device_No))//有托盘
                                        {
                                            //获取扫码枪
                                            var scan = scanHelpers?.FirstOrDefault(p => p.Scanner.NO == stop.Scan_Device_No);
                                            scan?.TriggerScan();//触发扫码枪，进行扫码
                                        }
                                        #endregion

                                        PLCMsg plcMsg = new PLCMsg();
                                        plcMsg.DEVICE_TYPE = stop.DEVICE_TYPE;
                                        plcMsg.NO = stop.NO;
                                        plcMsg.STATUS = getValues[i];
                                        string strJson = JsonConvert.SerializeObject(plcMsg);
                                        mqClient?.SentMessage(strJson);
                                        Thread.Sleep(50);
                                    }

                                    #endregion

                                    //TODO:测试代码，直接发放行命令
                                    if (getValues[i] == 1)
                                    {
                                        var resp = plc.Send($"WR {dmAddr}.U 2\r");
                                        if (resp.HasError)
                                        {
                                            ShowMsg(resp.ErrorMsg);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MyLog.WriteLog("MonitorDevice异常！", ex);
                    ShowMsg("MonitorDevice异常！" + ex.Message + "\r" + ex.StackTrace);
                }
                Thread.Sleep(2000);
            }
        }

        #endregion

        #region StopCmd
        private RelayCommand _StopCmd;
        public RelayCommand StopCmd
        {
            get
            {
                if (_StopCmd == null)
                {
                    _StopCmd = new RelayCommand(Stop, () => InitCompleted);
                }
                return _StopCmd;
            }
        }

        private void Stop()
        {
            cancelToken?.Cancel();
            cancelToken = null;
        }
        #endregion

        #endregion

        public void Dispose()
        {
            mqClient?.Close();
            foreach (var plc in plcs)
            {
                plc?.DisConnect();
            }
        }

        private void ShowMsg(string msg)
        {
            try
            {
                OnShowMsg?.Invoke(msg);
            }
            catch (Exception ex)
            {
                MyLog.WriteLog("OnShowMsg委托调用异常！", ex);
            }
        }
    }
}
