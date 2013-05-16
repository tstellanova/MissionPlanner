using System;
using apmcomms.Comms;
using apmgeometry;

using log4net;
using log4net.Config;

using KMLib;
using KMLib.Feature;
using KMLib.Geometry;

using System.IO.Ports;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System.Drawing;
using ICSharpCode.SharpZipLib.Zip;
using Core.Geometry;


namespace apmlog
{
	class MainClass
	{
		private static readonly ILog log = LogManager.GetLogger("Program");

		apmcomms.Comms.SerialPort comPort;

		bool threadrun = true;


		int receivedbytes = 0;
		serialstatus status = serialstatus.Connecting;
		Object thisLock = new Object();
		System.IO.StreamWriter sw;
		string logFileName = "";
		List<Data> flightdata = new List<Data>();
		Model runmodel = new Model();
		int currentlog = 0;
		int logcount = 0;



		public struct Data
		{
			public Model model;
			public string[] ntun;
			public string[] ctun;
			public int datetime;
		}

		enum serialstatus
		{
			Connecting,
			Createfile,
			Closefile,
			Reading,
			Waiting,
			Done
		}

		public static void Main (string[] args)
		{

			XmlConfigurator.Configure();
			log.Info("******************* Logging Configured *******************");

			Console.WriteLine ("******************* Connecting to APM *******************");
//			foreach (string a in args) {
//				Console.WriteLine ("'" + a + "'");
//			}

			string lePort = "bogus";
			string[] portNames = apmcomms.Comms.SerialPort.GetPortNames ();
			Console.WriteLine ("portNames: ");
			foreach (string portName in portNames) {
			//	Console.WriteLine (portName);
				lePort = portName;
			}


			MainClass monster = new MainClass ();
			monster.loadLog (lePort, 115200); //"tty.usbmodem1a12131",115200);
		}

		static void handleException(Exception ex)
		{
			log.Debug (ex.ToString());
		}

		private void waitForBytesAvailable(int time)
		{
			reportStatus ("waitForBytesAvailable: " + time);

			DateTime start = DateTime.Now;

			while ((DateTime.Now - start).TotalMilliseconds < time) {
				try {
					if (comPort.BytesToRead > 0)
					{
						return;
					}
				}
				catch {
					threadrun = false; 
					return; 
				}
			}
		}

		private void readAndSleep(int time)
		{
			reportStatus ("readAndSleep: " + time);

			DateTime start = DateTime.Now;

			while ((DateTime.Now - start).TotalMilliseconds < time )
			{
				try
				{
					if (comPort.BytesToRead > 0) {
						comPort_DataReceived((object)null, (SerialDataReceivedEventArgs)null);
					}
				}
				catch { 
					threadrun = false;  
					return; 
				}
			}
		}

		private void loadLog (string port, int baud)
		{
//			status = serialstatus.Connecting;
//			comPort = MainV2.comPort.BaseStream;

			comPort = new apmcomms.Comms.SerialPort ();

			comPort.PortName = port;
			comPort.BaudRate = baud;
			comPort.DtrEnable = false;
			comPort.RtsEnable = false;
			comPort.ReadBufferSize = 4 * 1024;

			Console.WriteLine ("open comPort: " + comPort.PortName);

			try
			{

				if (!comPort.IsOpen) {
					comPort.Open();
				}

				reportStatus("comPort open: " + comPort.IsOpen);

				comPort.toggleDTR();
				comPort.DiscardInBuffer();

				try {
					// try provoke a response

					//TODO loop sending CR LF until we get ArduPlane or ArduCopter
					comPort.Write("\n\n?\r\n\n");
				}
				catch { }

				waitForBytesAvailable(15000);

				reportStatus ("BytesToRead: " + comPort.BytesToRead );

			}
			catch (Exception ex)
			{
				log.Error("Error opening comport", ex);
				reportError("comport open ex: " + ex.ToString());
				if (ex.Message == "No such file or directory") {
					reportError("comport open ex data: " + ex.Data.ToString());
				}
			}

//			var t11 = new System.Threading.Thread(delegate()
//			                                      {
//				threadrun = true;
			//				readAndSleep(100);
//
//				try{
//					comPort.Write("\n\n\n\nexit\r\nlogs\r\n"); // more in "connecting"
//				}
//				catch{}
//
//
//				while (threadrun)
//				{
//					try
//					{
//						System.Threading.Thread.Sleep(10);
//						if (!comPort.IsOpen)
//							break;
//						while (comPort.BytesToRead >= 4)
//						{
//							comPort_DataReceived((object)null, (SerialDataReceivedEventArgs)null);
//						}
//					}
//					catch (Exception ex)
//					{
//						log.Error("crash in comport reader " + ex);
//					} // cant exit unless told to
//				}
//				log.Info("Comport thread close");
//			}) {Name = "comport reader"};
//			t11.Start();


			downloadThread (2,3); //TODO

		}

		void comPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
		{
			try {
				while (comPort.BytesToRead > 0 && threadrun) {
					//### updateDisplay();

					string line = "";

					comPort.ReadTimeout = 500;
					try {
						line = comPort.ReadLine(); 
						if (!line.Contains("\n"))
							line = line + "\n";
					}
					catch (Exception readEx) {
						reportError("ex while reading [" + comPort.BytesToRead + "]: " + readEx);

						try {
							line = comPort.ReadExisting();
						}
						catch {}
					}

					receivedbytes += line.Length;

//					reportStatus("status: " + status + " length: " + receivedbytes);
//					reportStatus("line: " + line);

					switch (status) {

						case serialstatus.Done:
							reportStatus(" " + status + " line: " + line);

							{
								Regex regex2 = new Regex(@"^Log ([0-9]+)[,\s]", RegexOptions.IgnoreCase);
								if (regex2.IsMatch(line))
								{
									MatchCollection matchs = regex2.Matches(line);
									logcount = int.Parse(matchs[0].Groups[1].Value);
									reportStatus("logcount: " + logcount);
									//genchkcombo(logcount);
									//status = serialstatus.Done;
								}
							}
							if (line.Contains("No logs")) {
								status = serialstatus.Done;
							}
							break;

						case serialstatus.Connecting:
							reportStatus(" " + status + " line: " + line);

							if (line.Contains("ENTER") || 
							    line.Contains("GROUND START") || 
							    line.Contains("reset to FLY") || 
							    line.Contains("interactive setup") || 
							    line.Contains("CLI") || 
							    line.Contains("Ardu"))
							{
								try
								{
									System.Threading.Thread.Sleep(200);
									comPort.Write("\n\n\n\n");
								}
								catch { }

								System.Threading.Thread.Sleep(500);

								//request logs mode
								comPort.Write("logs\r");
								status = serialstatus.Done;
							}
							break;

						case serialstatus.Closefile:
							reportStatus(" " + status + " line: " + line);

							sw.Close();
							System.IO.TextReader tr = new System.IO.StreamReader(logFileName);

							reportStatus("Creating KML for " + logFileName);

							while (tr.Peek() != -1) {
								processRawLogLine(tr.ReadLine());
							}
							tr.Close();

							try {
								writeKML(logFileName + ".kml");
							}
							catch { 
								//TODO  usualy invalid lat long error
							} 

							status = serialstatus.Done;
							comPort.DiscardInBuffer();
							break;

						case serialstatus.Createfile:
							reportStatus(" " + status + " line: " + line);
							receivedbytes = 0;
							status = serialstatus.Waiting;
							logFileName = Directory.GetCurrentDirectory() + 
								Path.DirectorySeparatorChar +
								DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + 
								".log";
							reportStatus ("Creating log output file: " + logFileName);
							sw = new System.IO.StreamWriter(logFileName);
							status = serialstatus.Reading;
							break;


						case serialstatus.Reading:
							if (line.Contains("packets read") || 
						    line.Contains("Done") || 
						    line.Contains("logs enabled"))
							{
								reportStatus(" " + status + " line: " + line);
								status = serialstatus.Closefile;
								break;
							}
							sw.Write(line);
							continue;

						case serialstatus.Waiting:
							reportStatus(" " + status + " line: " + line);
							if (line.Contains("Dumping Log") || 
							    line.Contains("GPS:") || 
							    line.Contains("NTUN:") || 
							    line.Contains("CTUN:") || 
							    line.Contains("PM:"))
							{
								status = serialstatus.Reading;
							}
							break;
					}

				}

			}
			catch (Exception ex) { reportError ("Error reading data" + ex.ToString()); }
		}


		string lastline = "";
		string[] ctunlast = new string[] { "", "", "", "", "", "", "", "", "", "", "", "", "", "" };
		string[] ntunlast = new string[] { "", "", "", "", "", "", "", "", "", "", "", "", "", "" };
		List<PointLatLngAlt> cmd = new List<PointLatLngAlt>();
		Point3D oldlastpos = new Point3D();
		Point3D lastpos = new Point3D();

		private void processRawLogLine(string line)
		{
			//TODO WTF
//			if (CHK_arducopter.Checked)
//			{
//				MainV2.comPort.MAV.cs.firmware = MainV2.Firmwares.ArduCopter2;
//			}
//			else
//			{
//				MainV2.comPort.MAV.cs.firmware = MainV2.Firmwares.ArduPlane;
//			}

			reportStatus ("processRawLogLine:");
			reportStatus (line);

			try
			{
				line = line.Replace(", ", ",");
				line = line.Replace(": ", ":");

				string[] items = line.Split(',', ':');

				if (items[0].Contains("CMD"))
				{
					if (flightdata.Count == 0)
					{
						if (int.Parse(items[2]) <= (int)95) //TODO ArdupilotMega.MAVLink.MAV_CMD.LAST) // wps
						{
							PointLatLngAlt temp = new PointLatLngAlt(double.Parse(items[7], new System.Globalization.CultureInfo("en-US")) / 10000000, double.Parse(items[8], new System.Globalization.CultureInfo("en-US")) / 10000000, double.Parse(items[6], new System.Globalization.CultureInfo("en-US")) / 100, items[1].ToString());
							cmd.Add(temp);
						}
					}
				}
				if (items[0].Contains("MOD"))
				{
					positionindex++;
					modelist.Add(""); // i cant be bothered doing this properly
					modelist.Add("");
					modelist[positionindex] = (items[1]);
				}
				//GPS, 1, 15691, 10, 0.00, -35.3629379, 149.1650850, -0.08, 585.41, 0.00, 126.89
				if (items[0].Contains("GPS") && items[2] == "1" && items[4] != "0" && items[4] != "-1" && lastline != line) // check gps line and fixed status
				{
					//TODO wtf
//					MainV2.comPort.MAV.cs.firmware = MainV2.Firmwares.ArduPlane;

					if (position[positionindex] == null)
						position[positionindex] = new List<Point3D>();

					if (double.Parse(items[4], new System.Globalization.CultureInfo("en-US")) == 0)
						return;

					double alt = double.Parse(items[6], new System.Globalization.CultureInfo("en-US"));

					if (items.Length == 11 && items[6] == "0.0000")
						alt = double.Parse(items[7], new System.Globalization.CultureInfo("en-US"));
					if (items.Length == 11 && items[6] == "0")
						alt = double.Parse(items[7], new System.Globalization.CultureInfo("en-US"));


					position[positionindex].Add(new Point3D(double.Parse(items[5], new System.Globalization.CultureInfo("en-US")), double.Parse(items[4], new System.Globalization.CultureInfo("en-US")), alt));
					oldlastpos = lastpos;
					lastpos = (position[positionindex][position[positionindex].Count - 1]);
					lastline = line;
				}
				if (items[0].Contains("GPS") && items[4] != "0" && items[4] != "-1" && items.Length <= 9) // AC
				{
					//TODO wtf

//					MainV2.comPort.MAV.cs.firmware = MainV2.Firmwares.ArduCopter2;

					if (position[positionindex] == null)
						position[positionindex] = new List<Point3D>();

					if (double.Parse(items[4], new System.Globalization.CultureInfo("en-US")) == 0)
						return;

					double alt = double.Parse(items[5], new System.Globalization.CultureInfo("en-US"));

					position[positionindex].Add(new Point3D(double.Parse(items[4], new System.Globalization.CultureInfo("en-US")), double.Parse(items[3], new System.Globalization.CultureInfo("en-US")), alt));
					oldlastpos = lastpos;
					lastpos = (position[positionindex][position[positionindex].Count - 1]);
					lastline = line;

				}
				//GPS, 1, 15691, 10, 0.00, -35.3629379, 149.1650850, -0.08, 585.41, 0.00, 126.89
				if (items[0].Contains("GPS") && items[1] == "3" && items[4] != "0" && items[4] != "-1" && lastline != line) // check gps line and fixed status
				{
					if (position[positionindex] == null)
						position[positionindex] = new List<Point3D>();

					//  if (double.Parse(items[4], new System.Globalization.CultureInfo("en-US")) == 0)
					//     return;

					double alt = double.Parse(items[8], new System.Globalization.CultureInfo("en-US"));

					position[positionindex].Add(new Point3D(double.Parse(items[6], new System.Globalization.CultureInfo("en-US")), double.Parse(items[5], new System.Globalization.CultureInfo("en-US")), alt));
					oldlastpos = lastpos;
					lastpos = (position[positionindex][position[positionindex].Count - 1]);
					lastline = line;
				}
				if (items[0].Contains("CTUN"))
				{
					ctunlast = items;
				}
				if (items[0].Contains("NTUN"))
				{
					ntunlast = items;
					line = "ATT:" + double.Parse(ctunlast[3], new System.Globalization.CultureInfo("en-US")) * 100 + "," + double.Parse(ctunlast[6], new System.Globalization.CultureInfo("en-US")) * 100 + "," + double.Parse(items[1], new System.Globalization.CultureInfo("en-US")) * 100;
					items = line.Split(',', ':');
				}
				if (items[0].Contains("ATT"))
				{
					try
					{
						if (lastpos.X != 0 && oldlastpos.X != lastpos.X && oldlastpos.Y != lastpos.Y)
						{
							Data dat = new Data();

							try
							{
								dat.datetime = int.Parse(lastline.Split(',', ':')[1]);
							}
							catch { }

							runmodel = new Model();

							runmodel.Location.longitude = lastpos.X;
							runmodel.Location.latitude = lastpos.Y;
							runmodel.Location.altitude = lastpos.Z;

							oldlastpos = lastpos;

							runmodel.Orientation.roll = double.Parse(items[1], new System.Globalization.CultureInfo("en-US")) / -100;
							runmodel.Orientation.tilt = double.Parse(items[2], new System.Globalization.CultureInfo("en-US")) / -100;
							runmodel.Orientation.heading = double.Parse(items[3], new System.Globalization.CultureInfo("en-US")) / 100;

							dat.model = runmodel;
							dat.ctun = ctunlast;
							dat.ntun = ntunlast;

							flightdata.Add(dat);
						}
					}
					catch { }
				}
			}
			catch (Exception)
			{
				// if items is to short or parse fails.. ignore
			}
		}

		List<string> modelist = new List<string>();
		List<Point3D>[] position = new List<Point3D>[200];
		int positionindex = 0;

		private void writeGPX(string filename)
		{
			System.Xml.XmlTextWriter xw = new System.Xml.XmlTextWriter(Path.GetDirectoryName(filename) + 
			                                                           Path.DirectorySeparatorChar + 
			                                                           Path.GetFileNameWithoutExtension(filename) + 
			                                                           ".gpx",
			                                                           System.Text.Encoding.ASCII);

			xw.WriteStartElement("gpx");

			xw.WriteStartElement("trk");

			xw.WriteStartElement("trkseg");

			DateTime start = new DateTime(DateTime.Now.Year,DateTime.Now.Month,DateTime.Now.Day,0,0,0);

			foreach (Data mod in flightdata)
			{
				xw.WriteStartElement("trkpt");
				xw.WriteAttributeString("lat", mod.model.Location.latitude.ToString(new System.Globalization.CultureInfo("en-US")));
				xw.WriteAttributeString("lon", mod.model.Location.longitude.ToString(new System.Globalization.CultureInfo("en-US")));

				xw.WriteElementString("ele", mod.model.Location.altitude.ToString(new System.Globalization.CultureInfo("en-US")));
				xw.WriteElementString("time", start.AddMilliseconds(mod.datetime).ToString("yyyy-MM-ddTHH:mm:sszzzzzz"));
				xw.WriteElementString("course", (mod.model.Orientation.heading).ToString(new System.Globalization.CultureInfo("en-US")));

				xw.WriteElementString("roll", mod.model.Orientation.roll.ToString(new System.Globalization.CultureInfo("en-US")));
				xw.WriteElementString("pitch", mod.model.Orientation.tilt.ToString(new System.Globalization.CultureInfo("en-US")));
				//xw.WriteElementString("speed", mod.model.Orientation.);
				//xw.WriteElementString("fix", mod.model.Location.altitude);

				xw.WriteEndElement();
			}

			xw.WriteEndElement();
			xw.WriteEndElement();
			xw.WriteEndElement();

			xw.Close();
		}

		private void writeKML(string filename)
		{
			try
			{
				writeGPX(filename);
			}
			catch { }

			Color[] colours = { Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Indigo, Color.Violet, Color.Pink };

			AltitudeMode altmode = AltitudeMode.absolute;

			//TODO handle 
//			if (MainV2.comPort.MAV.cs.firmware == MainV2.Firmwares.ArduCopter2)
//			{
//				altmode = AltitudeMode.relativeToGround; // because of sonar, this is both right and wrong. right for sonar, wrong in terms of gps as the land slopes off.
//			}

			KMLRoot kml = new KMLRoot();
			Folder fldr = new Folder("Log");

			Style style = new Style();
			style.Id = "yellowLineGreenPoly";
			style.Add(new LineStyle(HexStringToColor("7f00ffff"), 4));

			PolyStyle pstyle = new PolyStyle();
			pstyle.Color = HexStringToColor("7f00ff00");
			style.Add(pstyle);

			kml.Document.AddStyle(style);

			int stylecode = 0xff;
			int g = -1;
			foreach (List<Point3D> poslist in position)
			{
				g++;
				if (poslist == null)
					continue;

				LineString ls = new LineString();
				ls.AltitudeMode = altmode;
				ls.Extrude = true;
				//ls.Tessellate = true;

				Coordinates coords = new Coordinates();
				coords.AddRange(poslist);

				ls.coordinates = coords;

				Placemark pm = new Placemark();

				string mode = "";
				if (g < modelist.Count)
					mode = modelist[g];

				pm.name = g + " Flight Path " + mode;
				pm.styleUrl = "#yellowLineGreenPoly";
				pm.LineString = ls;

				stylecode = colours[g % (colours.Length - 1)].ToArgb();

				Style style2 = new Style();
				Color color = Color.FromArgb(0xff, (stylecode >> 16) & 0xff, (stylecode >> 8) & 0xff, (stylecode >> 0) & 0xff);
				log.Info("colour " + color.ToArgb().ToString("X") + " " + color.ToKnownColor().ToString());
				style2.Add(new LineStyle(color, 4));



				pm.AddStyle(style2);

				fldr.Add(pm);
			}

			Folder planes = new Folder();
			planes.name = "Planes";
			fldr.Add(planes);

			Folder waypoints = new Folder();
			waypoints.name = "Waypoints";
			fldr.Add(waypoints);


			LineString lswp = new LineString();
			lswp.AltitudeMode = AltitudeMode.relativeToGround;
			lswp.Extrude = true;

			Coordinates coordswp = new Coordinates();

			foreach (PointLatLngAlt p1 in cmd)
			{
				Point3D lePt = new  Point3D (p1.Lng, p1.Lat, p1.Alt);
				coordswp.Add(lePt);
			}

			lswp.coordinates = coordswp;

			Placemark pmwp = new Placemark();

			pmwp.name = "Waypoints";
			//pm.styleUrl = "#yellowLineGreenPoly";
			pmwp.LineString = lswp;

			waypoints.Add(pmwp);

			int a = 0;
			int l = -1;

			Model lastmodel = null;

			foreach (Data mod in flightdata)
			{
				l++;
				if (mod.model.Location.latitude == 0)
					continue;

				if (lastmodel != null)
				{
					if (lastmodel.Location.Equals(mod.model.Location))
					{
						continue;
					}
				}
				Placemark pmplane = new Placemark();
				pmplane.name = "Plane " + a;

				pmplane.visibility = false;

				Model model = mod.model;
				model.AltitudeMode = altmode;
				model.Scale.x = 2;
				model.Scale.y = 2;
				model.Scale.z = 2;

				try
				{
					pmplane.description = @"<![CDATA[
              <table>
                <tr><td>Roll: " + model.Orientation.roll + @" </td></tr>
                <tr><td>Pitch: " + model.Orientation.tilt + @" </td></tr>
                <tr><td>Yaw: " + model.Orientation.heading + @" </td></tr>
                <tr><td>WP dist " + mod.ntun[2] + @" </td></tr>
				<tr><td>tar bear " + mod.ntun[3] + @" </td></tr>
				<tr><td>nav bear " + mod.ntun[4] + @" </td></tr>
				<tr><td>alt error " + mod.ntun[5] + @" </td></tr>
              </table>
            ]]>";
				}
				catch { }

				try
				{

					pmplane.Point = new KmlPoint((float)model.Location.longitude, (float)model.Location.latitude, (float)model.Location.altitude);
					pmplane.Point.AltitudeMode = altmode;

					Link link = new Link();
					link.href = "block_plane_0.dae";

					model.Link = link;

					pmplane.Model = model;

					planes.Add(pmplane);
				}
				catch { } // bad lat long value

				lastmodel = mod.model;

				a++;
			}

			kml.Document.Add(fldr);

			kml.Save(filename);

			// create kmz - aka zip file

			FileStream fs = File.Open(filename.Replace(".log.kml", ".kmz"), FileMode.Create);
			ZipOutputStream zipStream = new ZipOutputStream(fs);
			zipStream.SetLevel(9); //0-9, 9 being the highest level of compression

			//TODO nonexist?
			//zipStream.UseZip64 = UseZip64.Off; // older zipfile

			// entry 1
			string entryName = ZipEntry.CleanName(Path.GetFileName(filename)); // Removes drive from name and fixes slash direction
			ZipEntry newEntry = new ZipEntry(entryName);
			newEntry.DateTime = DateTime.Now;

			zipStream.PutNextEntry(newEntry);

			// Zip the file in buffered chunks
			// the "using" will close the stream even if an exception occurs
//			byte[] buffer = new byte[4096];
			using (FileStream streamReader = File.OpenRead(filename))
			{
				streamReader.CopyTo (zipStream);
				//StreamUtils.Copy(streamReader, zipStream, buffer);
			}
			zipStream.CloseEntry();

			File.Delete(filename);

			//TODO verify for cmd line 
			string dir =  Directory.GetCurrentDirectory (); //Path.GetDirectoryName(Application.ExecutablePath)
			filename =  dir + Path.DirectorySeparatorChar + "block_plane_0.dae";

			// entry 2
			entryName = ZipEntry.CleanName(Path.GetFileName(filename)); // Removes drive from name and fixes slash direction
			newEntry = new ZipEntry(entryName);
			newEntry.DateTime = DateTime.Now;

			zipStream.PutNextEntry(newEntry);

			// Zip the file in buffered chunks
			// the "using" will close the stream even if an exception occurs
//			buffer = new byte[4096];
			using (FileStream streamReader = File.OpenRead(filename))
			{
				streamReader.CopyTo (zipStream);
//				StreamUtils.Copy(streamReader, zipStream, buffer);
			}
			zipStream.CloseEntry();


			zipStream.IsStreamOwner = true;	// Makes the Close also Close the underlying stream
			zipStream.Close();

			positionindex = 0;
			modelist.Clear();
			flightdata.Clear();
			position = new List<Point3D>[200];
			cmd.Clear();
		}


		private void downloadThread(int startlognum, int endlognum)
		{
			threadrun = true;
			System.Threading.Thread t12 = new System.Threading.Thread (delegate() { 

				try {
					comPort.Write("\n\n\n\nexit\r\nlogs\r\n"); 

					for (int a = startlognum; a <= endlognum; a++) {
						currentlog = a;
						System.Threading.Thread.Sleep(100);
						comPort.Write ("dump " + a.ToString() + "\r\n");
						System.Threading.Thread.Sleep(100);
						comPort.DiscardInBuffer();
						status = serialstatus.Createfile;

						while (status != serialstatus.Done) {
							readAndSleep(100);
						}
					}
				}
				catch (Exception downloadEx) {
					reportError("ex downloadThread" + downloadEx);
				}
			});

			t12.Name = "Log Download All thread";
			t12.Start();


		}



		public static Color HexStringToColor(string hexColor)
		{
			string hc = (hexColor);
			if (hc.Length != 8)
			{
				// you can choose whether to throw an exception
				//throw new ArgumentException("hexColor is not exactly 6 digits.");
				return Color.Empty;
			}
			string a = hc.Substring(0, 2);
			string r = hc.Substring(6, 2);
			string g = hc.Substring(4, 2);
			string b = hc.Substring(2, 2);
			Color color = Color.Empty;
			try
			{
				int ai
					= Int32.Parse(a, System.Globalization.NumberStyles.HexNumber);
				int ri
					= Int32.Parse(r, System.Globalization.NumberStyles.HexNumber);
				int gi
					= Int32.Parse(g, System.Globalization.NumberStyles.HexNumber);
				int bi
					= Int32.Parse(b, System.Globalization.NumberStyles.HexNumber);
				color = Color.FromArgb(ai, ri, gi, bi);
			}
			catch
			{
				// you can choose whether to throw an exception
				//throw new ArgumentException("Conversion failed.");
				return Color.Empty;
			}
			return color;
		}


		/**
		 * Replaces TXT_seriallog nonsense
		 * */
		private void reportStatus(string status) 
		{
			lock (thisLock) {
				Console.WriteLine (status);
			}
		}

		/**
		 * Replaces CustomMessageBox nonsense
		 * */
		private void reportError(string error) 
		{
			Console.Error.WriteLine(error);
		}
	}


}
