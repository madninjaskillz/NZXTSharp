﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;

using NZXTSharp;
using NZXTSharp.COM;
using NZXTSharp.Exceptions;

using HidLibrary;

namespace NZXTSharp.KrakenX
{
    /// <summary>
    /// Which thread to stop.
    /// </summary>
    public enum OverrideThread
    {
        /// <summary>
        /// Stop the fan override thread.
        /// </summary>
        Fan = 0,

        /// <summary>
        /// Stop the pump override thread.
        /// </summary>
        Pump = 1,
    }

    /// <summary>
    /// Thread stop types.
    /// </summary>
    public enum ThreadStopType
    {
        /// <summary>
        /// If you need the override thread to stop immediately.
        /// Best/ safest method is to use <see cref="ThreadStopType.Flag"/>.
        /// </summary>
        Abort = 0,

        /// <summary>
        /// Stops the override thread with a flag. This is the best/ safest method, 
        /// but may take up to 20 seconds to take effect.
        /// </summary>
        Flag = 1,
    }


    /// <summary>
    /// Represents an NZXT KrakenX device.
    /// </summary>
    public class KrakenX : INZXTDevice
    {
        #region Fields and Properties
        internal DeviceLoadFilter[] LoadFilters = new DeviceLoadFilter[]
        {
            DeviceLoadFilter.All,
            DeviceLoadFilter.Coolers,
            DeviceLoadFilter.Kraken,
            DeviceLoadFilter.KrakenX
        };

        private KrakenXChannel _Both;
        private KrakenXChannel _Logo;
        private KrakenXChannel _Ring;
        private Version _FirmwareVersion;
        private Thread PumpOverrideThread;
        private bool StopPumpOverrideLoop = false;

        private Thread FanOverrideThread;
        private bool StopFanOverrideLoop = false;

        private USBController _COMController;

        /// <summary>
        /// The <see cref="HIDDeviceID"/> of the <see cref="KrakenX"/> device. Will always be <see cref="HIDDeviceID.KrakenX"/>.
        /// </summary>
        public HIDDeviceID DeviceID { get => HIDDeviceID.KrakenX; }

        /// <summary>
        /// The <see cref="NZXTDeviceType"/> of the <see cref="KrakenX"/> device. Will always be <see cref="NZXTDeviceType.KrakenX"/>.
        /// </summary>
        public NZXTDeviceType Type { get => NZXTDeviceType.KrakenX; }

        /// <inheritdoc/>
        public int ID { get => 0x170e; }

        /// <summary>
        /// Represents both the <see cref="Logo"/>, and <see cref="Ring"/> channels.
        /// </summary>
        public KrakenXChannel Both { get => _Both; }

        /// <summary>
        /// Represents the <see cref="KrakenX"/>'s logo RGB channel.
        /// </summary>
        public KrakenXChannel Logo { get => _Logo; }
        
        /// <summary>
        /// Represents the <see cref="KrakenX"/>'s ring RGB channel.
        /// </summary>
        public KrakenXChannel Ring { get => _Ring; }

        /// <summary>
        /// The <see cref="KrakenX"/> device's firmware version.
        /// </summary>
        public Version FirmwareVersion { get => _FirmwareVersion; }
        
        #endregion

        /// <summary>
        /// Constructs an instance of a <see cref="KrakenX"/> device.
        /// </summary>
        public KrakenX()
        {
            Initialize();
        }

        #region Methods
        private void Initialize()
        {
            InitializeChannels();
            _COMController = new USBController(Type);
            InitializeDeviceInfo();
        }

        private void InitializeChannels()
        {
            _Both = new KrakenXChannel(0x00, this);
            _Logo = new KrakenXChannel(0x01, this);
            _Ring = new KrakenXChannel(0x02, this);
            Console.WriteLine("Channels Initialized");
        }

        private void InitializeDeviceInfo()
        {
            this._FirmwareVersion = GetFirmwareVersion();
        }

        /// <summary>
        /// Writes a custom <paramref name="Buffer"/> to the <see cref="KrakenX"/> device.
        /// </summary>
        /// <param name="Buffer"></param>
        public void WriteCustom(byte[] Buffer)
        {
            _COMController.Write(Buffer);
        }

        /// <summary>
        /// Stops the thread overriding a given <paramref name="Thread"/>, using a given <paramref name="StopType"/>.
        /// </summary>
        /// <param name="Thread">Which <see cref="OverrideThread"/> to stop.</param>
        /// <param name="StopType">How to stop the given <paramref name="Thread"/>.</param>
        public void StopOverrideThread(OverrideThread Thread, ThreadStopType StopType = ThreadStopType.Flag)
        {
            switch (Thread)
            {
                case OverrideThread.Fan:
                    if (StopType == ThreadStopType.Abort) {
                        FanOverrideThread.Abort();
                    }
                    else if (StopType == ThreadStopType.Flag) {
                        StopFanOverrideLoop = true;
                    }
                    break;
                case OverrideThread.Pump:
                    if (StopType == ThreadStopType.Abort){
                        PumpOverrideThread.Abort();
                    }
                    else if (StopType == ThreadStopType.Flag){
                        StopPumpOverrideLoop = true;
                    }
                    break;
            }
        }

        /// <summary>
        /// Applies an <paramref name="Effect"/> to both channels.
        /// </summary>
        /// <param name="Effect">An <see cref="IEffect"/>.</param>
        public void ApplyEffect(IEffect Effect)
        {
            ApplyEffect(this.Both, Effect);
        }

        /// <summary>
        /// Applies a given <see cref="IEffect"/> <paramref name="Effect"/> to a given 
        /// <see cref="KrakenXChannel"/> <paramref name="Channel"/>.
        /// </summary>
        /// <param name="Channel"></param>
        /// <param name="Effect"></param>
        /// <param name="ApplyToChannel">Whether or not to apply <paramref name="Effect"/>
        /// to the <paramref name="Channel"/> as its last applied effect.</param>
        public void ApplyEffect(KrakenXChannel Channel, IEffect Effect, bool ApplyToChannel = true)
        {

            Console.WriteLine("Applying Effect");
            Console.WriteLine(Channel.ChannelByte);
            if (!Effect.IsCompatibleWith(Type))
                throw new IncompatibleEffectException("KrakenX", Effect.EffectName);

            if (ApplyToChannel)
            {
                if (Channel.ChannelByte == 0x00)
                {
                    this._Both.UpdateEffect(Effect);
                    this._Logo.UpdateEffect(Effect);
                    this._Ring.UpdateEffect(Effect);
                }
                else if (Channel.ChannelByte == 0x01)
                {
                    this.Logo.UpdateEffect(Effect);
                }
                else if (Channel.ChannelByte == 0x02)
                {
                    this.Ring.UpdateEffect(Effect);
                }
            }
            
            List<byte[]> CommandQueue = Effect.BuildBytes(Type, Channel);
            //_COMController.SimulWrite(CommandQueue.ToArray());
            foreach (byte[] Command in CommandQueue)
                _COMController.Write(Command);
        }

        /// <summary>
        /// Gets the pump speed last reported by the <see cref="KrakenX"/> device.
        /// </summary>
        /// <returns>The last reported pump speed in RPM.</returns>
        public int GetPumpSpeed()
        {
            if (_COMController.LastReport != null)
            {
                HidLibrary.HidReport report = _COMController.LastReport;
                return report.Data[4] << 8 | report.Data[5];
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Sets the <see cref="KrakenX"/>'s pump speed to a given percent or RPM.
        /// </summary>
        /// <param name="Speed">
        /// The speed value to set. Must be 50-100 (inclusive).
        /// </param>
        public void SetPumpSpeed(int Speed)
        {
            if (PumpOverrideThread != null)
                PumpOverrideThread.Abort(); // I know it's bad code, but no other safe method works properly :/

            if (true)
            {
                if (Speed > 100 || Speed < 50) {
                    throw new InvalidParamException("Pump speed percentages must be between 50-100 (inclusive).");
                }
            }

            byte[] command = new byte[] { 0x02, 0x4d, 0x40, 0x00, Convert.ToByte(Speed) };
            this.StopPumpOverrideLoop = false;
            PumpOverrideThread = new Thread(new ParameterizedThreadStart(PumpSpeedOverrideLoop));

            PumpOverrideThread.Start(command);
        }

        /// <summary>
        /// Gets the last fan speed reported by the <see cref="KrakenX"/> device.
        /// </summary>
        /// <returns>The last reported fan speed in RPM.</returns>
        public int GetFanSpeed()
        {
            if (_COMController.LastReport != null)
            {
                HidLibrary.HidReport report = _COMController.LastReport;
                return report.Data[4] * 0x100 + report.Data[5];
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Sets all fans connected to the <see cref="KrakenX"/> device to a given <paramref name="Percent"/>.
        /// </summary>
        /// <param name="Percent">The percentage to set the fans to. Must be 25-100 (inclusive).</param>
        public void SetFanSpeed(int Percent)
        {
            if (FanOverrideThread != null)
                FanOverrideThread.Abort(); // I know it's bad code, but no other safe method works properly :/

            if (Percent > 100 || Percent < 25) {
                throw new InvalidParamException("Fan speed percentage must be between 25-100 (inclusive).");
            }

            byte[] command = new byte[] { 0x02, 0x4d, 0x00, 0x00, Convert.ToByte(Percent) };
            this.StopFanOverrideLoop = false;
            FanOverrideThread = new Thread(new ParameterizedThreadStart(FanSpeedOverrideLoop));

            FanOverrideThread.Start(command);
        }

        /// <summary>
        /// Gets the last reported liquid temp.
        /// </summary>
        /// <returns>The last reported liquid temp as a rounded integer, in degrees C.</returns>
        public int? GetLiquidTemp()
        {
            if (_COMController.LastReport != null)
            {
                HidLibrary.HidReport report = _COMController.LastReport;
                double temp = (report.Data[0] + (report.Data[1] * 0.1));
                return temp.Round();
            } else
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the last HID report received from the KrakenX device.
        /// </summary>
        /// <returns>An <see cref="HidLibrary.HidReport"/>.</returns>
        public HidReport GetLastReport()
        {
            return _COMController.LastReport;
        }
        
        /// <summary>
        /// Gets the <see cref="KrakenX"/>'s firmware version.
        /// </summary>
        /// <returns>A <see cref="System.Version"/> object.</returns>
        public Version GetFirmwareVersion()
        {
            while (_COMController.LastReport == null)
            {
                Thread.Sleep(25);
            }

            HidReport report = _COMController.LastReport;
            int Major = report.Data[10];
            int Minor = report.Data[12].ConcatenateInt(report.Data[13]);
            return new Version(Major, Minor);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _COMController.Dispose();
        }

        /// <inheritdoc/>
        public void Reconnect()
        {
            Dispose();
            InitializeChannels();
            Initialize();
        }

        internal void PumpSpeedOverrideLoop(object Buffer)
        {
            while (!this.StopPumpOverrideLoop)
            {
                _COMController.Write((byte[])Buffer);
                Thread.Sleep(5000);
            }
        }

        internal void FanSpeedOverrideLoop(object Buffer)
        {
            while (!this.StopFanOverrideLoop)
            {
                _COMController.Write((byte[])Buffer);
                Thread.Sleep(5000);
            }
        }
        #endregion
    }
}
