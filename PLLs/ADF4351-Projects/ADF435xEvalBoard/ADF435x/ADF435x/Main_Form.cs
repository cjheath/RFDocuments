using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using CyUSB;
using sdpApi1;

//  Written and managed by:
//
//  Robert Brennan
//  robert.brennan@analog.com
//  http://about.me/robertbrennan
//


namespace ADF435x
{
    public partial class Main_Form : Form
    {

        #region Constants

        USBDeviceList usbDevices;
        static CyFX2Device connectedDevice;
        static bool FirmwareLoaded;
        static bool XferSuccess;

        static SdpBase sdp;
        Spi session;
        Gpio g_session;

        static int buffer_length = 1;
        static byte[] buffer = new byte[5];     // (Bits per register / 8) + 1

        #endregion

        #region Global variables

        bool protocol = false;                  // false = USB adapter, true = SDP

        double RFout, REFin, RFoutMax = 4400, RFoutMin = 34.375, REFinMax = 250, PFDMax = 32, OutputChannelSpacing, INT, MOD, FRAC;

        decimal N, PFDFreq;

        int ChannelUpDownCount = 0, LoadIndex = -1;

        uint[] Reg = new uint[6];
        uint[] Rprevious = new uint[6];

        bool SweepActive = false, HopActive = false, RandomActive = false, messageShownToUser = false;

        #endregion

        #region Main sections

        public Main_Form(string ADIsimPLL_import_file)
        {
            InitializeComponent();
            InitializeMenus();

            if (ADIsimPLL_import_file != "")
                importADIsimPLL(ADIsimPLL_import_file);

            usbDevices = new USBDeviceList(CyConst.DEVICES_CYUSB);

            usbDevices.DeviceAttached += new EventHandler(usbDevices_DeviceAttached);
            usbDevices.DeviceRemoved += new EventHandler(usbDevices_DeviceRemoved);

            this.FormClosing += new FormClosingEventHandler(exitEventHandler);
        }

        private void CallBuildRegisters(object sender, EventArgs e)
        {
            BuildRegisters();
        }

        private void BuildRegisters()
        {
            #region Declarations



            for (int i = 0; i < 6; i++)
                Rprevious[i] = Reg[i];

            #endregion

            #region Calculate N, INT, FRAC, MOD

            RFout = Convert.ToDouble(RFOutFreqBox.Text);
            REFin = Convert.ToDouble(RefFreqBox.Text);
            OutputChannelSpacing = Convert.ToDouble(OutputChannelSpacingBox.Text);

            PFDFreqBox.Text = (REFin * (RefDoublerBox.Checked ? 2 : 1) / (RefD2Box.Checked ? 2 : 1) / (double)RcounterBox.Value).ToString();
            PFDFreq = Convert.ToDecimal(PFDFreqBox.Text);


      




            #region Select divider
            if (RFout >= 2200)
                OutputDividerBox.Text = "1";
            if (RFout < 2200)
                OutputDividerBox.Text = "2";
            if (RFout < 1100)
                OutputDividerBox.Text = "4";
            if (RFout < 550)
                OutputDividerBox.Text = "8";
            if (RFout < 275)
                OutputDividerBox.Text = "16";
            if (RFout < 137.5)
                OutputDividerBox.Text = "32";
            if (RFout < 68.75)
                OutputDividerBox.Text = "64";
            #endregion


            if (FeedbackSelectBox.SelectedIndex == 1)
                N = ((decimal)RFout * Convert.ToInt16(OutputDividerBox.Text)) / PFDFreq;
            else
                N = ((decimal)RFout / PFDFreq);

            INT = (uint)N;
            MOD = (uint)(Math.Round(1000 * (PFDFreq / (decimal)OutputChannelSpacing)));
            FRAC = (uint)(Math.Round(((double)N - INT) * MOD));

            if (EnableGCD.Checked)
            {
                uint gcd = GCD((uint)MOD, (uint)FRAC);

                MOD = MOD / gcd;
                FRAC = FRAC / gcd;
            }

            if (MOD == 1)
                MOD = 2;

            INTBox.Text = INT.ToString();
            MODBox.Text = MOD.ToString();
            FRACBox.Text = FRAC.ToString();
            PFDBox.Text = PFDFreq.ToString();
            DivBox.Text = OutputDividerBox.Text;
            RFoutBox.Text = (((INT + (FRAC / MOD)) * (double)PFDFreq / Convert.ToInt16(DivBox.Text)) * ((FeedbackSelectBox.SelectedIndex == 1) ? 1 : Convert.ToInt16(DivBox.Text))).ToString();
            NvalueLabel.Text = (INT + (FRAC / MOD)).ToString();

            #region PFD max error check

            if ((PFDFreq > (decimal)PFDMax) && (BandSelectClockModeBox.SelectedIndex == 0))
                PFDWarningIcon.Visible = true;
            else if ((ADF4351.Checked) && (PFDFreq > (decimal)PFDMax) && (BandSelectClockModeBox.SelectedIndex == 1) && (FRAC != 0))
                PFDWarningIcon.Visible = true;
            else if ((ADF4351.Checked) && (PFDFreq > 90) && (BandSelectClockModeBox.SelectedIndex == 1) && (FRAC != 0))
                PFDWarningIcon.Visible = true;
            else
                PFDWarningIcon.Visible = false;

            #endregion


            #endregion

            #region Band Select Clock

            if (BandSelectClockAutosetBox.Checked)
            {
                if (BandSelectClockModeBox.SelectedIndex == 0)
                {
                    uint temp = (uint)Math.Round(8 * PFDFreq, 0);
                    if ((8 * PFDFreq - temp) > 0)
                        temp++;
                    temp = (temp > 255) ? 255 : temp;
                    BandSelectClockDividerBox.Value = (decimal)temp;
                }
                else
                {
                    uint temp = (uint)Math.Round((PFDFreq * 2), 0);
                    if ((2 * PFDFreq - temp) > 0)
                        temp++;
                    temp = (temp > 255) ? 255 : temp;
                    BandSelectClockDividerBox.Value = (decimal)temp;

                }
            }

            BandSelectClockFrequencyBox.Text = (1000 * PFDFreq / (uint)BandSelectClockDividerBox.Value).ToString("0.000");

            if (Convert.ToDouble(BandSelectClockFrequencyBox.Text) > 500)
            {
                BSCWarning1Icon.Visible = true;
                BSCWarning2Icon.Visible = false;
                BSCWarning3Icon.Visible = false;
            }
            else if ((Convert.ToDouble(BandSelectClockFrequencyBox.Text) > 125) & (BandSelectClockModeBox.SelectedIndex == 1) & (ADF4351.Checked))
            {
                BSCWarning1Icon.Visible = false;
                BSCWarning2Icon.Visible = false;
                BSCWarning3Icon.Visible = false;
            }
            else if ((Convert.ToDouble(BandSelectClockFrequencyBox.Text) > 125) & (BandSelectClockModeBox.SelectedIndex == 0) & (ADF4351.Checked))
            {
                BSCWarning1Icon.Visible = false;
                BSCWarning2Icon.Visible = true;
                BSCWarning3Icon.Visible = false;
            }
            else if ((Convert.ToDouble(BandSelectClockFrequencyBox.Text) > 125) & (ADF4350.Checked))
            {
                BSCWarning1Icon.Visible = false;
                BSCWarning2Icon.Visible = false;
                BSCWarning3Icon.Visible = true;
            }
            else
            {
                BSCWarning1Icon.Visible = false;
                BSCWarning2Icon.Visible = false;
                BSCWarning3Icon.Visible = false;
            }

            #endregion

            #region Filling in registers


            #region ADF4350
            if (ADF4350.Checked)
            {
                Reg[0] = (uint)(
                    ((int)INT & 0xFFFF) * Math.Pow(2, 15) +
                    ((int)FRAC & 0xFFF) * Math.Pow(2, 3) +
                    0);
                Reg[1] = (uint)(
                    PrescalerBox.SelectedIndex * Math.Pow(2, 27) +
                    (double)PhaseValueBox.Value * Math.Pow(2, 15) +
                    ((int)MOD & 0xFFF) * Math.Pow(2, 3) +
                    1
                    );
                Reg[2] = (uint)(
                    LowNoiseSpurModeBox.SelectedIndex * Math.Pow(2, 29) +
                    MuxoutBox.SelectedIndex * Math.Pow(2, 26) +
                    (RefDoublerBox.Checked ? 1 : 0) * Math.Pow(2, 25) +
                    (RefD2Box.Checked ? 1 : 0) * Math.Pow(2, 24) +
                    (double)RcounterBox.Value * Math.Pow(2, 14) +
                    DoubleBuffBox.SelectedIndex * Math.Pow(2, 13) +
                    ChargePumpCurrentBox.SelectedIndex * Math.Pow(2, 9) +
                    LDFBox.SelectedIndex * Math.Pow(2, 8) +
                    LDPBox.SelectedIndex * Math.Pow(2, 7) +
                    PDPolarityBox.SelectedIndex * Math.Pow(2, 6) +
                    PowerdownBox.SelectedIndex * Math.Pow(2, 5) +
                    CP3StateBox.SelectedIndex * Math.Pow(2, 4) +
                    CounterResetBox.SelectedIndex * Math.Pow(2, 3) +
                    2
                    );
                Reg[3] = (uint)(
                    CSRBox.SelectedIndex * Math.Pow(2, 18) +
                    CLKDivModeBox.SelectedIndex * Math.Pow(2, 15) +
                    (double)ClockDividerValueBox.Value * Math.Pow(2, 3) +
                    3
                    );
                Reg[4] = (uint)(
                    FeedbackSelectBox.SelectedIndex * Math.Pow(2, 23) +
                    Math.Log(Convert.ToInt16(OutputDividerBox.Text), 2) * Math.Pow(2, 20) +
                    (double)BandSelectClockDividerBox.Value * Math.Pow(2, 12) +
                    VCOPowerdownBox.SelectedIndex * Math.Pow(2, 11) +
                    MTLDBox.SelectedIndex * Math.Pow(2, 10) +
                    AuxOutputSelectBox.SelectedIndex * Math.Pow(2, 9) +
                    AuxOutputEnableBox.SelectedIndex * Math.Pow(2, 8) +
                    AuxOutputPowerBox.SelectedIndex * Math.Pow(2, 6) +
                    RFOutputEnableBox.SelectedIndex * Math.Pow(2, 5) +
                    RFOutputPowerBox.SelectedIndex * Math.Pow(2, 3) +
                    4
                    );
                Reg[5] = (uint)(
                    LDPinModeBox.SelectedIndex * Math.Pow(2, 22) +
                    ReadSelBox.SelectedIndex * Math.Pow(2, 21) +
                    ICPADJENBox.SelectedIndex * Math.Pow(2, 19) +
                    SDTestmodesBox.SelectedIndex * Math.Pow(2, 15) +
                    PLLTestmodesBox.SelectedIndex * Math.Pow(2, 11) +
                    PDSynthBox.SelectedIndex * Math.Pow(2, 10) +
                    ExtBandEnBox.SelectedIndex * Math.Pow(2, 9) +
                    BandSelectBox.SelectedIndex * Math.Pow(2, 5) +
                    VCOSelBox.SelectedIndex * Math.Pow(2, 3) +
                    5
                    );
            }
            #endregion
            #region ADF4351
            if (ADF4351.Checked)
            {
                Reg[0] = (uint)(
                    ((int)INT & 0xFFFF) * Math.Pow(2, 15) +
                    ((int)FRAC & 0xFFF) * Math.Pow(2, 3) +
                    0);
                Reg[1] = (uint)(
                    PhaseAdjustBox.SelectedIndex * Math.Pow(2, 28) +
                    PrescalerBox.SelectedIndex * Math.Pow(2, 27) +
                    (double)PhaseValueBox.Value * Math.Pow(2, 15) +
                    ((int)MOD & 0xFFF) * Math.Pow(2, 3) +
                    1
                    );
                Reg[2] = (uint)(
                    LowNoiseSpurModeBox.SelectedIndex * Math.Pow(2, 29) +
                    MuxoutBox.SelectedIndex * Math.Pow(2, 26) +
                    (RefDoublerBox.Checked ? 1 : 0) * Math.Pow(2, 25) +
                    (RefD2Box.Checked ? 1 : 0) * Math.Pow(2, 24) +
                    (double)RcounterBox.Value * Math.Pow(2, 14) +
                    DoubleBuffBox.SelectedIndex * Math.Pow(2, 13) +
                    ChargePumpCurrentBox.SelectedIndex * Math.Pow(2, 9) +
                    LDFBox.SelectedIndex * Math.Pow(2, 8) +
                    LDPBox.SelectedIndex * Math.Pow(2, 7) +
                    PDPolarityBox.SelectedIndex * Math.Pow(2, 6) +
                    PowerdownBox.SelectedIndex * Math.Pow(2, 5) +
                    CP3StateBox.SelectedIndex * Math.Pow(2, 4) +
                    CounterResetBox.SelectedIndex * Math.Pow(2, 3) +
                    2
                    );
                Reg[3] = (uint)(
                    BandSelectClockModeBox.SelectedIndex * Math.Pow(2, 23) +
                    ABPBox.SelectedIndex * Math.Pow(2, 22) +
                    ChargeCancellationBox.SelectedIndex * Math.Pow(2, 21) +
                    CSRBox.SelectedIndex * Math.Pow(2, 18) +
                    CLKDivModeBox.SelectedIndex * Math.Pow(2, 15) +
                    (double)ClockDividerValueBox.Value * Math.Pow(2, 3) +
                    3
                    );
                Reg[4] = (uint)(
                    FeedbackSelectBox.SelectedIndex * Math.Pow(2, 23) +
                    Math.Log(Convert.ToInt16(OutputDividerBox.Text), 2) * Math.Pow(2, 20) +
                    (double)BandSelectClockDividerBox.Value * Math.Pow(2, 12) +
                    VCOPowerdownBox.SelectedIndex * Math.Pow(2, 11) +
                    MTLDBox.SelectedIndex * Math.Pow(2, 10) +
                    AuxOutputSelectBox.SelectedIndex * Math.Pow(2, 9) +
                    AuxOutputEnableBox.SelectedIndex * Math.Pow(2, 8) +
                    AuxOutputPowerBox.SelectedIndex * Math.Pow(2, 6) +
                    RFOutputEnableBox.SelectedIndex * Math.Pow(2, 5) +
                    RFOutputPowerBox.SelectedIndex * Math.Pow(2, 3) +
                    4
                    );
                Reg[5] = (uint)(
                    LDPinModeBox.SelectedIndex * Math.Pow(2, 22) +
                    ReadSelBox.SelectedIndex * Math.Pow(2, 21) +
                    ICPADJENBox.SelectedIndex * Math.Pow(2, 19) +
                    SDTestmodesBox.SelectedIndex * Math.Pow(2, 15) +
                    PLLTestmodesBox.SelectedIndex * Math.Pow(2, 11) +
                    PDSynthBox.SelectedIndex * Math.Pow(2, 10) +
                    ExtBandEnBox.SelectedIndex * Math.Pow(2, 9) +
                    BandSelectBox.SelectedIndex * Math.Pow(2, 5) +
                    VCOSelBox.SelectedIndex * Math.Pow(2, 3) +
                    5
                    );
            }
            #endregion




            R0Box.Text = String.Format("{0:X}", Reg[0]);
            R1Box.Text = String.Format("{0:X}", Reg[1]);
            R2Box.Text = String.Format("{0:X}", Reg[2]);
            R3Box.Text = String.Format("{0:X}", Reg[3]);
            R4Box.Text = String.Format("{0:X}", Reg[4]);
            R5Box.Text = String.Format("{0:X}", Reg[5]);


            if (Reg[0] != Rprevious[0])
                R0Box.BackColor = Color.LightGreen;
            if (Reg[1] != Rprevious[1])
                R1Box.BackColor = Color.LightGreen;
            if (Reg[2] != Rprevious[2])
                R2Box.BackColor = Color.LightGreen;
            if (Reg[3] != Rprevious[3])
                R3Box.BackColor = Color.LightGreen;
            if (Reg[4] != Rprevious[4])
                R4Box.BackColor = Color.LightGreen;
            if (Reg[5] != Rprevious[5])
                R5Box.BackColor = Color.LightGreen;

            #endregion

            #region Misc stuff and error check
            UpdateVCOChannelSpacing();
            UpdateVCOOutputFrequencyBox();

            if (CLKDivModeBox.SelectedIndex == 2)
            {
                TsyncLabel.Visible = true;
                TsyncLabel.Text = "Tsync = " + ((1 / (double)PFDFreq) * MOD * Convert.ToInt32(ClockDividerValueBox.Value)).ToString() + " us";
            }
            else
                TsyncLabel.Visible = false;

            if (Autowrite.Checked)
                WriteAllButton.PerformClick();

            if (MOD > 4095)
            {
                //log("MOD must be less than or equal to 4095.");
                //MODBox.BackColor = Color.Tomato;
                MODWarningIcon.Visible = true;
            }
            else
            {
                //MODBox.BackColor = SystemColors.Control;
                MODWarningIcon.Visible = false;
            }

            if (Convert.ToDouble(RFoutBox.Text) != Convert.ToDouble(RFOutFreqBox.Text))
            {
                //RFoutBox.BackColor = Color.Tomato;
                RFoutWarningIcon.Visible = true;
            }
            else
            {
                //RFoutBox.BackColor = SystemColors.Control;
                RFoutWarningIcon.Visible = false;
            }

            if ((PhaseAdjustBox.SelectedIndex == 1) && (EnableGCD.Checked))
            {
                PhaseAdjustWarningIcon.Visible = true;
            }
            else
                PhaseAdjustWarningIcon.Visible = false;

            Limit_Check();


            if (FeedbackSelectBox.SelectedIndex == 0)
                FeedbackFrequencyLabel.Text = Convert.ToDouble(RFoutBox.Text) + " MHz";
            else
                FeedbackFrequencyLabel.Text = (Convert.ToDouble(RFoutBox.Text) * (Convert.ToInt16(OutputDividerBox.Text))).ToString() + " MHz";

            if ((LowNoiseSpurModeBox.SelectedIndex == 3) && (MOD < 50))
                LowNoiseSpurModeWarningIcon.Visible = true;
            else
                LowNoiseSpurModeWarningIcon.Visible = false;

            WarningsCheck();

            #endregion
        }

        #endregion

        #region Device connections

        public void Connect_CyUSB()
        {

            // add thing to nullify SDP device if connected

            log("Attempting USB adapter board connection...");
            ConnectingLabel.Visible = true;
            FirmwareLoaded = false;
            Application.DoEvents();

            int PID = 0xB40D;
            int PID2 = 0xB403;

            connectedDevice = usbDevices[0x0456, PID] as CyFX2Device;
            if (connectedDevice != null)
                FirmwareLoaded = connectedDevice.LoadExternalRam(Application.StartupPath + "\\adf4xxx_usb_fw_2_0.hex");
            else
            {
                connectedDevice = usbDevices[0x0456, PID2] as CyFX2Device;
                if (connectedDevice != null)
                    FirmwareLoaded = connectedDevice.LoadExternalRam(Application.StartupPath + "\\adf4xxx_usb_fw_1_0.hex");
            }

            if (FirmwareLoaded)
            {
                log("Firmware loaded.");

                connectedDevice.ControlEndPt.Target = CyConst.TGT_DEVICE;
                connectedDevice.ControlEndPt.ReqType = CyConst.REQ_VENDOR;
                connectedDevice.ControlEndPt.Direction = CyConst.DIR_TO_DEVICE;
                connectedDevice.ControlEndPt.ReqCode = 0xDD;                       // DD references the function in the firmware ADF_uwave_2.hex to write to the chip
                connectedDevice.ControlEndPt.Value = 0;
                connectedDevice.ControlEndPt.Index = 0;

                DeviceConnectionStatus.Text = connectedDevice.FriendlyName + " connected.";
                DeviceConnectionStatus.ForeColor = Color.Green;

                log("USB adapter board connected.");
                ConnectDeviceButton.Enabled = false;
                protocol = false;

                #region USB Delay
                USBDelayBar.Visible = true;
                Thread.Sleep(1000);
                USBDelayBar.Value = 20;
                Application.DoEvents();
                Thread.Sleep(1000);
                USBDelayBar.Value = 40;
                Application.DoEvents();
                Thread.Sleep(1000);
                USBDelayBar.Value = 60;
                Application.DoEvents();
                Thread.Sleep(1000);
                USBDelayBar.Value = 80;
                Application.DoEvents();
                Thread.Sleep(1000);
                USBDelayBar.Value = 100;
                Application.DoEvents();
                USBDelayBar.Visible = false;
                log("USB ready.");
                #endregion
            }
            else
                log("No USB adapter board attached. Try unplugging and re-plugging the USB cable.");

            ConnectingLabel.Visible = false;

        }

        void usbDevices_DeviceAttached(object sender, EventArgs e)
        {
            USBEventArgs usbEvent = e as USBEventArgs;

            FirmwareLoaded = false;
            int PID = 0xB40D;
            int PID2 = 0xB403;

            connectedDevice = usbDevices[0x0456, PID] as CyFX2Device;
            if (connectedDevice != null)
                FirmwareLoaded = connectedDevice.LoadExternalRam(Application.StartupPath + "\\adf4xxx_usb_fw_2_0.hex");
            else
            {
                connectedDevice = usbDevices[0x0456, PID2] as CyFX2Device;
                if (connectedDevice != null)
                    FirmwareLoaded = connectedDevice.LoadExternalRam(Application.StartupPath + "\\adf4xxx_usb_fw_1_0.hex");
            }

            if (FirmwareLoaded)
            {
                log("Firmware loaded.");

                connectedDevice.ControlEndPt.Target = CyConst.TGT_DEVICE;
                connectedDevice.ControlEndPt.ReqType = CyConst.REQ_VENDOR;
                connectedDevice.ControlEndPt.Direction = CyConst.DIR_TO_DEVICE;
                connectedDevice.ControlEndPt.ReqCode = 0xDD;                       // DD references the function in the firmware ADF_uwave_2.hex to write to the chip
                connectedDevice.ControlEndPt.Value = 0;
                connectedDevice.ControlEndPt.Index = 0;

                DeviceConnectionStatus.Text = connectedDevice.FriendlyName + " connected.";
                DeviceConnectionStatus.ForeColor = Color.Green;

                log("USB adapter board connected.");
                ConnectDeviceButton.Enabled = false;
                protocol = false;

                #region USB Delay
                USBDelayBar.Visible = true;
                Thread.Sleep(1000);
                USBDelayBar.Value = 20;
                Application.DoEvents();
                Thread.Sleep(1000);
                USBDelayBar.Value = 40;
                Application.DoEvents();
                Thread.Sleep(1000);
                USBDelayBar.Value = 60;
                Application.DoEvents();
                Thread.Sleep(1000);
                USBDelayBar.Value = 80;
                Application.DoEvents();
                Thread.Sleep(1000);
                USBDelayBar.Value = 100;
                Application.DoEvents();
                USBDelayBar.Visible = false;
                log("USB ready.");
                #endregion
            }
            else
                log("No USB adapter board attached. Try unplugging and re-plugging the USB cable.");

            ConnectingLabel.Visible = false;
        }

        void usbDevices_DeviceRemoved(object sender, EventArgs e)
        {
            USBEventArgs usbEvent = e as USBEventArgs;

            log("USB device removal detected.");

            if (USBselector.Checked)
            {
                DeviceConnectionStatus.Text = usbEvent.FriendlyName + " removed.";
                DeviceConnectionStatus.ForeColor = Color.Tomato;
            }
            connectedDevice = null;
            ConnectDeviceButton.Enabled = true;
        }

        public void Connect_SDP()
        {
            String message;

            log("Attempting SDP connection...");

            try
            {

                sdp = new SdpBase();

                SdpManager.connectVisualStudioDialog("6065711100000001", "", false, out sdp);

                log("Flashing LED.");
                sdp.flashLed1();
                messageShownToUser = false;
                try
                {
                    sdp.programSdram(Application.StartupPath + "\\SDP_Blackfin_Firmware.ldr", true, true);
                    sdp.reportBootStatus(out message);

                    try
                    {
                        configNormal(sdp.ID1Connector, 0);
                    }
                    catch (Exception e)
                    {
                        if ((e is SdpApiErrEx) && (e as SdpApiErrEx).number == SdpApiErr.FunctionNotSupported)
                        {
                            if (e.Message.Substring(17) == "Use Connector A")
                            {
                                MessageBox.Show(e.Message.Substring(17));
                                messageShownToUser = true;
                                sdp.Unlock();
                                throw new Exception();
                                // Disconnect from SDP-B Rev B
                            }
                            else if (e.Message.Substring(17) == "For optimal performance ensure CLKOUT is disabled")
                            {
                                MessageBox.Show("Remove R57 from the SDP board to ensure optimum performance", "Warning!");
                                messageShownToUser = true;
                                // Ok to continue. User must have removed R57 to ensure expected performance
                            }
                            else
                                throw e;
                        }
                        else
                            throw e;
                    }
                }
                catch (Exception e)
                {
                    if ((e is SdpApiWarnEx) && (e as SdpApiWarnEx).number == SdpApiWarn.NonFatalFunctionNotSupported)
                    { }
                    else
                        throw e;
                }

                ConnectDeviceButton.Enabled = false;

                DeviceConnectionStatus.Text = "SDP board connected. Using " + sdp.ID1Connector.ToString(); ;
                DeviceConnectionStatus.ForeColor = Color.Green;
                protocol = true;
                log("SDP connected.");

                sdp.newSpi(sdp.ID1Connector, 0, 32, false, false, false, 4000000, 0, out session);

                sdp.newGpio(sdp.ID1Connector, out g_session);
                g_session.configOutput(0x1);
                g_session.bitSet(0x01);

                // id1Connector = ConnectorA on the adapter board
                // 0 = use SPI_SEL_A for LE
                // wordSize = 32
                // false = clock polarity
                // false = clock phase
                // 4,000,000 = clock frequency
                // 0 = frame frequency (irrelevant because only using 1 frame)
                // s = this is the 'session' of the connection for the SPI                

            }
            catch
            {
                sdp.Unlock();
                MessageBox.Show("SDP connection failed");
                log("SDP connection failed.");
            }
        }

        #endregion

        #region Top Menu options
        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {

            log("Exiting.");

            
            this.Close();

        }

        string version = "4.4.0";
        string version_date = "October 2014";

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Analog Devices ADF435x software - v" + version + " - " + version_date);

            // v4.4.0 - Added random hop feature
            // v4.3.6 - Changed band select clock divider autoset to 500 kHz for band select clock mode high.
            //        - Minor bugs and stuff
            // v4.3.5 - Improved maximum PFD frequency warning to handle high PFD frequencies in Int-N mode.
            // v4.3.4 - Bug fix.
            //          Added warning when using Phase adjust with MOD GCD. 
            //          Added option to disable event log during sweep.
        }

        private void SaveConfigurationStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog SaveConfiguration = new SaveFileDialog();
            SaveConfiguration.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
            SaveConfiguration.Title = "Save a configuration file";
            SaveConfiguration.FileName = "ADF435x_settings.txt";

            #region Which part?
            if (ADF4350.Checked)
            {
                SaveConfiguration.FileName = "ADF4350_settings.txt";
            }
            else if (ADF4351.Checked)
            {
                SaveConfiguration.FileName = "ADF4351_settings.txt";
            }

            #endregion

            SaveConfiguration.ShowDialog();
            System.IO.File.WriteAllText(SaveConfiguration.FileName, "");

            SaveControls(TabControl, ref SaveConfiguration);

            SaveConfiguration.Dispose();
        }

        private void LoadConfigurationStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PartInUseLabel2.Text != "None")
            {
                OpenFileDialog LoadConfiguration = new OpenFileDialog();
                LoadConfiguration.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
                LoadConfiguration.ShowDialog();

                if (LoadConfiguration.FileName != "")
                {
                    string[] contents = System.IO.File.ReadAllLines(LoadConfiguration.FileName);
                    LoadControls(TabControl, contents);
                }

                LoadIndex = -1;
                LoadConfiguration.Dispose();
            }
            else
                MessageBox.Show("Select a part first.");

        }
        #endregion

        #region Other

        private void log(string message)
        {
            if (enableEventLogToolStripMenuItem.Checked)
            {
                DateTime time = DateTime.Now;
                string hour = time.Hour.ToString();
                string minute = time.Minute.ToString();
                string second = time.Second.ToString();

                if (hour.Length == 1)
                    hour = "0" + hour;
                if (minute.Length == 1)
                    minute = "0" + minute;
                if (second.Length == 1)
                    second = "0" + second;

                //EventLog.Text += "\r\n" + hour + ":" + minute + ":" + second + ": " + message;
                EventLog.AppendText("\r\n" + hour + ":" + minute + ":" + second + ": " + message);
                
                EventLog.Update();
                EventLog.SelectionStart = EventLog.Text.Length;
                EventLog.ScrollToCaret();
            }
        }

        private void ConnectDeviceButton_Click(object sender, EventArgs e)
        {
            if (USBselector.Checked)
                Connect_CyUSB();
            else if (SDPSelector.Checked)
                Connect_SDP();
        }

        private void SDPsPicture_Click(object sender, EventArgs e)
        {
            SDPSelector.Checked = true;
        }

        private void USBadapterPicture_Click(object sender, EventArgs e)
        {
            USBselector.Checked = true;
        }

        private void exitEventHandler(object sender, System.EventArgs e)
        {
            try
            {
                if (sdp != null)
                    sdp.Unlock();
            }
            catch { }

            try
            {
                if (connectedDevice != null)
                {
                    connectedDevice.Reset();
                    connectedDevice.Dispose();
                }
            }
            catch { }
        }

        #endregion

        #region Write to device

        private void WriteToDevice(uint data)
        {
            uint[] toWrite = new uint[1];
            int x = 1;                                          // for checking the result of .writeU32()

            if (protocol)                                       // protocol: true = SDP, false = CyUSB
            {
                if (session != null)
                {
                    toWrite[0] = data;

                    //if (UseSPI_SEL_BOption.Checked)
                    //    session.slaveSelect = SpiSel.selB;
                    //else
                    //    session.slaveSelect = SpiSel.selA;

                    session.slaveSelect = UseSPI_SEL_BOption.Checked ? SpiSel.selB : SpiSel.selA;

                    configNormal(sdp.ID1Connector, 3);
                    g_session.bitClear(0x1);                    // Clear GPIO0 pin (LE)
                    x = session.writeU32(toWrite);              // Write SPI CLK and DATA (and CS)
                    g_session.bitSet(0x1);                      // Set GPIO0 pin (LE)
                    configQuiet(sdp.ID1Connector, 3);

                    if (x == 0)
                        log("0x" + String.Format("{0:X}", data) + " written to device.");
                }
                else
                    log("Writing failed.");
            }
            else
            {
                if (connectedDevice != null)
                {
                    for (int i = 0; i < 4; i++)
                        buffer[i] = (byte)(data >> (i * 8));

                    buffer[4] = 32;
                    buffer_length = 5;

                    XferSuccess = connectedDevice.ControlEndPt.XferData(ref buffer, ref buffer_length);

                    if (XferSuccess)
                        log("0x" + String.Format("{0:X}", data) + " written to device.");
                }
                else
                    log("Writing failed.");
            }
        }

        #endregion

        #region Change Part stuff

        private void resetToDefaultValuesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ChangePart(sender, e);
        }

        private void ResetAllBoxes()
        {
            ABPBox.Visible = true;
            ABPLabel.Visible = true;
            ChargeCancellationBox.Visible = true;
            ChargeCancellationLabel.Visible = true;
            BandSelectClockModeBox.Visible = true;
            BandSelectModeLabel.Visible = true;
            PhaseAdjustBox.Visible = true;
            PhaseAdjustLabel.Visible = true;

            RFoutMin = 34.375;
        }

        private void ChangePart(object sender, EventArgs e)
        {
            DeviceWarningIcon.Visible = false;
            WarningsCheck();
            ResetAllBoxes();

            PFDFreqBox.Text = "200";


            #region ADF4350
            if (ADF4350.Checked)
            {
                PartInUseLabel2.Text = "ADF4350";

                ABPBox.Visible = false;
                ABPBox.SelectedIndex = 0;
                ABPLabel.Visible = false;
                ChargeCancellationBox.Visible = false;
                ChargeCancellationBox.SelectedIndex = 0;
                ChargeCancellationLabel.Visible = false;
                BandSelectClockModeBox.Visible = false;
                BandSelectClockModeBox.SelectedIndex = 0;
                BandSelectModeLabel.Visible = false;
                PhaseAdjustBox.Visible = false;
                PhaseAdjustBox.SelectedIndex = 0;
                PhaseAdjustLabel.Visible = false;

                RFoutMin = 137.5;

            }
            #endregion
            #region ADF4351
            if (ADF4351.Checked)
            {
                PartInUseLabel2.Text = "ADF4351";
            }
            #endregion

            InitializeMenus();
            BuildRegisters();


        }

        private void InitializeMenus()
        {
            FeedbackSelectBox.SelectedIndex = 1;
            PrescalerBox.SelectedIndex = 1;

            PhaseAdjustBox.SelectedIndex = 0;
            PDPolarityBox.SelectedIndex = 1;
            LowNoiseSpurModeBox.SelectedIndex = 0;
            MuxoutBox.SelectedIndex = 0;
            DoubleBuffBox.SelectedIndex = 0;
            ChargePumpCurrentBox.SelectedIndex = 7;
            LDFBox.SelectedIndex = 0;
            LDPBox.SelectedIndex = 0;

            PowerdownBox.SelectedIndex = 0;
            CP3StateBox.SelectedIndex = 0;
            CounterResetBox.SelectedIndex = 0;

            BandSelectClockModeBox.SelectedIndex = 0;
            ABPBox.SelectedIndex = 0;
            ChargeCancellationBox.SelectedIndex = 0;
            CSRBox.SelectedIndex = 0;
            CLKDivModeBox.SelectedIndex = 0;

            VCOPowerdownBox.SelectedIndex = 0;
            MTLDBox.SelectedIndex = 0;
            AuxOutputSelectBox.SelectedIndex = 0;
            AuxOutputEnableBox.SelectedIndex = 0;
            AuxOutputPowerBox.SelectedIndex = 0;
            RFOutputEnableBox.SelectedIndex = 1;
            RFOutputPowerBox.SelectedIndex = 3;

            LDPinModeBox.SelectedIndex = 1;

            ReadSelBox.SelectedIndex = 0;
            ICPADJENBox.SelectedIndex = 3;
            SDTestmodesBox.SelectedIndex = 0;
            PLLTestmodesBox.SelectedIndex = 0;
            PDSynthBox.SelectedIndex = 0;
            ExtBandEnBox.SelectedIndex = 0;
            BandSelectBox.SelectedIndex = 0;
            VCOSelBox.SelectedIndex = 0;

            SoftwareVersionLabel.Text = version;
        }

        #endregion

        #region Other stuff

        private void MainControlsTab_Enter(object sender, EventArgs e)
        {
            if ((ADF4350.Checked) || (ADF4351.Checked))
                DeviceWarningIcon.Visible = false;
            else
                DeviceWarningIcon.Visible = true;

            WarningsCheck();
        }

        private void ChannelUpDownButton_ValueChanged(object sender, EventArgs e)
        {
            if (ChannelUpDownButton.Value > ChannelUpDownCount)
            {
                RFOutFreqBox.Text = (Convert.ToDouble(RFOutFreqBox.Text) + (Convert.ToDouble(OutputChannelSpacingBox.Text) / 1000)).ToString();
            }
            else
            {
                RFOutFreqBox.Text = (Convert.ToDouble(RFOutFreqBox.Text) - (Convert.ToDouble(OutputChannelSpacingBox.Text) / 1000)).ToString();
            }

            ChannelUpDownCount = (int)ChannelUpDownButton.Value;
        }

        private uint GCD(uint a, uint b)
        {
            if (a == 0)
                return b;
            if (b == 0)
                return a;

            if (a > b)
                return GCD(a % b, b);
            else
                return GCD(a, b % a);
        }

        private void websiteStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://www.analog.com/en/rfif-components/pll-synthesizersvcos/products/index.html");
        }

        private void ADIsimPLLLink_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://forms.analog.com/form_pages/rfcomms/adisimpll.asp");
        }

        private void EngineerZoneLink_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://ez.analog.com/community/rf");
        }

        private void aDF4350ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://www.analog.com/adf4350");
        }

        private void aDF4351ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://www.analog.com/adf4351");
        }

        private void ADILogo_Click(object sender, EventArgs e)
        {
            TestmodesGroup.Visible = !TestmodesGroup.Visible;
            TabControl.SelectedIndex = 4;
        }

        private void UpdateVCOChannelSpacing()
        {
            if (Convert.ToDouble(RFOutFreqBox.Text) < 2200)
                VCOChannelSpacingBox.Text = (Convert.ToDouble(OutputChannelSpacingBox.Text) * Convert.ToInt16(OutputDividerBox.Text)).ToString();
            else
                VCOChannelSpacingBox.Text = OutputChannelSpacingBox.Text;
        }

        private void UpdateVCOOutputFrequencyBox()
        {
            if (Convert.ToDouble(RFOutFreqBox.Text) < 2200)
                VCOFreqBox.Text = (Convert.ToDouble(RFOutFreqBox.Text) * Convert.ToInt16(OutputDividerBox.Text)).ToString();
            else
                VCOFreqBox.Text = RFOutFreqBox.Text;
        }

        private void BandSelectClockAutosetBox_CheckedChanged(object sender, EventArgs e)
        {
            BandSelectClockDividerBox.Enabled = (BandSelectClockAutosetBox.Checked) ? false : true;
        }

        private void UseSPI_SEL_BOption_Click(object sender, EventArgs e)
        {
            UsingSPISELBLabel.Visible = UseSPI_SEL_BOption.Checked ? true : false;
        }

        private void enableEventLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (enableEventLogToolStripMenuItem.Checked)
            {
                log("Disabling event log. Re-enable in Tools menu.");
                enableEventLogToolStripMenuItem.Checked = false;
            }
            else
            {
                enableEventLogToolStripMenuItem.Checked = true;
                log("Re-enabled event log.");
            }
        }

        #endregion

        #region Error checking

        private void RFOutFreqBox_TextChanged(object sender, EventArgs e)
        {
            try
            {
                RFout = Convert.ToDouble(RFOutFreqBox.Text);

                if ((RFout > RFoutMax) || (RFout < RFoutMin))
                {
                    RFWarningIcon.Visible = true;
                }
                else
                {
                    RFWarningIcon.Visible = false;
                }

                BuildRegisters();
            }
            catch
            {
                RFWarningIcon.Visible = true;
            }
        }

        private void RefFreqBox_TextChanged(object sender, EventArgs e)
        {
            try
            {
                REFin = Convert.ToDouble(RefFreqBox.Text);

                if (REFin > REFinMax)
                {
                    //StatusBarLabel.Text = "Reference input frequency too high!";
                    //RefFreqBox.BackColor = Color.Tomato;
                    ReferenceFrequencyWarningIcon.Visible = true;
                }
                else
                {
                    //StatusBarLabel.Text = "";
                    //RefFreqBox.BackColor = Color.White;
                    ReferenceFrequencyWarningIcon.Visible = false;
                }

                BuildRegisters();
            }
            catch
            {
                ReferenceFrequencyWarningIcon.Visible = true;   
            }
        }

        private void OutputChannelSpacingBox_TextChanged(object sender, EventArgs e)
        {
            try
            {
                OutputChannelSpacing = Convert.ToDouble(OutputChannelSpacingBox.Text);
                StatusBarLabel.Text = "";
                BuildRegisters();
            }
            catch
            {
                StatusBarLabel.Text = "Invalid channel spacing input. Please enter a numeric value.";
            }
        }

        private void Limit_Check()
        {

            if ((ADF4350.Checked) && (PrescalerBox.SelectedIndex == 0) && (RFout > 3000))
            {
                PrescalerWarning(true);
            }
            else if((ADF4351.Checked) && (PrescalerBox.SelectedIndex == 0) && (RFout > 3600))
            {
                PrescalerWarning(true);
            }
            else
            {
                PrescalerWarning(false);
            }



            //if ((PrescalerBox.SelectedIndex == 1) & (N < 75))
            //    PrescalerWarning(true);
            //else
            //    PrescalerWarning(false);

            if ((N < 23) | (N > 65635))
            {
                //StatusBarLabel.Text = "Warning! N value should be between 23 and 65535 inclusive.";
                //log("Warning! N value should be between 23 and 65535 inclusive.");
                //INTBox.BackColor = Color.Tomato;
                INTWarningIcon.Visible = true;
            }
            else
            {
                //StatusBarLabel.Text = "";
                //INTBox.BackColor = SystemColors.Control;
                INTWarningIcon.Visible = false;
            }

        }

        private void PrescalerWarning(bool isError)
        {
            if (isError)
            {
                //StatusBarLabel.Text = "Warning! Prescaler output too high. Try changing prescaler...";
                //log("Warning! Prescaler output too high. Try changing prescaler...");
                //PrescalerBox.BackColor = Color.Tomato;
                PrescalerWarningIcon.Visible = true;
            }
            else
            {
                //StatusBarLabel.Text = "";
                //PrescalerBox.BackColor = Color.White;
                PrescalerWarningIcon.Visible = false;
            }
        }

        private void WarningsCheck()
        {
            if (
             (RFWarningIcon.Visible) ||
             (ReferenceFrequencyWarningIcon.Visible) ||
             (PFDWarningIcon.Visible) ||
             (PrescalerWarningIcon.Visible) ||
             (INTWarningIcon.Visible) ||
             (MODWarningIcon.Visible) ||
             (RFoutWarningIcon.Visible) ||
             (LowNoiseSpurModeWarningIcon.Visible) ||
             (BSCWarning1Icon.Visible) ||
             (BSCWarning2Icon.Visible) ||
             (BSCWarning3Icon.Visible) ||
             (PhaseAdjustWarningIcon.Visible) ||
             (DeviceWarningIcon.Visible)
             )
                WarningsPanel.Visible = true;
            else
                WarningsPanel.Visible = false;
        }

        #endregion

        #region Write Register Buttons

        private void WriteR5Button_Click(object sender, EventArgs e)
        {
            log("Writing R5...");
            WriteToDevice(Reg[5]);
            R5Box.BackColor = SystemColors.Control;
        }

        private void WriteR4Button_Click(object sender, EventArgs e)
        {
            log("Writing R4...");
            WriteToDevice(Reg[4]);
            R4Box.BackColor = SystemColors.Control;
        }

        private void WriteR3Button_Click(object sender, EventArgs e)
        {
            log("Writing R3...");
            WriteToDevice(Reg[3]);
            R3Box.BackColor = SystemColors.Control;
        }

        private void WriteR2Button_Click(object sender, EventArgs e)
        {
            log("Writing R2...");
            WriteToDevice(Reg[2]);
            R2Box.BackColor = SystemColors.Control;
        }

        private void WriteR1Button_Click(object sender, EventArgs e)
        {
            log("Writing R1...");
            WriteToDevice(Reg[1]);
            R1Box.BackColor = SystemColors.Control;
        }

        private void WriteR0Button_Click(object sender, EventArgs e)
        {
            log("Writing R0...");
            WriteToDevice(Reg[0]);
            R0Box.BackColor = SystemColors.Control;
        }

        private void WriteAllButton_Click(object sender, EventArgs e)
        {

            log("Writing R5...");
            WriteToDevice(Reg[5]);

            log("Writing R4...");
            WriteToDevice(Reg[4]);

            log("Writing R3...");
            WriteToDevice(Reg[3]);

            log("Writing R2...");
            WriteToDevice(Reg[2]);

            log("Writing R1...");
            WriteToDevice(Reg[1]);

            Thread.Sleep(10);

            log("Writing R0...");
            WriteToDevice(Reg[0]);


            R0Box.BackColor = SystemColors.Control;
            R1Box.BackColor = SystemColors.Control;
            R2Box.BackColor = SystemColors.Control;
            R3Box.BackColor = SystemColors.Control;
            R4Box.BackColor = SystemColors.Control;
            R5Box.BackColor = SystemColors.Control;
        }

        private void DirectWriteButton_Click(object sender, EventArgs e)
        {
            try
            {
                uint value = Convert.ToUInt32(DirectWriteBox.Text, 16);
                log("Writing 0x" + String.Format("{0:X}", value));
                WriteToDevice(value);
            }
            catch { }
        }

        #endregion

        #region Registers tab

        private void FillMainControlsFromRegisters(object sender, EventArgs e)
        {
            int R0 = 0, R1 = 1, R2 = 2, R3 = 3, R4 = 4, R5 = 5;
            double i, f, m;

            StatusBarLabel.Text = "";

            #region Take inputs
            try
            {
                R0 = Convert.ToInt32(R0HexBox.Text, 16);
            }
            catch
            {
                log("Error with R0 hex input");
            }
            try
            {
                R1 = Convert.ToInt32(R1HexBox.Text, 16);
            }
            catch
            {
                log("Error with R1 hex input");
            }
            try
            {
                R2 = Convert.ToInt32(R2HexBox.Text, 16);
            }
            catch
            {
                log("Error with R2 hex input");
            }
            try
            {
                R3 = Convert.ToInt32(R3HexBox.Text, 16);
            }
            catch
            {
                log("Error with R3 hex input");
            }
            try
            {
                R4 = Convert.ToInt32(R4HexBox.Text, 16);
            }
            catch
            {
                log("Error with R4 hex input");
            }
            try
            {
                R5 = Convert.ToInt32(R5HexBox.Text, 16);
            }
            catch
            {
                log("Error with R5 hex input");
            }

            #endregion

            #region ADF4350
            if (ADF4350.Checked)
            {
                i = (R0 >> 15) & 0xFFFF;
                f = (R0 >> 3) & 0xFFF;

                PrescalerBox.SelectedIndex = (R1 >> 27) & 0x1;
                PhaseValueBox.Value = (R1 >> 15) & 0xFFF;
                m = (R1 >> 3) & 0xFFF;

                LowNoiseSpurModeBox.SelectedIndex = (R2 >> 29) & 0x3;
                MuxoutBox.SelectedIndex = (R2 >> 26) & 0x7;
                RefDoublerBox.Checked = ((((R2 >> 25) & 0x1) == 1) ? true : false);
                RefD2Box.Checked = ((((R2 >> 24) & 0x1) == 1) ? true : false);
                RcounterBox.Value = (R2 >> 14) & 0x3FF;
                DoubleBuffBox.SelectedIndex = (R2 >> 13) & 0x1;
                ChargePumpCurrentBox.SelectedIndex = (R2 >> 9) & 0xF;
                LDFBox.SelectedIndex = (R2 >> 8) & 0x1;
                LDPBox.SelectedIndex = (R2 >> 7) & 0x1;
                PDPolarityBox.SelectedIndex = (R2 >> 6) & 0x1;
                PowerdownBox.SelectedIndex = (R2 >> 5) & 0x1;
                CP3StateBox.SelectedIndex = (R2 >> 4) & 0x1;
                CounterResetBox.SelectedIndex = (R2 >> 3) & 0x1;

                CSRBox.SelectedIndex = (R3 >> 18) & 0x1;
                CLKDivModeBox.SelectedIndex = (R3 >> 15) & 0x3;
                ClockDividerValueBox.Value = (R3 >> 3) & 0xFFF;

                FeedbackSelectBox.SelectedIndex = (R4 >> 23) & 0x1;
                OutputDividerBox.Text = (Math.Pow(2, ((R4 >> 20) & 0x7))).ToString();
                BandSelectClockDividerBox.Value = (R4 >> 12) & 0xFF;
                VCOPowerdownBox.SelectedIndex = (R4 >> 11) & 0x1;
                MTLDBox.SelectedIndex = (R4 >> 10) & 0x1;
                AuxOutputSelectBox.SelectedIndex = (R4 >> 9) & 0x1;
                AuxOutputEnableBox.SelectedIndex = (R4 >> 8) & 0x1;
                AuxOutputPowerBox.SelectedIndex = (R4 >> 6) & 0x3;
                RFOutputEnableBox.SelectedIndex = (R4 >> 5) & 0x1;
                RFOutputPowerBox.SelectedIndex = (R4 >> 3) & 0x3;

                LDPinModeBox.SelectedIndex = (R5 >> 22) & 0x3;
                ReadSelBox.SelectedIndex = (R5 >> 21) & 0x1;
                ICPADJENBox.SelectedIndex = (R5 >> 19) & 0x3;
                SDTestmodesBox.SelectedIndex = (R5 >> 15) & 0xF;
                PLLTestmodesBox.SelectedIndex = (R5 >> 11) & 0xF;
                PDSynthBox.SelectedIndex = (R5 >> 10) & 0x1;
                ExtBandEnBox.SelectedIndex = (R5 >> 9) & 0x1;
                BandSelectBox.SelectedIndex = (R5 >> 5) & 0xF;
                VCOSelBox.SelectedIndex = (R5 >> 3) & 0x3;

                PFDFreq = (decimal)(Convert.ToDouble(RefFreqBox.Text) / (double)RcounterBox.Value * ((RefDoublerBox.Checked) ? 2 : 1) * ((RefD2Box.Checked) ? 0.5 : 1));
                PFDFreqBox.Text = PFDFreq.ToString();
                RFout = (double)PFDFreq * (i + (f / m)) / (Convert.ToDouble(OutputDividerBox.Text));
                RFOutFreqBox.Text = RFout.ToString();
            }
            #endregion

            #region ADF4351
            if (ADF4351.Checked)
            {
                i = (R0 >> 15) & 0xFFFF;
                f = (R0 >> 3) & 0xFFF;

                PhaseAdjustBox.SelectedIndex = (R1 >> 28) & 0x1;
                PrescalerBox.SelectedIndex = (R1 >> 27) & 0x1;
                PhaseValueBox.Value = (R1 >> 15) & 0xFFF;
                m = (R1 >> 3) & 0xFFF;

                LowNoiseSpurModeBox.SelectedIndex = (R2 >> 29) & 0x3;
                MuxoutBox.SelectedIndex = (R2 >> 26) & 0x7;
                RefDoublerBox.Checked = ((((R2 >> 25) & 0x1) == 1) ? true : false);
                RefD2Box.Checked = ((((R2 >> 24) & 0x1) == 1) ? true : false);
                RcounterBox.Value = (R2 >> 14) & 0x3FF;
                DoubleBuffBox.SelectedIndex = (R2 >> 13) & 0x1;
                ChargePumpCurrentBox.SelectedIndex = (R2 >> 9) & 0xF;
                LDFBox.SelectedIndex = (R2 >> 8) & 0x1;
                LDPBox.SelectedIndex = (R2 >> 7) & 0x1;
                PDPolarityBox.SelectedIndex = (R2 >> 6) & 0x1;
                PowerdownBox.SelectedIndex = (R2 >> 5) & 0x1;
                CP3StateBox.SelectedIndex = (R2 >> 4) & 0x1;
                CounterResetBox.SelectedIndex = (R2 >> 3) & 0x1;

                BandSelectClockModeBox.SelectedIndex = (R3 >> 23) & 0x1;
                ABPBox.SelectedIndex = (R3 >> 22) & 0x1;
                ChargeCancellationBox.SelectedIndex = (R3 >> 21) & 0x1;
                CSRBox.SelectedIndex = (R3 >> 18) & 0x1;
                CLKDivModeBox.SelectedIndex = (R3 >> 15) & 0x3;
                ClockDividerValueBox.Value = (R3 >> 3) & 0xFFF;

                FeedbackSelectBox.SelectedIndex = (R4 >> 23) & 0x1;
                OutputDividerBox.Text = (Math.Pow(2, ((R4 >> 20) & 0x7))).ToString();
                BandSelectClockDividerBox.Value = (R4 >> 12) & 0xFF;
                VCOPowerdownBox.SelectedIndex = (R4 >> 11) & 0x1;
                MTLDBox.SelectedIndex = (R4 >> 10) & 0x1;
                AuxOutputSelectBox.SelectedIndex = (R4 >> 9) & 0x1;
                AuxOutputEnableBox.SelectedIndex = (R4 >> 8) & 0x1;
                AuxOutputPowerBox.SelectedIndex = (R4 >> 6) & 0x3;
                RFOutputEnableBox.SelectedIndex = (R4 >> 5) & 0x1;
                RFOutputPowerBox.SelectedIndex = (R4 >> 3) & 0x3;

                LDPinModeBox.SelectedIndex = (R5 >> 22) & 0x3;
                ReadSelBox.SelectedIndex = (R5 >> 21) & 0x1;
                ICPADJENBox.SelectedIndex = (R5 >> 19) & 0x3;
                SDTestmodesBox.SelectedIndex = (R5 >> 15) & 0xF;
                PLLTestmodesBox.SelectedIndex = (R5 >> 11) & 0xF;
                PDSynthBox.SelectedIndex = (R5 >> 10) & 0x1;
                ExtBandEnBox.SelectedIndex = (R5 >> 9) & 0x1;
                BandSelectBox.SelectedIndex = (R5 >> 5) & 0xF;
                VCOSelBox.SelectedIndex = (R5 >> 3) & 0x3;

                PFDFreq = (decimal)(Convert.ToDouble(RefFreqBox.Text) / (double)RcounterBox.Value * ((RefDoublerBox.Checked) ? 2 : 1) * ((RefD2Box.Checked) ? 0.5 : 1));
                PFDFreqBox.Text = PFDFreq.ToString();
                RFout = (double)PFDFreq * (i + (f / m)) / (Convert.ToDouble(OutputDividerBox.Text));
                RFOutFreqBox.Text = RFout.ToString();
            }
            #endregion


            Reg[0] = (uint)R0;
            Reg[1] = (uint)R1;
            Reg[2] = (uint)R2;
            Reg[3] = (uint)R3;
            Reg[4] = (uint)R4;
            Reg[5] = (uint)R5;

            R0Box.Text = String.Format("{0:X}", Reg[0]);
            R1Box.Text = String.Format("{0:X}", Reg[1]);
            R2Box.Text = String.Format("{0:X}", Reg[2]);
            R3Box.Text = String.Format("{0:X}", Reg[3]);
            R4Box.Text = String.Format("{0:X}", Reg[4]);
            R5Box.Text = String.Format("{0:X}", Reg[5]);

        }

        private void TestFillButton_Click(object sender, EventArgs e)
        {
            FillMainControlsFromRegisters(this, e);
        }

        private void RegistersTab_Enter(object sender, EventArgs e)
        {
            R0HexBox.Text = R0Box.Text;
            R1HexBox.Text = R1Box.Text;
            R2HexBox.Text = R2Box.Text;
            R3HexBox.Text = R3Box.Text;
            R4HexBox.Text = R4Box.Text;
            R5HexBox.Text = R5Box.Text;


            if ((!ADF4350.Checked) & (!ADF4351.Checked))
                SelectADeviceWarningLabel.Visible = true;
            else
                SelectADeviceWarningLabel.Visible = false;
        }




        #endregion

        #region Sweep and hop

        private void SweepStartButton_Click(object sender, EventArgs e)
        {
            HopStopButton.PerformClick();

            SweepStartButton.Enabled = false;       // Update buttons on GUI
            SweepStopButton.Enabled = true;
            SweepActive = true;

            double SweepStop = 0, SweepCur = 0, SweepSpacing = 0, SweepStart = 0;
            int SweepDelay = 0, percent = 0, seconds_remaining = 0, minutes_remaining = 0, hours_remaining = 0;

            SweepProgress.Value = 0;                // Reset progress bar


            // Take inputs from GUI, and check for valid inputs
            try
            {
                SweepStop = double.Parse(SweepStopBox.Text);
            }
            catch
            {
                MessageBox.Show("Invalid Stop Frequency input!\n\nEnter numeric values only.\n\nValue reset to default.", "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SweepStopBox.Text = "4400".ToString();
                SweepCur = SweepStop + 1;
            }
            if (SweepStop > 8000)
            {
                SweepStop = 8000;
                SweepStopBox.Text = 8000.ToString();
            }

            try
            {
                SweepCur = double.Parse(SweepStartBox.Text);
            }
            catch
            {
                MessageBox.Show("Invalid Start Frequency input!\n\nEnter numeric values only.\n\nValue reset to default.", "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SweepStartBox.Text = "35".ToString();
                SweepCur = SweepStop + 1;
            }
            if (SweepCur < 1)
            {
                SweepCur = 1;
                SweepStartBox.Text = 1.ToString();
            }

            try
            {
                SweepSpacing = double.Parse(SweepSpacingBox.Text);
            }
            catch
            {
                MessageBox.Show("Invalid Spacing input!\n\nEnter numeric values only.\n\nValue reset to default.", "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SweepSpacingBox.Text = "1".ToString();
                SweepCur = SweepStop + 1;
            }


            try
            {
                SweepDelay = int.Parse(SweepDelayBox.Text);
            }
            catch
            {
                MessageBox.Show("Invalid Delay input!\n\nEnter numeric values only.\n\nValue reset to default.", "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SweepDelayBox.Text = "500".ToString();
                SweepCur = SweepStop + 1;
            }

            SweepStart = SweepCur;

            SweepCurrent.BackColor = Color.GreenYellow;
            SweepStopButton.BackColor = Color.GreenYellow;



            while ((SweepCur <= SweepStop) & SweepActive)
            {

                RFOutFreqBox.Text = SweepCur.ToString("0.000");
                BuildRegisters();
                WriteAllButton.PerformClick();

                SweepCurrent.Text = SweepCur.ToString("0.000");

                SweepCur += SweepSpacing;

                percent = (int)(((SweepCur - SweepStart) / (SweepStop - SweepStart)) * 100);
                percent = (percent > 100) ? 100 : percent;
                SweepProgress.Value = percent;
                SweepPercentage.Text = percent.ToString() + "%";   

                #region Timer

                seconds_remaining = (int)((((SweepStop - SweepCur) / SweepSpacing) * (SweepDelay + 15)) / 1000) + 1; // 15 is for time taking for execution

                while (seconds_remaining > 59)
                {
                    minutes_remaining++;
                    seconds_remaining -= 60;
                }
                while (minutes_remaining > 59)
                {
                    hours_remaining++;
                    minutes_remaining -= 60;
                }
                if (hours_remaining > 24)
                {
                    time_remaining.Text = "1 day+";
                }
                else
                    time_remaining.Text = hours_remaining.ToString("00") + ":" + minutes_remaining.ToString("00") + ":" + seconds_remaining.ToString("00");

                seconds_remaining = minutes_remaining = hours_remaining = 0;

                #endregion

                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(SweepDelay);

                if (SweepReturnStartBox.Checked)
                {
                    if (SweepCur == SweepStop)
                        SweepCur = SweepStart;
                }

            }

            percent = 0;
            SweepCur = 0;
            SweepStopButton.PerformClick();
        }

        private void SweepStopButton_Click(object sender, EventArgs e)
        {
            SweepActive = false;
            SweepProgress.Value = 100;
            SweepPercentage.Text = "100%";
            time_remaining.Text = "00:00:00";

            SweepCurrent.BackColor = Color.FromArgb(212, 208, 200);
            SweepStopButton.BackColor = Color.FromArgb(212, 208, 200);

            SweepStopButton.Enabled = false;
            SweepStartButton.Enabled = true;
        }

        private void HopStartButton_Click(object sender, EventArgs e)
        {
            SweepStopButton.PerformClick();

            HopStartButton.Enabled = false;
            HopStopButton.Enabled = true;

            double HopStart = 0, HopStop = 0, HopCur = 0;
            int HopDelay = 0;

            HopCurrent.BackColor = Color.GreenYellow;
            HopStopButton.BackColor = Color.GreenYellow;


            try
            {
                HopStart = double.Parse(HopFABox.Text);
            }
            catch
            {
                MessageBox.Show("Invalid Start Frequency input!\n\nEnter numeric values only.\n\nValue reset to default.", "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                HopFABox.Text = "1000".ToString();
            }
            if (HopStart < 1)
            {
                HopStart = 1;
                HopFABox.Text = 1.ToString();
            }

            try
            {
                HopStop = double.Parse(HopFBBox.Text);
            }
            catch
            {
                MessageBox.Show("Invalid Stop Frequency input!\n\nEnter numeric values only.\n\nValue reset to default.", "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                HopFBBox.Text = "2000".ToString();
            }
            if (HopStop > 8000)
            {
                HopStop = 8000;
                HopFBBox.Text = 8000.ToString();
            }

            try
            {
                HopDelay = int.Parse(HopDelayBox.Text);
            }
            catch
            {
                MessageBox.Show("Invalid Delay input!\n\nEnter numeric values only.\n\nValue reset to default.", "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                HopDelayBox.Text = "500".ToString();
            }



            HopCur = HopStart;
            RFOutFreqBox.Text = HopCur.ToString("0.000");
            BuildRegisters();
            WriteAllButton.PerformClick();

            HopCurrent.Text = HopCur.ToString("0.000");

            HopActive = true;

            while (HopActive)
            {
                HopCur = (HopCur == HopStart) ? HopStop : HopStart;

                RFOutFreqBox.Text = HopCur.ToString("0.000");
                BuildRegisters();
                WriteAllButton.PerformClick();

                HopCurrent.Text = HopCur.ToString("0.000");

                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(HopDelay);



            }
            HopStopButton.PerformClick();
        }

        private void HopStopButton_Click(object sender, EventArgs e)
        {
            HopActive = false;

            HopCurrent.BackColor = Color.FromArgb(212, 208, 200);
            HopStopButton.BackColor = Color.FromArgb(212, 208, 200);

            HopStopButton.Enabled = false;
            HopStartButton.Enabled = true;

            HopGroupBox.BackColor = Color.FromArgb(212, 208, 200);
        }

        #endregion

        #region SDP stuff
        private void configQuiet(SdpConnector connector, uint quietParam)
        {
            try
            {
                sdp.configQuiet(sdp.ID1Connector, quietParam);
            }
            catch (Exception e)
            {
                if ((e is SdpApiErrEx) && (e as SdpApiErrEx).number == SdpApiErr.FunctionNotSupported)
                {
                    if (messageShownToUser == false)
                    {
                        if (e.Message.Substring(17) == "SDPS: Quiet mode not supported")
                        { }
                        else
                            // Display message to user
                            throw e;
                    }
                }
                else
                    throw e;
            }
        }

        private void configNormal(SdpConnector connector, uint quietParam)
        {
            try
            {
                sdp.configNormal(sdp.ID1Connector, quietParam);
            }
            catch (Exception e)
            {
                if ((e is SdpApiErrEx) && (e as SdpApiErrEx).number == SdpApiErr.FunctionNotSupported)
                {
                    if (messageShownToUser == false)
                    {
                        if (e.Message.Substring(17) == "SDPS: Quiet mode not supported")
                        { }
                        else
                            // Display message to user
                            throw e;
                    }
                }
                else
                    throw e;
            }
        }

        private void FlashSDPLEDButton_Click(object sender, EventArgs e)
        {
            if ((protocol) & (session != null))
                sdp.flashLed1();
        }
        #endregion

        #region Save/Load configuration files
        private void SaveControls(Control ctrl, ref SaveFileDialog savefile)
        {
            foreach (Control c in ctrl.Controls)
            {
                if ((c is TextBox) | (c is CheckBox) | (c is NumericUpDown) | (c is RadioButton) | (c is ComboBox))
                {
                    if (c is TextBox)
                        System.IO.File.AppendAllText(savefile.FileName, ((System.Windows.Forms.TextBox)(c)).Text + "\r\n");

                    if (c is CheckBox)
                        System.IO.File.AppendAllText(savefile.FileName, ((System.Windows.Forms.CheckBox)(c)).Checked + "\r\n");

                    if (c is NumericUpDown)
                        System.IO.File.AppendAllText(savefile.FileName, ((System.Windows.Forms.NumericUpDown)(c)).Value + "\r\n");

                    if (c is RadioButton)
                        System.IO.File.AppendAllText(savefile.FileName, ((System.Windows.Forms.RadioButton)(c)).Checked + "\r\n");

                    if (c is ComboBox)
                        System.IO.File.AppendAllText(savefile.FileName, ((System.Windows.Forms.ComboBox)(c)).SelectedIndex + "\r\n");
                }
                else
                {
                    if (c.Controls.Count > 0)
                    {
                        SaveControls(c, ref savefile);
                    }
                }
            }
        }

        private void LoadControls(Control ctrl, string[] contents)
        {
            foreach (Control c in ctrl.Controls)
            {
                if ((c is TextBox) | (c is CheckBox) | (c is NumericUpDown) | (c is RadioButton) | (c is ComboBox))
                {
                    LoadIndex++;

                    if (c is TextBox)
                        ((System.Windows.Forms.TextBox)(c)).Text = contents[LoadIndex];

                    if (c is CheckBox)
                        ((System.Windows.Forms.CheckBox)(c)).Checked = Convert.ToBoolean(contents[LoadIndex]);

                    if (c is NumericUpDown)
                        ((System.Windows.Forms.NumericUpDown)(c)).Value = Convert.ToDecimal(contents[LoadIndex]);

                    if (c is RadioButton)
                        ((System.Windows.Forms.RadioButton)(c)).Checked = Convert.ToBoolean(contents[LoadIndex]);

                    if (c is ComboBox)
                        ((System.Windows.Forms.ComboBox)(c)).SelectedIndex = Convert.ToInt16(contents[LoadIndex]);
                }
                else
                {
                    if (c.Controls.Count > 0)
                    {
                        LoadControls(c, contents);
                    }
                }
            }
        }
        #endregion

        #region Write Hex buttons

        private void WriteR0HexButton_Click(object sender, EventArgs e)
        {
            FillMainControlsFromRegisters(this, e);
            WriteR0Button.PerformClick();
        }

        private void WriteR1HexButton_Click(object sender, EventArgs e)
        {
            FillMainControlsFromRegisters(this, e);
            WriteR1Button.PerformClick();
        }

        private void WriteR2HexButton_Click(object sender, EventArgs e)
        {
            FillMainControlsFromRegisters(this, e);
            WriteR2Button.PerformClick();
        }

        private void WriteR3HexButton_Click(object sender, EventArgs e)
        {
            FillMainControlsFromRegisters(this, e);
            WriteR3Button.PerformClick();
        }

        private void WriteR4HexButton_Click(object sender, EventArgs e)
        {
            FillMainControlsFromRegisters(this, e);
            WriteR4Button.PerformClick();
        }

        private void WriteR5HexButton_Click(object sender, EventArgs e)
        {
            FillMainControlsFromRegisters(this, e);
            WriteR5Button.PerformClick();
        }

        #endregion

        #region Import ADIsimPLL

        void importADIsimPLL(string ADIsimPLL_import_file)
        {
            IniParser.FileIniDataParser parser = new IniParser.FileIniDataParser();
            IniParser.IniData data = parser.LoadFile(ADIsimPLL_import_file);

            try
            {
                Control[] controls = this.Controls.Find(data["ChipSettings"]["Chip"], true);
                RadioButton control = controls[0] as RadioButton;
                control.Checked = true;
            }
            catch
            {
                MessageBox.Show("Invalid input file.");
            }

            resetToDefaultValuesToolStripMenuItem.PerformClick();

            #region ADF4350
            if (ADF4350.Checked | ADF4351.Checked)
            {

                if (data["Specifications"]["DesignFrequency"] != null)
                {
                    //RFOutFreqBox.Text = (((Convert.ToDouble(data["Specifications"]["DesignFrequency"])) / 1000000) / Convert.ToInt16(data["ChipSettings"]["VCODiv"])).ToString();
                    RFOutFreqBox.Text = (((Convert.ToDouble(data["Specifications"]["DesignFrequency"])) / 1000000)).ToString();
                }

                if (data["Specifications"]["ChSpc"] != null)
                {
                    //OutputChannelSpacingBox.Text = (((Convert.ToDouble(data["Specifications"]["ChSpc"])) / 1000) / Convert.ToInt16(data["ChipSettings"]["VCODiv"])).ToString();
                    OutputChannelSpacingBox.Text = (((Convert.ToDouble(data["Specifications"]["ChSpc"])) / 1000)).ToString();
                }

                if (data["ChipSettings"]["RefDoubler"] != null)
                    RefDoublerBox.Checked = Convert.ToBoolean(Convert.ToInt16(data["ChipSettings"]["RefDoubler"]));

                if (data["ChipSettings"]["RefDivide2"] != null)
                    RefD2Box.Checked = Convert.ToBoolean(Convert.ToInt16(data["ChipSettings"]["RefDivide2"]));

                if (data["ChipSettings"]["RefDivider"] != null)
                    RcounterBox.Value = Convert.ToInt16(data["ChipSettings"]["RefDivider"]);

                //not sure about this one
                if (data["ChipSettings"]["VCODivOutsideLoop"] != null)
                    FeedbackSelectBox.SelectedIndex = Convert.ToInt16(data["ChipSettings"]["VCODivOutsideLoop"]);

                if (data["ChipSettings"]["Prescaler"] != null)
                    PrescalerBox.SelectedIndex = (Convert.ToInt16(data["ChipSettings"]["Prescaler"]) / 4) - 1;

                if (data["ChipSettings"]["Icp_index"] != null)
                    ChargePumpCurrentBox.SelectedIndex = Convert.ToInt16(data["ChipSettings"]["Icp_index"]);

                if (data["ChipSettings"]["Dither"] != null)
                    LowNoiseSpurModeBox.SelectedIndex = Convert.ToInt16(data["ChipSettings"]["Dither"]) * 3;

                if (data["ChipSettings"]["PD_Polarity"] != null)
                    PDPolarityBox.SelectedIndex = Convert.ToInt16(data["ChipSettings"]["PD_Polarity"]);

                if (data["ChipSettings"]["CSR"] != null)
                    CSRBox.SelectedIndex = Convert.ToInt16(data["ChipSettings"]["CSR"]);


                if (data["ChipSettings"]["LockDetect"] != null)
                {
                    string temp = data["ChipSettings"]["LockDetect"];
                    if (temp == "none")
                        MuxoutBox.SelectedIndex = 0;
                    if (temp == "analogue")
                        MuxoutBox.SelectedIndex = 5;
                    if (temp == "analogue_OD")
                        MuxoutBox.SelectedIndex = 5;
                    if (temp == "digital")
                        MuxoutBox.SelectedIndex = 6;
                }

                if (data["ChipSettings"]["RFout_A"] != null)
                    RFOutputEnableBox.SelectedIndex = Convert.ToInt16(data["ChipSettings"]["RFout_A"]);

                if (data["ChipSettings"]["RFout_B"] != null)
                    AuxOutputEnableBox.SelectedIndex = Convert.ToInt16(data["ChipSettings"]["RFout_B"]);

                if (data["Reference"]["Frequency"] != null)
                    RefFreqBox.Text = ((Convert.ToDouble(data["Reference"]["Frequency"])) / 1000000).ToString();

            }
            #endregion

            BuildRegisters();

        }

        private void importADIsimPLLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog ImportADIsimPLLDialog = new OpenFileDialog();
            ImportADIsimPLLDialog.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
            ImportADIsimPLLDialog.ShowDialog();

            if (ImportADIsimPLLDialog.FileName != "")
            {
                importADIsimPLL(ImportADIsimPLLDialog.FileName);
            }

            ImportADIsimPLLDialog.Dispose();
        }

        #endregion

        #region Readback
        private void ReadbackButton_Click(object sender, EventArgs e)
        {
            if (PLLTestmodesBox.SelectedIndex != 7)
                MessageBox.Show("Readback will fail. PLL testmodes not set to READBACK to MUXOUT.");
            else if (MuxoutBox.SelectedIndex != 7)
                MessageBox.Show("Readback will fail. Muxout not set to Testmodes.");

            for (int i = 0; i < 5; i++)
                buffer[i] = 5;

            if (connectedDevice != null)
            {
                connectedDevice.ControlEndPt.Target = CyConst.TGT_DEVICE;
                connectedDevice.ControlEndPt.ReqType = CyConst.REQ_VENDOR;
                connectedDevice.ControlEndPt.Direction = CyConst.DIR_FROM_DEVICE;
                connectedDevice.ControlEndPt.ReqCode = 0xDF;                       // DD references the function in the firmware ADF_uwave_2.hex to write to the chip
                connectedDevice.ControlEndPt.Value = 0;
                connectedDevice.ControlEndPt.Index = 0;

                buffer[4] = 32;
                buffer_length = 5;

                XferSuccess = connectedDevice.ControlEndPt.XferData(ref buffer, ref buffer_length);

                int readback_value = buffer[0] << 2;
                readback_value += buffer[1] >> 6;

                if (XferSuccess & (readback_value != 0))
                    log("Readback successful.");
                else
                    log("Readback failed. Did you write to any register before clicking Readback?");


                if (ReadSelBox.SelectedIndex == 0)
                {
                    ReadbackComparatorBox.Text = ((readback_value >> 7) & 0x7).ToString();
                    ReadbackVCOBandBox.Text = ((readback_value >> 3) & 0xF).ToString();
                    ReadbackVCOBox.Text = (readback_value & 0x7).ToString();

                    ReadbackVersionBox.Text = "-";
                }
                else
                {
                    ReadbackVersionBox.Text = readback_value.ToString();

                    ReadbackVCOBox.Text = "-";
                    ReadbackVCOBandBox.Text = "-";
                    ReadbackComparatorBox.Text = "-";
                }

                connectedDevice.ControlEndPt.ReqCode = 0xDD;
                connectedDevice.ControlEndPt.Direction = CyConst.DIR_TO_DEVICE;

            }
        }
        #endregion

        #region Random hopping

        private void RandomStartButton_Click(object sender, EventArgs e)
        {
            SweepStopButton.PerformClick();
            HopStopButton.PerformClick();

            RandomStartButton.Enabled = false;
            RandomStopButton.Enabled = true;

            RFOutFreqBox.Text = RandomStartBox.Value.ToString();
            BuildRegisters();
            WriteAllButton.PerformClick();

            Random rnd = new Random();

            RandomActive = true;

            while (RandomActive)
            {
                int NextFrequency = rnd.Next((int)RandomStopBox.Value - (int)RandomStartBox.Value);
                NextFrequency = (int)Math.Round((decimal)NextFrequency / (int)RandomMinStepBox.Value) * (int)RandomMinStepBox.Value;

                RFOutFreqBox.Text = (RandomStartBox.Value + NextFrequency).ToString();
                BuildRegisters();
                WriteAllButton.PerformClick();

                RandomCurrentBox.Value = RandomStartBox.Value + NextFrequency;

                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep((int)RandomTimeDelayBox.Value);
            }
        }

        private void RandomStopButton_Click(object sender, EventArgs e)
        {
            RandomActive = false;
            RandomStartButton.Enabled = true;
            RandomStopButton.Enabled = false;
        }
       
        #endregion








    }
}
