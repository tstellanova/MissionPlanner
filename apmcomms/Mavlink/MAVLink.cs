﻿using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections; // hashs
using System.Diagnostics; // stopwatch
using System.Reflection;
using System.Reflection.Emit;
using System.IO;
using System.Threading;
using System.ComponentModel;
using log4net;

using apmcomms.Comms;
using apmbase;


namespace apmcomms
{
	/// <summary>
	///  Supports MAVLink protocol version 1.0
	/// </summary>
    public partial class MAVLink
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        public apmcomms.Comms.ICommsSerial BaseStream { get; set; }

		public apmcomms.Comms.ICommsSerial MirrorStream { get; set; }
		
		public FirmwareVersion firmwareVersion;
		public DateTime mavStateDateTime;


        /// <summary>
        /// used to prevent comport access for exclusive use
        /// </summary>
        public bool giveComport { get { return _giveComport; } set { _giveComport = value; } }
        static bool _giveComport = false;

        /// <summary>
        /// mavlink remote sysid
        /// </summary>
        public byte sysid { get { return MAV.sysid; } set { MAV.sysid = value; } }
        /// <summary>
        /// mavlink remove compid
        /// </summary>
        public byte compid { get { return MAV.compid; } set { MAV.compid = value; } }
        /// <summary>
        /// storage for whole paramater list
        /// </summary>
        public Hashtable parameterList { get { return MAV.param; } set { MAV.param = value; } }
        /// <summary>
        /// storage of a previous packet recevied of a specific type
        /// </summary>
        public byte[][] packets { get { return MAV.packets; } set { MAV.packets = value; } }
        /// <summary>
        /// mavlink ap type
        /// </summary>
        public MAV_TYPE aptype { get { return MAV.aptype; } set { MAV.aptype = value; } }
        public MAV_AUTOPILOT apname { get { return MAV.apname; } set { MAV.apname = value; } }
        /// <summary>
        /// used as a snapshot of what is loaded on the ap atm. - derived from the stream
        /// </summary>
        public Dictionary<int, mavlink_mission_item_t> wps { get { return MAV.wps; } set { MAV.wps = value; } }

        public Dictionary<string, MAV_PARAM_TYPE> param_types = new Dictionary<string, MAV_PARAM_TYPE>();

        /// <summary>
        /// Store the guided mode wp location
        /// </summary>
        public mavlink_mission_item_t GuidedMode { get { return MAV.GuidedMode; } set { MAV.GuidedMode = value; } }

        internal int recvpacketcount { get { return MAV.recvpacketcount; } set { MAV.recvpacketcount = value; } }

        internal string plaintxtline = "";
        string buildplaintxtline = "";

        public MAVState MAV = new MAVState();

        public class MAVState
        {
            public MAVState()
            {
                this.sysid = 0;
                this.compid = 0;
                this.param = new Hashtable();
                this.packets = new byte[0x100][];
                this.packetseencount = new int[0x100];
                this.aptype = 0;
                this.apname = 0;
                this.recvpacketcount = 0;
            }

            /// <summary>
            /// the static global state of the currently connected MAV
            /// </summary>
//TODO            public CurrentState currentMAVState = new CurrentState();
            /// <summary>
            /// mavlink remote sysid
            /// </summary>
            public byte sysid { get; set; }
            /// <summary>
            /// mavlink remove compid
            /// </summary>
            public byte compid { get; set; }
            /// <summary>
            /// storage for whole paramater list
            /// </summary>
            public Hashtable param { get; set; }
            /// <summary>
            /// storage of a previous packet recevied of a specific type
            /// </summary>
            public byte[][] packets { get; set; }
            public int[] packetseencount { get; set; }
            /// <summary>
            /// mavlink ap type
            /// </summary>
            public MAV_TYPE aptype { get; set; }
            public MAV_AUTOPILOT apname { get; set; }
            /// <summary>
            /// used as a snapshot of what is loaded on the ap atm. - derived from the stream
            /// </summary>
            public Dictionary<int, mavlink_mission_item_t> wps = new Dictionary<int, mavlink_mission_item_t>();
            /// <summary>
            /// Store the guided mode wp location
            /// </summary>
            public mavlink_mission_item_t GuidedMode = new mavlink_mission_item_t();

            internal int recvpacketcount = 0;
        }

        private const double CONNECT_TIMEOUT_SECONDS = 30;

 
        /// <summary>
        /// used for outbound packet sending
        /// </summary>
        internal int packetcount = 0;
      
        /// <summary>
        /// used to calc packets per second on any single message type - used for stream rate comparaison
        /// </summary>
        public double[] packetspersecond { get; set; }
        /// <summary>
        /// time last seen a packet of a type
        /// </summary>
        DateTime[] packetspersecondbuild = new DateTime[256];


        private readonly Subject<int> _bytesReceivedSubj = new Subject<int>();
        private readonly Subject<int> _bytesSentSubj = new Subject<int>();

        /// <summary>
        /// Observable of the count of bytes received, notified when the bytes themselves are received
        /// </summary>
        public IObservable<int> BytesReceived { get { return _bytesReceivedSubj; } }

        /// <summary>
        /// Observable of the count of bytes sent, notified when the bytes themselves are received
        /// </summary>
        public IObservable<int> BytesSent { get { return _bytesSentSubj; } }

        /// <summary>
        /// Observable of the count of packets skipped (on reception), 
        /// calculated from periods where received packet sequence is not
        /// contiguous
        /// </summary>
        public Subject<int> WhenPacketLost { get; set; }

        public Subject<int> WhenPacketReceived { get; set; }

        /// <summary>
        /// used as a serial port write lock
        /// </summary>
        volatile object objlock = new object();
        /// <summary>
        /// used for a readlock on readpacket
        /// </summary>
        volatile object readlock = new object();
        /// <summary>
        /// time seen of last mavlink packet
        /// </summary>
        public DateTime lastvalidpacket { get; set; }
        /// <summary>
        /// old log support
        /// </summary>
        bool oldlogformat = false;

        /// <summary>
        /// mavlink version
        /// </summary>
        byte mavlinkversion = 0;

        /// <summary>
        /// turns on console packet display
        /// </summary>
        public bool debugmavlink { get; set; }
        /// <summary>
        /// enabled read from file mode
        /// </summary>
        public bool logreadmode { get; set; }
        public DateTime lastlogread { get; set; }
        public BinaryReader logplaybackfile { get; set; }
        public BinaryWriter logfile { get; set; }
        public BinaryWriter rawlogfile { get; set; }

        int bps1 = 0;
        int bps2 = 0;
        public int bps { get; set; }
        public DateTime bpstime { get; set; }

        float synclost;
        internal float packetslost = 0;
        internal float packetsnotlost = 0;
        DateTime packetlosttimer = DateTime.MinValue;

        public MAVLink()
        {
            // init fields
            this.BaseStream = new SerialPort();
            this.packetcount = 0;

            this.packetspersecond = new double[0x100];
            this.packetspersecondbuild = new DateTime[0x100];
            this._bytesReceivedSubj = new Subject<int>();
            this._bytesSentSubj = new Subject<int>();
            this.WhenPacketLost = new Subject<int>();
            this.WhenPacketReceived = new Subject<int>();
            this.readlock = new object();
            this.lastvalidpacket = DateTime.MinValue;
            this.oldlogformat = false;
            this.mavlinkversion = 0;

            this.debugmavlink = false;
            this.logreadmode = false;
            this.lastlogread = DateTime.MinValue;
            this.logplaybackfile = null;
            this.logfile = null;
            this.rawlogfile = null;
            this.bps1 = 0;
            this.bps2 = 0;
            this.bps = 0;
            this.bpstime = DateTime.MinValue;
 
            this.packetslost = 0f;
            this.packetsnotlost = 0f;
            this.packetlosttimer = DateTime.MinValue;
            this.lastbad = new byte[2];

        }

        public void Close()
        {
            try
            {
                logfile.Close();
            }
            catch { }
            try
            {
                rawlogfile.Close();
            }
            catch { }
            try
            {
                logplaybackfile.Close();
            }
            catch { }

            BaseStream.Close();
        }

        public void Open()
        {
            Open(false);
        }

        public void Open(bool getparams)
        {
            if (BaseStream.IsOpen)
                return;

			reportStatus ("Connecting MavLink...");

			//TODO eventually call OpenBg

			if (getparams) {
				OpenBg (true);
			} else {
				OpenBg(false);
			}
			//FrmProgressReporterDoWorkNOParams
			//FrmProgressReporterDoWorkAndParams
        }



        private void OpenBg(bool getparams)
        {
			reportStatus( "Mavlink Connecting...");

            giveComport = true;

            // allow settings to settle - previous dtr 
            System.Threading.Thread.Sleep(500);

            // reset
            sysid = 0;
            compid = 0;
            parameterList = new Hashtable();
            packets.Initialize();

            bool hbseen = false;

            try
            {
                BaseStream.ReadBufferSize = 4 * 1024;

                lock (objlock) // so we dont have random traffic
                {
					reportStatus("Open port with " + BaseStream.PortName + " " + BaseStream.BaudRate);

                    BaseStream.Open();
                    BaseStream.DiscardInBuffer();

                    Thread.Sleep(1000);
                }

                byte[] buffer = new byte[0];
                byte[] buffer1 = new byte[0];

                DateTime start = DateTime.Now;
                DateTime deadline = start.AddSeconds(CONNECT_TIMEOUT_SECONDS);

                var countDown = new System.Timers.Timer { Interval = 1000, AutoReset = false };
                countDown.Elapsed += (sender, e) =>
                {
                    int secondsRemaining = (deadline - e.SignalTime).Seconds;

					reportStatus( string.Format("Trying to connect.\nTimeout in {0}", secondsRemaining));
                    if (secondsRemaining > 0) 
						countDown.Start();
                };
                countDown.Start();

                int count = 0;

                while (true)
                {
					//TODO
                    if (wasBgActionCancelRequested())
                    {
                        countDown.Stop();
                        if (BaseStream.IsOpen)
                            BaseStream.Close();
                        giveComport = false;
                        return;
                    }

                    // incase we are in setup mode
                    //BaseStream.WriteLine("planner\rgcs\r");



                    reportStatus(DateTime.Now.Millisecond + " Start connect loop ");

                    if (lastbad[0] == '!' && lastbad[1] == 'G' || lastbad[0] == 'G' && lastbad[1] == '!') // waiting for gps lock
                    {
                        //if (Progress != null)
                        //    Progress(-1, "Waiting for GPS detection..");
						reportStatus( "Waiting for GPS detection..");
                        deadline = deadline.AddSeconds(5); // each round is 1.1 seconds
                    }

                    if (DateTime.Now > deadline)
                    {
                        //if (Progress != null)
                        //    Progress(-1, "No Heatbeat Packets");
                        countDown.Stop();
                        this.Close();
                        if (hbseen)
                        {
							reportError ("Only 1 Heatbeat Received");
                            throw new Exception("Only 1 Mavlink Heartbeat Packets was read from this port - Verify your hardware is setup correctly\nAPM Planner waits for 2 valid heartbeat packets before connecting");
                        }
                        else
                        {
							reportError( "No Heatbeat Packets Received" );
                            throw new Exception("No Mavlink Heartbeat Packets where read from this port - Verify Baud Rate and setup\nAPM Planner waits for 2 valid heartbeat packets before connecting");
                        }
                    }

                    System.Threading.Thread.Sleep(1);

                    // incase we are in setup mode
                    //BaseStream.WriteLine("planner\rgcs\r");

                    // can see 2 heartbeat packets at any time, and will connect - was one after the other

                    if (buffer.Length == 0)
                        buffer = getHeartBeat();

                    // incase we are in setup mode
                    //BaseStream.WriteLine("planner\rgcs\r");

                    System.Threading.Thread.Sleep(1);

                    if (buffer1.Length == 0)
                        buffer1 = getHeartBeat();


                    if (buffer.Length > 0 || buffer1.Length > 0)
                        hbseen = true;

                    count++;

                    if (buffer.Length > 5 && buffer1.Length > 5 && buffer[3] == buffer1[3] && buffer[4] == buffer1[4])
                    {
                        mavlink_heartbeat_t hb = buffer.ByteArrayToStructure<mavlink_heartbeat_t>(6);

                        mavlinkversion = hb.mavlink_version;
                        aptype = (MAV_TYPE)hb.type;
                        apname = (MAV_AUTOPILOT)hb.autopilot;

                        setAPType();

                        sysid = buffer[3];
                        compid = buffer[4];
                        recvpacketcount = buffer[2];
                        log.InfoFormat("ID sys {0} comp {1} ver{2}", sysid, compid, mavlinkversion);
                        break;
                    }

                }

                countDown.Stop();

                reportStatus ( "Getting Params.. (sysid " + sysid + " compid " + compid + ") ");

                if (getparams)
                {
                    getParamListBG();
                }

                if (wasBgActionCancelRequested())
                {
                    giveComport = false;
                    if (BaseStream.IsOpen)
                        BaseStream.Close();
                    return;
                }
            }
            catch (Exception e)
            {
                try
                {
                    BaseStream.Close();
                }
                catch { }
                giveComport = false;
                throw e;
            }

			giveComport = false;
			reportStatus("Done open " + sysid + " " + compid);
            packetslost = 0;
            synclost = 0;
        }

        byte[] getHeartBeat()
        {
            DateTime start = DateTime.Now;
            while (true)
            {
                byte[] buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == MAVLINK_MSG_ID_HEARTBEAT)
                    {
                        return buffer;
                    }
                }
                if (DateTime.Now > start.AddMilliseconds(2200)) // was 1200 , now 2.2 sec
                    return new byte[0];
            }
        }

        public void sendPacket(object indata)
        {
            bool validPacket = false;
            byte a = 0;
            foreach (Type ty in MAVLINK_MESSAGE_INFO)
            {
                if (ty == indata.GetType())
                {
                    validPacket = true;
                    generatePacket(a, indata);
                    return;
                }
                a++;
            }
            if (!validPacket)
            {
				reportStatus("Mavlink : NOT VALID PACKET sendPacket() " + indata.GetType().ToString());
            }
        }

        /// <summary>
        /// Generate a Mavlink Packet and write to serial
        /// </summary>
        /// <param name="messageType">type number</param>
        /// <param name="indata">struct of data</param>
        void generatePacket(byte messageType, object indata)
        {
            lock (objlock)
            {
                byte[] data;

                if (mavlinkversion == 3)
                {
                    data = MavlinkUtil.StructureToByteArray(indata);
                }
                else
                {
                    data = MavlinkUtil.StructureToByteArrayBigEndian(indata);
                }

				//reportStatus(DateTime.Now + " PC Doing req "+ messageType + " " + this.BytesToRead);
                byte[] packet = new byte[data.Length + 6 + 2];

                if (mavlinkversion == 3)
                {
                    packet[0] = 254;
                }
                else if (mavlinkversion == 2)
                {
                    packet[0] = (byte)'U';
                }
                packet[1] = (byte)data.Length;
                packet[2] = (byte)packetcount;

                packetcount++;

                packet[3] = 255; // this is always 255 - MYGCS
                packet[4] = (byte)MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER;
                packet[5] = messageType;

                int i = 6;
                foreach (byte b in data)
                {
                    packet[i] = b;
                    i++;
                }

                ushort checksum = MavlinkCRC.crc_calculate(packet, packet[1] + 6);

                if (mavlinkversion == 3)
                {
                    checksum = MavlinkCRC.crc_accumulate(MAVLINK_MESSAGE_CRCS[messageType], checksum);
                }

                byte ck_a = (byte)(checksum & 0xFF); ///< High byte
                byte ck_b = (byte)(checksum >> 8); ///< Low byte

                packet[i] = ck_a;
                i += 1;
                packet[i] = ck_b;
                i += 1;

                if (BaseStream.IsOpen)
                {
                    BaseStream.Write(packet, 0, i);
                    _bytesSentSubj.OnNext(i);
                }

                try
                {
                    if (logfile != null && logfile.BaseStream.CanWrite)
                    {
                        lock (logfile)
                        {
                            byte[] datearray = BitConverter.GetBytes((UInt64)((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds * 1000));
                            Array.Reverse(datearray);
                            logfile.Write(datearray, 0, datearray.Length);
                            logfile.Write(packet, 0, i);
                        }
                    }

                }
                catch { }
 
            }
        }

        public bool Write(string line)
        {
            lock (objlock)
            {
                BaseStream.Write(line);
            }
            _bytesSentSubj.OnNext(line.Length);
            return true;
        }

        /// <summary>
        /// Set parameter on apm
        /// </summary>
        /// <param name="paramname">name as a string</param>
        /// <param name="value"></param>
        public bool setParam(string paramname, float value)
        {
            if (!parameterList.ContainsKey(paramname))
            {
                log.Warn("Trying to set Param that doesnt exist " + paramname);
                return false;
            }

            if ((float)parameterList[paramname] == value)
            {
                log.Debug("setParam " + paramname + " not modified");
                return true;
            }

            giveComport = true;

            // param type is set here, however it is always sent over the air as a float 100int = 100f.
            var req = new mavlink_param_set_t { target_system = sysid, target_component = compid, param_type = (byte)param_types[paramname] };

            byte[] temp = Encoding.ASCII.GetBytes(paramname);

            Array.Resize(ref temp, 16);
            req.param_id = temp;
            req.param_value = (value);

            generatePacket(MAVLINK_MSG_ID_PARAM_SET, req);

            log.InfoFormat("setParam '{0}' = '{1}' sysid {2} compid {3}", paramname, req.param_value, sysid, compid);

            DateTime start = DateTime.Now;
            int retrys = 3;

            while (true)
            {
                if (!(start.AddMilliseconds(500) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
						reportStatus("setParam Retry " + retrys);
                        generatePacket(MAVLINK_MSG_ID_PARAM_SET, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - setParam " + paramname);
                }

                byte[] buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == MAVLINK_MSG_ID_PARAM_VALUE)
                    {
                        mavlink_param_value_t par = buffer.ByteArrayToStructure<mavlink_param_value_t>(6);

                        string st = System.Text.ASCIIEncoding.ASCII.GetString(par.param_id);

                        int pos = st.IndexOf('\0');

                        if (pos != -1)
                        {
                            st = st.Substring(0, pos);
                        }

                        if (st != paramname)
                        {
                            log.InfoFormat("MAVLINK bad param responce - {0} vs {1}", paramname, st);
                            continue;
                        }

                        parameterList[st] = (par.param_value);

                        giveComport = false;
                        //System.Threading.Thread.Sleep(100);//(int)(8.5 * 5)); // 8.5ms per byte
                        return true;
                    }
                }
            }
        }
        /*
        public Bitmap getImage()
        {
            MemoryStream ms = new MemoryStream();

        }
        */
        public void getParamList()
        {
			//TODO eventually async call getParamListBG
			reportStatus ("Getting Params...");

			getParamListBG ();

        }



        /// <summary>
        /// Get param list from apm
        /// </summary>
        /// <returns></returns>
        private Hashtable getParamListBG()
        {
            giveComport = true;
            List<int> indexsreceived = new List<int>();

            // clear old
            parameterList = new Hashtable();

            int retrys = 6;
            int param_count = 0;
            int param_total = 1;

        goagain:

            mavlink_param_request_list_t req = new mavlink_param_request_list_t();
            req.target_system = sysid;
            req.target_component = compid;

            generatePacket(MAVLINK_MSG_ID_PARAM_REQUEST_LIST, req);

            DateTime start = DateTime.Now;


            //hires.Stopwatch stopwatch = new hires.Stopwatch();
            int packets = 0;

            do
            {
                if (wasBgActionCancelRequested())
                {
                    giveComport = false;
                    return parameterList;
                }

                // 4 seconds between valid packets
                if (!(start.AddMilliseconds(4000) > DateTime.Now))
                {
                    // try getting individual params
                    for (short i = 0; i <= (param_total - 1); i++)
                    {
                        if (!indexsreceived.Contains(i))
                        {
                            // prevent dropping out of this get params loop
                            try
                            {
                                GetParam(i);
                                param_count++;
                                indexsreceived.Add(i);
                            }
                            catch {
                                // fail over to full list
                                break;
                            }
                        }
                    }

                    if (retrys == 4)
                    {
                        requestDatastream(MAVLink.MAV_DATA_STREAM.ALL, 1);
                    }

                    if (retrys > 0)
                    {
                        log.InfoFormat("getParamList Retry {0} sys {1} comp {2}", retrys, sysid, compid);
                        generatePacket(MAVLINK_MSG_ID_PARAM_REQUEST_LIST, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    if (packets > 0 && param_total == 1)
                    {
                        throw new Exception("Timeout on read - getParamList\n" + packets + " Packets where received, but no paramater packets where received\n");
                    }
                    if (packets == 0)
                    {
                        throw new Exception("Timeout on read - getParamList\nNo Packets where received\n");
                    }

                    throw new Exception("Timeout on read - getParamList\nReceived: " + indexsreceived.Count + " of " + param_total + " after 6 retrys\n\nPlease Check\n1. Link Speed\n2. Link Quality\n3. Hardware hasn't hung");
                }

				//reportStatus(DateTime.Now.Millisecond + " gp0 ");

                byte[] buffer = readPacket();
				//reportStatus(DateTime.Now.Millisecond + " gp1 ");
                if (buffer.Length > 5)
                {
                    packets++;
                    // stopwatch.Start();
                    if (buffer[5] == MAVLINK_MSG_ID_PARAM_VALUE)
                    {
                        start = DateTime.Now;

                        mavlink_param_value_t par = buffer.ByteArrayToStructure<mavlink_param_value_t>(6);

                        // set new target
                        param_total = (par.param_count);


                        string paramID = System.Text.ASCIIEncoding.ASCII.GetString(par.param_id);

                        int pos = paramID.IndexOf('\0');
                        if (pos != -1)
                        {
                            paramID = paramID.Substring(0, pos);
                        }

                        // check if we already have it
                        if (indexsreceived.Contains(par.param_index))
                        {
                            reportStatus("Already got " + (par.param_index) + " '" + paramID + "'");
                            continue;
                        }


                        parameterList[paramID] = (par.param_value);


                        param_count++;
                        indexsreceived.Add(par.param_index);

                        param_types[paramID] = (MAV_PARAM_TYPE)par.param_type;

						updateProgressAndStatus((indexsreceived.Count * 100) / param_total, "Got param " + paramID);

                        // we have them all - lets escape eq total = 176 index = 0-175
                        if (par.param_index == (param_total - 1))
                            break;
                    }
                    else
                    {
						//reportStatus(DateTime.Now + " PC paramlist " + buffer[5] + " want " + MAVLINK_MSG_ID_PARAM_VALUE + " btr " + BaseStream.BytesToRead);
                    }
                    //stopwatch.Stop();
					// reportStatus("Time elapsed: {0}", stopwatch.Elapsed);
					// reportStatus(DateTime.Now.Millisecond + " gp4 " + BaseStream.BytesToRead);
                }
            } while (indexsreceived.Count < param_total);

            if (indexsreceived.Count != param_total)
            {
                if (retrys > 0)
                {
					updateProgressAndStatus((indexsreceived.Count * 100) / param_total, "Getting missed params");
                    retrys--;
                    goto goagain;
                }
                throw new Exception("Missing Params");
            }
            giveComport = false;
            return parameterList;
        }

        public float GetParam(string name)
        {
            return GetParam(name,-1);
        }

        public float GetParam(short index)
        {
            return GetParam("", index);
        }

        /// <summary>
        /// Get param by either index or name
        /// </summary>
        /// <param name="index"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        internal float GetParam(string name = "", short index = -1)
        {
            if (name == "" && index == -1)
                return 0;

			reportStatus("GetParam name: "+ name + " index: " + index);

            giveComport = true;
            byte[] buffer;

            mavlink_param_request_read_t req = new mavlink_param_request_read_t();
            req.target_system = sysid;
            req.target_component = compid;
            if (index == -1)
            {
                req.param_id = System.Text.ASCIIEncoding.ASCII.GetBytes(name);
            }
            else
            {
                req.param_index = index;
            }

            generatePacket(MAVLINK_MSG_ID_PARAM_REQUEST_READ, req);

            DateTime start = DateTime.Now;
            int retrys = 3;

            while (true)
            {
                if (!(start.AddMilliseconds(200) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
						reportStatus("GetParam Retry " + retrys);
                        generatePacket(MAVLINK_MSG_ID_PARAM_REQUEST_READ, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - GetParam");
                }

                buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == MAVLINK_MSG_ID_PARAM_VALUE)
                    {
                        giveComport = false;

                        mavlink_param_value_t par = buffer.ByteArrayToStructure<mavlink_param_value_t>(6);

                        // not the correct id
                        if (!(par.param_index == index || par.param_id == req.param_id))
                            continue;

                        string st = System.Text.ASCIIEncoding.ASCII.GetString(par.param_id);

                        int pos = st.IndexOf('\0');

                        if (pos != -1)
                        {
                            st = st.Substring(0, pos);
                        }

                        // update table
                        parameterList[st] = par.param_value;

                        param_types[st] = (MAV_PARAM_TYPE)par.param_type;

						reportStatus(DateTime.Now.Millisecond + " got param " + (par.param_index) + " of " + (par.param_count) + " name: " + st);

                        return par.param_value;
                    }
                }
            }
        }

        public static void modifyParamForDisplay(bool fromapm, string paramname, ref float value)
        {

            if (paramname.ToUpper().EndsWith("_IMAX") || paramname.ToUpper().EndsWith("ALT_HOLD_RTL") || paramname.ToUpper().EndsWith("APPROACH_ALT") || paramname.ToUpper().EndsWith("TRIM_ARSPD_CM") || paramname.ToUpper().EndsWith("MIN_GNDSPD_CM")
                || paramname.ToUpper().EndsWith("XTRK_ANGLE_CD") || paramname.ToUpper().EndsWith("LIM_PITCH_MAX") || paramname.ToUpper().EndsWith("LIM_PITCH_MIN")
                || paramname.ToUpper().EndsWith("LIM_ROLL_CD") || paramname.ToUpper().EndsWith("PITCH_MAX") || paramname.ToUpper().EndsWith("WP_SPEED_MAX"))
            {
                if (paramname.ToUpper().EndsWith("THR_RATE_IMAX") || paramname.ToUpper().EndsWith("THR_HOLD_IMAX"))
                    return;

                if (fromapm)
                {
                    value /= 100.0f;
                }
                else
                {
                    value *= 100.0f;
                }
            }
            else if (paramname.ToUpper().StartsWith("TUNE_"))
            {
                if (fromapm)
                {
                    value /= 1000.0f;
                }
                else
                {
                    value *= 1000.0f;
                }
            }
        }

        /// <summary>
        /// Stops all requested data packets.
        /// </summary>
        public void stopall(bool forget)
        {
            mavlink_request_data_stream_t req = new mavlink_request_data_stream_t();
            req.target_system = sysid;
            req.target_component = compid;

            req.req_message_rate = 10;
            req.start_stop = 0; // stop
            req.req_stream_id = 0; // all

            // no error on bad
            try
            {
                generatePacket(MAVLINK_MSG_ID_REQUEST_DATA_STREAM, req);
                System.Threading.Thread.Sleep(20);
                generatePacket(MAVLINK_MSG_ID_REQUEST_DATA_STREAM, req);
                System.Threading.Thread.Sleep(20);
                generatePacket(MAVLINK_MSG_ID_REQUEST_DATA_STREAM, req);
				reportStatus("Stopall Done");

            }
            catch { }
        }

        public void setWPACK()
        {
            MAVLink.mavlink_mission_ack_t req = new MAVLink.mavlink_mission_ack_t();
            req.target_system = sysid;
            req.target_component = compid;
            req.type = 0;

            generatePacket(MAVLINK_MSG_ID_MISSION_ACK, req);
        }

        public bool setWPCurrent(ushort index)
        {
            giveComport = true;
            byte[] buffer;

            mavlink_mission_set_current_t req = new mavlink_mission_set_current_t();

            req.target_system = sysid;
            req.target_component = compid;
            req.seq = index;

            generatePacket(MAVLINK_MSG_ID_MISSION_SET_CURRENT, req);

            DateTime start = DateTime.Now;
            int retrys = 5;

            while (true)
            {
                if (!(start.AddMilliseconds(2000) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
						reportStatus("setWPCurrent Retry " + retrys);
                        generatePacket(MAVLINK_MSG_ID_MISSION_SET_CURRENT, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - setWPCurrent");
                }

                buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == MAVLINK_MSG_ID_MISSION_CURRENT)
                    {
                        giveComport = false;
                        return true;
                    }
                }
            }
        }

        [Obsolete("Mavlink 09", true)]
        public bool doAction(object actionid)
        {
            // mavlink 09
            throw new NotImplementedException();
        }

        public bool doARM(bool armit)
        {
            return doCommand(MAV_CMD.COMPONENT_ARM_DISARM, armit ? 1 : 0, 0, 0, 0, 0, 0, 0);
        }

        public bool doCommand(MAV_CMD actionid, float p1, float p2, float p3, float p4, float p5, float p6, float p7)
        {

            giveComport = true;
            byte[] buffer;

            mavlink_command_long_t req = new mavlink_command_long_t();

            req.target_system = sysid;
            req.target_component = compid;

            if (actionid == MAV_CMD.COMPONENT_ARM_DISARM)
            {
                req.target_component = (byte)MAV_COMPONENT.MAV_COMP_ID_SYSTEM_CONTROL;
            }

            req.command = (ushort)actionid;

            req.param1 = p1;
            req.param2 = p2;
            req.param3 = p3;
            req.param4 = p4;
            req.param5 = p5;
            req.param6 = p6;
            req.param7 = p7;

            generatePacket(MAVLINK_MSG_ID_COMMAND_LONG, req);

            DateTime start = DateTime.Now;
            int retrys = 3;

            int timeout = 2000;

            // imu calib take a little while
            if (actionid == MAV_CMD.PREFLIGHT_CALIBRATION && p5 == 1)
            {
                // this is for advanced accel offsets, and blocks execution
                return true;
            }  else if (actionid == MAV_CMD.PREFLIGHT_CALIBRATION)
            {
                retrys = 1;
                timeout = 25000;
            }
            else if (actionid == MAV_CMD.PREFLIGHT_REBOOT_SHUTDOWN)
            {
                generatePacket(MAVLINK_MSG_ID_COMMAND_LONG, req);
                giveComport = false;
                return true;
            }
            else if (actionid == MAV_CMD.COMPONENT_ARM_DISARM)
            {
                // 10 seconds as may need an imu calib
                timeout = 10000;
            }

            while (true)
            {
                if (!(start.AddMilliseconds(timeout) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
						reportStatus("doAction Retry " + retrys);
                        generatePacket(MAVLINK_MSG_ID_COMMAND_LONG, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - doAction");
                }

                buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == MAVLINK_MSG_ID_COMMAND_ACK)
                    {


                        var ack = buffer.ByteArrayToStructure<mavlink_command_ack_t>(6);


                        if (ack.result == (byte)MAV_RESULT.ACCEPTED)
                        {
                            giveComport = false;
                            return true;
                        }
                        else
                        {
                            giveComport = false;
                            return false;
                        }
                    }
                }
            }
        }

        public void requestDatastream(MAVLink.MAV_DATA_STREAM id, byte hzrate)
        {

            double pps = 0;

            switch (id)
            {
                case MAVLink.MAV_DATA_STREAM.ALL:

                    break;
                case MAVLink.MAV_DATA_STREAM.EXTENDED_STATUS:
                    if (packetspersecondbuild[MAVLINK_MSG_ID_SYS_STATUS] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[MAVLINK_MSG_ID_SYS_STATUS];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.EXTRA1:
                    if (packetspersecondbuild[MAVLINK_MSG_ID_ATTITUDE] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[MAVLINK_MSG_ID_ATTITUDE];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.EXTRA2:
                    if (packetspersecondbuild[MAVLINK_MSG_ID_VFR_HUD] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[MAVLINK_MSG_ID_VFR_HUD];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.EXTRA3:
                    if (packetspersecondbuild[MAVLINK_MSG_ID_AHRS] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[MAVLINK_MSG_ID_AHRS];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.POSITION:
                    if (packetspersecondbuild[MAVLINK_MSG_ID_GLOBAL_POSITION_INT] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[MAVLINK_MSG_ID_GLOBAL_POSITION_INT];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.RAW_CONTROLLER:
                    if (packetspersecondbuild[MAVLINK_MSG_ID_RC_CHANNELS_SCALED] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[MAVLINK_MSG_ID_RC_CHANNELS_SCALED];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.RAW_SENSORS:
                    if (packetspersecondbuild[MAVLINK_MSG_ID_RAW_IMU] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[MAVLINK_MSG_ID_RAW_IMU];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.RC_CHANNELS:
                    if (packetspersecondbuild[MAVLINK_MSG_ID_RC_CHANNELS_RAW] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[MAVLINK_MSG_ID_RC_CHANNELS_RAW];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
            }

            //packetspersecond[temp[5]];

            if (pps == 0 && hzrate == 0)
            {
                return;
            }


            log.InfoFormat("Request stream {0} at {1} hz", Enum.Parse(typeof(MAV_DATA_STREAM), id.ToString()), hzrate);
            getDatastream(id, hzrate);
        }

        // returns true for ok
        bool hzratecheck(double pps, int hzrate)
        {

            if (hzrate == 0 && pps == 0)
            {
                return true;
            }
            else if (hzrate == 1 && pps >= 0.5 && pps <= 2)
            {
                return true;
            }
            else if (hzrate == 3 && pps >= 2 && hzrate < 5)
            {
                return true;
            }
            else if (hzrate == 10 && pps > 5 && hzrate < 15)
            {
                return true;
            }
            else if (hzrate > 15 && pps > 15)
            {
                return true;
            }

            return false;

        }

        void getDatastream(MAVLink.MAV_DATA_STREAM id, byte hzrate)
        {
            mavlink_request_data_stream_t req = new mavlink_request_data_stream_t();
            req.target_system = sysid;
            req.target_component = compid;

            req.req_message_rate = hzrate;
            req.start_stop = 1; // start
            req.req_stream_id = (byte)id; // id

            // send each one twice.
            generatePacket(MAVLINK_MSG_ID_REQUEST_DATA_STREAM, req);
            generatePacket(MAVLINK_MSG_ID_REQUEST_DATA_STREAM, req);
        }

        /// <summary>
        /// Returns WP count
        /// </summary>
        /// <returns></returns>
        public byte getWPCount()
        {
            giveComport = true;
            byte[] buffer;
            mavlink_mission_request_list_t req = new mavlink_mission_request_list_t();

            req.target_system = sysid;
            req.target_component = compid;

            // request list
            generatePacket(MAVLINK_MSG_ID_MISSION_REQUEST_LIST, req);

            DateTime start = DateTime.Now;
            int retrys = 6;

            while (true)
            {
                if (!(start.AddMilliseconds(500) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
						reportStatus("getWPCount Retry " + retrys + " - giv com " + giveComport);
                        generatePacket(MAVLINK_MSG_ID_MISSION_REQUEST_LIST, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    //return (byte)int.Parse(param["WP_TOTAL"].ToString());
                    throw new Exception("Timeout on read - getWPCount");
                }

                buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == MAVLINK_MSG_ID_MISSION_COUNT)
                    {



                        var count = buffer.ByteArrayToStructure<mavlink_mission_count_t>(6);


						reportStatus("wpcount: " + count.count);
                        giveComport = false;
                        return (byte)count.count; // should be ushort, but apm has limited wp count < byte
                    }
                    else
                    {
						reportStatus(DateTime.Now + " PC wpcount " + buffer[5] + " need " + MAVLINK_MSG_ID_MISSION_COUNT);
                    }
                }
            }

        }
        /// <summary>
        /// Gets specfied WP
        /// </summary>
        /// <param name="index"></param>
        /// <returns>WP</returns>
        public Locationwp getWP(ushort index)
        {
            giveComport = true;
            Locationwp loc = new Locationwp();
            mavlink_mission_request_t req = new mavlink_mission_request_t();

            req.target_system = sysid;
            req.target_component = compid;

            req.seq = index;

			//reportStatus("getwp req "+ DateTime.Now.Millisecond);

            // request
            generatePacket(MAVLINK_MSG_ID_MISSION_REQUEST, req);

            DateTime start = DateTime.Now;
            int retrys = 5;

            while (true)
            {
                if (!(start.AddMilliseconds(800) > DateTime.Now)) // apm times out after 1000ms
                {
                    if (retrys > 0)
                    {
						reportStatus("getWP Retry " + retrys);
                        generatePacket(MAVLINK_MSG_ID_MISSION_REQUEST, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - getWP");
                }
				//reportStatus("getwp read " + DateTime.Now.Millisecond);
                byte[] buffer = readPacket();
				//reportStatus("getwp readend " + DateTime.Now.Millisecond);
                if (buffer.Length > 5)
                {
                    if (buffer[5] == MAVLINK_MSG_ID_MISSION_ITEM)
                    {
						//reportStatus("getwp ans " + DateTime.Now.Millisecond);


                        //Array.Copy(buffer, 6, buffer, 0, buffer.Length - 6);

                        var wp = buffer.ByteArrayToStructure<mavlink_mission_item_t>(6);


                        loc.options = (byte)(wp.frame & 0x1);
                        loc.id = (byte)(wp.command);
                        loc.p1 = (wp.param1);
                        loc.p2 = (wp.param2);
                        loc.p3 = (wp.param3);
                        loc.p4 = (wp.param4);

                        loc.alt = ((wp.z));
                        loc.lat = ((wp.x));
                        loc.lng = ((wp.y));
 
                        log.InfoFormat("getWP {0} {1} {2} {3} {4} opt {5}", loc.id, loc.p1, loc.alt, loc.lat, loc.lng, loc.options);

                        break;
                    }
                    else
                    {
						reportStatus(DateTime.Now + " PC getwp " + buffer[5]);
                    }
                }
            }
            giveComport = false;
            return loc;
        }

        public object DebugPacket(byte[] datin)
        {
            string text = "";
            return DebugPacket(datin, ref text, true);
        }

        public object DebugPacket(byte[] datin, bool PrintToConsole)
        {
            string text = "";
            return DebugPacket(datin, ref text, PrintToConsole);
        }

        public object DebugPacket(byte[] datin, ref string text)
        {
            return DebugPacket(datin, ref text, true);
        }

        /// <summary>
        /// Print entire decoded packet to console
        /// </summary>
        /// <param name="datin">packet byte array</param>
        /// <returns>struct of data</returns>
        public object DebugPacket(byte[] datin, ref string text, bool PrintToConsole, string delimeter = " ")
        {
            string textoutput;
            try
            {
                if (datin.Length > 5)
                {
                    byte header = datin[0];
                    byte length = datin[1];
                    byte seq = datin[2];
                    byte sysid = datin[3];
                    byte compid = datin[4];
                    byte messid = datin[5];

                    textoutput = string.Format("{0,2:X}{6}{1,2:X}{6}{2,2:X}{6}{3,2:X}{6}{4,2:X}{6}{5,2:X}{6}", header, length, seq, sysid, compid, messid, delimeter);

                    object data = Activator.CreateInstance(MAVLINK_MESSAGE_INFO[messid]);

                    MavlinkUtil.ByteArrayToStructure(datin, ref data, 6);

                    Type test = data.GetType();

                    if (PrintToConsole)
                    {

                        textoutput = textoutput + test.Name + delimeter;

                        foreach (var field in test.GetFields())
                        {
                            // field.Name has the field's name.

                            object fieldValue = field.GetValue(data); // Get value

                            if (field.FieldType.IsArray)
                            {
                                textoutput = textoutput + field.Name + delimeter;
                                byte[] crap = (byte[])fieldValue;
                                foreach (byte fiel in crap)
                                {
                                    if (fiel == 0)
                                    {
                                        break;
                                    }
                                    else
                                    {
                                        textoutput = textoutput + (char)fiel;
                                    }
                                }
                                textoutput = textoutput + delimeter;
                            }
                            else
                            {
                                textoutput = textoutput + field.Name + delimeter + fieldValue.ToString() + delimeter;
                            }
                        }
                        textoutput = textoutput + delimeter + "Len" + delimeter + datin.Length + "\r\n";
                        if (PrintToConsole)
                            Console.Write(textoutput);

                        if (text != null)
                            text = textoutput;
                    }

                    return data;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Sets wp total count
        /// </summary>
        /// <param name="wp_total"></param>
        public void setWPTotal(ushort wp_total)
        {
            giveComport = true;
            mavlink_mission_count_t req = new mavlink_mission_count_t();

            req.target_system = sysid;
            req.target_component = compid; // MAVLINK_MSG_ID_MISSION_COUNT

            req.count = wp_total;

            generatePacket(MAVLINK_MSG_ID_MISSION_COUNT, req);

            DateTime start = DateTime.Now;
            int retrys = 3;

            while (true)
            {
                if (!(start.AddMilliseconds(700) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
						reportStatus("setWPTotal Retry " + retrys);
                        generatePacket(MAVLINK_MSG_ID_MISSION_COUNT, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - setWPTotal");
                }
                byte[] buffer = readPacket();
                if (buffer.Length > 9)
                {
                    if (buffer[5] == MAVLINK_MSG_ID_MISSION_REQUEST)
                    {



                        var request = buffer.ByteArrayToStructure<mavlink_mission_request_t>(6);

                        if (request.seq == 0)
                        {
                            if (parameterList["WP_TOTAL"] != null)
                                parameterList["WP_TOTAL"] = (float)wp_total - 1;
                            if (parameterList["CMD_TOTAL"] != null)
                                parameterList["CMD_TOTAL"] = (float)wp_total - 1;

                            wps.Clear();

                            giveComport = false;
                            return;
                        }
                    }
                    else
                    {
						//reportStatus(DateTime.Now + " PC getwp " + buffer[5]);
                    }
                }
            }

        }

        /// <summary>
        /// Save wp to eeprom
        /// </summary>
        /// <param name="loc">location struct</param>
        /// <param name="index">wp no</param>
        /// <param name="frame">global or relative</param>
        /// <param name="current">0 = no , 2 = guided mode</param>
        public MAV_MISSION_RESULT setWP(Locationwp loc, ushort index, MAV_FRAME frame, byte current = 0)
        {
            giveComport = true;
            mavlink_mission_item_t req = new mavlink_mission_item_t();

            req.target_system = sysid;
            req.target_component = compid; // MAVLINK_MSG_ID_MISSION_ITEM

            req.command = loc.id;
            req.param1 = loc.p1;

            req.current = current;

            req.frame = (byte)frame;
            req.y = (float)(loc.lng);
            req.x = (float)(loc.lat);
            req.z = (float)(loc.alt);

            req.param1 = loc.p1;
            req.param2 = loc.p2;
            req.param3 = loc.p3;
            req.param4 = loc.p4;

            req.seq = index;

            log.InfoFormat("setWP {6} frame {0} cmd {1} p1 {2} x {3} y {4} z {5}", req.frame, req.command, req.param1, req.x, req.y, req.z, index);

            // request
            generatePacket(MAVLINK_MSG_ID_MISSION_ITEM, req);


            DateTime start = DateTime.Now;
            int retrys = 10;

            while (true)
            {
                if (!(start.AddMilliseconds(150) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
						reportStatus("setWP Retry " + retrys);
                        generatePacket(MAVLINK_MSG_ID_MISSION_ITEM, req);

                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - setWP");
                }
                byte[] buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == MAVLINK_MSG_ID_MISSION_ACK)
                    {
                        var ans = buffer.ByteArrayToStructure<mavlink_mission_ack_t>(6);
						reportStatus("set wp " + index + " ACK 47 : " + buffer[5] + " ans " + Enum.Parse(typeof(MAV_MISSION_RESULT), ans.type.ToString()));

                        if (req.current == 2)
                        {
                            GuidedMode = req;
                        }
                        else if (req.current == 3)
                        {

                        }
                        else
                        {
                            wps[req.seq] = req;
                        }

                        return (MAV_MISSION_RESULT)ans.type;
                    }
                    else if (buffer[5] == MAVLINK_MSG_ID_MISSION_REQUEST)
                    {
                        var ans = buffer.ByteArrayToStructure<mavlink_mission_request_t>(6);
                        if (ans.seq == (index + 1))
                        {
							reportStatus("set wp doing " + index + " req " + ans.seq + " REQ 40 : " + buffer[5]);
                            giveComport = false;

                            if (req.current == 2)
                            {
                                GuidedMode = req;
                            }
                            else if (req.current == 3)
                            {

                            }
                            else
                            {
                                wps[req.seq] = req;
                            }

                            return MAV_MISSION_RESULT.MAV_MISSION_ACCEPTED;
                        }
                        else
                        {
                            log.InfoFormat("set wp fail doing " + index + " req " + ans.seq + " ACK 47 or REQ 40 : " + buffer[5] + " seq {0} ts {1} tc {2}", req.seq, req.target_system, req.target_component);
                            //break;
                        }
                    }
                    else
                    {
						//reportStatus(DateTime.Now + " PC setwp " + buffer[5]);
                    }
                }
            }

            // return MAV_MISSION_RESULT.MAV_MISSION_INVALID;
        }

        public void setNextWPTargetAlt(ushort wpno, float alt)
        {
            // get the existing wp
            Locationwp current = getWP(wpno);

            mavlink_mission_write_partial_list_t req = new mavlink_mission_write_partial_list_t();
            req.target_system = sysid;
            req.target_component = compid;

            req.start_index = (short)wpno;
            req.end_index = (short)wpno;

            // change the alt
            current.alt = alt;

            // send a request to update single point
            generatePacket(MAVLINK_MSG_ID_MISSION_WRITE_PARTIAL_LIST, req);
            Thread.Sleep(10);
            generatePacket(MAVLINK_MSG_ID_MISSION_WRITE_PARTIAL_LIST, req);

            MAV_FRAME frame = (current.options & 0x1) == 0 ? MAV_FRAME.GLOBAL : MAV_FRAME.GLOBAL_RELATIVE_ALT;

            //send the point with new alt
            setWP(current, wpno, MAV_FRAME.GLOBAL_RELATIVE_ALT, 0);

            // set the point as current to reload the modified command
            setWPCurrent(wpno);

        }

        public void setGuidedModeWP(Locationwp gotohere)
        {
            if (gotohere.alt == 0 || gotohere.lat == 0 || gotohere.lng == 0)
                return;

            giveComport = true;

            try
            {
                gotohere.id = (byte)MAV_CMD.WAYPOINT;

                MAV_MISSION_RESULT ans = setWP(gotohere, 0, MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT, (byte)2);

                if (ans != MAV_MISSION_RESULT.MAV_MISSION_ACCEPTED)
                    throw new Exception("Guided Mode Failed");
            }
            catch (Exception ex) { log.Error(ex); }

            giveComport = false;
        }

        public void setNewWPAlt(Locationwp gotohere)
        {
            giveComport = true;

            try
            {
                gotohere.id = (byte)MAV_CMD.WAYPOINT;

                MAV_MISSION_RESULT ans = setWP(gotohere, 0, MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT, (byte)3);

                if (ans != MAV_MISSION_RESULT.MAV_MISSION_ACCEPTED)
                    throw new Exception("Alt Change Failed");
            }
            catch (Exception ex) { giveComport = false; log.Error(ex); throw; }

            giveComport = false;
        }

        public void setDigicamConfigure()
        {
            // not implmented
        }

        public void setDigicamControl(bool shot)
        {
            mavlink_digicam_control_t req = new mavlink_digicam_control_t();

            req.target_system = sysid;
            req.target_component = compid;
            req.shot = (shot == true) ? (byte)1 : (byte)0;

            generatePacket(MAVLINK_MSG_ID_DIGICAM_CONTROL, req);
            System.Threading.Thread.Sleep(20);
            generatePacket(MAVLINK_MSG_ID_DIGICAM_CONTROL, req);
        }

        public void setMountConfigure(MAV_MOUNT_MODE mountmode, bool stabroll, bool stabpitch, bool stabyaw)
        {
            mavlink_mount_configure_t req = new mavlink_mount_configure_t();

            req.target_system = sysid;
            req.target_component = compid;
            req.mount_mode = (byte)mountmode;
            req.stab_pitch = (stabpitch == true) ? (byte)1 : (byte)0;
            req.stab_roll = (stabroll == true) ? (byte)1 : (byte)0;
            req.stab_yaw = (stabyaw == true) ? (byte)1 : (byte)0;

            generatePacket(MAVLINK_MSG_ID_MOUNT_CONFIGURE, req);
            System.Threading.Thread.Sleep(20);
            generatePacket(MAVLINK_MSG_ID_MOUNT_CONFIGURE, req);
        }

        public void setMountControl(double pa, double pb, double pc, bool islatlng)
        {
            mavlink_mount_control_t req = new mavlink_mount_control_t();

            req.target_system = sysid;
            req.target_component = compid;
            if (!islatlng)
            {
                req.input_a = (int)pa;
                req.input_b = (int)pb;
                req.input_c = (int)pc;
            }
            else
            {
                req.input_a = (int)(pa * 10000000.0);
                req.input_b = (int)(pb * 10000000.0);
                req.input_c = (int)(pc * 100.0);
            }

            generatePacket(MAVLINK_MSG_ID_MOUNT_CONTROL, req);
            System.Threading.Thread.Sleep(20);
            generatePacket(MAVLINK_MSG_ID_MOUNT_CONTROL, req);
        }

 

        public void setMode(mavlink_set_mode_t mode, MAV_MODE_FLAG base_mode = 0)
        {
            mode.base_mode |= (byte)base_mode;

            generatePacket((byte)MAVLink.MAVLINK_MSG_ID_SET_MODE, mode);
            System.Threading.Thread.Sleep(10);
            generatePacket((byte)MAVLink.MAVLINK_MSG_ID_SET_MODE, mode);
        }

        /// <summary>
        /// used for last bad serial characters
        /// </summary>
        byte[] lastbad = new byte[2];

        /// <summary>
        /// Serial Reader to read mavlink packets. POLL method
        /// </summary>
        /// <returns></returns>
        public byte[] readPacket()
        {
            byte[] buffer = new byte[300];
            int count = 0;
            int length = 0;
            int readcount = 0;
            lastbad = new byte[2];

            byte[] headbuffer = new byte[6];

            BaseStream.ReadTimeout = 1200; // 1200 ms between chars - the gps detection requires this.

            //DateTime start = DateTime.Now;

			//reportStatus(DateTime.Now.Millisecond + " SR0 " + BaseStream.BytesToRead);

            try
            {
                // test fabs idea - http://diydrones.com/profiles/blogs/flying-with-joystick?commentId=705844%3AComment%3A818712&xg_source=msg_com_blogpost
                if (BaseStream.IsOpen && BaseStream.BytesToWrite > 0)
                {
                    // slow down execution. else 100% cpu
                    Thread.Sleep(1);
                    return new byte[0];
                }
            }
            catch (Exception ex) { 
				reportStatus ("Write exception: " + ex.ToString());
			}

            lock (readlock)
            {
				//reportStatus(DateTime.Now.Millisecond + " SR1 " + BaseStream.BytesToRead);

                while (BaseStream.IsOpen || logreadmode)
                {
                    try
                    {
                        if (readcount > 300)
                        {
							reportStatus("MAVLink readpacket No valid mavlink packets");
                            break;
                        }
                        readcount++;
                        if (logreadmode)
                        {
                            try
                            {
                                if (logplaybackfile.BaseStream.Position == 0)
                                {
                                    if (logplaybackfile.PeekChar() == '-')
                                    {
                                        oldlogformat = true;
                                    }
                                    else
                                    {
                                        oldlogformat = false;
                                    }
                                }
                            }
                            catch { oldlogformat = false; }

                            if (oldlogformat)
                            {
                                buffer = readlogPacket(); //old style log
                            }
                            else
                            {
                                buffer = readlogPacketMavlink();
                            }
                        }
                        else
                        {
							mavStateDateTime = DateTime.Now;

                            DateTime to = DateTime.Now.AddMilliseconds(BaseStream.ReadTimeout);

							// reportStatus(DateTime.Now.Millisecond + " SR1a " + BaseStream.BytesToRead);

                            while (BaseStream.BytesToRead <= 0)
                            {
                                if (DateTime.Now > to)
                                {
									reportError (string.Format("MAVLINK: 1 wait time out btr {0} len {1}", BaseStream.BytesToRead, length));
                                    throw new Exception("Timeout");
                                }
                                System.Threading.Thread.Sleep(1);
                                //reportStatus(DateTime.Now.Millisecond + " SR0b " + BaseStream.BytesToRead);
                            }
							//reportStatus(DateTime.Now.Millisecond + " SR1a " + BaseStream.BytesToRead);
                            if (BaseStream.IsOpen)
                            {
                                BaseStream.Read(buffer, count, 1);
                                if (rawlogfile != null && rawlogfile.BaseStream.CanWrite)
                                    rawlogfile.Write(buffer[count]);
                            }
							//reportStatus(DateTime.Now.Millisecond + " SR1b " + BaseStream.BytesToRead);
                        }
                    }
                    catch (Exception e) { 
						reportError("MAVLink readpacket read error: " + e.ToString()); 
						break; 
					}

                    // check if looks like a mavlink packet and check for exclusions and write to console
                    if (buffer[0] != 254)
                    {
                        if (buffer[0] >= 0x20 && buffer[0] <= 127 || buffer[0] == '\n' || buffer[0] == '\r')
                        {
                            // check for line termination
                            if (buffer[0] == '\r' || buffer[0] == '\n')
                            {
                                // check new line is valid
                                if (buildplaintxtline.Length > 3)
                                    plaintxtline = buildplaintxtline;

                                // reset for next line
                                buildplaintxtline = "";
                            }

                            //TODO removed for now
							//TCPConsole.Write(buffer[0]);
                            //Console.Write((char)buffer[0]);

                            buildplaintxtline += (char)buffer[0];
                        }
                        _bytesReceivedSubj.OnNext(1);
                        count = 0;
                        lastbad[0] = lastbad[1];
                        lastbad[1] = buffer[0];
                        buffer[1] = 0;
                        continue;
                    }
                    // reset count on valid packet
                    readcount = 0;

					//reportStatus(DateTime.Now.Millisecond + " SR2 " + BaseStream.BytesToRead);

                    // check for a header
                    if (buffer[0] == 254)
                    {
                        // if we have the header, and no other chars, get the length and packet identifiers
                        if (count == 0 && !logreadmode)
                        {
                            DateTime to = DateTime.Now.AddMilliseconds(BaseStream.ReadTimeout);

                            while (BaseStream.BytesToRead < 5)
                            {
                                if (DateTime.Now > to)
                                {
                                    log.InfoFormat("MAVLINK: 2 wait time out btr {0} len {1}", BaseStream.BytesToRead, length);
                                    throw new Exception("Timeout");
                                }
                                System.Threading.Thread.Sleep(1);
								//reportStatus(DateTime.Now.Millisecond + " SR0b " + BaseStream.BytesToRead);
                            }
                            int read = BaseStream.Read(buffer, 1, 5);
                            count = read;
                            if (rawlogfile != null && rawlogfile.BaseStream.CanWrite)
                                rawlogfile.Write(buffer, 1, read);
                        }

                        // packet length
                        length = buffer[1] + 6 + 2 - 2; // data + header + checksum - U - length
                        if (count >= 5 || logreadmode)
                        {
                            if (sysid != 0)
                            {
                                if (sysid != buffer[3] || compid != buffer[4])
                                {
                                    if (buffer[3] == '3' && buffer[4] == 'D')
                                    {
                                        // this is a 3dr radio rssi packet
                                    }
                                    else
                                    {
                                        log.InfoFormat("Mavlink Bad Packet (not addressed to this MAV) got {0} {1} vs {2} {3}", buffer[3], buffer[4], sysid, compid);
                                        return new byte[0];
                                    }
                                }
                            }

                            try
                            {
                                if (logreadmode)
                                {

                                }
                                else
                                {
                                    DateTime to = DateTime.Now.AddMilliseconds(BaseStream.ReadTimeout);

                                    while (BaseStream.BytesToRead < (length - 4))
                                    {
                                        if (DateTime.Now > to)
                                        {
                                            log.InfoFormat("MAVLINK: 3 wait time out btr {0} len {1}", BaseStream.BytesToRead, length);
                                            break;
                                        }
                                        System.Threading.Thread.Sleep(1);
                                    }
                                    if (BaseStream.IsOpen)
                                    {
                                        int read = BaseStream.Read(buffer, 6, length - 4);
                                        if (rawlogfile != null && rawlogfile.BaseStream.CanWrite)
                                        {
                                            // write only what we read, temp is the whole packet, so 6-end
                                            rawlogfile.Write(buffer, 6, read);
                                        }
                                    }
                                }
                                count = length + 2;
                            }
                            catch { break; }
                            break;
                        }
                    }

                    count++;
                    if (count == 299)
                        break;
                }

				//reportStatus(DateTime.Now.Millisecond + " SR3 " + BaseStream.BytesToRead);
            }// end readlock

            Array.Resize<byte>(ref buffer, count);

            _bytesReceivedSubj.OnNext(buffer.Length);

            if (!logreadmode && packetlosttimer.AddSeconds(5) < DateTime.Now)
            {
                packetlosttimer = DateTime.Now;
                packetslost = (packetslost * 0.8f);
                packetsnotlost = (packetsnotlost * 0.8f);
            }
            else if (logreadmode && packetlosttimer.AddSeconds(5) < lastlogread)
            {
                packetlosttimer = lastlogread;
                packetslost = (packetslost * 0.8f);
                packetsnotlost = (packetsnotlost * 0.8f);
            }

            //MAV.cs.linkqualitygcs = (ushort)((packetsnotlost / (packetsnotlost + packetslost)) * 100.0);

            if (bpstime.Second != DateTime.Now.Second && !logreadmode)
            {
                Console.Write("bps {0} loss {1} left {2} mem {3}      \n", bps1, synclost, BaseStream.BytesToRead, System.GC.GetTotalMemory(false) / 1024 / 1024.0);
                bps2 = bps1; // prev sec
                bps1 = 0; // current sec
                bpstime = DateTime.Now;
            }

            bps1 += buffer.Length;

            bps = (bps1 + bps2) / 2;

            if (buffer.Length >= 5 && buffer[3] == 255 && logreadmode) // gcs packet
            {
                getWPsfromstream(ref buffer);
                return buffer;// new byte[0];
            }

            ushort crc = MavlinkCRC.crc_calculate(buffer, buffer.Length - 2);

            if (buffer.Length > 5 && buffer[0] == 254)
            {
                crc = MavlinkCRC.crc_accumulate(MAVLINK_MESSAGE_CRCS[buffer[5]], crc);
            }

            if (buffer.Length > 5 && buffer[1] != MAVLINK_MESSAGE_LENGTHS[buffer[5]])
            {
                if (MAVLINK_MESSAGE_LENGTHS[buffer[5]] == 0) // pass for unknown packets
                {

                }
                else
                {
                    log.InfoFormat("Mavlink Bad Packet (Len Fail) len {0} pkno {1}", buffer.Length, buffer[5]);
                    if (buffer.Length == 11 && buffer[0] == 'U' && buffer[5] == 0)
                    {
                        string message = "Mavlink 0.9 Heartbeat, Please upgrade your AP, This planner is for Mavlink 1.0\n\n";
						reportError (message);
                        throw new Exception(message);
                    }
                    return new byte[0];
                }
            }

            if (buffer.Length < 5 || buffer[buffer.Length - 1] != (crc >> 8) || buffer[buffer.Length - 2] != (crc & 0xff))
            {
                int packetno = -1;
                if (buffer.Length > 5)
                {
                    packetno = buffer[5];
                }
                log.InfoFormat("Mavlink Bad Packet (crc fail) len {0} crc {1} pkno {2}", buffer.Length, crc, packetno);
                return new byte[0];
            }

            try
            {
                if ((buffer[0] == 'U' || buffer[0] == 254) && buffer.Length >= buffer[1])
                {
                    if (buffer[3] == '3' && buffer[4] == 'D')
                    {

                    }
                    else
                    {


                        byte packetSeqNo = buffer[2];
                        int expectedPacketSeqNo = ((recvpacketcount + 1) % 0x100);

                        {
                            if (packetSeqNo != expectedPacketSeqNo)
                            {
                                synclost++; // actualy sync loss's
                                int numLost = 0;

                                if (packetSeqNo < ((recvpacketcount + 1))) // recvpacketcount = 255 then   10 < 256 = true if was % 0x100 this would fail
                                {
                                    numLost = 0x100 - expectedPacketSeqNo + packetSeqNo;
                                }
                                else
                                {
                                    numLost = packetSeqNo - recvpacketcount;
                                }
                                packetslost += numLost;
                                WhenPacketLost.OnNext(numLost);

                                log.InfoFormat("lost {0} pkts {1}", packetSeqNo, (int)packetslost);
                            }

                            packetsnotlost++;

                            recvpacketcount = packetSeqNo;
                        }
                        WhenPacketReceived.OnNext(1);
						// reportStatus(DateTime.Now.Millisecond);
                    }

                    // Console.Write(temp[5] + " " + DateTime.Now.Millisecond + " " + packetspersecond[temp[5]] + " " + (DateTime.Now - packetspersecondbuild[temp[5]]).TotalMilliseconds + "     \n");

                    if (double.IsInfinity(packetspersecond[buffer[5]]))
                        packetspersecond[buffer[5]] = 0;

                    packetspersecond[buffer[5]] = (((1000 / ((DateTime.Now - packetspersecondbuild[buffer[5]]).TotalMilliseconds) + packetspersecond[buffer[5]]) / 2));

                    packetspersecondbuild[buffer[5]] = DateTime.Now;

					//reportStatus("Packet {0}",temp[5]);
                    // store packet history
                    lock (objlock)
                    {
                        MAV.packets[buffer[5]] = buffer;
                        MAV.packetseencount[buffer[5]]++;
                    }

                    if (debugmavlink)
                        DebugPacket(buffer);

                    if (buffer[5] == MAVLink.MAVLINK_MSG_ID_STATUSTEXT) // status text
                    {
                        string logdata = Encoding.ASCII.GetString(buffer, 7, buffer.Length - 7);
                        int ind = logdata.IndexOf('\0');
                        if (ind != -1)
                            logdata = logdata.Substring(0, ind);
						reportStatus(DateTime.Now + " " + logdata);

                    }

                    // set ap type
                    if (buffer[5] == MAVLink.MAVLINK_MSG_ID_HEARTBEAT)
                    {
                        mavlink_heartbeat_t hb = buffer.ByteArrayToStructure<mavlink_heartbeat_t>(6);

                        mavlinkversion = hb.mavlink_version;
                        aptype = (MAV_TYPE)hb.type;
                        apname = (MAV_AUTOPILOT)hb.autopilot;
                        setAPType();
                    }

                    getWPsfromstream(ref buffer);

                    try
                    {
                        if (logfile != null && logfile.BaseStream.CanWrite && !logreadmode)
                        {
                            lock (logfile)
                            {
                                byte[] datearray = BitConverter.GetBytes((UInt64)((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds * 1000));
                                Array.Reverse(datearray);
                                logfile.Write(datearray, 0, datearray.Length);
                                logfile.Write(buffer, 0, buffer.Length);

                                if (buffer[5] == 0)
                                {// flush on heartbeat - 1 seconds
                                    logfile.BaseStream.Flush();
                                    rawlogfile.BaseStream.Flush();
                                }
                            }
                        }

                    }
                    catch { }

                    try
                    {
                        // full rw from mirror stream
                        if (MirrorStream != null && MirrorStream.IsOpen)
                        {
                            MirrorStream.Write(buffer, 0, buffer.Length);

                            while (MirrorStream.BytesToRead > 0)
                            {
                                byte[] buf = new byte[1024];

                                int len = MirrorStream.Read(buf, 0, buf.Length);

                                BaseStream.Write(buf,0,len);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            if (buffer[3] == '3' && buffer[4] == 'D')
            {
                // dont update last packet time for 3dr radio packets
            }
            else
            {
                lastvalidpacket = DateTime.Now;
            }

            //            Console.Write((DateTime.Now - start).TotalMilliseconds.ToString("00.000") + "\t" + temp.Length + "     \r");

			//   reportStatus(DateTime.Now.Millisecond + " SR4 " + BaseStream.BytesToRead);

            return buffer;
        }

        /// <summary>
        /// Used to extract mission from log file
        /// </summary>
        /// <param name="buffer">packet</param>
        void getWPsfromstream(ref byte[] buffer)
        {
            if (buffer[5] == MAVLINK_MSG_ID_MISSION_COUNT)
            {
                // clear old
                wps.Clear();
                //new PointLatLngAlt[wps.Length];
            }

            if (buffer[5] == MAVLink.MAVLINK_MSG_ID_MISSION_ITEM)
            {
                mavlink_mission_item_t wp = buffer.ByteArrayToStructure<mavlink_mission_item_t>(6);

                if (wp.current == 2)
                {
                    // guide mode wp
                    GuidedMode = wp;
                }
                else
                {
                    wps[wp.seq] = wp;
                }


				reportStatus(String.Format("WP # {7} cmd {8} p1 {0} p2 {1} p3 {2} p4 {3} x {4} y {5} z {6}", 
				                           wp.param1, wp.param2, wp.param3, wp.param4, wp.x, wp.y, wp.z, wp.seq, wp.command));
            }
        }

        public PointLatLngAlt getFencePoint(int no, ref int total)
        {
            byte[] buffer;

            giveComport = true;

            PointLatLngAlt plla = new PointLatLngAlt();
            mavlink_fence_fetch_point_t req = new mavlink_fence_fetch_point_t();

            req.idx = (byte)no;
            req.target_component = compid;
            req.target_system = sysid;

            // request point
            generatePacket(MAVLINK_MSG_ID_FENCE_FETCH_POINT, req);

            DateTime start = DateTime.Now;
            int retrys = 3;

            while (true)
            {
                if (!(start.AddMilliseconds(500) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
						reportStatus("getFencePoint Retry " + retrys + " - giv com " + giveComport);
                        generatePacket(MAVLINK_MSG_ID_FENCE_FETCH_POINT, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - getFencePoint");
                }

                buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == MAVLINK_MSG_ID_FENCE_POINT)
                    {
                        giveComport = false;

                        mavlink_fence_point_t fp = buffer.ByteArrayToStructure<mavlink_fence_point_t>(6);

                        plla.Lat = fp.lat;
                        plla.Lng = fp.lng;
                        plla.Tag = fp.idx.ToString();

                        total = fp.count;

                        return plla;
                    }
                }
            }
        }

        public bool setFencePoint(byte index, PointLatLngAlt plla, byte fencepointcount)
        {
            mavlink_fence_point_t fp = new mavlink_fence_point_t();

            fp.idx = index;
            fp.count = fencepointcount;
            fp.lat = (float)plla.Lat;
            fp.lng = (float)plla.Lng;
            fp.target_component = compid;
            fp.target_system = sysid;

            int retry = 3;

            while (retry > 0)
            {
                generatePacket(MAVLINK_MSG_ID_FENCE_POINT, fp);
                int counttemp = 0;
                PointLatLngAlt newfp = getFencePoint(fp.idx, ref counttemp);

                if (newfp.Lat == plla.Lat && newfp.Lng == fp.lng)
                    return true;
                retry--;
            }

            return false;
        }

        byte[] readlogPacket()
        {
            byte[] temp = new byte[300];

            sysid = 0;

            int a = 0;
            while (a < temp.Length && logplaybackfile.BaseStream.Position != logplaybackfile.BaseStream.Length)
            {
                temp[a] = (byte)logplaybackfile.BaseStream.ReadByte();
                //Console.Write((char)temp[a]);
                if (temp[a] == ':')
                {
                    break;
                }
                a++;
                if (temp[0] != '-')
                {
                    a = 0;
                }
            }

            //Console.Write('\n');

            //Encoding.ASCII.GetString(temp, 0, a);
            string datestring = Encoding.ASCII.GetString(temp, 0, a);
			//reportStatus(datestring);
            long date = Int64.Parse(datestring);
            DateTime date1 = DateTime.FromBinary(date);

            lastlogread = date1;

            int length = 5;
            a = 0;
            while (a < length)
            {
                temp[a] = (byte)logplaybackfile.BaseStream.ReadByte();
                if (a == 1)
                {
                    length = temp[1] + 6 + 2 + 1;
                }
                a++;
            }

            return temp;
        }

        byte[] readlogPacketMavlink()
        {
            byte[] temp = new byte[300];

            sysid = 0;

            //byte[] datearray = BitConverter.GetBytes((ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds);

            byte[] datearray = new byte[8];

            //int tem = logplaybackfile.BaseStream.Read(datearray, 0, datearray.Length);

            Array.Reverse(datearray);

            DateTime date1 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            UInt64 dateint = BitConverter.ToUInt64(datearray, 0);

            try
            {
                date1 = date1.AddMilliseconds(dateint / 1000);

                lastlogread = date1.ToLocalTime();
            }
            catch { }

			mavStateDateTime = lastlogread;

            int length = 5;
            int a = 0;
            while (a < length)
            {
                temp[a] = (byte)logplaybackfile.ReadByte();
                if (temp[0] != 'U' && temp[0] != 254)
                {
                    log.InfoFormat("lost sync byte {0} pos {1}", temp[0], logplaybackfile.BaseStream.Position);
                    a = 0;
                    continue;
                }
                if (a == 1)
                {
                    length = temp[1] + 6 + 2; // 6 header + 2 checksum
                }
                a++;
            }

            // set ap type for log file playback
            if (temp[5] == 0)
            {
                mavlink_heartbeat_t hb = temp.ByteArrayToStructure<mavlink_heartbeat_t>(6);

                mavlinkversion = hb.mavlink_version;
                aptype = (MAV_TYPE)hb.type;
                apname = (MAV_AUTOPILOT)hb.autopilot;
                setAPType();
            }

            return temp;
        }


        public void setAPType()
        {
            switch (apname)
            {
                case MAV_AUTOPILOT.ARDUPILOTMEGA:
                    switch (aptype)
                    {
                        case MAVLink.MAV_TYPE.FIXED_WING:
							firmwareVersion = FirmwareVersion.ArduPlane;
                            break;
                        case MAVLink.MAV_TYPE.QUADROTOR:
							firmwareVersion = FirmwareVersion.ArduCopter2;
                            break;
                        case MAVLink.MAV_TYPE.GROUND_ROVER:
							firmwareVersion = FirmwareVersion.ArduRover;
                            break;
                        default:
                            break;
                    }
                    break;
                case MAV_AUTOPILOT.UDB:
                    switch (aptype)
                    {
                        case MAVLink.MAV_TYPE.FIXED_WING:
							firmwareVersion = FirmwareVersion.ArduPlane;
                            break;
                    }
                    break;
                case MAV_AUTOPILOT.GENERIC:
                    switch (aptype)
                    {
                        case MAVLink.MAV_TYPE.FIXED_WING:
							firmwareVersion = FirmwareVersion.Ateryx;
                            break;
                    }
                    break;
            }
        }

		public Type getFirmwareModes()
		{
		Type fwModes = typeof(apmmodes);

			switch (firmwareVersion) {

			case FirmwareVersion.ArduPlane:
			case FirmwareVersion.Ateryx:
				fwModes = typeof(apmmodes);
				break;

			case FirmwareVersion.ArduCopter2:
				fwModes = typeof(ac2modes);
				break;

			case FirmwareVersion.ArduRover:
				fwModes = typeof(aprovermodes);
				break;

			}

			return fwModes;
		}


		private bool wasBgActionCancelRequested()
		{

			bool result = false;
			//TODO currently there's no real distinction between foreground and background (same thread)
			//frmProgressReporter.doWorkArgs.CancelRequested

//			if (frmProgressReporter.doWorkArgs.CancelRequested)
//			{
//				frmProgressReporter.doWorkArgs.CancelAcknowledged = true;
//				giveComport = false;
//				frmProgressReporter.doWorkArgs.ErrorMessage = "User Canceled";
//				return parameterList;
//			}

			return result;
		}
		private void updateProgressAndStatus(float progress, string status)
		{
			reportStatus ("progress: " + progress + " status: " + status);
		}

		private void reportStatus(string status) 
		{
			Console.WriteLine (status);
		}

		private void reportError(string msg) 
		{
			Console.WriteLine (msg);
			Console.Error.WriteLine (msg);
		}

        public override string ToString() 
        {
            return "MAV " + sysid + " on " + BaseStream.PortName;
        }

    }
}