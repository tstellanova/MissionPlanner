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

		int totalLogIdsCount = 0;

		//String firmwareVersionString = "";
		SortedSet<String> allLogIds = new SortedSet<String> ();
		System.Globalization.CultureInfo cultureInfo = new System.Globalization.CultureInfo("en-US");

		int receivedbytes = 0;
		serialstatus status = serialstatus.Idle;
		Object thisLock = new Object();
		System.IO.StreamWriter sw;
		string logFileName = "";
		List<Data> flightdata = new List<Data>();
		Model runmodel = new Model();



		public struct Data
		{
			public Model model;
			public string[] ntun;
			public string[] ctun;
			public int datetime;
		}

		enum serialstatus
		{
			Idle,
			Wakeup,
			Awake,
			SeekingPrompt,
			SeekingLogs,
			CollectingLogIds,
			CreateLogFile,
			Closefile,
			Reading,
			Waiting,
			DoneDumping,
			ConfirmErasing,
			Finished
		}

		public static void Main (string[] args)
		{

			XmlConfigurator.Configure();
			log.Info("******************* Logging Configured *******************");

			Console.WriteLine ("******************* Connecting to APM *******************");


			string lePort = "bogus";
			string[] portNames = apmcomms.Comms.SerialPort.GetPortNames ();
			foreach (string portName in portNames) {
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

		private void readUntilTime(int time)
		{
			DateTime start = DateTime.Now;
			DateTime endTime = start.AddMilliseconds (time);
			reportStatus ("readUntilTime: " + endTime.ToLocalTime());

			while ((DateTime.Now - start).TotalMilliseconds < time ) {
				try {
					if (comPort.BytesToRead > 0) {
						processReceivedData();
					} else {
						//System.Threading.Thread.Sleep(10);
					}
				}
				catch { 
					threadrun = false;  
					return; 
				}
			}
		}

		private void seekLogsMode()
		{
			try
			{
				System.Threading.Thread.Sleep(200);
				reportStatus ("Sending logs mode request...");
				comPort.Write("\n\n\n\n");
				System.Threading.Thread.Sleep(500);
				comPort.DiscardInBuffer();
				comPort.Write("logs\r");
				//request logs mode
				status = serialstatus.SeekingLogs;
			}
			catch { 
			
			}
	
		}

		private void seekCommandPrompt()
		{

			var t11 = new System.Threading.Thread(delegate() {
				threadrun = true;
				status = serialstatus.SeekingPrompt;

			//	readUntilTime(100);

				try {
					reportStatus ("Requesting command prompt...");
					comPort.Write("\n\n\n\n"); 
					//comPort.Write("\n\n\n\nexit\r\nlogs\r\n"); // more in "connecting"
				}
				catch {
				}

				while (threadrun) {
					try {
						System.Threading.Thread.Sleep(10);
						if (!comPort.IsOpen)
							break;

						while (threadrun && (comPort.BytesToRead >= 4) ) {
							processReceivedData();
						}
					}
					catch (Exception ex) {
						log.Error("crash in comport reader " + ex);
					} // cant exit unless told to
				}

				if (allLogIds.Count > 0) {
					dumpAllLogs();
				}

				reportStatus("T11 thread close");
			}) {Name = "T11"};
			t11.Start();
		}

		private void wakeupArdupilot()
		{
			status = serialstatus.Wakeup;

			bool continueWakeup = true;
			bool wakeupSuccess = false;

			reportStatus("open comPort: " + comPort.PortName);

			try {
				comPort.ReadTimeout = 500;

				if (!comPort.IsOpen) {
					comPort.Open();
				}

				reportStatus("comPort open: " + comPort.IsOpen);
				if (!comPort.IsOpen) {
					String errorMsg = "Failed to open comPort " + comPort.PortName;
					reportError(errorMsg);
					return;
				}
				comPort.toggleDTR();
				comPort.DiscardInBuffer();


				for (int i = 0; (i < 10) && continueWakeup; i++) {
					reportStatus("Sending wakeup...");
					try {
						comPort.Write("\r\n\r\n\r\n");

						System.Threading.Thread.Sleep(500);

						if (comPort.BytesToRead > 7) {
							continueWakeup = false;
							wakeupSuccess = true;
						}
					}
					catch (Exception wakeEx) {
						reportError("Error on wakeup: " + wakeEx.ToString());
						continueWakeup = false;
					}
				}



//				if (wakeupSuccess) {
//					firmwareVersionString.Trim();
//					//"ArduPlane V2.72]"
//					int lastBracketIdx = firmwareVersionString.LastIndexOf(']');
//					if (-1 != lastBracketIdx) {
//						firmwareVersionString.Remove(lastBracketIdx);
//					}
//					reportStatus("firmwareVersionString: " + firmwareVersionString);
//				}

			}
			catch (Exception ex) {
				reportError("comPort wakeup ex: " + ex.ToString());
			}

			if (!wakeupSuccess) {
				reportError("Could not wakeup board!");
			} else {
				status =	serialstatus.Awake;
			}


		}

		private void loadLog (string port, int baud)
		{

			comPort = new apmcomms.Comms.SerialPort ();

			comPort.PortName = port;
			comPort.BaudRate = baud;
			comPort.DtrEnable = false;
			comPort.RtsEnable = false;
			comPort.ReadBufferSize = 4 * 1024;


			wakeupArdupilot ();

			if (status == serialstatus.Awake) {
				seekCommandPrompt();
			}

		}

		void processReceivedData()
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
						if (comPort.BytesToRead > 0) {
							reportError("ex while reading [" + comPort.BytesToRead + "]: " + readEx);

							try {
								line = comPort.ReadExisting();
							}
							catch {}
						}
					}

					receivedbytes += line.Length;

					switch (status) {

						case serialstatus.CollectingLogIds:
							{
								Regex regex2 = new Regex(@"^Log ([0-9]+)[,\s]", RegexOptions.IgnoreCase); //"Log 26,    start 567,   end 1140\r\n"
								if (regex2.IsMatch(line)) {
									MatchCollection match = regex2.Matches(line);
									String curLogId = match[0].Groups[1].Value;
									reportStatus ("Log Available: " + line); // curLogId);
									allLogIds.Add(curLogId);
									if (allLogIds.Count == totalLogIdsCount) {
										reportStatus("Done collecting log IDs...");
										comPort.DiscardInBuffer();
										threadrun = false;
									}
								} 
								else {
									reportStatus("status: " + status + " line: " + line);
									if (line.Contains("No logs")) {
										status = serialstatus.DoneDumping;
									}
								}
							}

							break;

						case serialstatus.SeekingPrompt:
							reportStatus("status: " + status + " line: " + line);

							if (line.Contains("ENTER") || 
							    line.Contains("GROUND START") || 
							    line.Contains("reset to FLY") || 
							    line.Contains("interactive setup") || 
							    line.Contains("CLI") || 
							    line.Contains("Ardu"))
							{
								seekLogsMode();
							}
							break;

						case serialstatus.SeekingLogs:
							{
								//Look for line that says eg "19 logs"
								Regex regex3 = new Regex(@"([0-9]+) logs" , RegexOptions.IgnoreCase);
								if (regex3.IsMatch(line)) {
									MatchCollection match = regex3.Matches(line);
									totalLogIdsCount = int.Parse ( match[0].Groups[1].Value);
									reportStatus ("Expecting " + totalLogIdsCount + " log IDs");
									status = serialstatus.CollectingLogIds;
								}
								else {
									reportStatus("status: " + status + " line: " + line);
								}
							}
							break;



						case serialstatus.Closefile:
							//reportStatus(" " + status + " line: " + line);
							reportStatus ("Flushing " + logFileName);
							sw.Flush();
							sw.Close();

							flightdata.Clear();
							System.IO.TextReader tr = new System.IO.StreamReader(logFileName);							
							while (tr.Peek() != -1) {
								line = tr.ReadLine();
								processRawLogLine(line);
							}
							tr.Close();

							if (flightdata.Count > 0) {
								try {
									string gpxFileName = logFileName.Replace (".log", ".gpx");
									writeGPX(gpxFileName);
									writeKML(logFileName + ".kml");
								}
								catch { 
									//TODO  usualy invalid lat long error
								} 
							} 
							else {
								reportStatus("No flight data: skip GPX + KML");
							}


							positionindex = 0;
							modelList.Clear();
							flightdata.Clear();
							positionsList = new List<Point3D>[200];
							cmdList.Clear();

							status = serialstatus.DoneDumping;
							comPort.DiscardInBuffer();
							break;

					case serialstatus.CreateLogFile:
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
								reportStatus("status: " + status + " line: " + line);
								status = serialstatus.Closefile;
								break;
							}
							sw.Write(line);
							continue;

						case serialstatus.Waiting:
							reportStatus("status: " + status + " line: " + line);
							if (line.Contains("Dumping Log") || 
							    line.Contains("GPS:") || 
							    line.Contains("NTUN:") || 
							    line.Contains("CTUN:") || 
							    line.Contains("PM:"))
							{
								status = serialstatus.Reading;
							}
							break;

					case serialstatus.DoneDumping:
						eraseAllLogs();
						break;

					case serialstatus.ConfirmErasing:
						if (line.Contains ("No logs") ||
						    line.Contains ("logs enabled")) {
							status = serialstatus.Finished;
							reportStatus ("Huzzah! Finished OK");
							Environment.Exit (0);
						}
						break;

					default:
						reportStatus("status: " + status + " line: " + line);
						break;
					}

				}

			}
			catch (Exception ex) { reportError ("Error reading data" + ex.ToString()); }
		}


		string lastline = "";
		string[] ctunlast = new string[] { "", "", "", "", "", "", "", "", "", "", "", "", "", "" };
		string[] ntunlast = new string[] { "", "", "", "", "", "", "", "", "", "", "", "", "", "" };
		List<PointLatLngAlt> cmdList = new List<PointLatLngAlt>();
		Point3D oldlastpos = new Point3D();
		Point3D lastpos = new Point3D();

		private void processGPSLogLine(string[] items)
		{

		}

		private void processRawLogLine(string line)
		{

//			reportStatus ("processRawLogLine: " + line);

			try {
				line = line.Replace(", ", ",");
				line = line.Replace(": ", ":");

				string[] items = line.Split(',', ':');

				//reportStatus ("items[0]: " + items[0]);

				if (items[0].Contains("CMD")) {
					if (flightdata.Count == 0) {
						if (int.Parse(items[2]) <= (int)95) { //TODO ArdupilotMega.MAVLink.MAV_CMD.LAST) // wps
							PointLatLngAlt temp = new PointLatLngAlt(double.Parse(items[7], cultureInfo) / 10000000, double.Parse(items[8], cultureInfo) / 10000000, double.Parse(items[6], cultureInfo) / 100, items[1].ToString());
							cmdList.Add(temp);
						}
					}
				}
				if (items[0].Contains("MOD")) {
					positionindex++;
					modelList.Add(""); // i cant be bothered doing this properly
					modelList.Add("");
					modelList[positionindex] = (items[1]);
				}
				//GPS, 1, 15691, 10, 0.00, -35.3629379, 149.1650850, -0.08, 585.41, 0.00, 126.89
				if (items[0].Contains("GPS") && 
				    items[2] == "1" && 
				    items[4] != "0" && 
				    items[4] != "-1" && 
				    lastline != line) 
				{ // check gps line and fixed status

					//TODO arduplane
					if (positionsList[positionindex] == null)
						positionsList[positionindex] = new List<Point3D>();

					if (double.Parse(items[4], cultureInfo) == 0)
						return;

					double alt = double.Parse(items[6], cultureInfo);

					if (items.Length == 11) {
						if ((items[6] == "0.0000") || (items[6] == "0")) {
							alt = double.Parse(items[7], cultureInfo);
						}
					}


					double x = double.Parse(items[5],cultureInfo);
					double y = double.Parse(items[4], cultureInfo);


                    positionsList[positionindex].Add(new Point3D(x, y, alt));

					oldlastpos = lastpos;
					lastpos = (positionsList[positionindex][positionsList[positionindex].Count - 1]);
					lastline = line;
				}
				if (items[0].Contains("GPS") && 
					    items[4] != "0" && 
					    items[4] != "-1" && 
					    items.Length <= 9) // AC
				{
						//TODO arducopter

					if (positionsList[positionindex] == null)
						positionsList[positionindex] = new List<Point3D>();

					if (double.Parse(items[4], cultureInfo) == 0)
						return;

					double alt = double.Parse(items[5], cultureInfo);

					positionsList[positionindex].Add(new Point3D(double.Parse(items[4], cultureInfo), double.Parse(items[3], cultureInfo), alt));
					oldlastpos = lastpos;
					lastpos = (positionsList[positionindex][positionsList[positionindex].Count - 1]);
					lastline = line;

				}
				//GPS, 1, 15691, 10, 0.00, -35.3629379, 149.1650850, -0.08, 585.41, 0.00, 126.89
				if (items[0].Contains("GPS") && 
					    items[1] == "3" && 
					    items[4] != "0" && 
					    items[4] != "-1" && 
					    lastline != line) // check gps line and fixed status
				{
					if (positionsList[positionindex] == null)
						positionsList[positionindex] = new List<Point3D>();


					double alt = double.Parse(items[8], cultureInfo);

					positionsList[positionindex].Add(new Point3D(double.Parse(items[6], cultureInfo), double.Parse(items[5], cultureInfo), alt));
					oldlastpos = lastpos;
					lastpos = (positionsList[positionindex][positionsList[positionindex].Count - 1]);
					lastline = line;
				}
				if (items[0].Contains("CTUN"))
				{
					ctunlast = items;
				}
				if (items[0].Contains("NTUN"))
				{
					ntunlast = items;
					line = "ATT:" + double.Parse(ctunlast[3], cultureInfo) * 100 + "," + double.Parse(ctunlast[6], cultureInfo) * 100 + "," + double.Parse(items[1], cultureInfo) * 100;
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

							runmodel.Orientation.roll = double.Parse(items[1], cultureInfo) / -100;
							runmodel.Orientation.tilt = double.Parse(items[2], cultureInfo) / -100;
							runmodel.Orientation.heading = double.Parse(items[3], cultureInfo) / 100;

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

		List<string> modelList = new List<string>();
		List<Point3D>[] positionsList = new List<Point3D>[200];
		int positionindex = 0;

		private void writeGPX(string gpxFilename)
		{
//			string gpxFilename = Path.GetDirectoryName (filename) + 
//				Path.DirectorySeparatorChar + 
//				Path.GetFileNameWithoutExtension (filename) + 
//				".gpx";

			reportStatus ("writeGPX: " + gpxFilename);
			System.Xml.XmlTextWriter xw = new System.Xml.XmlTextWriter(gpxFilename,
			                                                           System.Text.Encoding.ASCII);

			xw.WriteStartElement("gpx");
			xw.WriteStartElement("trk");
			xw.WriteStartElement("trkseg");

			DateTime start = new DateTime(DateTime.Now.Year,DateTime.Now.Month,DateTime.Now.Day,0,0,0);

			reportStatus ("Writing GPX flightdata for [" + flightdata.Count + "] points ");
			foreach (Data mod in flightdata) {
				xw.WriteStartElement("trkpt");
				xw.WriteAttributeString("lat", mod.model.Location.latitude.ToString(cultureInfo));
				xw.WriteAttributeString("lon", mod.model.Location.longitude.ToString(cultureInfo));

				xw.WriteElementString("ele", mod.model.Location.altitude.ToString(cultureInfo));
				xw.WriteElementString("time", start.AddMilliseconds(mod.datetime).ToString("yyyy-MM-ddTHH:mm:sszzzzzz"));
				xw.WriteElementString("course", (mod.model.Orientation.heading).ToString(cultureInfo));

				xw.WriteElementString("roll", mod.model.Orientation.roll.ToString(cultureInfo));
				xw.WriteElementString("pitch", mod.model.Orientation.tilt.ToString(cultureInfo));
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
			foreach (List<Point3D> poslist in positionsList)
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
				if (g < modelList.Count)
					mode = modelList[g];

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

			foreach (PointLatLngAlt p1 in cmdList)
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

			foreach (Data mod in flightdata) {
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

				try {
					pmplane.Point = new KmlPoint((float)model.Location.longitude, 
					                             (float)model.Location.latitude, 
					                             (float)model.Location.altitude);
					pmplane.Point.AltitudeMode = altmode;

					Link link = new Link();
					link.href = "block_plane_0.dae"; //TODO

					model.Link = link;

					pmplane.Model = model;

					planes.Add(pmplane);
				}
				catch { } // bad lat long value

				lastmodel = mod.model;

				a++;
			}

			kml.Document.Add(fldr);
			reportStatus ("Saving KML in " + filename);
			kml.Save(filename);

			// create kmz - aka zip file

			string kmzFilename = filename.Replace (".log.kml", ".kmz");
			FileStream fs = File.Open(kmzFilename, FileMode.Create);
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
			reportStatus ("Flushing KMZ file: " + kmzFilename);
			zipStream.Flush ();
			zipStream.Close();

		}


		private void dumpAllLogs()
		{
			threadrun = true;
			System.Threading.Thread t12 = new System.Threading.Thread (delegate() { 

				try {
					//comPort.Write("\n\n\n\nexit\r\nlogs\r\n"); 

					foreach (String logId in allLogIds) {
						comPort.DiscardInBuffer();

						String cmd = "dump " + logId + "\r";
						reportStatus (cmd);
						System.Threading.Thread.Sleep(100);
						comPort.Write (cmd );
						System.Threading.Thread.Sleep(100);
						comPort.DiscardInBuffer();
						status = serialstatus.CreateLogFile;

						while (status != serialstatus.DoneDumping) {
							readUntilTime(1000);
						}
					}
				}
				catch (Exception downloadEx) {
					reportError("ex dumpAllLogs" + downloadEx);
				}

				reportStatus("T12 thread close");

			});

			t12.Name = "T12 dumpAllLogs";
			t12.Start();


		}


		private void eraseAllLogs() 
		{
			reportStatus("Erasing logs...");
			comPort.Write("erase\r");
			status = serialstatus.ConfirmErasing;
			reportStatus("ERASE CAN TAKE A MINUTE OR LONGER");
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
		private void reportStatus(String status) 
		{
			lock (thisLock) {
				log.Info(status);
				Console.WriteLine (status);
			}
		}

		/**
		 * Replaces CustomMessageBox nonsense
		 * */
		private void reportError(String error) 
		{
			log.Error (error);
			Console.WriteLine (status);
			Console.Error.WriteLine(error);
		}
	}


}
