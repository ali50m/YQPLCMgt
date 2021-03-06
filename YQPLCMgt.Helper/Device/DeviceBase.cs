﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YQPLCMgt.Helper
{
    public class DeviceBase : ObservableObject
    {

        public DeviceBase(int lineno,string no, string name)
        {
            this.LineNo = lineno;
            this.NO = no;
            try
            {
                this.DEVICE_TYPE = no.Substring(0, 4);
            }
            catch (Exception e)
            {
                this.DEVICE_TYPE = e.Message;
            }
            this.NAME = name;
        }

        public int LineNo { get; set; }

        private string _NO;
        /// <summary>
        /// 设备编号
        /// </summary>
        public string NO { get => _NO; set => Set(ref _NO, value); }

        private string _DEVICE_TYPE;
        /// <summary>
        /// 设备类型
        /// </summary>
        public string DEVICE_TYPE { get => _DEVICE_TYPE; set => Set(ref _DEVICE_TYPE, value); }

        private string _NAME;
        /// <summary>
        /// 设备名称
        /// </summary>
        public string NAME { get => _NAME; set => Set(ref _NAME, value); }

        private int _PALLET_COUNT;
        /// <summary>
        /// 托盘数 
        /// </summary>
        public int PALLET_COUNT { get => _PALLET_COUNT; set => Set(ref _PALLET_COUNT, value); }

        private bool _Enable = false;
        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Enable { get => _Enable; set => Set(ref _Enable, value); }
    }
}
